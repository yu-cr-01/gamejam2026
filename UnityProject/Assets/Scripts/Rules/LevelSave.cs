using System;
using System.Collections.Generic;
using System.Globalization;
using GameJam.Data;

namespace GameJam.Rules
{
    // ══════════════════════════════════════════════════════════════════
    //  存档的数据结构（DTO）
    //
    //  【为什么 DTO 放在 Rules 这一层，而不是 Table】
    //    ① 离线探针（Tools/RuleProbe）只编 `Assets/Scripts/Rules/**` + 几个 Data 文件 ——
    //       "存 → 读 → 逐字段比对"这组断言要落在离线，DTO 与映射就必须在这一层；
    //    ② 这一层不认识 MonoBehaviour / MaterialCard / PlayCard，于是"存档里到底记了什么"
    //       与"3D 上摆了哪些卡"天然分开：前者是规则状态，后者是表现（由 Table 那一层负责重建）。
    //
    //  【它是"状态快照"，不是我另开的一份状态】
    //    采集（Capture）与落地（TryBuild）都只是搬运：规则语义、结算顺序、阈值一个字节都没动。
    //    Table 那一边拿到 BuiltState 之后仍然是**一次性覆盖**现有字段（见 TableRulesV21.CommitSaveState），
    //    不存在"两套状态并存"的局面。
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 刀片上**一类**附魔的两份计数。
    ///
    /// 【为什么必须分两份存，不能只存总数（这是最容易漏的一条）】
    ///   LayerLedger 里每类层数本来就是 decaying / permanent 两份：
    ///   回合结束只掉 decaying、而"消耗所有 X 层数"两份都吃。
    ///   只存总数的话，读回来只能二选一：
    ///     · 全塞进 decaying → 玩家好不容易拿到的"不衰退"层下次回合结束就掉了；
    ///     · 全塞进 permanent → 该衰退的层永远不掉，"每回合 −1"这条规则当场失效。
    ///   两种都错，而且错得很隐蔽（分数面板上看不出差别，只有跨回合才现形）。
    /// </summary>
    [Serializable]
    public class SaveLayer
    {
        /// <summary>层数类别的中文名（热 / 冷 / 酸 / 催化）—— 存档是给人看的，不写枚举数字。</summary>
        public string kind = "";

        /// <summary>会随回合衰退的层数</summary>
        public int decaying;

        /// <summary>不衰退的层数</summary>
        public int permanent;
    }

    /// <summary>
    /// 刀片被动的一条（吞噬带「献祭」标签的卡时登记，例如玻璃的"刀片每次启动，其他素材D-2"）。
    ///
    /// 【为什么连原文一起存】被动的本体是一个 RuleClause（解析结果），它是**从卡表的献祭文本
    ///   解析出来的**。只存"哪张卡"的话，读档时得再去卡表里找那张卡、再解析一遍 ——
    ///   卡表改了（策划改了文案）就会读回一条**不一样的**被动，而玩家只是"读了个档"。
    ///   所以原文 text 一起存：读档时就地重新解析这段文本，恢复的是**当时那条规则**。
    /// </summary>
    [Serializable]
    public class SaveBladePassive
    {
        /// <summary>来源卡的 id（日志用）</summary>
        public string cardId = "";

        /// <summary>来源卡的名字（日志用）</summary>
        public string cardName = "";

        /// <summary>来源卡「献祭」字段的原文</summary>
        public string text = "";

        /// <summary>是哪一句（同一张卡可能有多句）</summary>
        public string sentence = "";
    }

    /// <summary>刀片（核心卡 id / H / V / 四种层数 / 被动列表）。</summary>
    [Serializable]
    public class SaveBlade
    {
        public string cardId = "";
        public string name = "";
        public int H;
        public int V;

        /// <summary>四种层数，一类一条（即使全 0 也写出来 —— 缺项要能报错，见 LevelSaveJson.Read）</summary>
        public List<SaveLayer> layers = new List<SaveLayer>();

        public List<SaveBladePassive> passives = new List<SaveBladePassive>();
    }

    /// <summary>
    /// 桌面上的一张素材。
    ///
    /// 【card 存的是"当前形态对应的卡 id"，不是当初打出去的那张】
    ///   形态变化会换掉 MaterialState.card，所以只有"现在的卡 id"才能还原现场。
    /// </summary>
    [Serializable]
    public class SaveMaterial
    {
        public string cardId = "";
        public string cardName = "";

        public int H;
        public int D;

        /// <summary>D 满值（"形态变化后 D 重置为满"要用它）</summary>
        public int fullD;

        public int V;

        /// <summary>已经离场（形态变化 / 溶解 / D 耗尽 / 被吞噬）</summary>
        public bool removed;

        /// <summary>旧兼容字段</summary>
        public bool consumed;

        /// <summary>
        /// **本回合是否已经被启动过** —— "还能不能拖回手牌"就看它。
        ///
        /// 【漏了它会怎样】收回手牌的限制条件是"只在本回合、且还没启动过时能收"，
        ///   而这份名单在规则侧是 startedThisTurnList（按回合清空）。
        ///   不存它的话，读档后"启动过的素材"又能被收回去 —— 玩家白赚一次撤回，
        ///   或者反过来（取决于读档后清没清），两种都和存档前的局面不一致。
        /// </summary>
        public bool startedThisTurn;

        /// <summary>
        /// **卡自己的** H/D/V（Ingredient.h/d/v）—— 卡面属性区和元素皮肤读的是它。
        ///
        /// 【为什么必须单独存这三个数，而不能按 id 再查一次卡表】
        ///   这三个数在原局里是怎么来的，取决于这张卡**从哪条路进来**：
        ///     · 牌组带来的卡：GameConfig 那一步是"从卡表字段重建、丢掉旧图鉴的 attrs"，
        ///       于是数值可能是全 0 → 再由 ApplyValues 补一套占位值；
        ///     · 从卡表补进来的卡：直接取卡表的 h/d/v。
        ///   按 id 再查一次卡表**不保证**得到同一组数。实测反例就是硝石：
        ///   原局里是占位 12/3/1（元素皮肤 = 冰晶），按 id 查回来是旧图鉴的 3/4/8
        ///   （元素皮肤变成气团）—— 卡面插画和三个数全对不上，而规则状态其实是对的。
        ///   存档把它一起存，读档按它覆盖，卡面才和存档前逐字一致。
        /// </summary>
        public int cardH;
        public int cardD;
        public int cardV;

        /// <summary>出牌那一刻的数值来源（0=卡表 1=旧配置 2=占位），见 MaterialCard.ValueSource。</summary>
        public int valueSource;
    }

    /// <summary>手牌里的一张（素材 / 法术共用这个壳，用 spell 区分）。</summary>
    [Serializable]
    public class SaveHandCard
    {
        public string cardId = "";
        public string cardName = "";

        /// <summary>素材：手里那份 H/D/V（出牌时会变成 MaterialState 的初值）。法术：留 0。</summary>
        public int H;
        public int D;
        public int V;

        /// <summary>卡自己的 h/d/v（卡面读它，见 SaveMaterial.cardH 的说明）。法术：留 0。</summary>
        public int cardH;
        public int cardD;
        public int cardV;

        /// <summary>数值来源（0=卡表 1=旧配置 2=占位）</summary>
        public int valueSource;

        /// <summary>这张是法术（v2.1 的法术是另一套对象，读回来要放回 handSpells）</summary>
        public bool spell;
    }

