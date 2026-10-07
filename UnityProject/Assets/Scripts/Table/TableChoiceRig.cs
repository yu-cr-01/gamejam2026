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

        /// <summary>当前选中的牌组卡（-1 = 还没选）。</summary>
        /// <remarks>
        /// ★ 一定要初始化成 -1。默认值 0 的意思是"第 0 张已被选中"，
        /// 于是确认键一开局就是可用的、玩家什么都没点就选定了第一副牌组。
        /// </remarks>
        public int DeckSelected { get; private set; } = -1;

        /// <summary>
        /// 选中第 index 张牌组卡。玩家点卡片走的是同一条路 ——
        /// 自动化探针和鼠标点击共用这个入口，不另开一条捷径。
        /// </summary>
        public void SelectDeck(int index)
        {
            if (index < 0 || index >= deckCards.Count) return;
            DeckSelected = index;
        }

        // ══════════════════════════════════════════════════════════════
        //  建 / 拆
        // ══════════════════════════════════════════════════════════════

        public void BuildDeckCards(Choice choice)
        {
            ClearDeckCards();

            if (choice == null || choice.options == null) return;

            int n = choice.options.Count;
            if (n == 0) return;

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
        /// </summary>
        public void BuildLevelCards(List<LevelData> levels, int currentIndex)
        {
            ClearDeckCards();

            if (levels == null) return;

            int n = levels.Count;
            if (n == 0) return;

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

                float pitch = CardD * RowPitchK;
                float totalZ = (rows - 1) * pitch;
                float zFront = Mathf.Clamp(RowZ - totalZ * 0.5f, view.nearZ, view.farZ);

                // ★ 取这一排**近缘**的宽度：透视下近处最窄，用远处宽度算出来的尺寸会偏大
                //   （第一版就是取成远缘了，实测右边出去 18.6 像素）。
                float x0, x1;
                view.XRangeAt(zFront - CardD * 0.5f, out x0, out x1);
                float availX = Mathf.Max(0.2f, (x1 - x0) * 0.94f);

                float sX = availX / ((per - 1) * Spacing + CardW);
                float sZ = rows > 1
                    ? Mathf.Max(0.2f, (view.farZ - view.nearZ) * 0.92f)
                      / ((rows - 1) * RowPitchK * CardD + CardD)
                    : 1f;

                float s = Mathf.Min(1f, Mathf.Min(sX, sZ));
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
                if (!ProjectRowBounds(spots, out bx0, out by0, out bx1, out by1)) break;
                if (bx0 >= edge && bx1 <= Screen.width - edge &&
                    by0 >= edge && by1 <= Screen.height - edge) break;

                float shrink = Mathf.Min(
                    (Screen.width  - edge * 2f) / Mathf.Max(1f, bx1 - bx0),
                    (Screen.height - edge * 2f) / Mathf.Max(1f, by1 - by0));

                bestScale = Mathf.Clamp(bestScale * Mathf.Clamp(shrink, 0.5f, 0.98f), 0.30f, 1f);
                spots = BuildSpots(n, bestRows, bestScale, view);
            }

            LastLayoutRows  = bestRows;
            LastLayoutScale = bestScale;
            return spots;
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
            float pitchZ = CardD * scale * RowPitchK;

            // ── 横向：能保持原来"左偏 0.14"就保持，放不下就自动回正 ──
            float rowHalfW = ((perRow - 1) * Spacing + CardW) * scale * 0.5f;
            float halfD    = CardD * scale * 0.5f;

            float cx0, cx1;
            view.XRangeAt(RowZ - ((rows - 1) * pitchZ) * 0.5f - halfD, out cx0, out cx1);

            float cx = Mathf.Clamp((cx0 + cx1) * 0.5f + RowXOff,
                                   Mathf.Min(cx0 + margin + rowHalfW, cx1 - margin - rowHalfW),
                                   cx1 - margin - rowHalfW);

            // ── 纵向：几行以 RowZ 为中心摊开，夹在可视范围里 ──
            float totalZ = (rows - 1) * pitchZ;
            float zMin = view.nearZ + margin + halfD;
            float zMax = Mathf.Max(zMin, view.farZ - margin - halfD - totalZ);
            float z0 = Mathf.Clamp(RowZ - totalZ * 0.5f, zMin, zMax);

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
        /// </summary>
        private void TickDeckPick()
        {
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
