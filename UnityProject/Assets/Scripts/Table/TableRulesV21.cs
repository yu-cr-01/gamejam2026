using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;
using GameJam.Rules;

namespace GameJam.Prototype
{
    /// <summary>
    /// 牌桌上的一张**素材**的 3D 外壳。它把 MonoBehaviour 和 <see cref="MaterialState"/> 接起来：
    ///   · 3D 卡是给人点、给人看的（选中当启动目标）
    ///   · MaterialState 是规则内核认识的东西（H / D / V / removed）
    /// 两者一一对应，方向是：先有 MaterialState，再造 3D 卡并挂上这个组件。
    ///
    /// 【为什么不把 MaterialState 塞进 PlayCard】
    /// PlayCard 是"一张牌怎么动、长什么样"，手牌 / 投放区 / 牌组卡都在用它。
    /// 让它认识规则内核的 MaterialState，表现层就依赖上了规则层，
    /// 以后 CardBrowser / TablePreviewCapture 这些纯展示的地方也得跟着拖一层依赖进来。
    /// 所以用这个只有两行的外壳组件做桥，两边都不用改。
    /// </summary>
    public class TableMaterialCard : MonoBehaviour
    {
        public MaterialState state;

        /// <summary>桌面上对应的那张 3D 卡。</summary>
        public PlayCard view;
    }

    /// <summary>
    /// 手牌里的一张**法术**的 3D 外壳。点它就是"打出这张法术"（附魔到刀片，不消耗行动机会）。
    ///
    /// 【为什么单独一个组件，而不是复用 TableMaterialCard 塞个 null state】
    /// 素材是"拖到桌上、之后被反复启动"，法术是"点一下就没"，交互完全不同。
    /// 用类型区分开，交互层就不用再判"这张牌的 state 是不是空"。
    /// </summary>
    public class TableSpellCard : MonoBehaviour
    {
        public SpellCard state;
        public PlayCard view;
    }

    /// <summary>
    /// 手牌里的一张素材卡。
    ///
    /// 【为什么手里还要留一份 H/D/V，而不是只留 Ingredient】
    ///   手牌里的卡要带着卡面数值；被出到桌面时，这份数值直接变成 MaterialState 的初始值。
    ///   若只留 Ingredient，数值就得在出牌那一刻重新查表 ——
    ///   而"形态变化产出的新卡"是运行时才出现的，卡表里没有它的数值。
    ///
    /// 【为什么提到类外面】TableSpellCard / TableInteraction / TableTurnLoop 都要引用它，
    ///   嵌在 TableRulesV21 里就得处处写 TableRulesV21.MaterialCard 这种长名字。
    /// </summary>
    public class MaterialCard
    {
        public Ingredient card;
        public int H, D, V;

        /// <summary>
        /// 这张卡的 H/D/V 是从哪来的（HUD 要照着说，不然 0 分看起来像"这张卡就是 0 分"）。
        ///
        /// 【为什么必须记这个】手里的牌不全是 v2.1 卡表来的：
        ///   牌组里的卡（秘银锭 / 卤水 / 硝石…）来自旧 game_config.json，
        ///   而它们的 v 就是 0（旧配置把 V 都留空了）。玩家看到"V=0"会以为是张废卡，
        ///   其实是"这套数值还没设计完"。三种来源必须分得开：
        ///     v2.1 卡表 / 旧配置 / 占位值（两套都没有）
        /// </summary>
        public enum ValueSource
        {
            /// <summary>cards_v21.json 的 h/d/v</summary>
            CardTable = 0,

            /// <summary>旧 game_config.json 的三属性列</summary>
            LegacyConfig = 1,

            /// <summary>两套配置都没给，用了占位表</summary>
            Placeholder = 2,
        }

        public ValueSource source = ValueSource.CardTable;

        public string name { get { return card != null ? card.name : "?"; } }
        public string id { get { return card != null ? card.id : ""; } }

        /// <summary>
        /// 卡面显示名 = 中文名 + 当前 D（+ 数值来源标记）。
        ///
        /// 【为什么把 D 拼进名字里】3D 卡面是造卡那一刻烤死的（见 CardFactory），
        /// 运行时的 D 变了贴图不会变。而 D 是玩家最需要看的一个数
        /// （还剩几次能用、这一次启动会不会触发 D 耗尽）。改名字至少能让手牌上的 D 是对的。
        /// 规范做法是给 CardFactory 加一条"运行时角标"通道，那是下一步的事。
        /// </summary>
        public string DisplayName
        {
            get
            {
                string s = name + " D" + D;
                if (V > 0) return s;

                if (source == ValueSource.CardTable)      return s + "（V=0）";
                if (source == ValueSource.LegacyConfig)   return s + "（旧配置V=0）";
                return s + "（占位V=0）";
            }
        }
    }

    /// <summary>手牌里的一张法术卡。</summary>
    public class SpellCard
    {
        public Spell spell;

        public string name { get { return spell != null ? spell.name : "?"; } }
        public string id { get { return spell != null ? spell.id : ""; } }

        /// <summary>引擎只认这个五字段的小结构（见 RuleText.SpellSpec 的说明）。</summary>
        public SpellSpec Spec()
        {
            if (spell == null) return new SpellSpec("", "", "", "", "");
            return new SpellSpec(spell.id, spell.name, spell.enchant, spell.category, spell.requirement);
        }
    }

    /// <summary>
    /// v2.1 回合循环的**规则侧**总装 —— 把已经写好、离线验证过的规则内核
    /// （LayerLedger / BladeState / EnchantRules / RuleText / TurnEngine）
    /// 挂到 3D 桌面上。
    ///
    /// 【它和 TableTurnLoop 的分工】—— 这条是这次整合的主轴
    ///   TableTurnLoop —— **阶段机**：现在该谁操作、能不能拖牌、按哪个键确认。
    ///                     它只回答"流程走到哪了"，一个分数都不算。
    ///   TableRulesV21 —— **规则与状态**：刀片、桌面素材、手牌法术、分数、行动机会、回合。
    ///                     它只回答"发生了什么"，一次阶段切换都不管。
    ///
    ///   依赖是单向的：规则侧只读 loop 的少数字段（setup / turn / notice），
    ///   从不读 TablePhase；阶段机要什么就问这里要。
    ///   这样规则内核拿去做别的表现（2D 棋盘 / 纯文字）时，搬走这个文件就够了。
    ///
    /// 【为什么不把引擎直接塞进 TableTurnLoop】
    ///   引擎的 LevelState / TurnEngine 已经是干净的"一关 = 一个对象"，
    ///   而 TableTurnLoop 身上挂着 3D 引用、阶段、投放区、杯内模拟。
    ///   混在一起的结果是规则没法离线测 —— RuleProbe 现在能不开 Unity 跑，
    ///   正是因为 Assets/Scripts/Rules/ 下不引 UnityEngine。所以整合只能**加一层适配**。
    ///
    /// 【卡面数值从哪来】见 ApplyValues
    ///   cards_v21.json 里**没有** h/d/v（正文 §2.2 原话："暂时还没有设计具体数值，
    ///   等待第二周设计关卡一并处理"）。所以这里补一套**临时数值**，
    ///   和 Tools/RuleProbe 的 FixtureValues 是同一套口径 ——
    ///   两边不一致的话会出现"探针全绿、实机不对"。策划补了 h/d/v 之后把 ApplyValues 删掉即可。
    /// </summary>
    public class TableRulesV21 : MonoBehaviour
    {
        /// <summary>桌面上的回合循环（阶段机）。规则侧只读它的少数字段，不指挥它。</summary>
        public TableTurnLoop loop;

        /// <summary>3D 卡的容器（和 TableSetup.cardsRoot 是同一个）。</summary>
        public Transform cardsRoot;

        /// <summary>榨汁机 —— 启动时的冲压反馈和罐子液面。</summary>
        public JuicerRig juicer;

        // ══════════════════════════════════════════════════════════════
        //  一关的状态（这一份就是 LevelState 在桌面上的对应物）
        // ══════════════════════════════════════════════════════════════

        /// <summary>刀片：H（启动次数池，归零爆刀）+ V（得分加成）+ 四种附魔层数。**没有 D**（正文 §2.5）。</summary>
        public BladeState blade = new BladeState("", "（未选刀片核心）", 0, 0);

        /// <summary>桌面上的素材（可被启动的目标）。</summary>
        public readonly List<MaterialState> table = new List<MaterialState>();

        /// <summary>手牌：素材一副、法术一副（正文 §2.6 的初始手牌是 4 素材 + 1 法术）。</summary>
        public readonly List<MaterialCard> hand = new List<MaterialCard>();
        public readonly List<SpellCard> handSpells = new List<SpellCard>();

        /// <summary>
        /// 当前关卡的目标分。v2.1 用 <see cref="TableSettings.V21TargetScore"/>（见 BeginLevel 的说明），
        /// 配置为 0 时退回 <see cref="FallbackTargetScore"/>。
        /// </summary>
        public int targetScore;

        /// <summary>当前回合（从 1 开始）与行动机会。</summary>
        public int turnIndex = 1;
        public int actionPoints = LevelRun.ActionPointsPerTurn;

        /// <summary>本回合已经启动过几次（HUD 的"本回合是否已用过启动"看它）。</summary>
        public int startsThisTurn;

        /// <summary>
        /// **本回合被启动过的那些桌面素材**（"收回手牌"的限制条件要用它）。
        ///
        /// 【为什么不能只看 startsThisTurn】那是"本回合启动过几次"的计数，
        ///   回答不了"这一张启动过没有" —— 玩家先启动 A、再想收回 B 时，
        ///   计数大于 0 会把 B 一起拒掉，而 B 明明还能收（用户要的正是这个区分）。
        ///
        /// 【为什么存在这里而不是 MaterialState 上】Assets/Scripts/Rules/** 是封版基线
        ///   （210 项断言守着），不许为了表现层往 MaterialState 上加字段。
        ///   而 MaterialState 在桌面这一层是**按引用**用的（BuildState 直接把 table 传进引擎，
        ///   Absorb 是就地读回），所以"记住是哪些对象"就够了，一个 List 足够。
        ///
        /// 清空点：开一关（BeginLevel）、第 1 回合（StartFirstTurn）、每个新回合（EndRound 里
        /// 引擎开新回合之后）、清桌面（ClearTable）—— 也就是"回合"这个概念会重置的每一处。
        /// </summary>
        private readonly List<MaterialState> startedThisTurnList = new List<MaterialState>();

        /// <summary>这一张桌面素材本回合启动过没有。</summary>
        public bool StartedThisTurn(MaterialState st)
        {
            return st != null && startedThisTurnList.Contains(st);
        }

        /// <summary>
        /// 出牌那一刻这张素材的**数值来源**（收回手牌时原样带回去）。
        ///
        /// 【为什么要记】MaterialCard.source 决定卡面名字后面那句
        ///   "（V=0）/（旧配置V=0）/（占位V=0）"（见 MaterialCard.DisplayName）。
        ///   收回手牌时会新建一张 MaterialCard，重猜来源会让一句本来正确的注释变错。
        ///   出牌那一步是唯一知道来源的地方，顺手记下来最省事。
        /// </summary>
        private readonly Dictionary<MaterialState, MaterialCard.ValueSource> sourceOfState =
            new Dictionary<MaterialState, MaterialCard.ValueSource>();