    /// <summary>
    /// 投放区里"待放置"的那一张。
    ///
    /// 【v2.1 里它常态为空】落槽即结算（把牌拖进槽 = 当场生效），所以正常玩到不了这个状态。
    ///   留着它是因为**旧流程的摆法**（先摆好、再按确认）和探针都会把牌停在投放区 ——
    ///   那时"待放置的是哪几张"必须跟着存档走，否则读档后 self-check 会报"暂存张数对不上"，
    ///   而那是假警（自检误报比不检更坏）。
    /// </summary>
    [Serializable]
    public class SaveStaged
    {
        /// <summary>true = 这张在手牌法术里，false = 在手牌素材里</summary>
        public bool spell;

        /// <summary>在 handMaterials / handSpells 里的下标</summary>
        public int index;

        /// <summary>它占着的卡槽下标（-1 = 没占槽）</summary>
        public int slot = -1;
    }

    /// <summary>
    /// **一关的完整状态**（存档里真正要比对的那一份）。
    ///
    /// 【比对为什么以它为单位】存档前 / 读档后各采集一次，两份 LevelSaveData 逐字段比 ——
    ///   比对的口径 = 存档写出去的口径，所以"少存了一个字段"会同时表现为
    ///   "读档后状态不对"和"CompareStates 报差异"，不会静默漏过去。
    /// </summary>
    [Serializable]
    public class LevelSaveData
    {
        // ── 关卡 / 回合 / 分数 ──
        public int turnIndex;
        public int actionPoints;
        public int score;
        public int targetScore;

        /// <summary>本回合已经启动过几次</summary>
        public int startsThisTurn;

        /// <summary>空白卡计数（正文 §2.1：需单独统计）</summary>
        public int blankCount;

        /// <summary>
        /// 本回合**最后登记的那个待献祭目标**在 table 里的下标（-1 = 没有 / 目标已离场）。
        ///
        /// 【不存它会怎样】"回合真正结束时才吞噬"意味着这个待办事项会**跨过一次存档**存在：
        ///   玩家启动一次（登记了目标）→ 存档 → 读档 → 点「结束回合」。
        ///   不存下标的话，读档后引擎手里没有待献祭目标，那一次吞噬就静默丢了 ——
        ///   表现和"漏吞"一模一样，而且只在这条路径上出现（比一直不吞更难查）。
        ///
        /// 【为什么目标已离场时是 -1 却还要存名字】
        ///   目标被形态变化 / 溶解 / D耗尽带走之后就**不在 table 里**了，下标无从谈起；
        ///   但"这一回合登记过、且登记的就是它"这件事必须留着 —— 回合结束时要照实写
        ///   「本次献祭不生效」，而不是当成"这一回合没启动过"。所以名字单独一个字段。
        /// </summary>
        public int pendingSacrificeIndex = -1;

        /// <summary>待献祭目标登记时的卡名（也是"本回合登记过没有"的依据；见 pendingSacrificeIndex）。</summary>
        public string pendingSacrificeName = "";

        public bool levelOver;
        public bool bursted;

        /// <summary>关卡结束的原因（结算面板要写清是哪一种结束）</summary>
        public string endReason = "";

        // ── 刀片 ──
        public SaveBlade blade = new SaveBlade();

        // ── 桌面素材（**顺序 = 级联顺序**，读回来按同一个顺序重摆）──
        public List<SaveMaterial> table = new List<SaveMaterial>();

        // ── 手牌（素材一副、法术一副，顺序 = 摆出来的顺序）──
        public List<SaveHandCard> handMaterials = new List<SaveHandCard>();
        public List<SaveHandCard> handSpells = new List<SaveHandCard>();

        // ── 选择 / 暂存 ──
        /// <summary>当前启动目标在 table 里的下标（-1 = 没选）</summary>
        public int selectedIndex = -1;

        /// <summary>这次选中是不是"出牌自动选的最新一张"（HUD 会写出来）</summary>
        public bool selectedAuto;

        /// <summary>
        /// 投放区里待放置的牌。
        /// v2.1 是"落槽即结算"，所以它**常态为空** —— 存下来只为两件事：
        ///   ① 万一不为 0（旧流程的摆法 / 探针摆的），读档能如实对上而不是假装没这回事；
        ///   ② 自检（CompareStates）里它是一项，少了就会报差异，不会静默漏过去。
        /// </summary>
        public List<SaveStaged> staged = new List<SaveStaged>();

        /// <summary>一行摘要（日志和菜单里显示用）。</summary>
        public string Describe()
        {
            return "第 " + turnIndex + "/" + LevelRun.TurnsPerLevel + " 回合"
                 + "　行动机会 " + actionPoints + "/" + LevelRun.ActionPointsPerTurn
                 + "　分数 " + score + "/" + targetScore
                 + "　刀片 " + (blade != null ? blade.name + " H=" + blade.H + " V=" + blade.V : "（无）")
                 + "　附魔 " + LayersText()
                 + "　桌面 " + LiveTableCount() + " 张　手牌 " + (handMaterials.Count + handSpells.Count) + " 张"
                 + "　空白卡 " + blankCount
                 + (levelOver ? "　【关卡已结束：" + endReason + "】" : "");
        }

        /// <summary>桌面还活着的素材张数（removed 的不算）。</summary>
        public int LiveTableCount()
        {
            int n = 0;
            for (int i = 0; i < table.Count; i++) if (table[i] != null && !table[i].removed) n++;
            return n;
        }

        /// <summary>四种层数的一行摘要（含"不衰退"那份）—— 和 LayerLedger.Describe() 同一个口径。</summary>
        public string LayersText()
        {
            if (blade == null || blade.layers == null || blade.layers.Count == 0) return "（无层数）";

            List<string> parts = new List<string>();
            for (int i = 0; i < blade.layers.Count; i++)
            {
                SaveLayer l = blade.layers[i];
                if (l == null || l.decaying + l.permanent <= 0) continue;
                string t = l.kind + "×" + (l.decaying + l.permanent);
                if (l.permanent > 0) t += "（含" + l.permanent + "不衰退）";
                parts.Add(t);
            }
            return parts.Count > 0 ? string.Join(" ", parts.ToArray()) : "（无层数）";
        }
    }

    /// <summary>存档文件的根（版本 + 关卡定位 + 状态）。</summary>
    [Serializable]
    public class SaveFileDto
    {
        public int version = LevelSave.Version;
        public string kind = LevelSave.KindName;

        /// <summary>写盘时刻（人能看懂的那个时间，出问题时用来判断"这是哪一局的档"）</summary>
        public string savedAt = "";

        /// <summary>关卡下标 + id/名字（id 用来核对"卡表/配置变过没有"）</summary>
        public int levelIndex;
        public string levelId = "";
        public string levelName = "";

        /// <summary>本局用的牌组（读档要按同一副牌组重开 level，见 ContinueFromSave）</summary>
        public string deckId = "";
        public string deckName = "";

        /// <summary>存档时所处阶段（诊断用；恢复时统一回到可操作阶段，见 TableTurnLoop）</summary>
        public string phaseAtSave = "";

        public LevelSaveData state = new LevelSaveData();

        /// <summary>一行摘要（日志、开场「继续」的提示、暂停菜单那一行说明都用它）。</summary>
        public string Describe()
        {
            return (string.IsNullOrEmpty(levelName) ? ("第 " + (levelIndex + 1) + " 关") : levelName)
                 + "　" + (state != null ? state.Describe() : "（没有状态）");
        }

