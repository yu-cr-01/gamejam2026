using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面上的两个选择环节：三选一牌组、选刀片。
    ///
    /// 【为什么要单独一个 rig，而不是塞进 TableInteraction】
    /// TableInteraction 管的是"把牌从这里拖到那里"，是回合循环里的动作；
    /// 这两个环节是**开局前的准备**，没有拖拽，只有"点一下选中"。
    /// 混在一起的话，交互层里会多出一堆"现在是什么阶段所以该不该拖"的分支。
    ///
    /// 【和 Flow2D 的关系】
    /// 规则、文案、候选项全部来自同一份配置（LevelData.choices / game_config.json），
    /// 这里只负责把它摆到桌面上：牌组是一排大卡，刀片是"点手牌即交换"。
    /// </summary>
    public class TableChoiceRig : MonoBehaviour
    {
        public Camera         cam;
        public TableSetup     setup;
        public TableTurnLoop  loop;

        /// <summary>牌组卡片的容器</summary>
        public Transform root;

        // ══════════════════════════════════════════════════════════════
        //  牌组卡片
        // ══════════════════════════════════════════════════════════════

        /// <summary>桌上的一张牌组卡。只是个数据壳，不是 MonoBehaviour。</summary>
        private class DeckCard
        {
            public GameObject go;
            public Renderer   body;
            public Renderer   face;
            public Color      bodyBase;
            public Color      faceBase;
            public float      lift;      // 平滑后的抬升量
        }

        // 比手牌大一整圈 —— 三张并排摆在桌子中央，是开局最显眼的东西。
        //
        // ★ 整排往左偏 0.14：榨汁机在 x≈0.74 那侧，卡片如果居中摆，
        //   最右那张会插进机器的立柱里。整体让开之后构图也更平衡。
        private const float CardW     = 0.40f;
        private const float CardD     = 0.52f;
        private const float CardT     = 0.018f;
        private const float Spacing   = 0.44f;
        private const float RowZ      = 0.16f;
        private const float RowXOff   = -0.14f;

        // ── 一排大卡的自适应摆放（见 LayoutCards）────────────────────────
        /// <summary>缩到这个比例就不再缩了，改为换行 —— 再小卡上的字就看不清了。</summary>
        private const float MinCardScale = 0.70f;

        /// <summary>两排之间的间距按卡深算：1.24 意味着后排刚好不压住前排。</summary>
        private const float RowPitchK = 1.24f;

        /// <summary>最多排几行（再多就该改交互了，比如翻页）。</summary>
        private const int MaxCardRows = 3;

        /// <summary>刀片摆哪 —— 桌面正中偏近侧，和 Flow2D 的"上方当前刀片"对应。</summary>
        public static readonly Vector3 BladeSpot = new Vector3(0f, 0f, 0.14f);

        private static readonly Color EdgeColor = new Color(0.130f, 0.118f, 0.104f);
        private static readonly Color InkOnPaper = new Color(0.145f, 0.132f, 0.118f);

        private readonly List<DeckCard> deckCards = new List<DeckCard>();

        /// <summary>鼠标悬停在第几张牌组卡上（-1 = 没有）。</summary>
        public int DeckHovered { get; private set; } = -1;

        /// <summary>当前选中的是第几项（-1 = 还没选）。牌组和关卡共用这一个槽位。</summary>
        /// <remarks>
        /// ★ 一定要初始化成 -1。默认值 0 的意思是"第 0 张已被选中"，
        /// 于是确认键一开局就是可用的、玩家什么都没点就选定了第一副牌组。
        /// </remarks>
        public int DeckSelected { get; private set; } = -1;

        /// <summary>
        /// 当前这批候选项有几项（牌组 = 几副牌，关卡 = 几关）。
        ///
        /// 【为什么不能拿 deckCards.Count 当上界】"选了什么"和"画了什么"是两件事：
        ///   v2.1 的关卡只在 UI 窗口里选（用户："不要关卡手牌了，就放一个 ui 就行"），
        ///   桌上一张 3D 关卡卡都不建 —— 上界要是还跟着卡片数量走，那就成了
        ///   "窗口里点一行没反应、确认键永远灰着"，选择和视图被绑死。
        ///   所以项数由**这批候选**决定，谁摆出来谁登记（BuildDeckCards /
        ///   BuildLevelCards / SetChoiceState 三个入口），建不建卡只影响画面。
        /// </remarks>
        private int choiceCount;

        /// <summary>
        /// 选中第 index 项。玩家点卡片、点关卡窗口里的一行走的都是同一条路 ——
        /// 自动化探针和鼠标点击共用这个入口，不另开一条捷径。
        /// </summary>
        public void SelectDeck(int index)
        {
            if (index < 0 || index >= choiceCount) return;
            DeckSelected = index;
        }

        /// <summary>
        /// 登记"这次有几项可选、当前选中第几项"，**一张 3D 卡都不建**。
        ///
        /// 【给谁用】v2.1 的关卡界面：关卡只由 TableHud 的关卡窗口来选，
        ///   桌上不再摆那排 3D 关卡卡。但"选中的是哪一关"这份状态必须还在 ——
        ///   窗口点一行 = <see cref="SelectDeck"/>、确认 = TableTurnLoop.ConfirmLevelSelect()，
        ///   两条路和从前一模一样，区别只是有没有视图。
        ///
        /// 【为什么默认就选中当前关卡】窗口进关卡界面时那一行本来就是高亮的
        ///   （levelWindowPick 停在当前关卡上）。状态要是还停在"没选"，
        ///   玩家按「进入这一关」就会被一句"先选一关"挡回来 ——
        ///   界面说选了、状态说没选，这是最容易被当成"坏了"的那种不一致。
        ///
        /// 【旧流程不走这里】UseRulesV21 = false 时照旧 BuildLevelCards 摆 3D 卡。
        /// </summary>
        public void SetChoiceState(int count, int selected)
        {
            ClearDeckCards();

            choiceCount  = Mathf.Max(0, count);
            DeckSelected = (selected >= 0 && selected < choiceCount) ? selected : -1;
        }

        /// <summary>
        /// 桌上现在摆着几张 3D 大卡（牌组 / 关卡共用这一个列表）。
        /// 探针和自检用 —— "v2.1 的关卡界面桌上一张卡都没有"这句话得能量出来。
        /// </summary>
        public int BigCardCount { get { return deckCards.Count; } }

        // ══════════════════════════════════════════════════════════════
        //  建 / 拆
        // ══════════════════════════════════════════════════════════════

        public void BuildDeckCards(Choice choice)
        {
            ClearDeckCards();

            if (choice == null || choice.options == null) return;

            int n = choice.options.Count;
            if (n == 0) return;

            choiceCount = n;                    // 选中态的合法范围（见 choiceCount 的说明）
            List<CardSpot> spots = LayoutCards(n);

            for (int i = 0; i < n; i++)
            {
                ChoiceOption o = choice.options[i];
                if (o == null) continue;

                Color accent = ProceduralArt.IngredientColor(o.id);

                // 印记形状用牌组的开局刀片决定 —— 它是这副牌的门面
                AttrId dominant = AttrId.Salt;
                Ingredient blade = o.deck != null ? o.deck.InitialBlade() : null;
                if (blade != null) dominant = ProceduralArt.DominantAttr(blade);

                string module = "无";
                if (o.deck != null && o.deck.modules != null && o.deck.modules.Count > 0
                    && o.deck.modules[0] != null)
                    module = o.deck.modules[0].name + "（" + o.deck.modules[0].Description() + "）";

                CardSpot sp = i < spots.Count ? spots[i] : new CardSpot { x = CountToX(i, n), z = RowZ, scale = 1f };

                deckCards.Add(BuildOne(o.id, o.title, accent, dominant,
                    o.deck != null ? o.deck.DescribeIngredients() : "（无）",
                    "模块：" + module,
                    "开局刀片：" + (blade != null ? blade.name : "（无）"),
                    sp, false));
            }
        }

        /// <summary>
        /// 关卡卡片：一排关卡，每张写着名字、目标分、状态。
        ///
        /// 和牌组卡共用同一套卡片建模 —— 两者都是"一排大卡点一张"，
        /// 只是上面的字不同。分开写两份的话，改一次卡面尺寸要改两处。
        ///
        /// 【只有旧流程会调它】v2.1 的关卡只在 UI 窗口里选（见 SetChoiceState）——
        /// 那条路一张卡都不建，所以这里的方法体对 v2.1 是"用不上"而不是"被删掉"。
        /// </summary>
        public void BuildLevelCards(List<LevelData> levels, int currentIndex)
        {
            ClearDeckCards();

            if (levels == null) return;

            int n = levels.Count;
            if (n == 0) return;

            choiceCount = n;                    // 选中态的合法范围（见 choiceCount 的说明）
            List<CardSpot> spots = LayoutCards(n);

            for (int i = 0; i < n; i++)
            {
                LevelData lv = levels[i];
                if (lv == null) continue;

                bool isCurrent = (i == currentIndex);

                Color accent = isCurrent
                    ? new Color(0.95f, 0.72f, 0.30f)          // 当前这关用暖金
                    : new Color(0.45f, 0.48f, 0.55f);

                CardSpot sp = i < spots.Count ? spots[i] : new CardSpot { x = CountToX(i, n), z = RowZ, scale = 1f };

                deckCards.Add(BuildOne(lv.id, lv.name, accent, AttrId.Salt,
                    // ★ 目标分要跟当前规则模式一致：v2.1 用的是 TableSettings.V21TargetScore（60），
                    //   旧流程才是 levels[].targetScore（1000/1500/2000）。
                    //   卡片上写旧数值、关卡窗口里写新数值，玩家会以为其中一个坏了。
                    "目标分：" + TableSettings.LevelTargetScore(lv),
                    "选择环节：" + lv.ChoiceCount + " 个",
                    isCurrent ? "▸ 当前关卡" : ("第 " + (i + 1) + " 关"),
                    sp, isCurrent));
            }
        }

        private static float CountToX(int i, int n)
        {
            return RowXOff + (i - (n - 1) * 0.5f) * Spacing;
        }

        // ══════════════════════════════════════════════════════════════
        //  自适应摆放：牌组从 3 副变 6 副，写死的一行就顶出屏幕了
        //
        //  【为什么不能写死】原来是 x = RowXOff + (i-(n-1)/2)*0.44、尺寸固定 ——
        //   n=3 时正好摆满屏幕中间，n=6 时最右边两张直接跑到屏幕外
        //   （用户截图里"铁·熔融流""硫·…"就是这么没的）。
        //   关卡那排共用同一套，以后加关卡不会再踩一次。
        //
        //  【排序规则】一行放得下就一行；放不下先缩尺寸和间距（缩到 0.70 为止）；
        //   再放不下就换行（最多三行）。所有卡都必须完整落在**屏幕能看到的桌面范围**里。
        // ══════════════════════════════════════════════════════════════

        /// <summary>一张大卡摆在哪、多大（scale = 1 就是原来的尺寸）。</summary>
        private struct CardSpot
        {
            public float x;
            public float z;
            public float scale;
        }

        /// <summary>
        /// 桌子平面（y = 0）上"屏幕四边"围出来的可见区域：一排大卡能用的地方就是它。
        ///
        /// 【为什么用相机算而不是写死一个范围】机位有 4 个（桌面 / 手牌特写 / 俯视 /
        ///   自由视角），写死的 ±1.1 在自由视角下立刻失效。视锥四角射线和桌面求交，
        ///   换什么机位都对。
        ///
        /// 【为什么还要分"近边 / 远边"】透视下桌面可见区是个梯形：近处窄、远处宽。
        ///   只用一个矩形的话，靠玩家这一侧的卡在同样的 x 上会先出屏 ——
        ///   所以近边和远边的 x 范围分开存，按 z 插值取用（见 XRangeAt）。
        /// </summary>
        private struct TableView
        {
            public float nearZ, farZ;
            public float nearX0, nearX1;
            public float farX0,  farX1;

            public bool valid;

            /// <summary>某个 z 处"屏幕左右边缘"对应的 x 范围（梯形内插）。</summary>
            public void XRangeAt(float z, out float x0, out float x1)
            {
                float span = Mathf.Max(0.0001f, farZ - nearZ);
                float t = Mathf.Clamp01((z - nearZ) / span);
                x0 = Mathf.Lerp(nearX0, farX0, t);
                x1 = Mathf.Lerp(nearX1, farX1, t);
            }
        }

        private bool TableVisibleArea(out TableView view)
        {
            view = new TableView();
            view.nearZ = -0.6f; view.farZ = 1.2f;
            view.nearX0 = -1.1f; view.nearX1 = 1.1f;
            view.farX0  = -1.6f; view.farX1  = 1.6f;
            view.valid  = false;

            if (cam == null) return false;

            Plane table = new Plane(Vector3.up, Vector3.zero);
            Vector2[] corners =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };

            Vector3[] hit = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Ray ray = cam.ViewportPointToRay(corners[i]);
                float dist;
                if (!table.Raycast(ray, out dist)) return false;   // 有一条边没打到桌面就别用这套
                hit[i] = ray.GetPoint(dist);
            }

            // 屏幕底边那两条射线打到的是桌子的**近边**（y=0 视口坐标里 y 是自下而上）。
            // 0/1 是底边两点、2/3 是顶边两点 —— 但如果相机是倒过来看的，
            // 这里按 z 排序更稳：z 最小的两点算近边、最大的两点算远边。
            Vector3 a = hit[0], b = hit[1], c = hit[2], d = hit[3];

            // 四条边的中点 z 排序：先把两点配对成"近边 / 远边"
            float zA = (a.z + b.z) * 0.5f;   // 底边
            float zB = (c.z + d.z) * 0.5f;   // 顶边

            Vector3 n0, n1, f0, f1;
            if (zA <= zB) { n0 = a; n1 = b; f0 = c; f1 = d; }
            else          { n0 = c; n1 = d; f0 = a; f1 = b; }

            view.nearZ = Mathf.Min(n0.z, n1.z);
            view.farZ  = Mathf.Max(f0.z, f1.z);
            view.nearX0 = Mathf.Min(n0.x, n1.x);
            view.nearX1 = Mathf.Max(n0.x, n1.x);
            view.farX0  = Mathf.Min(f0.x, f1.x);
            view.farX1  = Mathf.Max(f0.x, f1.x);
            view.valid  = true;
            return true;
        }

        /// <summary>
        /// 算 n 张卡各自的 (x, z, scale)。
        ///
        /// 【三道关】
        ///   ① 先用相机视锥 ∩ 桌面得到的梯形估一个"一行放得下 / 要缩到多大 / 要几行"；
        ///   ② 按这个估算把位置排出来（BuildSpots）；
        ///   ③ **投影到屏幕上量一遍**：有任何一个角超出屏幕边距就整体缩一点重排。
        ///
        /// 【为什么必须有第 ③ 步】第 ① 步是近似的（梯形内插 + 卡片有厚度），
        ///   实测 6 副牌组时右边缘还是出去了 18.6 像素 —— 估算差了 9%。
        ///   "完整在屏幕内"是硬要求，所以最后拿**同一套投影**去量、超了就缩，
        ///   量到合格为止（最多四轮）。这样这句话是量出来的，不是算出来的。
        /// </summary>
        private List<CardSpot> LayoutCards(int n)
        {
            List<CardSpot> spots = new List<CardSpot>();
            if (n <= 0) return spots;

            TableView view;
            TableVisibleArea(out view);

            // ── ① 估算：几行 + 缩到多大 ──
            //   逐行数试：缩得不低于 MinCardScale 就用它（行数越少越好）；
            //   都不行就取缩得最少的那种。
            int bestRows = 1;
            float bestScale = 0f;

            for (int rows = 1; rows <= MaxCardRows; rows++)
            {
                int per = (n + rows - 1) / rows;

                float sZ = rows > 1
                    ? Mathf.Max(0.2f, (view.farZ - view.nearZ) * 0.92f)
                      / ((rows - 1) * RowPitchK * CardD + CardD)
                    : 1f;

                // ★ 缩放要**估到自洽**：块越深 → 越容易被夹到最前面 → 可用宽度越小。
                //   拿 scale = 1 估一次就下结论会严重低估（实测：6 副牌组本该"一行 6 张、原尺寸"，
                //   却被估成 3 行 2 张、缩到 0.48）。所以从"深度允许的最大缩放"起步，
                //   **只减不增**地迭代到 s ≤ PlaceRow 给出的可用宽度为止 —— 单调收缩，必然收敛。
                float s = Mathf.Min(1f, sZ);
                for (int pass = 0; pass < 4; pass++)
                {
                    RowBox box = PlaceRow(rows, s, view);
                    float sX = Mathf.Max(0.2f, (box.x1 - box.x0) * 0.94f)
                               / ((per - 1) * Spacing + CardW);

                    float next = Mathf.Min(s, Mathf.Min(1f, sX));
                    if (next >= s - 0.005f) break;
                    s = next;
                }

                if (s > bestScale) { bestScale = s; bestRows = rows; }
                if (s >= MinCardScale) break;
            }

            bestScale = Mathf.Clamp(bestScale, 0.30f, 1f);

            // ── ② 排位置，然后 ③ 投影量、超了就缩 ──
            const float edge = 14f;   // 离屏幕边至少留这么多像素

            spots = BuildSpots(n, bestRows, bestScale, view);

            for (int pass = 0; pass < 4; pass++)
            {
                float bx0, by0, bx1, by1;
                bool measured = ProjectRowBounds(spots, out bx0, out by0, out bx1, out by1);
                bool onScreen = measured && bx0 >= edge && bx1 <= Screen.width - edge &&
                                           by0 >= edge && by1 <= Screen.height - edge;

                // ★ 还要"不穿模"：这一排的卡一个都不许压在挡路的东西上，
                //   也不许有立着的东西站在它前面把它盖住
                //   （用户截图：蜡烛从第一张牌组卡中间穿出来、机器压在第 4/5 张上）。
                //   量的是**真的卡**（spots 的世界矩形），不是估算 —— 和屏幕边距同一个口径。
                bool clearOfProps = !RowHitsProp(spots, bestScale) && !RowOccludedByProp(spots, bestScale);
                if (!measured || (onScreen && clearOfProps)) break;

                float shrink = Mathf.Min(
                    (Screen.width  - edge * 2f) / Mathf.Max(1f, bx1 - bx0),
                    (Screen.height - edge * 2f) / Mathf.Max(1f, by1 - by0));

                // 两者都不满足时，缩得动就缩（缩了之后 PlaceRow 会重挑 z / 通道，通常就够了）
                bestScale = Mathf.Clamp(bestScale * Mathf.Clamp(Mathf.Min(shrink, 0.98f), 0.5f, 0.98f), 0.30f, 1f);
                spots = BuildSpots(n, bestRows, bestScale, view);
            }

            if (RowHitsProp(spots, bestScale) || RowOccludedByProp(spots, bestScale))
                Debug.LogWarning("[V21][牌组排] ★ 这一排大卡和桌上的东西还有关系"
                                 + (RowOccludedByProp(spots, bestScale) ? "（有立着的东西站在它前面、会盖住卡面）" : "（世界矩形相交）")
                                 + " —— " + n + " 张、" + bestRows + " 行、缩放 " + bestScale.ToString("0.00")
                                 + "；挡路的：" + PropChannelText() + "。排不进去就该加行数或改 RowZ（见 PlaceRow）。");

            LastLayoutRows  = bestRows;
            LastLayoutScale = bestScale;

            // 一行日志把"这一排最后排成什么样、让开了什么"钉在日志里：
            //   用户报的那一幕（蜡烛插穿第一张卡 / 机器压在第 4、5 张上）修没修好，
            //   判据是"这张卡和那件东西的世界矩形还有没有交集"，不是"我看着还行"。
            float lx0, ly0, lx1, ly1;
            if (ProjectRowBounds(spots, out lx0, out ly0, out lx1, out ly1))
                Debug.Log("[V21][牌组排] " + n + " 张大卡：" + bestRows + " 行、缩放 " + bestScale.ToString("0.00")
                          + "｜屏幕 x " + lx0.ToString("0") + "~" + lx1.ToString("0")
                          + "，y " + ly0.ToString("0") + "~" + ly1.ToString("0")
                          + "（屏幕 " + Screen.width + "×" + Screen.height + "）"
                          + "｜让开的东西：" + PropChannelText()
                          + (RowHitsProp(spots, bestScale) || RowOccludedByProp(spots, bestScale)
                             ? "　★ 仍有交集/遮挡" : "　✓ 一个都不相交、也没被挡住"));

            return spots;
        }

        // ══════════════════════════════════════════════════════════════
        //  这一排大卡的"通道"：给蜡烛、量筒、破壁机让开
        //
        //  【它修的是用户这张截图】牌组选择界面（6 副）：**蜡烛从第一张卡中间穿出来**、
        //    破壁机立绘压在第 4/5 张卡上。
        //
        //  【为什么会这样】这一排大卡是按"屏幕里能放多大"自适应的（见 LayoutCards 的 ①②③），
        //    而它**完全不知道桌上有那三件立着的东西**：6 副牌组、宽窗口下会摆成一行铺满整屏，
        //    那一行正好从蜡烛和机器身上穿过去。
        //
        //  【为什么是"卡让开"而不是挪机位 / 挪机器】机位是桌面视角（按内容拟合，另一位在修
        //    手牌出画）、机器摆位是用户明确要过的（与桌边平行）。这两样都不该为了"一排卡"动。
        //    卡这一排本来就是自适应的 —— 让**行数**承担这件事最自然：排不进通道就换行。
        //
        //  【通道怎么算】按三件物件**活着的渲染器的世界包围盒**（和 TableRulesV21 的布局自检
        //    同一个口径，不另抄坐标）：中心在左边的（蜡烛 / 量筒）顶左边界、在右边的（破壁机）
        //    顶右边界，各留 PropGap。只有**这一排的 z 范围和它的 z 范围真的重叠**时才让 ——
        //    整排都在它前面（更靠玩家）时不必为它缩窄。
        // ══════════════════════════════════════════════════════════════

        /// <summary>一排大卡与物件之间至少留的余量（世界单位）。5 厘米 = 卡边到物件还有一条缝。</summary>
        private const float PropGap = 0.05f;

        /// <summary>
        /// 比这个高度还高的东西算"**立着的**、会挡住卡片"（蜡烛 / 量筒 / 破壁机：0.20~0.81 高）。
        /// 桌面上平躺的文字（槽名牌，厚度 ≈ 0）不算 —— 它盖不住卡面，只是"被卡压住就看不见了"，
        /// 那一条由"世界 XZ 不许相交"管（见 RowHitsProp）。
        /// </summary>
        private const float FlatKeepOutY = 0.05f;

        /// <summary>
        /// 这一排摆在哪：**z 基准 + 可用 x 范围**（把桌上挡路的东西都让开之后）。
        ///
        /// 【为什么不是"一个算式"而是"试几个候选再挑"】让开这件事有两个自由度（往前往后、往左往右），
        ///   而它们互相牵制：整排退到蜡烛前面（z）就不用让 x 了、但可能压到「附 魔 位」那两行字；
        ///   留在原处（z 不动）就得在 x 上挤那条不到 1 个单位宽的窄缝、一行 6 张立刻排不下。
        ///   硬写成一条公式必然顾此失彼，所以这里把候选摊开、按**可用宽度**挑最好的那个：
        ///     · 候选①＝历史位置（RowZ 居中）；
        ///     · 候选②～＝整排退到某件挡路东西的近缘前面（z 上完全错开）。
        ///   宽度相同就选离 RowZ 近的（少动摆位）。挑完的结果 ① 估算和 ② 摆位**共用**（同一个函数），
        ///   所以不会出现"估算说放得下、摆出来顶到蜡烛"。
        /// </summary>
        private RowBox PlaceRow(int rows, float scale, TableView view)
        {
            const float margin = 0.04f;

            RowBox r = new RowBox();
            r.totalZ = (rows - 1) * CardD * scale * RowPitchK;
            r.halfD  = CardD * scale * 0.5f;

            float zMinView = view.nearZ + margin + r.halfD;
            float zMaxView = Mathf.Max(zMinView, view.farZ - margin - r.halfD - r.totalZ);

            CollectKeepOuts();

            r.z0 = Mathf.Clamp(RowZ - r.totalZ * 0.5f, zMinView, zMaxView);
            ChannelX(r, view, out r.x0, out r.x1);

            // 候选②③：整排**退到某件东西前面**（远缘在它的近缘外侧留 PropGap）/
            //          **挪到某件东西后面**（近缘在它的远缘里侧留 PropGap）。
            //   ★ 方向不能随便选：相机在近端，**立着的东西一旦比卡片更靠玩家，就会盖住卡面** ——
            //     用户截图里"榨汁机压在第 4/5 张牌组卡上"正是这一种（两者的世界矩形其实错开，
            //     只是机器站在卡片前面把卡片挡住了）。所以：
            //       · 立着的（蜡烛 / 量筒 / 破壁机）→ **只能待在它前面**（它必须留在卡片后面）；
            //       · 平躺在桌面上的（「附 魔 位」那两行字）→ 前后都行（它盖不住卡面）。
            for (int i = 0; i < keepOuts.Count; i++)
            {
                TryRowZ(ref r, view, zMinView, zMaxView,
                        keepOuts[i].min.z - PropGap - r.halfD - r.totalZ);      // 前面

                if (keepOuts[i].size.y <= FlatKeepOutY)
                    TryRowZ(ref r, view, zMinView, zMaxView,
                            keepOuts[i].max.z + PropGap + r.halfD);             // 后面（只有平的才允许）
            }

            r.channeled = true;
            return r;
        }

        /// <summary>
        /// 试一个候选 z：**可用宽度更宽的那一档赢**（宽度 = 这一排能排多大，是这个界面最缺的东西）；
        /// 一样宽就选离 RowZ 近的（少动摆位）。
        /// </summary>
        private void TryRowZ(ref RowBox r, TableView view, float zMinView, float zMaxView, float z)
        {
            z = Mathf.Clamp(z, zMinView, zMaxView);

            float keepZ = r.z0;
            float cx0, cx1;
            r.z0 = z;
            ChannelX(r, view, out cx0, out cx1);
            r.z0 = keepZ;

            float w = cx1 - cx0;
            float bestW = r.x1 - r.x0;

            if (w > bestW + 1e-4f ||
                (Mathf.Abs(w - bestW) <= 1e-4f && Mathf.Abs(z - RowZ) < Mathf.Abs(r.z0 - RowZ)))
            {
                r.z0 = z; r.x0 = cx0; r.x1 = cx1;
            }
        }

        /// <summary>
        /// 把"这一排的 z 范围 + 视锥可见范围"夹出可用 x 范围 —— 与**挡路东西**（keepOuts）
        /// 的 x 边各留 PropGap。z 上和它错开的那些不参与（整排都在它前面 / 后面就互不相干）。
        /// </summary>
        private void ChannelX(RowBox r, TableView view, out float x0, out float x1)
        {
            view.XRangeAt(r.z0 - r.halfD, out x0, out x1);
            if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }

            float zNear = r.z0 - r.halfD;
            float zFar  = r.z0 + r.totalZ + r.halfD;

            for (int i = 0; i < keepOuts.Count; i++)
            {
                Bounds b = keepOuts[i];

                if (b.max.z + PropGap <= zNear || b.min.z - PropGap >= zFar) continue;

                // 中心在左半边就顶左边界、右半边顶右边界（正中间的东西两边都顶 ——
                // 那是"这个 z 上排不进去"，交给候选挑选 / 行数去解决，不在这里兜圈子）
                if (b.center.x < 0f) x0 = Mathf.Max(x0, b.max.x + PropGap);
                else                 x1 = Mathf.Min(x1, b.min.x - PropGap);
            }
        }

        /// <summary>PlaceRow 的结果：z 基准、块深、半深、可用 x 范围。</summary>
        private struct RowBox
        {
            public float z0, totalZ, halfD;
            public float x0, x1;
            public bool  channeled;
        }

        /// <summary>这一排大卡"挡路的东西"的世界包围盒（CollectKeepOuts 填）。</summary>
        private readonly List<Bounds> keepOuts = new List<Bounds>();

        /// <summary>
        /// 这一排大卡**不许压上去**的东西：三件立着的物件（蜡烛 / 量筒 / 破壁机）+
        /// 两块槽名牌（「附　魔 位」「上　桌 位」那两行字就刻在桌面上，被卡压住就看不见了）。
        ///
        /// 【为什么槽位框（那两个 L 形角标）不在里面】它们是**拿起牌才淡入**的提示框，
        ///   平时 alpha = 0（见 TableSetup.BuildSlots），卡压上去屏幕上什么都看不到 ——
        ///   把它算进来只会让这一排平白缩一圈。
        ///
        /// 【为什么按渲染器量】蜡烛的摆位在 TableTitleRig.CandleAt、量筒跟着蜡烛自己走、
        ///   立绘的宽高由美术图按像素反算、槽名牌的尺寸由字号决定 —— 在这里各抄一份坐标，
        ///   等于埋四个会过期的数。只算**活着的**渲染器：挂了立绘时程序化机身整组是关的，那部分不占地。
        /// </summary>
        private void CollectKeepOuts()
        {
            keepOuts.Clear();
            if (setup == null) return;

            AddPropBox(GameObject.Find(TableTitleRig.CandleName));

            if (setup.juicer != null)
            {
                if (setup.juicer.scoreBoard != null) AddPropBox(setup.juicer.scoreBoard.gameObject);
                AddPropBox(setup.juicer.gameObject);
            }

            // 两块槽名牌（名字见 TableSetup.BuildSlotLabels：SlotLabel0 / SlotLabel1）
            AddPropBox(GameObject.Find("SlotLabel0"));
            AddPropBox(GameObject.Find("SlotLabel1"));
        }

        private void AddPropBox(GameObject root)
        {
            if (root == null) return;

            Renderer[] rs = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds b = new Bounds();

            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled) continue;
                if (!r.gameObject.activeInHierarchy) continue;

                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }

            if (any) keepOuts.Add(b);
        }

        /// <summary>
        /// 这一排卡（按 spots 的真实矩形）有没有压在**挡路的东西**上。
        /// 判据是**世界 XZ 矩形相交** —— 和 TableRulesV21 的布局自检一套口径。
        /// </summary>
        private bool RowHitsProp(List<CardSpot> spots, float scale)
        {
            if (spots == null || spots.Count == 0) return false;

            CollectKeepOuts();
            if (keepOuts.Count == 0) return false;

            float hx = CardW * 0.5f * scale;
            float hz = CardD * 0.5f * scale;

            for (int i = 0; i < spots.Count; i++)
            {
                float x0 = spots[i].x - hx, x1 = spots[i].x + hx;
                float z0 = spots[i].z - hz, z1 = spots[i].z + hz;

                for (int k = 0; k < keepOuts.Count; k++)
                {
                    Bounds b = keepOuts[k];
                    if (x0 < b.max.x && x1 > b.min.x && z0 < b.max.z && z1 > b.min.z) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 有没有**立着的东西站在这一排前面**（会把它盖住）。
        ///
        /// 【为什么"世界矩形不相交"还不够】相机在近端：一件东西只要比卡片更靠玩家、又比卡片高，
        ///   它的剪影就会直接盖在卡面上 —— 用户截图里"榨汁机立绘压在第 4/5 张牌组卡上"
        ///   正是这一种（两者的世界 XZ 其实是错开的）。判据：立着的东西**整个**都在某张卡前面
        ///   （它的远缘还在卡的近缘外侧），而且 x 上有重叠 → 这张卡会被它挡住。
        /// </summary>
        private bool RowOccludedByProp(List<CardSpot> spots, float scale)
        {
            if (spots == null || spots.Count == 0) return false;

            CollectKeepOuts();
            if (keepOuts.Count == 0) return false;

            float hx = CardW * 0.5f * scale;
            float hz = CardD * 0.5f * scale;

            for (int i = 0; i < spots.Count; i++)
            {
                float x0 = spots[i].x - hx, x1 = spots[i].x + hx;
                float z0 = spots[i].z - hz;

                for (int k = 0; k < keepOuts.Count; k++)
                {
                    Bounds b = keepOuts[k];
                    if (b.size.y <= FlatKeepOutY) continue;               // 平的不挡卡面

                    bool xHit = x0 < b.max.x + PropGap && x1 > b.min.x - PropGap;
                    bool inFrontOfCard = b.max.z + PropGap <= z0;         // 它整个在卡的近侧（更靠玩家）

                    if (xHit && inFrontOfCard) return true;
                }
            }

            return false;
        }

        /// <summary>挡路东西的数字版描述（报警时附上，好一眼看出被谁夹住了）。</summary>
        private string PropChannelText()
        {
            if (keepOuts.Count == 0) return "（桌上没有挡路的东西）";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < keepOuts.Count; i++)
            {
                Bounds b = keepOuts[i];
                if (sb.Length > 0) sb.Append("；");
                sb.Append("x ").Append(b.min.x.ToString("0.000")).Append("~").Append(b.max.x.ToString("0.000"))
                  .Append(" z ").Append(b.min.z.ToString("0.000")).Append("~").Append(b.max.z.ToString("0.000"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 按给定的行数 / 缩放把 n 张卡摆出来（位置计算只在这一个地方，
        /// 免得"估算"和"重排"两份代码哪天走岔）。
        /// </summary>
        private List<CardSpot> BuildSpots(int n, int rows, float scale, TableView view)
        {
            List<CardSpot> spots = new List<CardSpot>();
            if (n <= 0) return spots;

            const float margin = 0.04f;

            int perRow = (n + rows - 1) / rows;

            // ── 纵向 + 横向：走和估算**同一个** PlaceRow（z 基准 / 块深 / 可用 x 一次算出来）──
            RowBox box = PlaceRow(rows, scale, view);

            float totalZ = box.totalZ;
            float halfD  = box.halfD;
            float z0     = box.z0;
            float pitchZ = CardD * scale * RowPitchK;

            float rowHalfW = ((perRow - 1) * Spacing + CardW) * scale * 0.5f;
            float cx0 = box.x0, cx1 = box.x1;

            float cx;
            if (cx1 - cx0 >= rowHalfW * 2f)
            {
                cx = Mathf.Clamp((cx0 + cx1) * 0.5f + RowXOff,
                                 Mathf.Min(cx0 + margin + rowHalfW, cx1 - margin - rowHalfW),
                                 cx1 - margin - rowHalfW);
            }
            else
            {
                cx = (cx0 + cx1) * 0.5f;      // 通道比这一排还窄：居中，由 LayoutCards 去缩/报警
            }

            for (int i = 0; i < n; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                int inRow = Mathf.Min(perRow, n - row * perRow);   // 最后一行可能不满

                CardSpot sp;
                sp.x     = cx + (col - (inRow - 1) * 0.5f) * Spacing * scale;
                sp.z     = z0 + row * pitchZ;
                sp.scale = scale;
                spots.Add(sp);
            }

            return spots;
        }

        /// <summary>
        /// 把"假设摆了这些卡"投影到屏幕上算包围盒（像素，左上原点）。
        ///
        /// 【为什么卡还没造出来就要算】排位置的时候要验一遍"会不会出屏"，
        ///   那时候还没来得及建 GameObject。这里按同一套尺寸假设投八个角，
        ///   和建完之后 CardScreenBounds 量出来的结果一致。
        /// </summary>
        private bool ProjectRowBounds(List<CardSpot> spots, out float minX, out float minY,
                                      out float maxX, out float maxY)
        {
            minX = float.MaxValue; minY = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue;

            if (cam == null || spots.Count == 0) return false;

            for (int i = 0; i < spots.Count; i++)
            {
                CardSpot sp = spots[i];
                float s  = Mathf.Clamp(sp.scale, 0.30f, 1f);
                float hx = CardW * 0.98f * 0.5f * s;
                float hz = CardD * 0.98f * 0.5f * s;

                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = new Vector3(
                        sp.x + ((c & 1) == 0 ? -hx : hx),
                        ((c & 2) == 0 ? 0f : CardT),
                        sp.z + ((c & 4) == 0 ? -hz : hz));

                    Vector3 p = cam.WorldToScreenPoint(corner);
                    if (p.z <= 0f) return false;   // 有角跑到相机后面，这次量不了

                    float px = p.x;
                    float py = Screen.height - p.y;

                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
            }

            return true;
        }

        /// <summary>这一批卡是按几行摆的 / 缩到多大（建完卡之后可读，日志和探针用）。</summary>
        public int LastLayoutRows { get; private set; }
        public float LastLayoutScale { get; private set; }

        /// <summary>
        /// 这一排大卡（牌组 / 关卡）在**世界里**的包围盒 —— 布局自检
        /// （TableRulesV21.CollectLayoutObstacles）拿它当障碍，探针也拿它两两求交。
        /// 一张卡都没有（v2.1 的关卡界面、回合里）时返回 false。
        ///
        /// 【为什么量卡身就够】卡身的 footprint 就是这一排的占地（卡面同尺寸、文字在卡面内），
        ///   量它和量整张卡对"谁压在谁的地盘上"是同一个答案，还省掉一堆文字网格。
        /// </summary>
        public bool RowWorldBounds(out Bounds b)
        {
            b = new Bounds();
            bool any = false;

            for (int i = 0; i < deckCards.Count; i++)
            {
                Renderer r = deckCards[i].body;
                if (r == null) continue;

                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }

            return any;
        }

        /// <summary>
        /// 这一批卡在屏幕上的包围盒（像素，左上原点）—— 验收"有没有跑出屏幕"用。
        ///
        /// 【为什么投影八个角而不是中心点】卡片是立体的、还带抬升动画：
        ///   只投中心点会漏掉边角，量出来的范围比实际小，等于没验。
        ///   这里把每张卡卡身的 bounds 八个角全投到屏幕上取包围盒。
        /// </summary>
        public bool CardScreenBounds(out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue; minY = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue;

            if (cam == null) return false;

            bool any = false;

            for (int i = 0; i < deckCards.Count; i++)
            {
                Renderer r = deckCards[i].body;
                if (r == null) continue;

                Bounds b = r.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);

                    Vector3 sp = cam.WorldToScreenPoint(corner);   // 左下原点
                    if (sp.z <= 0f) return false;                  // 有角跑到相机后面，这次量不了

                    float px = sp.x;
                    float py = Screen.height - sp.y;               // 换成左上原点，和 IMGUI 一套

                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                    any = true;
                }
            }

            return any;
        }

        public void ClearDeckCards()
        {
            for (int i = 0; i < deckCards.Count; i++)
                CardFactory.DestroySafe(deckCards[i].go);

            deckCards.Clear();
            DeckHovered  = -1;
            DeckSelected = -1;

            // 没有候选项 = 没有可选的东西。把项数一起清掉，
            // 否则离开界面之后 SelectDeck 还能改选中态，而界面上什么都看不到。
            choiceCount = 0;
        }

        /// <summary>
        /// 造一张大卡片。牌组卡和关卡卡共用这一个 ——
        /// 两者都是"一排大卡点一张"，只有上面的字不同，
        /// 分开写两份的话改一次卡面尺寸要改两处。
        ///
        /// 【scale 是什么】自适应摆放算出来的缩放（见 LayoutCards）：
        ///   卡身、卡面、**四行文字的位置和字号**必须一起乘，
        ///   只缩卡不缩字的话，小卡上的字会顶出卡面（字号是按卡宽定的）。
        ///   根节点的 scale 保持 1 不动 —— 选中时的抬升/放大动画动的是它。
        /// </summary>
        private DeckCard BuildOne(string id, string title, Color accent, AttrId dominant,
                                  string line1, string line2, string line3,
                                  CardSpot spot, bool highlight)
        {
            float s = Mathf.Clamp(spot.scale, 0.30f, 1f);

            GameObject go = new GameObject("BigCard_" + id);
            if (root != null) go.transform.SetParent(root, false);
            go.transform.position = new Vector3(spot.x, 0f, spot.z);

            // ── 卡身 ──
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale    = new Vector3(CardW * 0.98f * s, CardT, CardD * 0.98f * s);
            body.transform.localPosition = new Vector3(0f, CardT * 0.5f, 0f);

            Renderer bodyR = body.GetComponent<Renderer>();
            if (bodyR != null)
            {
                // ★ 统一走 CardFactory 的出口（见那里关于"打包版里 Standard 会被剥掉"的说明）
                Shader sh = CardFactory.StdShader();
                if (sh != null)
                {
                    bodyR.material = new Material(sh);

                    // 当前那关的卡身提亮一点 —— 金色卡面在深色桌面上本来就更显眼，
                    // 卡身跟上才不会像"贴错色"。
                    bodyR.material.color = highlight ? EdgeColor * 1.9f : EdgeColor;
                }
            }

            // ── 卡面 ──
            // 和手牌用同一张卡面生成器：圆角、顶部名字色带、中间印记都在，
            // 只是尺寸不同。朝向推导见 CardFactory，别在这里另写一套。
            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            face.transform.SetParent(go.transform, false);
            face.transform.localPosition = new Vector3(0f, CardT + 0.0006f, 0f);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale    = new Vector3(CardW * s, CardD * s, 1f);

            Collider fc = face.GetComponent<Collider>();
            if (fc != null) fc.enabled = false;      // 拾取只认卡身

            Renderer faceR = face.GetComponent<Renderer>();
            if (faceR != null)
                faceR.material = CardFactory.MakeUnlit(ProceduralArt.CardFace(accent, dominant));

            // ── 文字 ──
            //
            // ★ 全部落在 localZ 的**负半区**（靠近玩家这一侧）。
            //   牌组名一开始是写在正半区、也就是色带上的，结果一个字都渲染不出来 ——
            //   放大到 4 倍确认过是纯色块，而且诊断日志证明字符串本身没问题
            //   （title='硫硝爆燃'(4)），同一张卡上另外三行在负半区、全都正常。
            //   根因没查到，但规律很明确：这几张卡的正面文字只有近侧能显示。
            //   所以名字改成第一行，其余三行依次往下让位。
            //
            // ★ 高度必须显式给 CardT + 一点余量。用 CardFactory 的默认值
            //   （按手牌的 0.008 厚度算）会把字埋进这块 0.018 厚的卡身里。
            //   localY 不跟着 s 缩：卡身厚度 CardT 没缩，字沉下去就看不见了。
            float textY = CardT + 0.0022f;

            CardFactory.AddText(go.transform, title, -0.075f * s, 0.0115f * s, InkOnPaper, textY);

            if (!string.IsNullOrEmpty(line1))
                CardFactory.AddText(go.transform, line1, -0.115f * s, 0.0043f * s, InkOnPaper, textY);
            if (!string.IsNullOrEmpty(line2))
                CardFactory.AddText(go.transform, line2, -0.165f * s, 0.0039f * s, InkOnPaper, textY);
            if (!string.IsNullOrEmpty(line3))
                CardFactory.AddText(go.transform, line3, -0.215f * s, 0.0039f * s, InkOnPaper, textY);

            DeckCard c = new DeckCard();
            c.go       = go;
            c.body     = bodyR;
            c.face     = faceR;
            c.bodyBase = bodyR != null && bodyR.material != null ? bodyR.material.color : Color.white;
            c.faceBase = Color.white;
            return c;
        }

        // ══════════════════════════════════════════════════════════════
        //  输入
        // ══════════════════════════════════════════════════════════════

        void Update()
        {
            if (cam == null || loop == null) return;

            // ★ 三个"点一张大卡"的环节**都必须在这里**，漏一个就会出现
            //   "卡片建出来了、鼠标放上去也亮，但点了没反应、确认键永远灰着"。
            //
            //   关卡界面（LevelSelect）就漏过一次 —— 加那个阶段时只顾着建卡片，
            //   忘了接进输入分派。而且自动探针没测出来，因为它调的是
            //   SelectDeck() 这个 API，**绕过了鼠标那条路**。
            //   这种"界面建好了但不响应"的 bug，只有真按一遍才发现。
            if (loop.phase == TablePhase.LevelSelect || loop.phase == TablePhase.DeckPick)
                TickDeckPick();
            else if (loop.phase == TablePhase.BladePick)
                TickBladePick();
        }

        /// <summary>
        /// 牌组环节：点一张即选中（不走拖拽 —— 位置是固定的，拖来拖去没有意义）。
        ///
        /// 选中和确认分开：点一下只是高亮，真正定下来要按「确认选择该卡组」。
        /// 和投放区那条规则保持一致 —— 玩家应该有反悔的余地。
        ///
        /// 【桌上一张卡都没有时直接让开】v2.1 的关卡界面就是这个样子
        ///   （只在 UI 窗口里选，见 SetChoiceState）：没有卡可点，
        ///   这一帧就没必要打射线 —— 但悬停状态要归位，
        ///   否则从牌组界面切过来会留着一个旧的悬停下标。
        /// </summary>
        private void TickDeckPick()
        {
            if (deckCards.Count == 0) { DeckHovered = -1; return; }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            DeckHovered = HitDeckIndex(ray);

            if (Input.GetMouseButtonDown(0) && DeckHovered >= 0)
                DeckSelected = DeckHovered;

            AnimateDeckCards();
        }

        /// <summary>
        /// 刀片环节：点手牌里的食材即与当前刀片交换。
        ///
        /// 悬停状态直接借用 TableInteraction.Hovered —— 它每帧都在做同一件事，
        /// 这里再射线一次纯属重复劳动，而且两处判定迟早会不一致。
        /// </summary>
        private void TickBladePick()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (setup == null || setup.interaction == null) return;

            PlayCard hit = setup.interaction.Hovered;
            if (hit == null) return;

            loop.SwapBladeWith(hit);
        }

        /// <summary>鼠标底下是哪张牌组卡。</summary>
        private int HitDeckIndex(Ray ray)
        {
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 50f)) return -1;

            // 从命中的碰撞体往上走，找到属于列表里的那个根节点。
            // 不用 transform.root —— 卡片的祖先一直到整张桌子，那样会全部命中。
            Transform t = hit.collider.transform;
            while (t != null)
            {
                for (int i = 0; i < deckCards.Count; i++)
                    if (deckCards[i].go != null && deckCards[i].go == t.gameObject) return i;
                t = t.parent;
            }
            return -1;
        }

        /// <summary>抬升 / 放大 / 提亮 —— 三件事一起做，选中的那张一眼可辨。</summary>
        private void AnimateDeckCards()
        {
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);

            for (int i = 0; i < deckCards.Count; i++)
            {
                DeckCard c = deckCards[i];
                if (c.go == null) continue;

                bool selected = (i == DeckSelected);
                bool hovered  = (i == DeckHovered);

                c.lift = Mathf.Lerp(c.lift, selected ? 0.045f : (hovered ? 0.020f : 0f), k);

                Vector3 p = c.go.transform.position;
                c.go.transform.position = new Vector3(p.x, c.lift, p.z);

                float wantScale = selected ? 1.05f : 1f;
                c.go.transform.localScale = Vector3.Lerp(c.go.transform.localScale,
                                                         Vector3.one * wantScale, k);

                float mul = selected ? 1.35f : (hovered ? 1.18f : 1f);
                Tint(c.body, c.bodyBase, mul);
                Tint(c.face, c.faceBase, mul);
            }
        }

        private static void Tint(Renderer r, Color baseColor, float mul)
        {
            if (r == null || r.material == null) return;

            Color want = baseColor * mul;
            want.a = 1f;

            float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
            r.material.color = Color.Lerp(r.material.color, want, k);
        }
    }
}