        public int score;

        /// <summary>
        /// 空白卡计数（正文 §2.1 原话："需单独统计数量"）。
        /// 引擎里也有一个计数，但那份在每次调用时新建的 LevelRun 上、读不回来，
        /// 所以桌面这边自己数一份 —— 反正是"这一关产出了几张空白卡"，数起来没有歧义。
        /// </summary>
        public int blankCount;

        /// <summary>关卡结束的四个条件（正文 §八）：4 回合耗尽 / 爆刀 / 主动结束 / 达到目标分。</summary>
        public bool levelOver;
        public bool bursted;

        /// <summary>这一关结束的原因（结算面板要写清是哪一种结束）。</summary>
        public string endReason = "";

        // ── 与 3D 的对应关系 ──────────────────────────────────────────

        private readonly List<TableMaterialCard> tableCards = new List<TableMaterialCard>();

        /// <summary>最近一次结算的日志（HUD 的"上一次启动"区直接读它）。</summary>
        public readonly List<string> lastLog = new List<string>();

        public string lastSummary = "";

        /// <summary>⚠ 引擎报出来的警告（卡表缺产物、规则认不出、产出找不到卡……）。**不许忽略**。</summary>
        public readonly List<string> warnings = new List<string>();

        /// <summary>卡表整体解析报告（未识别清单在里面）。BeginLevel 时建一次。</summary>
        public RuleReport report;

        // ── 规则的"旋钮"（正文没写数的都在这里）────────────────────────

        /// <summary>引擎的结算参数。每关一份 —— 关卡之间不继承（正文 §八：每关独立）。</summary>
        public TurnRules rules = new TurnRules();

        private TurnEngine engine;

        // ── 选中的目标素材 ────────────────────────────────────────────

        /// <summary>当前选中的桌面素材（启动破壁机的目标）。没选中就是 null。</summary>
        public MaterialState selected;

        /// <summary>这次选中是"出牌自动选的最新一张"还是"玩家自己点的"。</summary>
        public bool selectedAuto;

        // ══════════════════════════════════════════════════════════════
        //  正文里写死的规则数（HUD 要显示，别在 HUD 里各写一个数）
        // ══════════════════════════════════════════════════════════════

        /// <summary>正文 §2.6：初始手牌 4 张素材。</summary>
        public const int InitialMaterialCount = 4;

        /// <summary>正文 §2.6：初始手牌 1 张法术。</summary>
        public const int InitialSpellCount = 1;

        /// <summary>素材 D 的默认值（正文没给）。卡表补 d 字段后自动失效。</summary>
        public const int DefaultMaterialD = 3;

        /// <summary>
        /// 本关目标分的兜底值。
        /// 配置里 targetScore 为 0 时用它 —— 目标分 0 会让 JuicerRig 的液面算成 NaN
        /// （SetScore 里是 score / target），而且"达到目标分即胜利"会变成一开局就胜利。
        /// 正文 §2.6 只说"达到目标分即胜利"，没给数值，所以这个数只能先兜着。
        /// </summary>
        public const int FallbackTargetScore = 30;

        // ── 桌面素材摆哪 ─────────────────────────────────────────────

        /// <summary>和投放区的两个槽位对齐（TableSetup 里 board 的 origin 是 z = −0.20、间距 0.46）。</summary>
        private static readonly Vector3 TableMaterialLeft  = new Vector3(-0.23f, 0f, -0.20f);
        private static readonly Vector3 TableMaterialRight = new Vector3( 0.23f, 0f, -0.20f);

        // ══════════════════════════════════════════════════════════════
        //  关卡生命周期
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 开一关：按 v2.1 的初始手牌（4 素材 + 1 法术）发牌。
        ///
        /// 【这里**不**定刀片核心】
        ///   正文 §2.5："玩家从初始手牌中选择一张素材作为刀片核心" —— 选是玩家的事。
        ///   BeginLevel 只负责"手里有什么"，核心由 ConfirmBladePick → ChooseCore 定。
        /// </summary>
        public void BeginLevel(Deck deck, int levelTargetScore)
        {
            CardSpecs.EnsureRegistered();

            ClearTable();
            hand.Clear();
            handSpells.Clear();
            coreCard = null;

            lastLog.Clear();
            lastSummary = "";
            warnings.Clear();
            selected = null;
            selectedAuto = false;

            levelOver      = false;
            bursted        = false;
            endReason      = "";
            score          = 0;
            turnIndex      = 1;
            actionPoints   = LevelRun.ActionPointsPerTurn;
            startsThisTurn = 0;

            // ★ 目标分：v2.1 用 TableSettings.V21TargetScore，**不用** levelTargetScore。
            //   传进来的那个值来自 game_config.json 的 levels[].targetScore（1000/1500/2000），
            //   那是旧流程的口径（旧流程一次杯内模拟能拿几百分）。
            //   v2.1 每次启动只有「目标 V + 刀片 V」= 几分，一关最多 20 次启动，
            //   拿 1000 当目标 = 永远达不到 → 玩家只看到"4 回合耗尽"，像是规则没生效。
            //   所以这里只做一件事：v2.1 走自己的目标分（默认 60，理由见 TableSettings）。
            //   levelTargetScore 留着不删、也不再参与 v2.1 结算：旧流程的数值一个字都没动。
            targetScore = TableSettings.V21TargetScore > 0
                ? TableSettings.V21TargetScore
                : FallbackTargetScore;
            if (levelTargetScore > 0 && levelTargetScore != targetScore)
            {
                Debug.Log("[V21] 目标分取 v2.1 专用值 " + targetScore +
                          "（配置里旧流程的 targetScore=" + levelTargetScore + " 不参与 v2.1 结算）");
            }

            // 规则旋钮每关一份。TargetScore 必须填进去 —— 引擎的 Finish() 靠它判"达到目标分"。
            rules = new TurnRules();
            rules.TargetScore = targetScore;

            // 引擎的卡表查询：规则文本里写的是**中文卡名**，必须由这里翻回卡对象。
            engine = new TurnEngine(rules, new V21CardLookup());

            // 牌组指定的开局刀片 —— 只用来做"默认候选"（玩家不点任何牌时的兜底）。
            // 旧流程里这个字段是"开局哪张牌装到刀片槽"，v2.1 正文没有这条规则
            // （核心由玩家选），所以这里只是"更合理的默认值"，不是规则。
            initialBladeId = deck != null ? (deck.initialBladeId != null ? deck.initialBladeId : "") : "";

            DealInitialHand(deck);

            blade = new BladeState("", "（未选刀片核心）", 0, 0);

            // 整份卡表的解析报告：HUD 要拿它报"几条规则没实现"。
            // 只建一次 —— 31 素材 + 8 法术全解析一遍不便宜，而且运行期卡表不会变。
            if (report == null) BuildReport();

            // ★★ 必须在这里就把手牌摆出来 ★★
            //   正文 §2.5 的刀片核心是**玩家从初始手牌里点一张**选的 ——
            //   手牌没摆出来，玩家就没有可点的东西，只能走"默认第一张"的兜底，
            //   表现是"桌上一张手牌都没有、直接就能确认"。
            //   （这一条真的踩到了：选刀片阶段桌面是空的，3D 手牌 0 张。）
            RebuildHand();

            Debug.Log("[V21] 开一关｜" + DescribeHud().Replace("\n", "　｜　"));
        }

        /// <summary>
        /// 发初始手牌：4 张素材 + 1 张法术（正文 §2.6 / §八）。
        ///
        /// 【牌组怎么用】—— 一个必须说清的口径
        ///   v2.1 正文里没有"牌组"这个概念（那是旧流程的），它只规定了手牌形状。
        ///   所以这里的口径是：**牌组决定用哪 4 张素材 + 1 张法术**；
        ///   牌组没给够时从 cards_v21.json 的素材表里按顺序补足。
        ///   这样"三选一牌组"在 v2.1 模式下依然有意义，而且换牌组 = 换一套开局，
        ///   不需要再给 v2.1 写第二套配置。
        /// </summary>
        private void DealInitialHand(Deck deck)
        {
            List<Ingredient> pool = new List<Ingredient>();
            List<Spell> spellPool = new List<Spell>();

            if (deck != null && deck.ingredients != null)
                for (int i = 0; i < deck.ingredients.Count; i++)
                    if (deck.ingredients[i] != null) pool.Add(deck.ingredients[i].Clone());

            // 旧牌组里的"变速模块"在这里当法术看待 —— 它们本来就是同一套机制的两种叫法
            // （见 Spell.cs 顶部那段）。但 v2.1 的法术要有 enchant / requirement 才有意义，
            // 所以只有在卡表里找得到同名法术时才收，否则丢掉。
            if (deck != null && deck.modules != null)
            {
                for (int i = 0; i < deck.modules.Count; i++)
                {
                    SpeedModule m = deck.modules[i];
                    Spell sp = CardSpecs.SpellByName(m != null ? m.name : "");
                    if (sp != null && !HasSpell(spellPool, sp.id)) spellPool.Add(CloneSpell(sp));
                }
            }

            // 素材不够 4 张 → 用卡表里靠前的素材补（跳过已经拿到的）
            // 素材池的来源要分开记：牌组带来的卡（前几张）走的是**旧 game_config** 那套数值，
            // 从 cards_v21.json 补进来的才是 v2.1 卡表的数值（见 MaterialCard.ValueSource）。
            int deckMaterialCount = CountDeckMaterials(deck);

            List<Ingredient> all = CardSpecs.Materials();
            for (int i = 0; i < all.Count && pool.Count < InitialMaterialCount; i++)
            {
                Ingredient m = all[i];
                if (m == null || ContainsIngredient(pool, m.id)) continue;
                pool.Add(m.Clone());
            }

            // 法术不够 1 张 → 用卡表里的第一个法术补
            if (spellPool.Count == 0)
            {
                List<Spell> spells = CardSpecs.Spells();
                if (spells.Count > 0 && spells[0] != null) spellPool.Add(CloneSpell(spells[0]));
            }

            int n = Mathf.Min(InitialMaterialCount, pool.Count);
            for (int i = 0; i < n; i++)
            {
                Ingredient ing = pool[i];

                // 前 deckMaterialCount 张是牌组带来的（旧配置），其余是从卡表补的
                MaterialCard.ValueSource src = (i < deckMaterialCount)
                    ? MaterialCard.ValueSource.LegacyConfig
                    : MaterialCard.ValueSource.CardTable;

                ApplyValues(ing, src);

                MaterialCard mc = new MaterialCard();
                mc.card = ing;
                mc.H = ing.h;
                mc.D = ing.d > 0 ? ing.d : DefaultMaterialD;
                mc.V = ing.v;
                mc.source = src;
                hand.Add(mc);
            }

            for (int i = 0; i < spellPool.Count && i < InitialSpellCount; i++)
                handSpells.Add(new SpellCard { spell = spellPool[i] });

            if (hand.Count < InitialMaterialCount)
                Debug.LogWarning("[V21] 素材只凑到 " + hand.Count + " 张（要 " + InitialMaterialCount +
                                 " 张）—— 卡表或牌组可能没配全");
            if (handSpells.Count == 0)
                Debug.LogWarning("[V21] 一张法术都没有（正文的初始手牌是 4 素材 + 1 法术）—— 卡表可能没读进来");
        }