        /// <summary>
        /// **短**摘要（暂停菜单里那行说明用它 —— 面板只有 400 宽，长摘要会被折行、还会被下面的字压住）。
        /// 只留"能认出是哪一局"的四件事：哪一关 · 第几回合 · 多少分 · 什么刀片。
        /// </summary>
        public string ShortLine()
        {
            if (state == null) return "（没有状态）";

            string blade = state.blade != null && !string.IsNullOrEmpty(state.blade.name) ? state.blade.name : "?";
            return (string.IsNullOrEmpty(levelName) ? ("第 " + (levelIndex + 1) + " 关") : levelName)
                 + "｜第 " + state.turnIndex + "/" + LevelRun.TurnsPerLevel + " 回合"
                 + "｜" + state.score + " 分"
                 + "｜刀片 " + blade;
        }
    }

    /// <summary>
    /// 按 id / 名字把存档里那张卡还原成运行时的 Ingredient。
    ///
    /// 【为什么由调用方给】离线探针从 cards_v21.json 造（它手里只有解析出来的卡表），
    /// 游戏里走 CardSpecs + ApplyValues（还要补上卡面数值）。两边共用的只有"怎么存怎么读"，
    /// "卡从哪来"本来就该各给各的。
    /// 参数里的 valueSource 是存档记下的数值来源，游戏那一侧要用它重放 ApplyValues。
    /// </summary>
    public delegate Ingredient SaveCardResolver(string cardId, string cardName, int valueSource);

    /// <summary>
    /// 存档的采集 / 还原 / 比对 —— **纯状态搬运，不含任何结算语义**。
    /// </summary>
    public static class LevelSave
    {
        /// <summary>存档格式版本。字段含义变了就 +1（读档只认自己这一版，见 LevelSaveJson.Read）。</summary>
        public const int Version = 1;

        /// <summary>存档类型标记 —— 防止把别的 JSON（配置表之类）当存档读进来。</summary>
        public const string KindName = "v21-table-save";

        // ── 空白卡（正文 §2.1：数值全 0、无特性、可被吞噬）────────────────
        //   卡表里**没有**它，所以它的定义只能写在代码里。出牌产出与读档必须用同一份口径 ——
        //   各写一份的话，读档回来的空白卡和当场产出的空白卡会长得不一样（H/D/V 或标签差一点）。

        /// <summary>空白卡的名字（规则文本与产出清单里用的就是这三个字）。</summary>
        public const string BlankCardName = "空白卡";

        /// <summary>空白卡的 id（卡表里没有它，所以由代码定）。</summary>
        public const string BlankCardId = "blank";

        /// <summary>现造一张空白卡。</summary>
        public static Ingredient MakeBlankCard()
        {
            Ingredient blank = new Ingredient(BlankCardId, BlankCardName, new AttrSet());
            blank.form        = "固体";
            blank.tags        = new string[0];
            blank.transitions = new FormChange[0];
            blank.exhaust     = new string[0];
            return blank;
        }


        // ══════════════════════════════════════════════════════════════
        //  采集（状态 → DTO）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 采集刀片 + 桌面素材。**逐条对应**：桌面按 table 的顺序抄（那就是级联顺序），
        /// 每条记下"本回合启动过没有"（决定读档后还能不能拖回手牌）。
        ///
        /// 其余单值字段（回合 / 分数 / 结束状态…）由调用方直接写在返回的 DTO 上 ——
        /// 参数列表再长下去就没人读得懂了，而它们是同一个对象的公开字段。
        /// </summary>
        /// <param name="valueSourceOf">每张桌面素材"出牌那一刻的数值来源"；给 null 就按 0（卡表）算</param>
        public static LevelSaveData Capture(BladeState blade,
                                            List<MaterialState> table,
                                            List<MaterialState> startedThisTurn,
                                            Func<MaterialState, int> valueSourceOf)
        {
            LevelSaveData d = new LevelSaveData();

            // ── 刀片 ──
            d.blade = new SaveBlade();
            if (blade != null)
            {
                d.blade.cardId = blade.cardId != null ? blade.cardId : "";
                d.blade.name   = blade.name != null ? blade.name : "";
                d.blade.H      = blade.H;
                d.blade.V      = blade.V;

                // 四类都写出来（全 0 也写）：读的时候"缺了哪一类"才有意义
                for (int i = 0; i < LayerLedger.KindCount; i++)
                {
                    LayerKind k = (LayerKind)i;
                    LayerStack s = blade.layers.Stack(k);
                    SaveLayer l = new SaveLayer();
                    l.kind      = LayerLedger.Name(k);
                    l.decaying  = s.decaying;
                    l.permanent = s.permanent;      // ★ 两份分开存，理由见 SaveLayer 的注释
                    d.blade.layers.Add(l);
                }
            }

            // ── 桌面素材 ──
            if (table != null)
            {
                for (int i = 0; i < table.Count; i++)
                {
                    MaterialState st = table[i];
                    if (st == null) continue;

                    SaveMaterial m = new SaveMaterial();
                    m.cardId     = st.card != null ? st.card.id : "";
                    m.cardName   = st.name;
                    m.H          = st.H;
                    m.D          = st.D;
                    m.fullD      = st.fullD;
                    m.V          = st.V;
                    m.cardH      = st.card != null ? st.card.h : 0;
                    m.cardD      = st.card != null ? st.card.d : 0;
                    m.cardV      = st.card != null ? st.card.v : 0;
                    m.removed    = st.removed;
                    m.consumed   = st.consumed;
                    m.startedThisTurn = StartedContains(startedThisTurn, st);
                    m.valueSource = valueSourceOf != null ? valueSourceOf(st) : 0;
                    d.table.Add(m);
                }
            }

            return d;
        }

        private static bool StartedContains(List<MaterialState> list, MaterialState st)
        {
            if (list == null || st == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == st) return true;
            return false;
        }

        /// <summary>采集一张手牌素材。</summary>
        public static SaveHandCard CaptureHandMaterial(string cardId, string cardName, int h, int d, int v, int valueSource)
        {
            SaveHandCard c = new SaveHandCard();
            c.cardId = cardId != null ? cardId : "";
            c.cardName = cardName != null ? cardName : "";
            c.H = h; c.D = d; c.V = v;
            c.valueSource = valueSource;
            c.spell = false;
            return c;
        }

        /// <summary>手牌素材的"卡自己的 h/d/v"（卡面读的那三个数）。</summary>
        public static void SetCardFace(SaveHandCard c, int cardH, int cardD, int cardV)
        {
            if (c == null) return;
            c.cardH = cardH; c.cardD = cardD; c.cardV = cardV;
        }

        /// <summary>
        /// 把存档里记的"卡面三个数"写回这张卡（h/d/v + 同步一份到旧 attrs —— 卡面读 attrs）。
        ///
        /// 【为什么要覆盖】见 <see cref="SaveMaterial.cardH"/> 的说明：按 id 再查卡表
        ///   不保证得到原局里那一组数（硝石就是反例）。这里的口径和 TableRulesV21.ApplyValues
        ///   最后那一步完全一致（attrs 就是 h/d/v 的翻版），只改了来源。
        /// </summary>
        public static void ApplyCardFace(Ingredient ing, int h, int d, int v)
        {
            if (ing == null) return;

            ing.h = h;
            ing.d = d;
            ing.v = v;

            if (ing.attrs == null) ing.attrs = new AttrSet();
            ing.attrs.Set(AttrId.Salt,    h);
            ing.attrs.Set(AttrId.Mercury, d);
            ing.attrs.Set(AttrId.Sulfur,  v);
        }

        /// <summary>采集一张手牌法术（法术没有 H/D/V，那三个字段留 0）。</summary>
        public static SaveHandCard CaptureHandSpell(string cardId, string cardName)
        {
            SaveHandCard c = new SaveHandCard();
            c.cardId = cardId != null ? cardId : "";
            c.cardName = cardName != null ? cardName : "";
            c.spell = true;
            return c;
        }

        // ══════════════════════════════════════════════════════════════
        //  还原（DTO → 一批**新对象**）
        //
        //  【为什么先建好再落地，而不是边读边改】
        //    "半读半不读把局面搞坏"是这一版明令禁止的。所以还原分成两步：
        //      ① TryBuild：只读存档、只建新对象。任何一处不对（卡表里没这张卡 / 层数缺项 /
        //         类型不是数字）→ 返回 false + 原因，**现有状态一个字节都没动**；
        //      ② 调用方拿到 BuiltState 之后一次性覆盖（TableRulesV21.CommitSaveState）。
        //    于是"读档失败"永远是"什么都没发生"，玩家可以接着打原来那一局。
        // ══════════════════════════════════════════════════════════════

        /// <summary>还原出来的一批新对象（还没落到任何现有状态上）。</summary>
        public sealed class BuiltState
        {
            public BladeState blade = new BladeState("", "（未选刀片核心）", 0, 0);

            public readonly List<MaterialState> table = new List<MaterialState>();

            /// <summary>与 table 一一对应："这一张本回合启动过吗"</summary>
            public readonly List<bool> started = new List<bool>();

            /// <summary>与 table 一一对应：数值来源</summary>
            public readonly List<int> valueSource = new List<int>();

            public int turnIndex = 1;
            public int actionPoints = LevelRun.ActionPointsPerTurn;
            public int score;
            public int targetScore;
            public int startsThisTurn;
            public int blankCount;

            /// <summary>本回合待献祭目标在 table 里的下标（-1 = 没有 / 已离场）</summary>
            public int pendingSacrificeIndex = -1;

            /// <summary>待献祭目标登记时的卡名</summary>
            public string pendingSacrificeName = "";

            public bool levelOver;
            public bool bursted;
            public string endReason = "";

            public int selectedIndex = -1;
            public bool selectedAuto;

            /// <summary>读回来的"待放置"清单（v2.1 常态为空）。</summary>
            public readonly List<SaveStaged> staged = new List<SaveStaged>();

            /// <summary>读回来的刀片被动（原文，交给引擎重新解析）。</summary>
            public readonly List<SaveBladePassive> passives = new List<SaveBladePassive>();
        }

        /// <summary>
        /// 把存档里的**规则侧**状态建成一批新对象。失败时 <paramref name="error"/> 说清是哪一条不对。
        /// （手牌是 Table 那一层的对象类型 —— 素材卡 / 法术卡的壳在 TableRulesV21 里，
        ///   所以那两份由它自己按同一个 resolve 建，失败一样发生在落地之前。）
        /// </summary>
        public static bool TryBuild(LevelSaveData d, SaveCardResolver resolve,
                                    out BuiltState built, out string error)
        {
            built = null;
            error = "";

            if (d == null) { error = "存档里没有状态（state 节点是空的）"; return false; }
            if (resolve == null) { error = "没有给卡表解析器，读不了卡"; return false; }

            BuiltState b = new BuiltState();

            // ── 刀片 ──
            if (d.blade == null) { error = "存档里没有 blade 节点"; return false; }

            b.blade = new BladeState(d.blade.cardId, d.blade.name, d.blade.H, d.blade.V);

            if (d.blade.layers == null || d.blade.layers.Count == 0)
            {
                error = "刀片的 layers 是空的 —— 四种层数必须逐类存（含不衰退那份），不接受缺项";
                return false;
            }

            bool[] seen = new bool[LayerLedger.KindCount];
            for (int i = 0; i < d.blade.layers.Count; i++)
            {
                SaveLayer l = d.blade.layers[i];
                if (l == null) { error = "刀片 layers 里有空项"; return false; }

                LayerKind kind;
                if (!LayerLedger.TryParse(l.kind, out kind))
                {
                    error = "刀片 layers 里有一类认不出：「" + l.kind + "」（只能是 热 / 冷 / 酸 / 催化）";
                    return false;
                }
                if (l.decaying < 0 || l.permanent < 0)
                {
                    error = "刀片层数不能是负数：" + l.kind + " decaying=" + l.decaying + " permanent=" + l.permanent;
                    return false;
                }

                seen[(int)kind] = true;
                b.blade.layers.Add(kind, l.decaying);            // 衰退那份
                b.blade.layers.Add(kind, l.permanent, true);     // 不衰退那份
            }

            for (int i = 0; i < LayerLedger.KindCount; i++)
            {
                if (seen[i]) continue;
                error = "刀片 layers 缺了「" + LayerLedger.Name((LayerKind)i) + "」这一类 —— 缺项的存档一律不读";
                return false;
            }

            if (d.blade.passives != null)
                for (int i = 0; i < d.blade.passives.Count; i++)
                    if (d.blade.passives[i] != null) b.passives.Add(d.blade.passives[i]);

            // ── 桌面素材 ──
            if (d.table == null) { error = "存档里没有 table 节点"; return false; }

            for (int i = 0; i < d.table.Count; i++)
            {
                SaveMaterial m = d.table[i];
                if (m == null) { error = "table 第 " + (i + 1) + " 项是空的"; return false; }

                Ingredient ing = resolve(m.cardId, m.cardName, m.valueSource);
                if (ing == null)
                {
                    error = "桌面第 " + (i + 1) + " 张「" + m.cardName + "」（id " + m.cardId +
                            "）在卡表里找不到 —— 存档与当前卡表对不上，拒绝半读";
                    return false;
                }

                // 卡面那三个数按存档覆盖（按 id 查表不保证得到原局那一组，见 SaveMaterial.cardH）
                ApplyCardFace(ing, m.cardH, m.cardD, m.cardV);

                int fullD = m.fullD > 0 ? m.fullD : m.D;
                MaterialState st = new MaterialState(ing, m.D, m.H, m.V);
                st.fullD    = fullD;              // 构造函数把 fullD 设成了 D，这里按存档覆盖
                st.removed  = m.removed;
                st.consumed = m.consumed;

                b.table.Add(st);
                b.started.Add(m.startedThisTurn);
                b.valueSource.Add(m.valueSource);
            }

            // ── 单值字段 ──
            b.turnIndex      = d.turnIndex;
            b.actionPoints   = d.actionPoints;
            b.score          = d.score;
            b.targetScore    = d.targetScore;
            b.startsThisTurn = d.startsThisTurn;
            b.blankCount     = d.blankCount;

            // 待献祭目标的**下标**要在桌面建完之后才能校（越界当"已离场"处理）
            b.pendingSacrificeName  = d.pendingSacrificeName != null ? d.pendingSacrificeName : "";
            b.pendingSacrificeIndex = (d.pendingSacrificeIndex >= 0 && d.pendingSacrificeIndex < b.table.Count)
                                    ? d.pendingSacrificeIndex : -1;

            b.levelOver      = d.levelOver;
            b.bursted        = d.bursted;
            b.endReason      = d.endReason != null ? d.endReason : "";

            // 选中的目标：下标越界当作"没选"（并存进日志由调用方报出来）
            b.selectedIndex = (d.selectedIndex >= 0 && d.selectedIndex < b.table.Count) ? d.selectedIndex : -1;
            b.selectedAuto  = d.selectedAuto;

            if (d.staged != null)
                for (int i = 0; i < d.staged.Count; i++)
                    if (d.staged[i] != null) b.staged.Add(d.staged[i]);

            built = b;
            return true;
        }