        private static bool ContainsIngredient(List<Ingredient> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].id == id) return true;
            return false;
        }

        /// <summary>牌组里实际带了几个有效素材（DealInitialHand 用它区分"这张卡从哪来的"）。</summary>
        private static int CountDeckMaterials(Deck deck)
        {
            if (deck == null || deck.ingredients == null) return 0;

            int n = 0;
            for (int i = 0; i < deck.ingredients.Count; i++)
                if (deck.ingredients[i] != null) n++;
            return n;
        }

        private static bool HasSpell(List<Spell> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].id == id) return true;
            return false;
        }

        /// <summary>法术的浅拷贝（Spell 没有 v2.1 那五个字段的构造函数重载）。</summary>
        private static Spell CloneSpell(Spell s)
        {
            if (s == null) return null;

            Spell c = new Spell(s.id, s.name);
            c.series      = s.series;
            c.enchant     = s.enchant;
            c.category    = s.category;
            c.requirement = s.requirement;
            c.sources     = s.sources;
            c.effects     = s.effects;
            return c;
        }

        /// <summary>定刀片核心：该卡 H → 刀片初始 H、V → 刀片初始 V，该卡移出手牌（正文 §2.5）。</summary>
        public void ChooseCore(MaterialCard mc)
        {
            if (mc == null) return;
            if (!hand.Remove(mc)) return;

            blade = new BladeState(mc.id, mc.name, mc.H, mc.V);

            // ★ 记住"是哪一张卡当的核心"。
            //   【为什么必须留这一份引用】桌面上那张刀片卡（BuildBladeCard）要照着它造 ——
            //   而这张卡**已经从手牌里移除了**，再想找它就只能问 CoreCandidate()，
            //   而那个方法问的是"手牌里第一张素材"，于是会答成另一张卡：
            //   实机踩到的就是这一条 —— 核心是水（刀片 H=2 V=2），桌上摆出来的却是冰的卡面
            //   （H5 D2 V3），玩家看到的是"面板写桌面 3 张、画面里 4 张，而且有两张一模一样的冰"。
            //   留着引用，桌面上那张卡就一定是玩家真正选中的那张。
            coreCard = mc;

            Debug.Log("[V21] 刀片核心 = " + mc.name + " → 刀片 H=" + blade.H + " V=" + blade.V +
                      "（该卡移出手牌，本关不再参与出牌）");
        }

        /// <summary>
        /// 当刀片核心的那张卡（玩家选中的那一张，已移出手牌）。
        /// 没定核心时是 null —— 桌面上那张"候选刀片卡"走 CoreCandidate()。
        /// 只由 <see cref="ChooseCore"/> 写、<see cref="BeginLevel"/> 清。
        /// </summary>
        public MaterialCard coreCard;

        /// <summary>有刀片核心吗（H&gt;0 才算 —— H=0 一进关卡就爆刀，等于没法玩）。</summary>
        public bool HasCore
        {
            get { return blade != null && blade.H > 0 && !string.IsNullOrEmpty(blade.name); }
        }

        /// <summary>
        /// 刀片核心的**兜底候选**：手牌里第一张 H&gt;0 的素材。
        ///
        /// 【为什么要有兜底】正文 §2.5 说核心由玩家选，但选之前桌面上得摆点东西给玩家看
        /// （TableTurnLoop.BuildBladeCard）。这个兜底和"玩家点了确认但没点任何牌"时用的是
        /// **同一个方法** —— 两处各写一份的话，会出现"桌上摆着 A、确认下去变成 B"。
        ///
        /// 【优先牌组指定的那张】旧 game_config 里每副牌组都有 initialBladeId
        /// （例如"硫硝爆燃"指定外星合金）。v2.1 正文没有这条规则，但当玩家什么都不点时，
        /// "配置里本来就想让它当刀片的那张"显然比"列表第一张"更合理 ——
        /// 旧流程里这个字段是开局刀片，v2.1 里降级成默认候选。
        ///
        /// H&gt;0 是必须的：H=0 的素材当核心会一进关卡就爆刀。
        /// </summary>
        public MaterialCard CoreCandidate()
        {
            MaterialCard first = null;

            for (int i = 0; i < hand.Count; i++)
            {
                MaterialCard mc = hand[i];
                if (mc == null || mc.card == null || mc.H <= 0) continue;

                if (first == null) first = mc;
                if (!string.IsNullOrEmpty(initialBladeId) && mc.id == initialBladeId) return mc;
            }

            return first;
        }

        /// <summary>牌组指定的开局刀片 id（只用于默认候选，见 CoreCandidate）。</summary>
        public string initialBladeId = "";

        /// <summary>进关卡：第 1 回合开始，行动机会重置、附魔层数不变（正文 §三.1）。</summary>
        public void StartFirstTurn()
        {
            turnIndex      = 1;
            actionPoints   = LevelRun.ActionPointsPerTurn;
            startsThisTurn = 0;

            lastLog.Clear();
            lastLog.Add("【第 1 回合开始】行动机会 " + actionPoints + "/" + LevelRun.ActionPointsPerTurn +
                        "；附魔层数不变：" + (blade != null ? blade.layers.Describe() : "（无）"));
        }

        // ══════════════════════════════════════════════════════════════
        //  出牌
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 打出一张素材：从手牌上桌，变成可被启动的 <see cref="MaterialState"/>。
        ///
        /// 【不消耗行动机会】正文 §2.6："手牌无上限，出牌不消耗行动机会"。
        ///   出牌是自由的，只有"启动破壁机"才吃行动机会 —— 这是 v2.1 和旧流程最大的手感区别
        ///   （旧流程每回合只有两次行动，出牌也算一次，所以 Stage 那边要做模式分流）。
        ///
        /// 【为什么自动把它设成启动目标】
        ///   正文 §三.2 要求启动要"选择桌面一张素材作为目标"。玩家刚出完牌，
        ///   下一个动作九成就是启动它 —— 自动选中省一次点击，但**必须显示出来**
        ///   （HUD 会写"自动选中"，点别的素材即可改），否则玩家不知道目标是谁。
        /// </summary>
        public MaterialState PlayMaterial(MaterialCard mc)
        {
            if (mc == null || levelOver) return null;
            if (!hand.Remove(mc)) return null;

            Ingredient ing = mc.card;
            if (ing != null) ApplyValues(ing, mc.source);   // 形态变化产出的卡也走这条，保证 H/D/V 有值

            MaterialState st = new MaterialState(ing,
                mc.D > 0 ? mc.D : DefaultMaterialD,
                mc.H,
                mc.V);
            table.Add(st);

            Debug.Log("[V21] 出素材：" + st.Describe() + "｜桌面 " + table.Count + " 张");

            Select(st, true);
            return st;
        }

        /// <summary>
        /// 打出一张法术：附魔到刀片 / 立即加分 / 作用于桌面素材，**不消耗行动机会**
        /// （正文 §2.1、§七-11）。结算整段交给引擎 —— 附魔类型、层数、冷热覆盖都在
        /// TurnEngine.PlaySpell 里，这边一行都不重复实现。
        /// </summary>
        public TurnResult PlaySpell(SpellCard sc)
        {
            if (sc == null || levelOver) return null;
            if (!handSpells.Remove(sc)) return null;

            TurnResult r = engine.PlaySpell(BuildState(), sc.Spec());
            Absorb(r);

            Debug.Log("[V21] 出法术：" + sc.name + "\n" + (r != null ? r.LogText() : "（引擎没返回结果）"));
            return r;
        }

        // ══════════════════════════════════════════════════════════════
        //  启动破壁机（正文 §四：整条结算流水线都在引擎里）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 启动破壁机。消耗 1 行动机会 + 1 刀片H，对 <paramref name="target"/> 跑一次完整结算。
        ///
        /// 【为什么这里一行结算都不写】
        ///   "消耗 → 查附魔 → 形态变化/D → 计分 → 爆刀 → 献祭"这个顺序本身就是规则，
        ///   写在 TurnEngine 一处、每一步都有中文日志。这里只做三件事：
        ///   把桌面状态灌进引擎、把引擎改完的状态读回来、把结果翻译成 3D 表现。
        ///   两边各写一份结算，迟早会出现"界面算的分和引擎算的分不一样"。
        ///
        /// 【isLastStart 怎么定】
        ///   正文 §四说明："最后一次启动不一定是第 5 次行动"，所以**不做次数判断** ——
        ///   玩家每一次启动都可能是最后一次（他随时能点"结束回合"）。
        ///   只有三种情况是确定的：行动机会用完 / 手牌空了 / 已到目标分。
        ///   那时这一次必然是本回合最后一次，献祭吞噬必须当场生效，否则玩家白丢一张卡。
        /// </summary>
        public TurnResult TryActivate(MaterialState target)
        {
            if (engine == null) return null;

            if (target == null)
            {
                LoopNotice("先在桌面上点一张素材当启动目标");
                return null;
            }

            bool last = IsLastStartForSure();
            TurnResult r = engine.StartBlade(BuildState(), target, last);
            Absorb(r);

            Debug.Log("[V21] 启动（" + (last ? "确定是本回合最后一次 → 结算后判定献祭吞噬" : "可能还有下一次") +
                      "）\n" + (r != null ? r.LogText() : "（引擎没返回结果）"));

            // ★ 反应特效必须在这里播 —— **在 SyncVisualsAfterActivate 之前**：
            //   那一步会把已经离场的素材交给 PlayCard.ConsumeInto（卡飞向罐口、0.55 秒后自毁）。
            //   等它跑完再播，卡片位置已经空了、卡面辉光也没地方挂，
            //   表现就是"特效漂在桌子中间，而卡早就没了"。
            PlayReactionFx(r, target);

            SyncVisualsAfterActivate();
            return r;
        }

        /// <summary>
        /// 「两者发生反应」那一刻的特效：目标素材卡位置迸发 + 扩散光环 + 卡面发光，
        /// 破壁机同步亮一下、轻震一下。
        ///
        /// 【为什么要读日志，而不是让引擎多给字段】
        ///   Assets/Scripts/Rules/** 是封版基线（210 项断言守着），表现层不许改它的签名。
        ///   引擎已经把"命中了哪类附魔、哪条规则、结果是什么"都写进了中文日志，
        ///   格式还是它自己的 Describe() 拼的（见 ReactionFx.TryReadFromLog 的说明）。
        ///   这里只做三件事：把 TurnResult 交给解析器、认出"是哪张 3D 卡"、把颜色和力度传下去。
        ///
        /// 【为什么"目标卡的位置"要从 3D 卡上取】
        ///   规则层的 MaterialState 只有名字和数值，没有坐标；
        ///   桌面卡的位置在 tableCards 里那张 PlayCard 身上（它可能已经被摆到第二槽 / 往后错开过）。
        ///   取不到卡（比如目标压根没建出 3D 卡）就用破壁机罐口兜底 ——
        ///   宁可特效出现在机器那边，也不要什么都没有。
        /// </summary>
        private void PlayReactionFx(TurnResult r, MaterialState target)
        {
            ReactionFxKind kind;
            string rule, outcome;
            if (!ReactionFx.TryReadFromLog(r, out kind, out rule, out outcome)) return;

            TableMaterialCard tc = FindTableCard(target);
            PlayCard view = tc != null ? tc.view : null;

            Vector3 at = view != null
                ? view.transform.position
                : (juicer != null ? juicer.MouthWorld : Vector3.zero);

            ReactionFx fx = ReactionFx.Play(at, kind, view != null ? view.transform : null);

            Color tint = ReactionFx.TintOf(kind);
            float power = (kind == ReactionFxKind.Explode) ? ReactionFx.ExplodePower : 1f;

            // 破壁机那一侧的呼应（机身亮一下 + 轻震）—— 和卡上的特效同一帧起
            if (juicer != null) juicer.PlayReactionEcho(tint, power);

            Debug.Log("[V21][反应特效] " + (target != null ? target.name : "?") +
                      "｜" + outcome + "：" + rule +
                      "｜颜色 " + ReactionFx.KindName(kind) +
                      "｜时长 " + ReactionFx.Life + "s" +
                      "｜位置 (" + at.x.ToString("0.00") + ", " + at.y.ToString("0.00") + ", " + at.z.ToString("0.00") + ")" +
                      (view != null ? "｜卡面辉光挂在该卡上" : "｜（桌面卡找不到，用破壁机罐口兜底）"));
        }

        /// <summary>这一次启动之后行动机会归零 / 手牌空了 / 已达标 → 必然是本回合最后一次启动。</summary>
        private bool IsLastStartForSure()
        {
            if (actionPoints <= 0) return true;
            if (HandCount == 0) return true;
            if (targetScore > 0 && score >= targetScore) return true;
            return false;
        }

        // ══════════════════════════════════════════════════════════════
        //  回合 / 关卡推进
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 结束本回合：所有附魔层数 −1、归零消失；桌面素材保留（正文 §三.6）。
        /// 第 4 回合结束时关卡结束（正文 §三.7）。
        /// </summary>
        public TurnResult EndRound()
        {
            if (levelOver) return null;

            TurnResult r = engine.EndTurn(BuildState());
            Absorb(r);

            if (levelOver && string.IsNullOrEmpty(endReason)) endReason = "4 回合耗尽";

            // 还没结束 → 开下一回合：行动机会重置、附魔层数不变（正文 §三.1）
            if (!levelOver)
            {
                TurnResult b = engine.BeginTurn(BuildState());
                Absorb(b);
                startsThisTurn = 0;
            }

            Debug.Log("[V21] 回合推进\n" + (r != null ? r.LogText() : "（引擎没返回结果）"));
            return r;
        }

        /// <summary>
        /// 玩家主动结束关卡（正文 §八）：按当前分数结算，**不触发爆刀双倍**。
        /// 已经因为爆刀 / 达标 / 回合耗尽结束的，就只是把原因抄一遍，不重复结算。
        /// </summary>
        public void EndLevel(string reason)
        {
            if (levelOver)
            {
                if (string.IsNullOrEmpty(endReason)) endReason = !string.IsNullOrEmpty(reason) ? reason : "关卡已结束";
                return;
            }

            // 走引擎的"主动结束"入口 —— "主动结束不翻倍"这条规则的中文日志在那里，
            // 不在这里再写一遍（写两遍就有两处会走偏）。
            Absorb(engine.EndLevelByChoice(BuildState()));

            levelOver = true;
            endReason = !string.IsNullOrEmpty(reason) ? reason : "玩家主动结束";

            Debug.Log("[V21] 【主动结束关卡】" + endReason + "｜按当前分数 " + score +
                      " 结算（不触发爆刀双倍）");
        }

        // ══════════════════════════════════════════════════════════════
        //  查 / 选
        // ══════════════════════════════════════════════════════════════

        public int HandCount { get { return hand.Count + handSpells.Count; } }

        /// <summary>把某个桌面素材设为启动目标。</summary>
        public void Select(MaterialState st, bool auto)
        {
            selected = st;
            selectedAuto = auto;
            ApplySelectionHighlight();
        }

        /// <summary>自动选中最新上桌的那张（出牌之后用）。</summary>
        public void SelectNewestTableMaterial()
        {
            MaterialState st = FirstLiveTableMaterial();
            Select(st, st != null);
        }

        /// <summary>这张 3D 卡对应哪个桌面素材（不是桌面卡就返回 null）。</summary>
        public MaterialState FindTable(PlayCard view)
        {
            if (view == null) return null;
            for (int i = 0; i < tableCards.Count; i++)
            {
                TableMaterialCard t = tableCards[i];
                if (t != null && t.view == view) return t.state;
            }
            return null;
        }

        /// <summary>这张 3D 卡对应哪张手牌法术（不是法术就返回 null）。</summary>
        public SpellCard FindSpell(PlayCard view)
        {
            if (view == null) return null;

            TableSpellCard t = view.GetComponent<TableSpellCard>();
            return t != null ? t.state : null;
        }

        /// <summary>
        /// 按 3D 卡找回手牌里那张素材（出牌 / 选刀片核心时用）。
        ///
        /// 【优先用绑在卡上的引用】见 PlayCard.bindingMaterial 那段 ——
        /// 靠显示名找曾经踩过"牌还在手里、点出牌没反应"的坑（D 变了名字就变了）。
        /// 名字只作为兜底（比如手工在 Inspector 里造的卡没有绑定）。
        /// </summary>
        public MaterialCard FindHandMaterial(PlayCard view)
        {
            if (view == null) return null;

            if (view.bindingMaterial != null && hand.Contains(view.bindingMaterial)) return view.bindingMaterial;

            for (int i = 0; i < hand.Count; i++)
                if (hand[i] != null && hand[i].DisplayName == view.DisplayName) return hand[i];

            return null;
        }

        /// <summary>
        /// 按名字找回手牌素材（给旧签名用；3D 卡走的是带引用的那个重载）。
        /// 保留它是因为"按名字找"在探针/工具里还读得懂，但**玩法路径不许用它**。
        /// </summary>
        public MaterialCard FindHandMaterial(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return null;

            for (int i = 0; i < hand.Count; i++)
                if (hand[i] != null && hand[i].DisplayName == displayName) return hand[i];
            return null;
        }

        /// <summary>这张 3D 卡是不是桌面上的素材。</summary>
        public bool IsTableCard(PlayCard view)
        {
            return FindTable(view) != null;
        }

        /// <summary>这张 3D 卡是不是手里的法术。</summary>
        public bool IsHandSpellCard(PlayCard view)
        {
            return view != null && view.GetComponent<TableSpellCard>() != null;
        }

        /// <summary>现在能不能启动（第 1 道门：行动机会、刀片、关卡状态）。</summary>
        public bool CanActivate
        {
            get
            {
                if (levelOver) return false;
                if (actionPoints <= 0) return false;
                if (blade == null || blade.H <= 0) return false;
                return true;
            }
        }

        /// <summary>
        /// 不能启动的原因（HUD 直接把这句话显示出来）。
        /// 按钮灰着却不解释，玩家只会以为是 bug —— 所以宁可多写几行。
        /// </summary>
        public string BlockReason
        {
            get
            {
                if (levelOver) return "关卡已结束";
                if (actionPoints <= 0) return "本回合行动机会已用完（" + LevelRun.ActionPointsPerTurn + " 次）→ 结束回合";
                if (blade == null || blade.H <= 0) return "刀片 H 已归零（爆刀）";
                if (selected == null) return "先在桌面上点一张素材当启动目标";
                if (selected.removed || !selected.OnTable) return "选中的素材已经不在桌面上了";
                if (selected.D <= 0) return "选中的素材 D 已耗尽，换一张";
                return "";
            }
        }

        /// <summary>目标素材这一项单独取，HUD 要单独画一行。</summary>
        public string TargetText
        {
            get
            {
                if (selected == null) return "（没选 —— 在桌面上点一张素材）";

                string s = selected.Describe();
                s += selectedAuto ? "　（出牌自动选中，点别的可换）" : "　（手动选中）";
                return s;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  HUD 文本
        // ══════════════════════════════════════════════════════════════

        /// <summary>v2.1 状态区的全部内容（HUD 一行一行画）。</summary>
        public string DescribeHud()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append("模式：").Append(TableSettings.UseRulesV21 ? "v2.1 规则（本分支默认）" : "旧流程").Append('\n');
            sb.Append("回合 ").Append(turnIndex).Append('/').Append(LevelRun.TurnsPerLevel)
              .Append("　　行动机会 ").Append(actionPoints).Append('/').Append(LevelRun.ActionPointsPerTurn)
              .Append("　　总分 ").Append(score).Append(" / ").Append(targetScore).Append('\n');

            sb.Append("刀片：").Append(blade != null ? blade.Describe() : "（无）").Append('\n');
            sb.Append("附魔：").Append(blade != null ? blade.layers.Describe() : "（无）")
              .Append("　　本回合已启动 ").Append(startsThisTurn).Append(" 次").Append('\n');

            sb.Append("目标素材：").Append(TargetText).Append('\n');
            sb.Append("手牌：素材 ").Append(hand.Count).Append(" 张｜法术 ").Append(handSpells.Count)
              .Append(" 张　　桌面素材 ").Append(LiveTableCount()).Append(" 张");

            return sb.ToString();
        }

        /// <summary>桌面还活着的素材张数（removed 的不算）。</summary>
        public int LiveTableCount()
        {
            int n = 0;
            for (int i = 0; i < table.Count; i++)
                if (table[i] != null && !table[i].removed) n++;
            return n;
        }

        /// <summary>手牌摘要（中文名，一行）。</summary>
        public string HandText()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < hand.Count; i++) names.Add(hand[i].DisplayName);
            for (int i = 0; i < handSpells.Count; i++) names.Add(handSpells[i].name + "（法术）");
            return names.Count > 0 ? string.Join("、", names.ToArray()) : "（空）";
        }

        /// <summary>桌面摘要。</summary>
        public string TableText()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < table.Count; i++)
                if (table[i] != null && !table[i].removed) names.Add(table[i].Describe());
            return names.Count > 0 ? string.Join("；", names.ToArray()) : "（空）";
        }

        /// <summary>上次结算日志的尾部（HUD 面板只画这么多行）。</summary>
        public List<string> LastLogTail(int max)
        {
            List<string> tail = new List<string>();
            int from = Mathf.Max(0, lastLog.Count - max);
            for (int i = from; i < lastLog.Count; i++) tail.Add(lastLog[i]);
            return tail;
        }

        /// <summary>
        /// ⚠ 未识别规则条数 —— HUD 上那条醒目提示的内容。
        /// **不可以是"看着像 0 其实就是 0"**：报告没建出来时返回 −1，
        /// HUD 会写成"解析报告没建出来"，而不是假装干净。
        /// </summary>
        public int UnrecognizedCount { get { return report != null ? report.unrecognized.Count : -1; } }

        // ══════════════════════════════════════════════════════════════
        //  3D 表现同步（TableTurnLoop / TableSetup / TableInteraction 调）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 重建整副手牌（素材 + 法术）。
        ///
        /// 【为什么整副重建而不是增量】卡面贴图和文字是造卡时烤死的（见 CardFactory），
        /// 改 data 不会让它们变；而且出牌之后剩下的牌要往中间收拢。
        /// 整副重建 + 复用 TableTurnLoop.HandSlot 的布局算法，是唯一不会和旧流程走偏的做法。
        /// </summary>
        public void RebuildHand()
        {
            if (loop == null || loop.setup == null || loop.setup.hand == null || cardsRoot == null)
            {
                Debug.LogWarning("[V21] RebuildHand 被跳过：loop=" + (loop != null) +
                                 " setup=" + (loop != null && loop.setup != null) +
                                 " cardsRoot=" + (cardsRoot != null));
                return;
            }

            // 旧状态机里的回合号跟着走：**只为了显示口径一致**（HUD 有地方读 turn.turnNumber），
            // 规则一点都不写回旧状态。
            if (loop.turn != null) loop.turn.turnNumber = turnIndex;

            DumpCardViews("RebuildHand 之前");

            TableSetup setup = loop.setup;
            setup.ClearHand();

            List<PlayCard> h = setup.hand;
            int total = hand.Count + handSpells.Count;
            int k = 0;

            Vector3 pos, euler;

            for (int i = 0; i < hand.Count; i++)
            {
                MaterialCard mc = hand[i];
                if (mc == null || mc.card == null) continue;

                TableTurnLoop.HandSlot(k, total, out pos, out euler);
                PlayCard pc = CardFactory.Create(Card.Of(mc.card), cardsRoot, pos, euler);
                if (pc != null)
                {
                    // ★ 引用直接绑在卡上：靠名字找会在 D 变了之后失配（见 PlayCard.bindingMaterial）
                    pc.bindingMaterial = mc;
                    h.Add(pc);
                }
                k++;
            }

            for (int i = 0; i < handSpells.Count; i++)
            {
                SpellCard sc = handSpells[i];
                if (sc == null || sc.spell == null) continue;

                TableTurnLoop.HandSlot(k, total, out pos, out euler);
                PlayCard pc = CardFactory.Create(Card.Of(BuildSpellModule(sc.spell)), cardsRoot, pos, euler);
                if (pc != null)
                {
                    pc.bindingSpell = sc;

                    TableSpellCard tag = pc.gameObject.AddComponent<TableSpellCard>();
                    tag.state = sc;
                    tag.view  = pc;
                    h.Add(pc);
                }
                k++;
            }

            // 收尾和旧流程共用同一条路（清掉空引用 + 重算手牌特写机位）
            loop.LayoutHand();

            // 摆完打一行 —— 出过"手牌数据在、3D 手牌是空的"这种问题，
            // 有这一行就能立刻看出是"没建出来"还是"建完又被清掉了"。
            Debug.Log("[V21] 手牌已摆出：素材 " + hand.Count + " 张｜法术 " + handSpells.Count +
                      " 张 → 3D " + h.Count + " 张（" + HandText() + "）");

            // ④ 残留清扫：手牌重建完还"没人认领"的卡，就是上一轮漏销毁的（见 SweepUnclaimedViews）
            SweepUnclaimedViews("RebuildHand");

            // ⑤ 自检（手牌侧）：3D 手牌张数必须 == 规则侧手牌张数
            VerifyHandSync("RebuildHand");

            DumpCardViews("RebuildHand 之后");
        }

        /// <summary>
        /// 把桌面上还活着的素材摆出来 —— **以规则状态为准的权威同步**。
        /// 每次启动之后都要调 —— 形态变化 / D耗尽 / 献祭 / 溶解都会让素材离场。
        ///
        /// 【不变式（这个方法返回时必须成立）】
        ///   cardsRoot 下"属于桌面的 3D 卡"与 table 里 `OnTable &amp;&amp; !removed` 的素材**一一对应**：
        ///   多出来的销毁、少了的补建、位置重排。
        ///   手牌那半边的不变式在 <see cref="RebuildHand"/> 里，两边各管一半。
        ///
        /// 【为什么必须"扫一遍场景"，不能只信 tableCards】
        ///   用户报过"面板写桌面素材 3 张、画面里画着 4 张"。那第 4 张是**只存在于场景里**的残留：
        ///   它不在 tableCards（所以按表遍历的同步永远看不见它），也不在手牌（ClearHand 也管不到它）。
        ///   只按 tableCards 同步的写法天然看不见这种卡 —— 既不会销毁，也不会报警。
        ///   所以这里第 ④ 步直接扫 cardsRoot 下所有 PlayCard：凡是不在手牌、
        ///   不是刀片卡、也没有 TableMaterialCard 标签的，一律当残留销毁。
        ///
        /// 【为什么不"每帧清一遍重画"】
        ///   那样会打断 ConsumeInto 的飞行动画（牌飞到罐口一半就被删），并且每帧重建贴图会抖。
        ///   这里的调用点是"状态真的变了"的几处（出牌 / 启动 / 结束回合 / 下一回合 / 开一关），
        ///   动画期间一次都不会被调。
        /// </summary>
        public void SyncTableVisuals()
        {
            if (cardsRoot == null) return;

            DumpCardViews("SyncTableVisuals 之前");

            // ① 已经离场的：销毁 3D 卡（规则上它已经不在桌面了，留着会让人以为还能启动）
            for (int i = tableCards.Count - 1; i >= 0; i--)
            {
                TableMaterialCard t = tableCards[i];
                if (t == null) { tableCards.RemoveAt(i); continue; }
                if (t.state == null || t.state.removed || !t.state.OnTable)
                {
                    // ★ 正在飞向罐口的卡**不在这里销毁**：ConsumeInto 已经排好了自己的 Destroy，
                    //   这里再 Destroy 一次会把"被机器吸进去"那一段动画整段砍掉
                    //   （献祭吞噬/溶解看起来就成了"凭空消失"）。让它自己飞完再消失。
                    if (t.view != null && !t.view.IsConsuming)
                        CardFactory.DestroySafe(t.view.gameObject);

                    tableCards.RemoveAt(i);
                }
            }

            // ② 还没有 3D 卡的：建出来
            for (int i = 0; i < table.Count; i++)
            {
                MaterialState st = table[i];
                if (st == null || st.removed || !st.OnTable) continue;
                if (FindTableCard(st) != null) continue;

                // 先占左槽，左槽有人就占右槽；再多的往后错开（见 ③）
                Vector3 at = (tableCards.Count == 0) ? TableMaterialLeft : TableMaterialRight;

                PlayCard pc = CardFactory.Create(Card.Of(st.card), cardsRoot, at, Vector3.zero);
                if (pc == null) continue;

                TableMaterialCard t = pc.gameObject.AddComponent<TableMaterialCard>();
                t.state = st;
                t.view  = pc;
                tableCards.Add(t);            }

            // ③ 位置重排：超过两张的往后错开一点，
            //    否则新卡完全压在旧卡上，玩家看不出"桌面又多了一张"
            for (int i = 0; i < tableCards.Count; i++)
            {
                TableMaterialCard t = tableCards[i];
                if (t == null || t.view == null) continue;

                Vector3 want = (i == 0) ? TableMaterialLeft : TableMaterialRight;
                if (i >= 2) want += new Vector3(0f, 0f, 0.40f * (i - 1));
                t.view.SetHome(want, Vector3.zero);
            }

            // ④ 残留清扫：场景里有、但谁都不认领的卡（第 4 张就是从这里抓出来的）
            SweepUnclaimedViews("SyncTableVisuals");

            // ⑤ 自检：数一遍"状态几张 / 场景几张"，对不上就打警告（回归时一眼看得见）
            VerifyTableSync("SyncTableVisuals");

            ApplySelectionHighlight();

            DumpCardViews("SyncTableVisuals 之后");
        }

        private TableMaterialCard FindTableCard(MaterialState st)
        {
            for (int i = 0; i < tableCards.Count; i++)
                if (tableCards[i] != null && tableCards[i].state == st) return tableCards[i];
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  3D 卡清单 / 自检 / 残留清扫
        //
        //  【为什么要单独一节】"状态改了但画面没跟上"这一类 bug（面板 3 张、画面 4 张、
        //  菜单上杵着上一关的卡）全都长得一样：**状态表里没有它，场景里却有它**。
        //  按 tableCards 遍历的同步看不见这种卡，所以这里改成"以场景为准"数一遍 ——
        //  这个清单既是权威同步的依据，也是数量对不上的报警内容。
        // ══════════════════════════════════════════════════════════════

        /// <summary>cardsRoot 下的 3D 卡分堆结果（<see cref="CollectCardViews"/> 填）。</summary>
        private class CardViews
        {
            /// <summary>手牌（素材 + 法术，含正摆在投放区的那些）。</summary>
            public readonly List<PlayCard> hand = new List<PlayCard>();

            /// <summary>刀片卡 —— 它是这一局的友方单位，单独一份，不是桌面素材。</summary>
            public readonly List<PlayCard> blade = new List<PlayCard>();

            /// <summary>挂着桌面标签的（tableCards 认领的 + 标签还在但状态已离场的）。</summary>
            public readonly List<TableMaterialCard> tagged = new List<TableMaterialCard>();

            /// <summary>正在飞向罐口、到点自毁的（ConsumeInto）—— 不算残留，也不该被销毁。</summary>
            public readonly List<PlayCard> flying = new List<PlayCard>();

            /// <summary>谁都不认领的 —— **残留卡**，必须销毁。</summary>
            public readonly List<PlayCard> unclaimed = new List<PlayCard>();
        }

        /// <summary>
        /// 把 cardsRoot 下所有 PlayCard 过一遍并分堆。
        ///
        /// 【判据是"谁认领它"，不是"它叫什么名字"】
        ///   手牌 ⊂ <see cref="TableSetup.hand"/>（RebuildHand 就是照着它摆的，权威同源）；
        ///   刀片卡 = loop.bladeCard（它不在任何列表里，只能按引用认）；
        ///   桌面卡 = 身上挂着 <see cref="TableMaterialCard"/>；
        ///   剩下三类都不是 = 残留。
        ///   按名字认会出错（卡面名字里带 D，D 一变名字就变，见 PlayCard.bindingMaterial 那段），
        ///   所以这里一个名字判断都没有。
        /// </summary>
        private CardViews CollectCardViews()
        {
            CardViews v = new CardViews();
            if (cardsRoot == null) return v;

            PlayCard[] all = cardsRoot.GetComponentsInChildren<PlayCard>(true);

            for (int i = 0; i < all.Length; i++)
            {
                PlayCard pc = all[i];

                // 已经 Destroy、只是还没到帧末的卡在这里是"假 null"（Unity 的 == 重载认它），
                // 刚被 DestroySafe 盖过章的也一样 —— 两种都必须跳过，
                // 否则每帧都会把"刚销毁、还没到帧末"的卡当成残留再报一次。
                if (pc == null || pc.markedForDestroy) continue;

                if (loop != null && loop.setup != null && loop.setup.hand != null &&
                    loop.setup.hand.Contains(pc))
                {
                    v.hand.Add(pc);
                    continue;
                }

                if (loop != null && loop.bladeCard == pc) { v.blade.Add(pc); continue; }

                if (pc.IsConsuming) { v.flying.Add(pc); continue; }

                TableMaterialCard t = pc.GetComponent<TableMaterialCard>();
                if (t != null) { v.tagged.Add(t); continue; }

                v.unclaimed.Add(pc);
            }

            return v;
        }

        /// <summary>
        /// 扫掉"谁都不认领"的残留 3D 卡。
        ///
        /// 【它抓的是哪一张】"面板写桌面 3 张、画面里画着 4 张"里的第 4 张：
        ///   既不在 setup.hand（ClearHand 销毁不到它）、也没有桌面标签
        ///   （SyncTableVisuals 的第①步也看不见它）—— 只有"扫场景"能抓到它。
        ///   抓到就**销毁并打警告**：这是状态与画面不同步的直接证据，不该静默处理。
        /// </summary>
        private void SweepUnclaimedViews(string where)
        {
            if (cardsRoot == null) return;

            CardViews v = CollectCardViews();

            for (int i = 0; i < v.unclaimed.Count; i++)
            {
                PlayCard pc = v.unclaimed[i];
                if (pc == null) continue;

                Debug.LogWarning("[V21][残留] " + where + " 抓到一张没人认领的 3D 卡：「"
                                 + pc.DisplayName + "」" + ViewPos(pc)
                                 + " → 已销毁（它既不在手牌、也不是刀片卡、也没有桌面标签）");
                CardFactory.DestroySafe(pc.gameObject);
            }
        }

        /// <summary>
        /// 自检（桌面侧）：table 里活着的素材张数必须 == 场景里挂着桌面标签的卡数。
        ///
        /// 【为什么值得常驻】数量对不上是"规则状态与 3D 卡不同步"最直接的信号，
        ///   而这类 bug 只靠眼睛看截图很容易漏（多一张少一张都要数）。
        ///   打的是 LogWarning：探针日志里带 Exception=0 的验收条件下也能一眼看到。
        /// </summary>
        private bool VerifyTableSync(string where)
        {
            if (cardsRoot == null) return true;

            CardViews v = CollectCardViews();

            int want = 0;
            List<string> wantNames = new List<string>();
            for (int i = 0; i < table.Count; i++)
            {
                MaterialState st = table[i];
                if (st == null || st.removed || !st.OnTable) continue;
                want++;
                wantNames.Add(st.name);
            }

            // 同一个 state 被两张卡认领也是不同步（画面会比状态多）
            int dup = 0;
            for (int i = 0; i < v.tagged.Count; i++)
            {
                TableMaterialCard t = v.tagged[i];
                if (t == null || t.state == null) continue;
                for (int j = i + 1; j < v.tagged.Count; j++)
                    if (v.tagged[j] != null && v.tagged[j].state == t.state) dup++;
            }

            bool ok = (v.tagged.Count == want) && (dup == 0) && (v.unclaimed.Count == 0);

            if (!ok)
            {
                Debug.LogWarning("[V21][自检] " + where + " 桌面卡数量对不上："
                                 + "状态里活着 " + want + " 张（" + Join(wantNames) + "）"
                                 + "｜场景里挂着桌面标签 " + v.tagged.Count + " 张"
                                 + (dup > 0 ? "（其中 " + dup + " 张是同一个素材的重复卡）" : "")
                                 + "｜残留 " + v.unclaimed.Count + " 张"
                                 + "｜手牌 view " + v.hand.Count + " 张"
                                 + "｜桌上 view：" + DescribeTagged(v.tagged));
            }

            if (SyncDebug)
            {
                Debug.Log("[V21][自检] " + where + " 状态 " + want + " 张｜桌面标签 " + v.tagged.Count
                          + "｜手牌 " + v.hand.Count + "｜刀片 " + v.blade.Count
                          + "｜飞行中 " + v.flying.Count + "｜残留 " + v.unclaimed.Count);
            }

            return ok;
        }

        /// <summary>
        /// 自检（手牌侧）：摆完手牌之后，setup.hand 的张数必须 == 规则侧手牌张数。
        ///
        /// 【两种错法都要抓】少一张 = "手牌数据在、3D 手牌少一张"；
        ///   多一张 = 有卡没被销毁（ClearHand 漏了它，下一帧就会变成桌上的残留）。
        /// </summary>
        private bool VerifyHandSync(string where)
        {
            if (loop == null || loop.setup == null) return true;

            int want = hand.Count + handSpells.Count;
            int got  = loop.setup.hand != null ? loop.setup.hand.Count : -1;

            // setup.hand 里的空引用（正在销毁的卡）不算
            if (got > 0)
            {
                int nulls = 0;
                for (int i = 0; i < loop.setup.hand.Count; i++)
                    if (loop.setup.hand[i] == null) nulls++;
                got -= nulls;
            }

            if (got == want)
            {
                if (SyncDebug)
                    Debug.Log("[V21][自检] " + where + " 手牌 状态 " + want + " 张｜场景 " + got + " 张 ✓");
                return true;
            }

            Debug.LogWarning("[V21][自检] " + where + " 手牌数量对不上："
                             + "规则侧 " + want + " 张（素材 " + hand.Count + "｜法术 " + handSpells.Count
                             + "：" + HandText() + "）｜3D 手牌 " + got + " 张");
            return false;
        }

        /// <summary>
        /// 给探针 / HUD 用的一句话自检摘要：状态几张、场景几张、手牌几张。
        /// 口径和 <see cref="SyncTableVisuals"/> 末尾那句自检完全一样（同一个 CollectCardViews），
        /// 所以它显示"✓"就说明两边一一对应；显示"★ 不一致"就是抓到了残留。
        /// </summary>
        public string ViewSyncSummary()
        {
            CardViews v = CollectCardViews();

            int want = LiveTableCount();
            bool ok = (v.tagged.Count == want) && (v.unclaimed.Count == 0);

            return (ok ? "✓ 状态与画面一致　" : "★ 状态与画面对不上　")
                 + "桌面状态 " + want + " 张｜桌面 view " + v.tagged.Count + " 张"
                 + "｜手牌 view " + v.hand.Count + " 张（规则侧 " + (hand.Count + handSpells.Count) + " 张）"
                 + "｜残留 " + v.unclaimed.Count + " 张";
        }

        /// <summary>「名字（位置）」—— 日志里认卡用，位置是判断"它是不是飘在桌外"的关键。</summary>
        private static string ViewPos(PlayCard pc)
        {
            if (pc == null) return "";
            Vector3 p = pc.transform.position;
            return "（pos " + p.x.ToString("0.00") + ", " + p.y.ToString("0.00") + ", " + p.z.ToString("0.00") + "）";
        }

        private string DescribeTagged(List<TableMaterialCard> tagged)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < tagged.Count; i++)
            {
                TableMaterialCard t = tagged[i];
                if (t == null || t.view == null) { parts.Add("（空）"); continue; }

                string state = (t.state == null)
                    ? "state=null"
                    : (t.state.removed ? "已离场" : (t.state.OnTable ? "在桌面" : "不在桌面"));

                parts.Add(t.view.DisplayName + "[" + state + "]");
            }
            return parts.Count > 0 ? string.Join("、", parts.ToArray()) : "（无）";
        }

        private static string Join(List<string> list)
        {
            return list.Count > 0 ? string.Join("、", list.ToArray()) : "（空）";
        }

        /// <summary>
        /// 3D 卡清单的**逐张诊断**输出开关：`DSH_V21_SYNC=1` 打开（默认关）。
        ///
        /// 【为什么不直接删掉这些诊断】下次再出"画面里多一张卡"时，
        ///   打开这个开关跑一遍，日志里就能看出是哪一步开始多出来的 ——
        ///   比重新加一遍打印快得多，而且它默认不出声，不会污染正常日志。
        /// </summary>
        private static readonly bool SyncDebug =
            System.Environment.GetEnvironmentVariable("DSH_V21_SYNC") == "1";

        /// <summary>把当前所有 3D 卡逐张打进日志（只在 <see cref="SyncDebug"/> 打开时出声）。</summary>
        private void DumpCardViews(string where)
        {
            if (!SyncDebug || cardsRoot == null) return;

            CardViews v = CollectCardViews();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[V21][清单] ").Append(where)
              .Append("　状态：桌面 ").Append(LiveTableCount()).Append(" 张｜手牌 ").Append(hand.Count)
              .Append(" 素材 + ").Append(handSpells.Count).Append(" 法术")
              .Append("｜tableCards ").Append(tableCards.Count).Append(" 条");

            sb.Append("\n　手牌 view ").Append(v.hand.Count).Append(" 张：");
            for (int i = 0; i < v.hand.Count; i++) sb.Append('「').Append(v.hand[i].DisplayName).Append('」').Append(ViewPos(v.hand[i]));

            sb.Append("\n　桌面标签 ").Append(v.tagged.Count).Append(" 张：").Append(DescribeTagged(v.tagged));
            for (int i = 0; i < v.tagged.Count; i++)
                if (v.tagged[i] != null && v.tagged[i].view != null) sb.Append(ViewPos(v.tagged[i].view));

            sb.Append("\n　刀片 ").Append(v.blade.Count).Append(" 张｜飞行中 ").Append(v.flying.Count)
              .Append(" 张｜**残留 ").Append(v.unclaimed.Count).Append(" 张**：");
            for (int i = 0; i < v.unclaimed.Count; i++) sb.Append('「').Append(v.unclaimed[i].DisplayName).Append('」').Append(ViewPos(v.unclaimed[i]));

            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 把"谁是启动目标"画出来。
        ///
        /// 【为什么借 PlayCard.SetHover 而不是新加一个高亮接口】
        ///   卡牌已经有"悬停 → 抬起来 + 变亮"这一套，而且它和拖动变亮共用同一个乘法器
        ///   （见 PlayCard.UpdateTint），两者不会互相覆盖。再写一套高亮要同时改 PlayCard
        ///   和 TableInteraction 的每帧刷新 —— 为这点表现动两个文件的输入逻辑不划算。
        ///
        /// 【为什么必须每帧重设】
        ///   TableInteraction 每帧都会按鼠标位置刷新悬停，鼠标一离开目标卡
        ///   Hovered 就变成 null 或别的卡，目标的高亮会跟着掉。所以这个方法是
        ///   "把目标重新压回去"，由 TableInteraction 每帧调（见它的 Update）。
        ///   顺序上放在悬停刷新**之后**，鼠标悬停才盖得住目标高亮。
        /// </summary>
        public void ApplySelectionHighlight()
        {
            for (int i = 0; i < tableCards.Count; i++)
            {
                TableMaterialCard t = tableCards[i];
                if (t == null || t.view == null) continue;
                t.view.SetHover(selected != null && t.state == selected);
            }

            highlightedTarget = selected;
        }

        /// <summary>
        /// 这张卡是不是"被选中当启动目标"的那张。
        /// TableInteraction 靠它决定"鼠标移开时要不要把手型高亮撤掉" ——
        /// 撤的话会把目标高亮一起撤掉，那张卡就不像是被选中的了。
        /// </summary>
        public bool IsPreviewTarget(PlayCard view)
        {
            if (view == null || highlightedTarget == null) return false;

            TableMaterialCard t = FindTableCard(highlightedTarget);
            return t != null && t.view == view;
        }

        private MaterialState highlightedTarget;

        /// <summary>罐子液面：总分 / 目标分。</summary>
        public void SyncJuicer()
        {
            if (juicer == null) return;
            juicer.SetScore(score, Mathf.Max(1, targetScore));
        }

        /// <summary>启动的 3D 反馈：冲压 + 罐子液面。</summary>
        public void PlayActivateFeedback()
        {
            if (juicer != null) juicer.PlayStamp();
            SyncJuicer();
        }

        /// <summary>留在桌面上的素材卡被点中了（TableInteraction 转过来）。</summary>
        public void OnTableCardClicked(PlayCard view)
        {
            MaterialState st = FindTable(view);
            if (st == null || st.removed) return;

            Select(st, false);
            LoopNotice("启动目标：" + st.name + "（D " + st.D + "/" + st.fullD + "）");
        }

        /// <summary>
        /// 手牌里的法术被点中了：直接打出（正文 §2.1：打出无代价、不消耗行动机会）。
        /// 返回 true 表示这张卡确实是法术、已经被这次点击消费掉。
        /// </summary>
        public bool OnSpellCardClicked(PlayCard view)
        {
            SpellCard sc = FindSpell(view);
            if (sc == null) return false;

            if (levelOver) { LoopNotice("关卡已结束，法术打不出去"); return true; }

            TurnResult r = PlaySpell(sc);
            if (r == null) return true;

            // 表现：飞进罐口再销毁（PlayCard.ConsumeInto 自己会在寿命到了之后自毁）
            if (view != null) view.ConsumeInto(JuicerMouth(view), 0.45f);

            RebuildHand();
            PlayActivateFeedback();
            LoopNotice("法术「" + sc.name + "」" + (r.rejected ? "打不出去" : "已生效（不消耗行动机会）"));
            return true;
        }

        private Vector3 JuicerMouth(PlayCard view)
        {
            if (juicer != null) return juicer.MouthWorld;
            return view != null ? view.transform.position + Vector3.up * 0.4f : Vector3.zero;
        }

        private void LoopNotice(string msg)
        {
            if (loop != null) loop.notice = msg;
        }

        // ══════════════════════════════════════════════════════════════
        //  引擎状态的灌入 / 读回
        //
        //  【方向】每次调用规则之前灌进去（BuildState），调用完读回来（Absorb）。
        //  为什么不干脆一直抱着同一个 LevelState 对象？
        //    因为引擎的 LevelState 里还有 hand（List<string>）、blankCount 这些桌面用不上的东西，
        //    让它直接当"桌面的状态"会变成两边都想改它、谁是真身说不清。
        //    现在的方向是单向的：**桌面状态是权威**，引擎那个只是每次调用的参数包。
        // ══════════════════════════════════════════════════════════════

        private LevelRun BuildState()
        {
            LevelRun st = new LevelRun();
            st.blade          = blade;
            st.table          = table;
            st.score          = score;
            st.turnIndex      = turnIndex;
            st.actionPoints   = actionPoints;
            st.startsThisTurn = startsThisTurn;
            st.levelOver      = levelOver;
            st.bursted        = bursted;
            return st;
        }

        /// <summary>把引擎改完的状态读回来，并把日志 / 警告 / 产出收进 HUD 用的字段。</summary>
        private void Absorb(TurnResult r)
        {
            if (r == null) return;

            // blade / table 是引用，引擎改的就是我们这一个；blade 也可能被换掉（BeginLevel）
            if (r.blade != null) blade = r.blade;

            if (r.state != null)
            {
                score          = r.state.score;
                turnIndex      = r.state.turnIndex;
                actionPoints   = r.state.actionPoints;
                startsThisTurn = r.state.startsThisTurn;
                levelOver      = r.state.levelOver;
                bursted        = r.state.bursted;
            }

            lastLog.Clear();
            for (int i = 0; i < r.log.Count; i++) lastLog.Add(r.log[i]);
            lastSummary = r.summary;

            for (int i = 0; i < r.warnings.Count; i++)
                if (!warnings.Contains(r.warnings[i])) warnings.Add(r.warnings[i]);

            // 引擎的产出直接进手牌（正文 §四.2 / 七-1）
            SyncHandFromEngine(r);

            // 旧状态机里的分数也跟一下：**纯粹是为了显示口径一致**
            // （切回旧模式时不会看到一个对不上的分数）。规则一点都不写回旧状态。
            if (loop != null && loop.turn != null) loop.turn.score = score;
        }

        /// <summary>
        /// 把引擎产出的"中文卡名"翻回手牌对象。
        /// 引擎只会给名字 —— 它不认识卡表之外的任何东西，这是 Rules/ 不依赖运行时的代价，
        /// 也是它的优点：整条规则链能离线跑。
        /// </summary>
        private void SyncHandFromEngine(TurnResult r)
        {
            if (r == null || r.produced == null) return;

            for (int i = 0; i < r.produced.Count; i++)
            {
                ProducedCard pc = r.produced[i];
                if (pc == null || string.IsNullOrEmpty(pc.name)) continue;

                for (int c = 0; c < pc.count; c++) AddProducedToHand(pc.name);
            }
        }

        private void AddProducedToHand(string name)
        {
            // 空白卡：所有数值为 0、无特性、可被吞噬（正文 §2.1）。
            // 卡表里没有它，所以现造一张 —— 用 Ingredient 而不是 Spell，
            // 因为它要能"放在桌面上、能被启动、能被吞噬"。
            if (name == "空白卡")
            {
                Ingredient blank = new Ingredient("blank", "空白卡", new AttrSet());
                blank.form        = "固体";
                blank.tags        = new string[0];
                blank.transitions = new FormChange[0];
                blank.exhaust     = new string[0];

                MaterialCard bc = new MaterialCard();
                bc.card = blank;
                bc.H = 0; bc.D = 0; bc.V = 0;
                hand.Add(bc);

                Debug.Log("[V21] 空白卡进手牌（数值全 0，可被吞噬但不提供 H/V）—— 当前空白卡 " + blankCount + " 张");
                return;
            }

            Ingredient mat = CardSpecs.MaterialByName(name);
            if (mat != null)
            {
                Ingredient copy = mat.Clone();
                MaterialCard.ValueSource src = MaterialCard.ValueSource.CardTable;
                ApplyValues(copy, src);

                MaterialCard mc = new MaterialCard();
                mc.card = copy;
                mc.H = copy.h;
                mc.D = copy.d > 0 ? copy.d : DefaultMaterialD;
                mc.V = copy.v;
                mc.source = src;
                hand.Add(mc);
                return;
            }

            Spell sp = CardSpecs.SpellByName(name);
            if (sp != null)
            {
                handSpells.Add(new SpellCard { spell = CloneSpell(sp) });
                return;
            }

            // 卡表里两边都找不到 —— 数据问题，必须留痕，不许静默丢掉
            string line = "产出「" + name + "」在卡表里找不到（既不是素材也不是法术），已丢弃";
            if (!warnings.Contains(line)) warnings.Add(line);
            Debug.LogWarning("[V21] " + line);
        }

        /// <summary>启动之后把 3D 上的东西对齐到规则状态。</summary>
        private void SyncVisualsAfterActivate()
        {
            // ① 已经离场的素材：先播一段"被吸进机器"再销毁。
            //    走 ConsumeInto 的卡会在这段动画里自毁，SyncTableVisuals 之后不会重复销毁。
            for (int i = 0; i < table.Count; i++)
            {
                MaterialState st = table[i];
                if (st == null || !st.removed) continue;

                TableMaterialCard tc = FindTableCard(st);
                if (tc != null && tc.view != null && !tc.view.IsConsuming)
                    tc.view.ConsumeInto(JuicerMouth(tc.view), 0.55f);
            }

            // ② 手牌可能变了（形态变化的新卡、D耗尽的产物）
            RebuildHand();

            // ③ 目标素材若已离场 → 自动换一张还活着的
            //    （省得玩家每次启动都要重新点；HUD 会写明这次是自动选的）
            if (selected == null || selected.removed || !selected.OnTable || selected.D <= 0)
            {
                MaterialState next = FirstLiveTableMaterial();
                Select(next, next != null);
            }

            // ④ 再对齐桌面（这一步会把已经进入 ConsumeInto 的卡留着，直到它们自毁）
            SyncTableVisuals();
            PlayActivateFeedback();
        }

        private MaterialState FirstLiveTableMaterial()
        {
            for (int i = 0; i < table.Count; i++)
                if (table[i] != null && !table[i].removed && table[i].OnTable) return table[i];
            return null;
        }

        /// <summary>
        /// 清掉桌面（重开一关 / 退关卡 / 回开场时用）。
        ///
        /// 【为什么末尾还要扫一遍残留】菜单类阶段是"桌面上不该有牌"的时刻：
        ///   表里认领的那几张这张方法已经销毁了，但**没被任何列表认领**的残留
        ///   （用户第二次截图里那张杵在牌组选择界面上的卡）只有扫场景才抓得到。
        ///   这里销毁的是"残留"，手牌与刀片卡不归它管（刀片卡由 KillBladeCard 负责）。
        /// </summary>
        public void ClearTable()
        {
            for (int i = 0; i < tableCards.Count; i++)
                if (tableCards[i] != null && tableCards[i].view != null)
                    CardFactory.DestroySafe(tableCards[i].view.gameObject);

            tableCards.Clear();
            table.Clear();
            selected = null;
            selectedAuto = false;

            SweepUnclaimedViews("ClearTable");
        }

        // ══════════════════════════════════════════════════════════════
        //  卡表桥接
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 引擎的卡表查询 —— 按**中文名**查（规则文本里写的都是中文名）。
        ///
        /// 【为什么名字也能当键】
        ///   RuleText 解析出来的产物名就是卡表的 name，而 CardSpecs 是按 id 存模板的。
        ///   这里建一份 name → 模板的索引。重名会让后一张覆盖前一张 ——
        ///   卡表目前没有重名（31 素材 + 8 法术），**但新增卡牌时要注意这一点**。
        /// </summary>
        private class V21CardLookup : ICardLookup
        {
            private readonly Dictionary<string, Ingredient> byName = new Dictionary<string, Ingredient>();
            private readonly Dictionary<string, string> enchantByName = new Dictionary<string, string>();
            private readonly List<string> spellNames = new List<string>();

            public V21CardLookup()
            {
                List<Ingredient> mats = CardSpecs.Materials();
                for (int i = 0; i < mats.Count; i++)
                {
                    Ingredient m = mats[i];
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    byName[m.name] = m;
                }

                List<Spell> sps = CardSpecs.Spells();
                for (int i = 0; i < sps.Count; i++)
                {
                    Spell s = sps[i];
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    enchantByName[s.name] = s.enchant != null ? s.enchant : "";
                    spellNames.Add(s.name);
                }
            }

            public Ingredient Material(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                Ingredient v;
                return byName.TryGetValue(name, out v) ? v : null;
            }

            public bool IsSpell(string name)
            {
                return !string.IsNullOrEmpty(name) && enchantByName.ContainsKey(name);
            }

            public List<string> SpellNames() { return spellNames; }

            public string EnchantOf(string spellName)
            {
                if (string.IsNullOrEmpty(spellName)) return "";
                string v;
                return enchantByName.TryGetValue(spellName, out v) ? v : "";
            }

            public CardValues ValuesOf(string cardName)
            {
                // ★ 走 Ingredient.h / .v（v2.1 的 H/V 口径），**不读 attrs** ——
                //   attrs 里的盐性/汞性/硫性是旧玩法的三属性，和 v2.1 的 H/D/V 是两套口径，
                //   混用会出现"卡面写着 H 4、结算按 0 算"这种查半天的错。
                Ingredient m;
                if (!string.IsNullOrEmpty(cardName) && byName.TryGetValue(cardName, out m) && m != null)
                    return new CardValues(m.h, m.d > 0 ? m.d : DefaultMaterialD, m.v, m.vGrade);

                return new CardValues(0, DefaultMaterialD, 0);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  卡面数值（卡表为准；卡表没给才用占位）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 素材的 H/D/V 兜底。**卡表里有值就一个都不动**。
        ///
        /// 【两级来源 + 一级占位，顺序不能乱】
        ///   ① `Ingredient.h / d / v` —— v2.1 卡表（cards_v21.json）的数值。**优先**。
        ///   ② `Ingredient.attrs`（盐性/汞性/硫性）—— 旧 game_config.json 的 h/d/v 列。
        ///
        ///      【为什么要有这一级】这是实跑踩出来的：**牌组里的卡（秘银锭 / 卤水 / 明矾 / 醋酸）
        ///      不在 cards_v21.json 里**，它们只有旧 game_config 那套数值。
        ///      早期版本直接跳过 attrs 去用占位表，于是这些卡全变成 H=1 ——
        ///      刀片 H 只有 1，玩家一进关卡启动一次就爆刀，4 回合的关卡根本走不完。
        ///      旧配置里明明写着秘银锭 H=16，没理由不用。
        ///   ③ 占位表（和离线探针 FixtureValues 同口径）—— 前两级都没有才用，
        ///      并且会把卡名记进 placeholderCards，HUD 上明说"这是占位值"。
        ///
        /// 【两套数值口径，别混用】—— 这条踩过就再也不会忘
        ///   `h/d/v` 是 v2.1 的 H/D/V（正文 §2.2）；`attrs` 是旧玩法的三属性。
        ///   两者**名字相同、含义不同**（旧配置恰好把 H/D/V 也存在 attrs 里，
        ///   所以这里能当第二级来源用；但反向不成立 —— attrs 里的"盐性"在 v2.1 里不是 H 的全部含义）。
        ///   规则一律读 h/d/v；attrs 那份另外还会被同步过去当**卡面显示**用（卡面版式读 attrs）。
        /// </summary>
        public void ApplyValues(Ingredient ing, MaterialCard.ValueSource source)
        {
            if (ing == null) return;

            bool fromCardTable = (ing.h != 0 || ing.v != 0 || ing.d != 0);

            if (!fromCardTable && ing.attrs != null)
            {
                int ah = ing.attrs.Get(AttrId.Salt);
                int ad = ing.attrs.Get(AttrId.Mercury);
                int av = ing.attrs.Get(AttrId.Sulfur);

                if (ah != 0 || ad != 0 || av != 0)
                {
                    ing.h = ah;
                    ing.d = ad > 0 ? ad : DefaultMaterialD;
                    ing.v = av;
                    if (string.IsNullOrEmpty(ing.vGrade)) ing.vGrade = "（旧配置）";
                }
            }

            // 还是全 0 → 这张卡两套配置里都没有数值。空白卡按正文就是三个 0，不参与占位。
            bool stillEmpty = (ing.h == 0 && ing.v == 0 && ing.d == 0);
            if (stillEmpty && TableSettings.UsePlaceholderCardValues && ing.name != "空白卡")
            {
                CardValues cv = FixtureValues(ing.name);
                ing.h = cv.H > 0 ? cv.H : PlaceholderMinH;
                ing.d = cv.D > 0 ? cv.D : DefaultMaterialD;
                ing.v = cv.V;

                // ★ 刀片 H = 核心卡的 H。没有这一步，任何一张"两套配置都没有数值"的卡
                //   当核心都会让刀片 H=1 —— 一进关卡启动一次就爆刀，4 回合根本走不完。
                if (ing.h < PlaceholderMinH) ing.h = PlaceholderMinH;

                ing.vGrade = cv.vGrade + "（占位）";
                source = MaterialCard.ValueSource.Placeholder;

                placeholderUsed = true;
                if (!placeholderCards.Contains(ing.name)) placeholderCards.Add(ing.name);
            }

            // 把 H/D/V 同步一份到旧 attrs —— **只为了卡面能显示**。
            // CardFactory 的属性版式读的是 attrs（沿用旧玩法那套），不同步的话
            // 桌上每张卡的属性区都是 "H 0 / D 0 / V 0"，看起来像数据没加载。
            if (ing.attrs == null) ing.attrs = new AttrSet();
            ing.attrs.Set(AttrId.Salt,    ing.h);
            ing.attrs.Set(AttrId.Mercury, ing.d);
            ing.attrs.Set(AttrId.Sulfur,  ing.v);
        }

        /// <summary>
        /// 占位数值下**素材 H 的下限**（正文没给数值时的临时口径）。
        ///
        /// 【为什么需要它】刀片 H = 核心卡的 H。某张卡两套配置都没给数值时 H=0，
        /// 拿它当核心等于"一进关卡就爆刀"。给一个下限，至少让 4 回合的关卡能跑完、
        /// 让规则链能被验证。这不是平衡性数字，是"能开局"的下限。
        /// </summary>
        public const int PlaceholderMinH = 12;

        /// <summary>本局有没有用过占位数值（HUD 据此提示"数值是占位的"）。</summary>
        public static bool placeholderUsed;

        /// <summary>这一关里哪些卡用了占位数值（HUD 要把名字列出来）。</summary>
        public readonly List<string> placeholderCards = new List<string>();

        /// <summary>占位数值提示（没有就返回空串）。</summary>
        public string PlaceholderWarning()
        {
            if (placeholderCards.Count == 0) return "";
            return "⚠ " + placeholderCards.Count + " 张卡用的是占位数值（两套配置都没给数值）："
                 + string.Join("、", placeholderCards.ToArray());
        }

        /// <summary>临时数值表（和离线探针 FixtureValues 同口径）。卡表补了 h/d/v 之后整段删掉。</summary>
        private static CardValues FixtureValues(string name)
        {
            switch (name)
            {
                case "白磷":   return new CardValues(3, 3, CardValues.VGradeValue("中"), "中");
                case "水":     return new CardValues(2, 3, CardValues.VGradeValue("低"), "低");
                case "冰":     return new CardValues(5, 2, CardValues.VGradeValue("中"), "中");
                case "水蒸气": return new CardValues(1, 2, CardValues.VGradeValue("低"), "低");
                case "盐":     return new CardValues(2, 3, CardValues.VGradeValue("低"), "低");
                case "玻璃":   return new CardValues(5, 3, CardValues.VGradeValue("低"), "低");
                case "硫磺粉": return new CardValues(2, 2, CardValues.VGradeValue("低"), "低");
                case "黄金":   return new CardValues(5, 3, CardValues.VGradeValue("高"), "高");
                case "铜":     return new CardValues(4, 3, CardValues.VGradeValue("低"), "低");
                case "铁":     return new CardValues(6, 3, CardValues.VGradeValue("中"), "中");
                case "空白卡": return new CardValues(0, 0, 0, "极低");
            }
            return new CardValues(1, DefaultMaterialD, CardValues.VGradeValue("极低"), "极低");
        }

        // ══════════════════════════════════════════════════════════════
        //  法术 → 3D 卡
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 把一张 v2.1 法术包成 3D 卡能认的 <see cref="SpeedModule"/>，卡面副标题写需求原文。
        ///
        /// 【为什么走 SpeedModule 这条老路】—— 这是一处**已知的表现层将就**，不是设计
        ///   Card 只有"素材 / 模块"两种 kind，CardFactory / PlayCard / TableInteraction
        ///   全都按这两种写好了（可拖、可悬停、可检视、有卡面）。
        ///   加第三种 kind 要改 Card 的 switch、CardFactory 的版面、以及每一处 IsModule 判断
        ///   —— 那是"动 20 个文件"的改动，而 v2.1 模式下法术**根本不走**旧流程
        ///   （不投杯子、不并效果），只需要"能被看见、能被点"。
        ///   所以先用现成的容器装上，玩法完全由 TableRulesV21 接管。
        ///   代价是卡面副标题前缀写着"【变速模块】"，而它其实是法术 —— 见整合文档"还没接的部分"。
        ///
        /// 【需求原文怎么塞进卡面】
        ///   CardFactory 只从 SpeedModule 取两个字符串：name → 卡面标题，
        ///   Description() → 卡面副标题。而 Description() 走 EffectGroup.Describe()，
        ///   空效果组合只会得到"（无效果）"。
        ///   所以这里把"名字 + 换行 + 需求原文"一起放进 SpeedModule.name ——
        ///   卡面标题是多行 TextMesh，换行能显示出来。
        ///   **不改 Card 的 kind、不改 CardFactory 的版面**，两边都不用动。
        /// </summary>
        public static SpeedModule BuildSpellModule(Spell sp)
        {
            string id   = sp != null ? sp.id : "";
            string name = sp != null ? sp.name : "?";

            string text = "";
            if (sp != null)
            {
                if (!string.IsNullOrEmpty(sp.requirement)) text = sp.requirement;
                else if (!string.IsNullOrEmpty(sp.enchant)) text = "附魔：" + sp.enchant + "（可叠加）";
            }

            string title = string.IsNullOrEmpty(text) ? name : name + "\n" + text;
            return new SpeedModule(id, title, new EffectGroup(name));
        }

        // ══════════════════════════════════════════════════════════════
        //  解析报告
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 把整份卡表过一遍，建出解析报告（未识别清单 / 规则表接不上 / 占位数值 ……）。
        /// HUD 靠它报"有几条规则没实现"，F2 面板靠它列全文。
        ///
        /// 【为什么用卡表模板而不是克隆】报告只看文本，不改数据。
        /// </summary>
        private void BuildReport()
        {
            try
            {
                List<SpellSpec> specs = new List<SpellSpec>();
                List<Spell> sps = CardSpecs.Spells();
                for (int i = 0; i < sps.Count; i++)
                {
                    Spell s = sps[i];
                    if (s == null) continue;
                    specs.Add(new SpellSpec(s.id, s.name, s.enchant, s.category, s.requirement));
                }

                report = RuleReport.BuildAll(CardSpecs.Materials(), specs, new V21CardLookup());

                Debug.Log("[V21] 卡表解析报告：" + report.SummaryLine());
                if (report.unrecognized.Count > 0)
                    Debug.LogWarning("[V21] ⚠ 有 " + report.unrecognized.Count +
                                     " 条规则文本没被解析 → 这些规则**不会生效**（HUD 有提示，F2 看全文）");
            }
            catch (System.Exception e)
            {
                // 报告建不出来绝不能拖垮玩法 —— 但也不能装作"报告是干净的"
                report = null;
                Debug.LogWarning("[V21] 卡表解析报告建失败：" + e.Message);
            }
        }
    }
}