        // ══════════════════════════════════════════════════════════════
        //  比对（读档自检的核心）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 逐字段比对两份状态，返回**差异清单**（空 = 完全相等）。
        ///
        /// 【为什么每一层都要比到最里面】"读回来状态不对"的报障最难查的地方在于
        ///   它往往只差一个字段（比如 D 的满值、某类层数的不衰退那份），
        ///   而面板上看起来一模一样。所以这里不比摘要、不比哈希 —— 一个字段一行，
        ///   不相等时日志里直接能读出"哪一张卡的哪一个数是几比几"。
        /// </summary>
        public static List<string> CompareStates(LevelSaveData a, LevelSaveData b)
        {
            List<string> diffs = new List<string>();
            if (a == null && b == null) return diffs;
            if (a == null || b == null)
            {
                diffs.Add("一边有状态、另一边没有（a=" + (a != null) + " b=" + (b != null) + "）");
                return diffs;
            }

            // ── 单值 ──
            Diff(diffs, "回合 turnIndex", a.turnIndex, b.turnIndex);
            Diff(diffs, "行动机会 actionPoints", a.actionPoints, b.actionPoints);
            Diff(diffs, "总分 score", a.score, b.score);
            Diff(diffs, "目标分 targetScore", a.targetScore, b.targetScore);
            Diff(diffs, "本回合已启动 startsThisTurn", a.startsThisTurn, b.startsThisTurn);
            Diff(diffs, "空白卡 blankCount", a.blankCount, b.blankCount);
            Diff(diffs, "待献祭目标下标 pendingSacrificeIndex", a.pendingSacrificeIndex, b.pendingSacrificeIndex);
            Diff(diffs, "待献祭目标名字 pendingSacrificeName", a.pendingSacrificeName, b.pendingSacrificeName);
            Diff(diffs, "关卡已结束 levelOver", a.levelOver, b.levelOver);
            Diff(diffs, "爆刀 bursted", a.bursted, b.bursted);
            Diff(diffs, "结束原因 endReason", a.endReason, b.endReason);
            Diff(diffs, "启动目标下标 selectedIndex", a.selectedIndex, b.selectedIndex);
            Diff(diffs, "自动选中 selectedAuto", a.selectedAuto, b.selectedAuto);

            // ── 待放置（v2.1 常态为空）──
            List<SaveStaged> sa = a.staged != null ? a.staged : new List<SaveStaged>();
            List<SaveStaged> sb = b.staged != null ? b.staged : new List<SaveStaged>();
            Diff(diffs, "待放置张数 staged", sa.Count, sb.Count);
            for (int i = 0; i < sa.Count && i < sb.Count; i++)
            {
                SaveStaged x = sa[i], y = sb[i];
                if (x == null || y == null) { if (x != y) diffs.Add("待放置第 " + (i + 1) + " 项：一边有一边没有"); continue; }
                Diff(diffs, "待放置第 " + (i + 1) + " 项是法术", x.spell, y.spell);
                Diff(diffs, "待放置第 " + (i + 1) + " 项手牌下标", x.index, y.index);
                Diff(diffs, "待放置第 " + (i + 1) + " 项占的槽", x.slot, y.slot);
            }

            // ── 刀片 ──
            SaveBlade ba = a.blade, bb = b.blade;
            if (ba == null || bb == null)
            {
                if (ba != bb) diffs.Add("刀片：一边有、一边没有");
            }
            else
            {
                Diff(diffs, "刀片 cardId", ba.cardId, bb.cardId);
                Diff(diffs, "刀片名字", ba.name, bb.name);
                Diff(diffs, "刀片 H", ba.H, bb.H);
                Diff(diffs, "刀片 V", ba.V, bb.V);

                for (int i = 0; i < LayerLedger.KindCount; i++)
                {
                    LayerKind k = (LayerKind)i;
                    SaveLayer la = FindLayer(ba, k);
                    SaveLayer lb = FindLayer(bb, k);
                    if (la == null || lb == null)
                    {
                        if (la != lb) diffs.Add("刀片层数「" + LayerLedger.Name(k) + "」：一边有一边没有");
                        continue;
                    }
                    Diff(diffs, "刀片「" + LayerLedger.Name(k) + "」衰退层", la.decaying, lb.decaying);
                    Diff(diffs, "刀片「" + LayerLedger.Name(k) + "」不衰退层", la.permanent, lb.permanent);
                }

                List<SaveBladePassive> pa = ba.passives != null ? ba.passives : new List<SaveBladePassive>();
                List<SaveBladePassive> pb = bb.passives != null ? bb.passives : new List<SaveBladePassive>();
                Diff(diffs, "刀片被动条数", pa.Count, pb.Count);
                for (int i = 0; i < pa.Count && i < pb.Count; i++)
                {
                    SaveBladePassive x = pa[i], y = pb[i];
                    if (x == null || y == null) { if (x != y) diffs.Add("刀片被动第 " + (i + 1) + " 条：一边有一边没有"); continue; }
                    Diff(diffs, "刀片被动 " + (i + 1) + " 来源卡", x.cardName, y.cardName);
                    Diff(diffs, "刀片被动 " + (i + 1) + " 原文", x.text, y.text);
                    Diff(diffs, "刀片被动 " + (i + 1) + " 句子", x.sentence, y.sentence);
                }
            }

            // ── 桌面 ──
            CompareMaterials(diffs, a.table, b.table);
            CompareHand(diffs, "手牌素材", a.handMaterials, b.handMaterials);
            CompareHand(diffs, "手牌法术", a.handSpells, b.handSpells);

            return diffs;
        }

        /// <summary>桌面逐张比（顺序也算 —— 顺序就是级联顺序）。</summary>
        private static void CompareMaterials(List<string> diffs, List<SaveMaterial> a, List<SaveMaterial> b)
        {
            int na = a != null ? a.Count : -1;
            int nb = b != null ? b.Count : -1;
            if (na != nb) { diffs.Add("桌面素材张数：" + na + " → " + nb); return; }
            if (na <= 0) return;

            for (int i = 0; i < na; i++)
            {
                SaveMaterial x = a[i], y = b[i];
                string who = "桌面第 " + (i + 1) + " 张";

                if (x == null || y == null) { if (x != y) diffs.Add(who + "：一边有一边没有"); continue; }

                Diff(diffs, who + " cardId", x.cardId, y.cardId);
                Diff(diffs, who + " cardName", x.cardName, y.cardName);
                Diff(diffs, who + "（" + x.cardName + "）H", x.H, y.H);
                Diff(diffs, who + "（" + x.cardName + "）D", x.D, y.D);
                Diff(diffs, who + "（" + x.cardName + "）fullD", x.fullD, y.fullD);
                Diff(diffs, who + "（" + x.cardName + "）V", x.V, y.V);
                Diff(diffs, who + "（" + x.cardName + "）卡面 h", x.cardH, y.cardH);
                Diff(diffs, who + "（" + x.cardName + "）卡面 d", x.cardD, y.cardD);
                Diff(diffs, who + "（" + x.cardName + "）卡面 v", x.cardV, y.cardV);
                Diff(diffs, who + "（" + x.cardName + "）removed", x.removed, y.removed);
                Diff(diffs, who + "（" + x.cardName + "）consumed", x.consumed, y.consumed);
                Diff(diffs, who + "（" + x.cardName + "）本回合已启动", x.startedThisTurn, y.startedThisTurn);
                Diff(diffs, who + "（" + x.cardName + "）数值来源", x.valueSource, y.valueSource);
            }
        }

        private static void CompareHand(List<string> diffs, string what, List<SaveHandCard> a, List<SaveHandCard> b)
        {
            int na = a != null ? a.Count : -1;
            int nb = b != null ? b.Count : -1;
            if (na != nb) { diffs.Add(what + "张数：" + na + " → " + nb); return; }

            for (int i = 0; i < na; i++)
            {
                SaveHandCard x = a[i], y = b[i];
                string who = what + "第 " + (i + 1) + " 张";

                if (x == null || y == null) { if (x != y) diffs.Add(who + "：一边有一边没有"); continue; }

                Diff(diffs, who + " cardId", x.cardId, y.cardId);
                Diff(diffs, who + "（" + x.cardName + "）名字", x.cardName, y.cardName);
                Diff(diffs, who + "（" + x.cardName + "）H", x.H, y.H);
                Diff(diffs, who + "（" + x.cardName + "）D", x.D, y.D);
                Diff(diffs, who + "（" + x.cardName + "）V", x.V, y.V);
                Diff(diffs, who + "（" + x.cardName + "）卡面 h", x.cardH, y.cardH);
                Diff(diffs, who + "（" + x.cardName + "）卡面 d", x.cardD, y.cardD);
                Diff(diffs, who + "（" + x.cardName + "）卡面 v", x.cardV, y.cardV);
                Diff(diffs, who + "（" + x.cardName + "）数值来源", x.valueSource, y.valueSource);
                Diff(diffs, who + "（" + x.cardName + "）是法术", x.spell, y.spell);
            }
        }

        private static SaveLayer FindLayer(SaveBlade b, LayerKind k)
        {
            if (b == null || b.layers == null) return null;
            string name = LayerLedger.Name(k);
            for (int i = 0; i < b.layers.Count; i++)
                if (b.layers[i] != null && b.layers[i].kind == name) return b.layers[i];
            return null;
        }

        private static void Diff(List<string> diffs, string what, int a, int b)
        {
            if (a == b) return;
            diffs.Add(what + "：" + a + " → " + b);
        }

        private static void Diff(List<string> diffs, string what, bool a, bool b)
        {
            if (a == b) return;
            diffs.Add(what + "：" + (a ? "是" : "否") + " → " + (b ? "是" : "否"));
        }

        private static void Diff(List<string> diffs, string what, string a, string b)
        {
            if (a == b) return;
            diffs.Add(what + "：「" + a + "」→「" + b + "」");
        }

        /// <summary>差异清单 → 一行日志用的话（空清单返回"逐字段全等"）。</summary>
        public static string DiffText(List<string> diffs)
        {
            if (diffs == null || diffs.Count == 0) return "逐字段全等";
            return diffs.Count + " 处不同：" + string.Join("；", diffs.ToArray());
        }
    }

    /// <summary>
    /// 存档文件的 **JSON schema** —— 写出去什么、读回来必须有什么，只有这一处说了算。
    ///
    /// 【严格到什么程度】不是"尽力读"：缺字段、类型不对、版本不符、层数缺项，
    ///   一律返回 false + 一句能直接照着查的原因，调用方据此**拒绝读档**并把
    ///   「继续」按回禁用状态（见 TableSaveIO / TableTurnLoop.ContinueFromSave）。
    ///   宁可让玩家读不了这一份档，也不能读出一个"看起来像、其实少了东西"的局面。
    /// </summary>
    public static class LevelSaveJson
    {
        // ══════════════════════════════════════════════════════════════
        //  写
        // ══════════════════════════════════════════════════════════════

        public static string Write(SaveFileDto f)
        {
            if (f == null) f = new SaveFileDto();
            LevelSaveData s = f.state != null ? f.state : new LevelSaveData();

            SaveJson.Writer w = new SaveJson.Writer();

            w.Obj();
            w.Put("version", f.version);
            w.Put("kind", f.kind);
            w.Put("savedAt", f.savedAt);

            w.Put("levelIndex", f.levelIndex);
            w.Put("levelId", f.levelId);
            w.Put("levelName", f.levelName);
            w.Put("deckId", f.deckId);
            w.Put("deckName", f.deckName);
            w.Put("phaseAtSave", f.phaseAtSave);

            w.Obj("state");
            w.Put("turnIndex", s.turnIndex);
            w.Put("actionPoints", s.actionPoints);
            w.Put("score", s.score);
            w.Put("targetScore", s.targetScore);
            w.Put("startsThisTurn", s.startsThisTurn);
            w.Put("blankCount", s.blankCount);
            w.Put("pendingSacrificeIndex", s.pendingSacrificeIndex);
            w.Put("pendingSacrificeName", s.pendingSacrificeName);
            w.Put("levelOver", s.levelOver);
            w.Put("bursted", s.bursted);
            w.Put("endReason", s.endReason);
            w.Put("selectedIndex", s.selectedIndex);
            w.Put("selectedAuto", s.selectedAuto);

            // ── 刀片 ──
            SaveBlade b = s.blade != null ? s.blade : new SaveBlade();
            w.Obj("blade");
            w.Put("cardId", b.cardId);
            w.Put("name", b.name);
            w.Put("H", b.H);
            w.Put("V", b.V);

            w.Arr("layers");
            if (b.layers != null)
            {
                for (int i = 0; i < b.layers.Count; i++)
                {
                    SaveLayer l = b.layers[i];
                    if (l == null) continue;
                    w.Obj();
                    w.Put("kind", l.kind);
                    w.Put("decaying", l.decaying);
                    w.Put("permanent", l.permanent);
                    w.End();
                }
            }
            w.End();                                   // layers

            w.Arr("passives");
            if (b.passives != null)
            {
                for (int i = 0; i < b.passives.Count; i++)
                {
                    SaveBladePassive p = b.passives[i];
                    if (p == null) continue;
                    w.Obj();
                    w.Put("cardId", p.cardId);
                    w.Put("cardName", p.cardName);
                    w.Put("text", p.text);
                    w.Put("sentence", p.sentence);
                    w.End();
                }
            }
            w.End();                                   // passives
            w.End();                                   // blade

            // ── 桌面素材（顺序 = 级联顺序）──
            w.Arr("table");
            for (int i = 0; i < s.table.Count; i++)
            {
                SaveMaterial m = s.table[i];
                if (m == null) continue;
                w.Obj();
                w.Put("cardId", m.cardId);
                w.Put("cardName", m.cardName);
                w.Put("H", m.H);
                w.Put("D", m.D);
                w.Put("fullD", m.fullD);
                w.Put("V", m.V);
                w.Put("cardH", m.cardH);
                w.Put("cardD", m.cardD);
                w.Put("cardV", m.cardV);
                w.Put("removed", m.removed);
                w.Put("consumed", m.consumed);
                w.Put("startedThisTurn", m.startedThisTurn);
                w.Put("valueSource", m.valueSource);
                w.End();
            }
            w.End();

            WriteHand(w, "handMaterials", s.handMaterials);
            WriteHand(w, "handSpells", s.handSpells);

            // ── 待放置（v2.1 常态为空数组）──
            w.Arr("staged");
            for (int i = 0; i < s.staged.Count; i++)
            {
                SaveStaged g = s.staged[i];
                if (g == null) continue;
                w.Obj();
                w.Put("spell", g.spell);
                w.Put("index", g.index);
                w.Put("slot", g.slot);
                w.End();
            }
            w.End();

            w.End();                                   // state
            w.End();                                   // 根

            return w.Text;
        }

        private static void WriteHand(SaveJson.Writer w, string key, List<SaveHandCard> list)
        {
            w.Arr(key);
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    SaveHandCard c = list[i];
                    if (c == null) continue;
                    w.Obj();
                    w.Put("cardId", c.cardId);
                    w.Put("cardName", c.cardName);
                    w.Put("H", c.H);
                    w.Put("D", c.D);
                    w.Put("V", c.V);
                    w.Put("cardH", c.cardH);
                    w.Put("cardD", c.cardD);
                    w.Put("cardV", c.cardV);
                    w.Put("valueSource", c.valueSource);
                    w.Put("spell", c.spell);
                    w.End();
                }
            }
            w.End();
        }

        // ══════════════════════════════════════════════════════════════
        //  读（严格映射：每一个必需字段都要在，且类型要对）
        // ══════════════════════════════════════════════════════════════

        public static bool Read(string text, out SaveFileDto file, out string error)
        {
            file = null;
            error = "";

            Dictionary<string, object> root;
            if (!SaveJson.TryParse(text, out root, out error)) return false;

            SaveFileDto f = new SaveFileDto();

            // ── 版本 / 类型（最先查：别的字段怎么解读全看它）──
            if (!NeedInt(root, "version", out f.version, out error)) return false;
            if (f.version != LevelSave.Version)
            {
                error = "存档版本是 " + f.version + "，这份程序只认 " + LevelSave.Version +
                        "（升级过格式的存档需要重新存一次；旧档不会被猜着读）";
                return false;
            }

            if (!NeedStr(root, "kind", out f.kind, out error)) return false;
            if (f.kind != LevelSave.KindName)
            {
                error = "这不是 v2.1 桌面的存档（kind=「" + f.kind + "」，应该是「" + LevelSave.KindName + "」）";
                return false;
            }

            // ── 关卡定位 / 牌组（这几个字段是必需的：读档要靠它们把这一关重新开起来）──
            if (!NeedInt(root, "levelIndex", out f.levelIndex, out error)) return false;
            if (!NeedStr(root, "levelId", out f.levelId, out error)) return false;
            if (!NeedStr(root, "levelName", out f.levelName, out error)) return false;
            if (!NeedStr(root, "deckId", out f.deckId, out error)) return false;
            if (!OptStr(root, "deckName", "", out f.deckName, out error)) return false;
            if (!OptStr(root, "savedAt", "", out f.savedAt, out error)) return false;
            if (!OptStr(root, "phaseAtSave", "", out f.phaseAtSave, out error)) return false;

            // ── 状态 ──
            Dictionary<string, object> st;
            if (!NeedObj(root, "state", out st, out error)) return false;

            LevelSaveData s = new LevelSaveData();

            if (!NeedInt(st,  "turnIndex",      out s.turnIndex, out error)) return false;
            if (!NeedInt(st,  "actionPoints",   out s.actionPoints, out error)) return false;
            if (!NeedInt(st,  "score",          out s.score, out error)) return false;
            if (!NeedInt(st,  "targetScore",    out s.targetScore, out error)) return false;
            if (!NeedInt(st,  "startsThisTurn", out s.startsThisTurn, out error)) return false;
            if (!NeedInt(st,  "blankCount",     out s.blankCount, out error)) return false;
            // ★ 这两个字段是"吞噬推迟到回合结束"带出来的新状态。这里用 Opt*（缺就按空处理）：
            //   它们缺了只会让"读档之后再结束回合"少吞一次，不至于让整份存档读不了 ——
            //   为了这两个字段把版本号 +1、把老档全判死，代价比这一条大得多。
            if (!OptInt(st,   "pendingSacrificeIndex", -1, out s.pendingSacrificeIndex, out error)) return false;
            if (!OptStr(st,   "pendingSacrificeName", "",  out s.pendingSacrificeName, out error)) return false;
            if (!NeedBool(st, "levelOver",      out s.levelOver, out error)) return false;
            if (!NeedBool(st, "bursted",        out s.bursted, out error)) return false;
            if (!OptStr(st,   "endReason", "",  out s.endReason, out error)) return false;
            if (!OptInt(st,   "selectedIndex", -1, out s.selectedIndex, out error)) return false;
            if (!OptBool(st,  "selectedAuto", false, out s.selectedAuto, out error)) return false;

            // ── 刀片 ──
            Dictionary<string, object> bl;
            if (!NeedObj(st, "blade", out bl, out error)) return false;

            s.blade = new SaveBlade();
            if (!NeedStr(bl, "cardId", out s.blade.cardId, out error)) return false;
            if (!NeedStr(bl, "name", out s.blade.name, out error)) return false;
            if (!NeedInt(bl, "H", out s.blade.H, out error)) return false;
            if (!NeedInt(bl, "V", out s.blade.V, out error)) return false;

            List<object> layers;
            if (!NeedArr(bl, "layers", out layers, out error)) return false;
            for (int i = 0; i < layers.Count; i++)
            {
                Dictionary<string, object> lo = layers[i] as Dictionary<string, object>;
                if (lo == null) { error = "blade.layers 第 " + (i + 1) + " 项不是一个对象"; return false; }

                SaveLayer l = new SaveLayer();
                if (!NeedStr(lo, "kind", out l.kind, out error)) return false;
                if (!NeedInt(lo, "decaying", out l.decaying, out error)) return false;
                if (!NeedInt(lo, "permanent", out l.permanent, out error)) return false;
                s.blade.layers.Add(l);
            }

            List<object> passives;
            if (!NeedArr(bl, "passives", out passives, out error)) return false;
            for (int i = 0; i < passives.Count; i++)
            {
                Dictionary<string, object> po = passives[i] as Dictionary<string, object>;
                if (po == null) { error = "blade.passives 第 " + (i + 1) + " 项不是一个对象"; return false; }

                SaveBladePassive p = new SaveBladePassive();
                if (!NeedStr(po, "cardId", out p.cardId, out error)) return false;
                if (!NeedStr(po, "cardName", out p.cardName, out error)) return false;
                if (!NeedStr(po, "text", out p.text, out error)) return false;
                if (!NeedStr(po, "sentence", out p.sentence, out error)) return false;
                s.blade.passives.Add(p);
            }

            // ── 桌面素材 ──
            List<object> table;
            if (!NeedArr(st, "table", out table, out error)) return false;
            for (int i = 0; i < table.Count; i++)
            {
                Dictionary<string, object> mo = table[i] as Dictionary<string, object>;
                if (mo == null) { error = "table 第 " + (i + 1) + " 项不是一个对象"; return false; }

                SaveMaterial m = new SaveMaterial();
                if (!NeedStr(mo,  "cardId", out m.cardId, out error)) return false;
                if (!NeedStr(mo,  "cardName", out m.cardName, out error)) return false;
                if (!NeedInt(mo,  "H", out m.H, out error)) return false;
                if (!NeedInt(mo,  "D", out m.D, out error)) return false;
                if (!NeedInt(mo,  "fullD", out m.fullD, out error)) return false;
                if (!NeedInt(mo,  "V", out m.V, out error)) return false;
                if (!OptInt(mo,   "cardH", 0, out m.cardH, out error)) return false;
                if (!OptInt(mo,   "cardD", 0, out m.cardD, out error)) return false;
                if (!OptInt(mo,   "cardV", 0, out m.cardV, out error)) return false;
                if (!NeedBool(mo, "removed", out m.removed, out error)) return false;
                if (!OptBool(mo,  "consumed", false, out m.consumed, out error)) return false;
                if (!NeedBool(mo, "startedThisTurn", out m.startedThisTurn, out error)) return false;
                if (!OptInt(mo,   "valueSource", 0, out m.valueSource, out error)) return false;
                s.table.Add(m);
            }

            if (!ReadHand(st, "handMaterials", false, s.handMaterials, out error)) return false;
            if (!ReadHand(st, "handSpells",    true,  s.handSpells,    out error)) return false;

            // ── 待放置（字段缺失按空数组处理：这一项在 v2.1 里常态为空）──
            object stagedRaw;
            if (st.TryGetValue("staged", out stagedRaw) && stagedRaw != null)
            {
                List<object> stagedArr = stagedRaw as List<object>;
                if (stagedArr == null) { error = "字段「staged」应该是一个数组"; return false; }

                for (int i = 0; i < stagedArr.Count; i++)
                {
                    Dictionary<string, object> go = stagedArr[i] as Dictionary<string, object>;
                    if (go == null) { error = "staged 第 " + (i + 1) + " 项不是一个对象"; return false; }

                    SaveStaged g = new SaveStaged();
                    if (!NeedBool(go, "spell", out g.spell, out error)) return false;
                    if (!NeedInt(go,  "index", out g.index, out error)) return false;
                    if (!OptInt(go,   "slot", -1, out g.slot, out error)) return false;
                    s.staged.Add(g);
                }
            }

            f.state = s;
            file = f;
            return true;
        }

        private static bool ReadHand(Dictionary<string, object> st, string key, bool spell,
                                     List<SaveHandCard> into, out string error)
        {
            error = "";

            List<object> arr;
            if (!NeedArr(st, key, out arr, out error)) return false;

            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> co = arr[i] as Dictionary<string, object>;
                if (co == null) { error = key + " 第 " + (i + 1) + " 项不是一个对象"; return false; }

                SaveHandCard c = new SaveHandCard();
                if (!NeedStr(co, "cardId", out c.cardId, out error)) return false;
                if (!NeedStr(co, "cardName", out c.cardName, out error)) return false;
                if (!OptInt(co,  "H", 0, out c.H, out error)) return false;
                if (!OptInt(co,  "D", 0, out c.D, out error)) return false;
                if (!OptInt(co,  "V", 0, out c.V, out error)) return false;
                if (!OptInt(co,  "cardH", 0, out c.cardH, out error)) return false;
                if (!OptInt(co,  "cardD", 0, out c.cardD, out error)) return false;
                if (!OptInt(co,  "cardV", 0, out c.cardV, out error)) return false;
                if (!OptInt(co,  "valueSource", 0, out c.valueSource, out error)) return false;
                c.spell = spell;
                into.Add(c);
            }
            return true;
        }

        // ── 字段取值小工具：缺字段 / 类型不对都返回 false + 原因 ──────────

        private static bool NeedObj(Dictionary<string, object> o, string key,
                                    out Dictionary<string, object> v, out string error)
        {
            v = null;
            object raw;
            if (!o.TryGetValue(key, out raw)) { error = "存档里缺字段「" + key + "」"; return false; }

            v = raw as Dictionary<string, object>;
            if (v == null) { error = "字段「" + key + "」应该是一个对象"; return false; }

            error = "";
            return true;
        }

        private static bool NeedArr(Dictionary<string, object> o, string key,
                                    out List<object> v, out string error)
        {
            v = null;
            object raw;
            if (!o.TryGetValue(key, out raw)) { error = "存档里缺字段「" + key + "」"; return false; }

            v = raw as List<object>;
            if (v == null) { error = "字段「" + key + "」应该是一个数组"; return false; }

            error = "";
            return true;
        }

        private static bool NeedStr(Dictionary<string, object> o, string key, out string v, out string error)
        {
            v = "";
            object raw;
            if (!o.TryGetValue(key, out raw)) { error = "存档里缺字段「" + key + "」"; return false; }

            v = raw as string;
            if (v == null) { error = "字段「" + key + "」应该是一个字符串（现在" + TypeName(raw) + "）"; return false; }

            error = "";
            return true;
        }

        private static bool OptStr(Dictionary<string, object> o, string key, string fallback,
                                   out string v, out string error)
        {
            error = "";
            object raw;
            if (!o.TryGetValue(key, out raw)) { v = fallback; return true; }
            if (raw == null) { v = fallback; return true; }

            v = raw as string;
            if (v == null) { error = "字段「" + key + "」应该是一个字符串（现在" + TypeName(raw) + "）"; return false; }
            return true;
        }

        private static bool NeedInt(Dictionary<string, object> o, string key, out int v, out string error)
        {
            object raw;
            if (!o.TryGetValue(key, out raw)) { error = "存档里缺字段「" + key + "」"; v = 0; return false; }

            if (!AsInt(raw, out v))
            {
                error = "字段「" + key + "」应该是一个整数（现在" + TypeName(raw) + "）";
                return false;
            }

            error = "";
            return true;
        }

        private static bool OptInt(Dictionary<string, object> o, string key, int fallback,
                                   out int v, out string error)
        {
            error = "";
            object raw;
            if (!o.TryGetValue(key, out raw) || raw == null) { v = fallback; return true; }

            if (!AsInt(raw, out v)) { error = "字段「" + key + "」应该是一个整数（现在" + TypeName(raw) + "）"; return false; }
            return true;
        }

        private static bool NeedBool(Dictionary<string, object> o, string key, out bool v, out string error)
        {
            object raw;
            if (!o.TryGetValue(key, out raw)) { error = "存档里缺字段「" + key + "」"; v = false; return false; }

            if (!(raw is bool)) { error = "字段「" + key + "」应该是 true / false（现在" + TypeName(raw) + "）"; v = false; return false; }

            v = (bool)raw;
            error = "";
            return true;
        }

        private static bool OptBool(Dictionary<string, object> o, string key, bool fallback,
                                    out bool v, out string error)
        {
            error = "";
            object raw;
            if (!o.TryGetValue(key, out raw) || raw == null) { v = fallback; return true; }

            if (!(raw is bool)) { error = "字段「" + key + "」应该是 true / false（现在" + TypeName(raw) + "）"; v = fallback; return false; }

            v = (bool)raw;
            return true;
        }

        /// <summary>JSON 里数字都是 double；只接受"确实是整数"的那种。</summary>
        private static bool AsInt(object raw, out int v)
        {
            v = 0;
            if (!(raw is double)) return false;

            double d = (double)raw;
            if (d != Math.Floor(d)) return false;
            if (d < int.MinValue || d > int.MaxValue) return false;

            v = (int)d;
            return true;
        }

        private static string TypeName(object raw)
        {
            if (raw == null) return "null";
            if (raw is string) return "字符串";
            if (raw is double) return "数字";
            if (raw is bool) return "布尔";
            if (raw is List<object>) return "数组";
            if (raw is Dictionary<string, object>) return "对象";
            return raw.GetType().Name;
        }

        /// <summary>版本 + 类型标记的一行摘要（日志里报"这是哪一版存档"用）。</summary>
        public static string VersionLine(SaveFileDto f)
        {
            if (f == null) return "（没有存档对象）";
            return "version " + f.version.ToString(CultureInfo.InvariantCulture) + " · " + f.kind
                 + (string.IsNullOrEmpty(f.savedAt) ? "" : " · 存于 " + f.savedAt);
        }
    }
}
