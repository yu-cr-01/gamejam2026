using System.Text;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 用数据造一张 3D 卡。
    ///
    /// 【节点结构】—— 这个结构是有讲究的，别改乱
    ///
    ///     Card_铁块                根节点，scale = 1
    ///       ├─ Rigidbody           刚体（默认 kinematic）
    ///       ├─ PlayCard            卡牌逻辑
    ///       ├─ Body               立方体：BoxCollider + 卡牌侧边，比卡面略小
    ///       ├─ Face               Quad：卡面贴图（圆角、带透明边），贴在 Body 顶面
    ///       ├─ NameText           TextMesh：名字（落在色带上）
    ///       └─ StatsText ×3       TextMesh：H / D / V 三个**数字**，
    ///                             分别落在数值牌上的菱形 / 圆形 / 方形里（见 CardFactory.AddStatNumbers）
    ///
    /// 【为什么 Body 是立方体而卡面是 Quad】
    ///   Body 负责"有厚度、能被射线打中"，它的顶面是方的。
    ///   卡面要圆角就得有透明区域，所以单独用一张带 alpha 的 Quad 盖上去，
    ///   并且 Body 比卡面小一圈 —— 这样圆角处透出去看到的是桌面，不是方块边。
    ///
    /// 【为什么文字挂在根节点上】
    /// Body 是非等比缩放（0.24 / 0.008 / 0.335），文字挂上去会被拉得完全变形。
    /// 所以文字挂在 scale=1 的根节点上，自己的位置和缩放自己控制。
    ///
    /// 【卡面文字的渲染次序只有一个出口】
    /// 所有卡片文字都从 <see cref="AddText"/> 出去，而它**默认**就排在卡面之后
    /// （<see cref="CardTextSortingOrder"/>）。这条以前只给了名字那一行，
    /// 于是"牌组选择界面下半屏的卡一个字都看不见"—— 理由见那个常量那一大段。
    /// 新加文字时什么都别设，走默认即可。
    ///
    /// 射线拾取打在 Body 的碰撞体上，用 GetComponentInParent&lt;PlayCard&gt;() 拿到逻辑。
    /// </summary>
    public static class CardFactory
    {
        public const float CardWidth = 0.24f;
        public const float CardThick = 0.008f;
        public const float CardDepth = 0.335f;

        /// <summary>
        /// Body 相对卡面的缩小比例 —— 让圆角真的能透出桌面。
        ///
        /// public 是因为 CardArt 要按**同一个数**把正式卡面正好铺满卡身：
        /// 卡面和卡身对不齐，卡面边缘就会露出一圈卡身 / 桌面 —— 那就是"半透明边框"那股脏边的来源。
        /// </summary>
        public const float BodyInset = 0.955f;

        /// <summary>
        /// 卡身的颜色 —— 也就是玩家在卡片**侧面看到的那圈"厚度"**。
        ///
        /// 【为什么从近黑色改成卡纸色】
        ///   卡是"立方体卡身 + 正面卡面贴图"两层。俯视 43° 时能同时看到卡身侧面，
        ///   原来这里是近黑 (0.13, 0.118, 0.104)，于是每张卡周围都镶了一道深灰边 ——
        ///   看起来就像卡面没裁干净、留了一圈半透明脏边（用户连着报过两次"这个边框删掉"）。
        ///   其实那不是贴图的问题（贴图那圈早在 CardArt 里修掉了），是**卡身侧面的颜色**。
        ///   现在取的是美术卡面**边框区域的平均色**（把 card_common_bg.png 外圈不透明像素求平均
        ///   得到 RGB(207,190,162)），侧面和正面就连成一片，看着就是一张有厚度的卡纸。
        /// </summary>
        private static readonly Color CardEdgeColor = new Color(0.813f, 0.745f, 0.636f);

        // 版面：卡面贴图里的分区（和 ProceduralArt 的常量对齐）
        //
        // 【字号是怎么定的】
        // TextMesh 的 world 高度 ≈ fontSize × characterSize × localScale × K，
        // 这个 K 跟字体和 fontSize 有关，纯算没法算准 —— 实机量出来 K ≈ 3.4
        // （localScale 0.015 时字高约 0.055 米，比卡宽 0.24 还大）。
        // 所以下面两个 size 是按"想要多高"反推的：
        //   名字想要 ~0.040 高 → 0.040 / 3.4 ≈ 0.0118
        //   属性想要 ~0.013 高 → 0.013 / 3.4 ≈ 0.0038
        // 改字号时请用 Assets/Editor/TablePreviewCapture 出图核对，别靠算。
        //
        // ★ 注意：正式美术卡面上的三个数字**不读 StatsSize** —— 它们的字号是
        //   "按形状几何 + 字体真实墨迹盒"算出来的（见 AddStatNumbers），
        //   因为三个格子一个菱形一个圆一个方，能塞下的高度本来就不一样。
        //   StatsSize 现在只服务程序化卡面那条路（没有美术时那三行字）。
        private const float NameZ  =  0.126f;   // 名字中心，落在名字色带上
        private const float NameSize = 0.0118f;
        private const float StatsZ = -0.112f;   // 属性块中心，落在下方属性区
        private const float StatsSize = 0.0038f;

        private static Font   cjkFont;
        private static Shader stdShader;
        private static bool   loggedUnlitShader;

        // ── 字体 ──────────────────────────────────────────────────────
        // Unity 内置字体不含中文字形，直接用会显示成方块。
        // 用 OS 字体拿到微软雅黑，再喂给 TextMesh。
        public static Font CjkFont()
        {
            if (cjkFont != null) return cjkFont;

            string[] candidates =
            {
                "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun",
                "PingFang SC", "Noto Sans CJK SC", "Arial Unicode MS"
            };
            cjkFont = Font.CreateDynamicFontFromOSFont(candidates, 48);
            return cjkFont;
        }

        /// <summary>
        /// 场景里那些"runtime 现建"的材质统一用哪个受光 shader。
        ///
        /// ★★ 打包版里可能找不到 Standard —— 这件事必须吵出来，不能静默降级 ★★
        ///
        /// 【为什么找不到】没有任何**资源**引用 Standard（本工程的美术资源都是贴图，
        ///   材质全是运行时 new 出来的），而 Standard 也不在
        ///   Project Settings → Graphics → Always Included Shaders 里 ——
        ///   于是打包时它被剥掉了，`Shader.Find("Standard")` 在播放器里返回 null。
        ///   实测（打完包跑起来打的那行日志）：
        ///     编辑器 —— shader=Standard
        ///     打包版 —— shader=Legacy Shaders/Diffuse      ← 悄悄换了个人
        ///   后果不只是"看着不亮"：Standard 才有的 _Glossiness / _Metallic 全部失效，
        ///   而**深色物体（桌面）在这条回退路上会暗到像纯黑** —— 用户报的"桌面变黑、木纹不见了"
        ///   就是它和"深色 _Color × 深色贴图"两条叠在一起的结果。
        ///
        /// 【怎么根治】把 Standard 加进 Always Included Shaders（或在 Resources 下放一个
        ///   引用 Standard 的材质资源）—— 那两处都在本文件的改动范围之外，
        ///   所以这里做的是**让它别再静默**：一旦回退，日志里就有一行 WARNING 指路。
        /// </summary>
        public static Shader StdShader()
        {
            if (stdShader == null) stdShader = Shader.Find("Standard");

            if (stdShader == null)
            {
                // 兜底：Legacy Shaders/Diffuse 在 Always Included Shaders 里，所以播放器一定找得到
                stdShader = Shader.Find("Diffuse");

                if (!loggedStdShaderFallback)
                {
                    loggedStdShaderFallback = true;
                    Debug.LogWarning("[CardFactory] 找不到 Standard shader，运行时材质退到「"
                                     + (stdShader != null ? stdShader.name : "★连 Diffuse 都没有")
                                     + "」。打包版里出现「颜色 / 明暗和编辑器不一样」就是这个原因 —— "
                                     + "把 Standard 加进 Project Settings → Graphics → Always Included Shaders 即可根治。");
                }
            }
            return stdShader;
        }

        /// <summary>"Standard 被剥掉"这件事只吵一次（每张卡都吵会把日志刷爆）。</summary>
        private static bool loggedStdShaderFallback;

        /// <summary>
        /// 不受光照影响的透明材质 —— 卡面 / 卡槽角标用，保证图案永远看得清。
        ///
        /// ★★ 选 shader 的唯一硬性要求：**必须带 _Color 属性** ★★
        /// Unity 内置的 "Unlit/Transparent" 只有 _MainTex、没有 _Color，
        /// 材质 .color 会被静默忽略 —— 卡槽角标曾经因此踩坑：
        /// 既染不上颜色，也没办法用 alpha 淡出（设 a=0 照样显示）。
        /// Sprites/Default 两者都支持，所以优先用它。
        /// </summary>
        public static Material MakeUnlit(Texture2D tex)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = StdShader();

            Material m = new Material(sh);
            if (tex != null) m.mainTexture = tex;
            m.color = Color.white;

            if (!loggedUnlitShader)
            {
                loggedUnlitShader = true;
                Debug.Log("[CardFactory] 透明材质 shader = " + (sh != null ? sh.name : "NULL")
                          + "，带 _Color = " + m.HasProperty("_Color"));
            }
            return m;
        }

        // ── 造卡 ──────────────────────────────────────────────────────

        /// <summary>
        /// 销毁一个对象，运行时和编辑模式都能用。
        ///
        /// 【为什么需要它】TablePreviewCapture 是在**编辑模式**下调 TableSetup.Awake 的
        /// （场景里不摆东西，全靠代码搭）。那条路径上 Object.Destroy 会直接报错，
        /// 必须走 DestroyImmediate。牌组卡片、刀片卡、重发手牌都要销毁重建，
        /// 所以这个判断只留一份，别到处抄。
        /// </summary>
        public static void DestroySafe(Object o)
        {
            if (o == null) return;

            // ★ 先盖章再销毁：Object.Destroy 是**帧末**才真删的，这一帧剩下的时间里
            //   这张卡还在 cardsRoot 下、还能被 GetComponentsInChildren 找到。
            //   TableRulesV21 的残留清扫靠这个标记把"已经排好销毁"的卡排除掉，
            //   否则每次重建手牌都会把刚销毁的卡报成残留（假警）。
            GameObject go = o as GameObject;
            if (go != null)
            {
                PlayCard pc = go.GetComponent<PlayCard>();
                if (pc != null) pc.markedForDestroy = true;
            }
            else
            {
                PlayCard pc = o as PlayCard;
                if (pc != null) pc.markedForDestroy = true;
            }

            if (Application.isPlaying) Object.Destroy(o);
            else                       Object.DestroyImmediate(o);
        }

        /// <summary>
        /// 按一张牌造 3D 卡。食材和模块共用这一条路径 ——
        /// 以前是两个重载，调用方得先判断是哪一种再挑一个调，
        /// 而那个判断在四个文件里各抄了一遍。
        /// </summary>
        public static PlayCard Create(Card c, Transform parent, Vector3 home, Vector3 euler)
        {
            if (c == null) return null;

            Color  accent;
            AttrId dominant;
            string sub;
            Ingredient statsIng = null;      // 正式美术卡面上"三个数字"的来源（模块没有）

            if (c.IsModule)
            {
                // 模块走冷色区，和食材一眼分得开
                accent   = ProceduralArt.ModuleColor(c.id);

                // 模块没有"自己的属性"，印记形状固定给液体形；
                // 卡面文字已经写明它是模块，形状不用再承担区分职责
                dominant = AttrId.Mercury;

                sub = "【变速模块】\n" + (c.module != null ? c.module.Description() : "");
            }
            else
            {
                accent   = ProceduralArt.IngredientColor(c.id);
                dominant = ProceduralArt.DominantAttr(c.ingredient);
                statsIng = c.ingredient;
                sub      = (c.ingredient != null && c.ingredient.attrs != null)
                           ? StatsText(c.ingredient) : null;
            }

            PlayCard card = BuildCard(c.name, accent, dominant, sub, statsIng, parent, CardArt.ElementOf(c));
            card.Setup(c, home, euler);
            return card;
        }

        /// <summary>
        /// 两类卡共用的建模部分。
        /// 只负责"长什么样"，绑定数据交给调用方 —— 食材和模块的绑定接口不一样。
        ///
        /// artElement 是正式美术的元素皮肤名（见 CardArt）；给 null 或美术缺图时
        /// 整张卡退回程序化卡面，连文字版面一起退 —— 两套版面不能混着用。
        ///
        /// statsIng 是**正式美术版面**专用的：那张卡面上本来就有三个装饰形状，
        /// 三个数字分别进这三格（见 AddStatNumbers）。程序化卡面没有这三个形状，
        /// 继续用 subText 那三行字，所以两条路各吃各的参数、互不影响。
        /// </summary>
        private static PlayCard BuildCard(string title, Color accent, AttrId dominant,
                                          string subText, Ingredient statsIng,
                                          Transform parent, string artElement)
        {
            // ① 根节点：scale = 1，挂脚本和刚体
            GameObject root = new GameObject("Card_" + title);
            root.transform.SetParent(parent, false);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;      // 默认脚本控制，按 G 才放开物理
            rb.useGravity = false;
            rb.mass = 0.05f;
            rb.drag = 0.6f;
            rb.angularDrag = 0.8f;

            PlayCard card = root.AddComponent<PlayCard>();

            // ② 卡身：立方体自带 MeshRenderer + BoxCollider。比卡面小一点，好让圆角透出桌面
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(
                CardWidth * BodyInset, CardThick, CardDepth * BodyInset);

            Renderer bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null && StdShader() != null)
            {
                bodyRenderer.material = new Material(StdShader());
                bodyRenderer.material.color = CardEdgeColor;
                if (bodyRenderer.material.HasProperty("_Glossiness"))
                    bodyRenderer.material.SetFloat("_Glossiness", 0.10f);
            }

            // ③ 卡面：一张 Quad 盖在卡身顶面。
            //
            // ★ 朝向是本文件最容易搞错的地方，说明一次：
            //   Quad 默认躺在 XY 平面、法线朝 -Z（正面朝 -Z 方向可见）。
            //   Euler(90,0,0) 把它放平：法线从 -Z 转到 +Y（朝上，相机在上方能看到）。
            //   同时局部 +Y 转到了世界 +Z，也就是"远端" —— 而相机在近端往远端看，
            //   屏幕上方正是远端，所以贴图的 V 方向和屏幕方向天然一致，图案不会上下颠倒。
            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            face.transform.SetParent(root.transform, false);
            face.transform.localPosition = new Vector3(0f, CardThick * 0.5f + 0.0006f, 0f);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(CardWidth, CardDepth, 1f);

            Collider faceCollider = face.GetComponent<Collider>();
            if (faceCollider != null) faceCollider.enabled = false;   // 拾取只认 Body，别让卡面参与

            Renderer faceRenderer = face.GetComponent<Renderer>();

            // 卡面贴图：优先正式美术（CardArt 会把通用卡底 + 元素插画 + 名字牌 + 数值牌拼好），
            // 任何一个切片缺失都会退回程序化卡面，不会画成空白。
            Texture2D faceTex = CardArt.Face(artElement);
            bool artFace = faceTex != null;
            if (!artFace) faceTex = ProceduralArt.CardFace(accent, dominant);

            if (faceRenderer != null)
                faceRenderer.material = MakeUnlit(faceTex);

            // ④ 文字：挂在根节点上（scale=1），避免被 Body 的非等比缩放拉变形
            //
            // 两套版面：
            //   程序化 —— 名字压在通栏色带上（居中）、三属性在下方属性区居中
            //   正式美术 —— 名字落在左上角的名字牌里；三属性**不再是三行字**，
            //              而是三个数字分别进数值牌上的菱形/圆形/方形（位置量自美术效果图）
            //
            // ★ 两条路都不传 sortingOrder：AddText 的默认值就是"排在卡面之后"
            //   （见 CardTextSortingOrder）。所有卡片文字一律走这个默认值。
            if (artFace)
            {
                float y = CardThick * 0.5f + 0.0022f;
                AddText(root.transform, title, CardArt.NameOnPlate.z, CardArt.NameOnPlateSize,
                        CardArt.InkOnPlate, y, CardArt.NameOnPlate.x, NameTextObject);

                // 三个数字：H / D / V 分别进 上（菱形）/ 中（圆）/ 下（方）
                if (statsIng != null && statsIng.attrs != null)
                    AddStatNumbers(root.transform, statsIng, y);
            }
            else
            {
                AddText(root.transform, title, NameZ, NameSize, ProceduralArt.InkOn(accent),
                        CardThick * 0.5f + 0.0022f, 0f, NameTextObject);

                // 副文本：食材写三属性，模块写效果描述
                if (!string.IsNullOrEmpty(subText))
                    AddText(root.transform, subText, StatsZ, StatsSize, InkOnPaper(),
                            CardThick * 0.5f + 0.0022f, 0f, StatsTextObject);
            }

            // 卡身和卡面都要跟着悬停/拖动变亮，所以两个渲染器都绑上
            card.BindRenderer(bodyRenderer, faceRenderer);
            return card;
        }

        // ══════════════════════════════════════════════════════════════
        //  正式美术卡面：三个数字进三个形状
        //
        //  【改的是什么】卡面美术上数值牌那一块本来就摞着三个装饰形状
        //    （⬥ 菱形 / ● 圆形 / ■ 方形，见 CardArt.StatSlots），
        //    而三属性以前是**三行字**："H 盐性 12" / "D 汞性 3" / "V 硫性 1" ——
        //    整块盖在形状上、还比数值牌宽。现在：
        //      · 只画三个**数字**，分别居中放进对应的形状里；
        //      · "H""D""V""盐性""汞性""硫性"这些字样**一个都不再出现**；
        //      · 数值来源一个字没变（还是 Ingredient.attrs 的 H/D/V 三项），只改排版。
        //
        //  【字号是算出来的，不是写死的】三个格子形状不同（菱形最紧、方最松），
        //    能塞下的高度天然不一样，所以不能用一个常量：
        //      ① 量出每个数字的**墨迹盒**（TextMesh 生成出来的网格顶点包围盒 ——
        //         宽就是这个串的真实宽度、高就是数字的字面高度）；
        //      ② CardArt.StatMaxDigitHeightPx 按形状几何算出"这个宽高比的矩形最多多高"；
        //      ③ size = 目标高度 ÷ 墨迹高度。
        //    两位数（"12"）的墨迹盒更宽 → 宽高比更大 → ②算出来的高度更小 →
        //    字号自己就缩下来了。0~3 位都不用手工调，也没有"按位数查表"这种会过期的写法。
        //
        //  【三个一位数一样大】基准字号取"三个形状里**最紧的那一格**装一位数"时的字号
        //    （实测是菱形），再对每一格取 min(基准, 这一格自己的上限)。
        //    所以 H=3 / D=3 / V=3 时三个数字一样大；只有某个数是两位数时它自己那一格缩。
        //
        //  【垂直方向为什么要挪】TextMesh 的 MiddleCenter 对齐的是**行盒**
        //    （ascent + descent，中文字体的 descent 占得不小），而数字只占基线上方那一截，
        //    两者差几个百分点 —— 直接摆上去数字会略偏上。所以缩放定下来之后，
        //    把"墨迹盒中心"对准形状中心（卡面局部坐标里的换算见下面 offset 那两行）。
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 把 H / D / V 三个数字分别画进数值牌上的菱形 / 圆形 / 方形。
        /// 顺序和 <see cref="CardArt.StatSlots"/> / AttrCatalog 一致：0 = H（上）、1 = D（中）、2 = V（下）。
        /// </summary>
        private static void AddStatNumbers(Transform root, Ingredient ing, float y)
        {
            int n = Mathf.Min(AttrCatalog.Count, CardArt.StatSlots.Length);
            GameObject[] numbers = new GameObject[n];

            for (int i = 0; i < n; i++)
            {
                AttrDef def = AttrCatalog.Get((AttrId)i);
                if (def == null) continue;

                int value = ing.attrs.Get(def.id);
                Vector2 at = CardArt.EffectPxToLocal(CardArt.StatSlots[i].centerPx);

                numbers[i] = AddText(root.transform, value.ToString(), at.y, FallbackStatSize,
                                     CardArt.InkOnPlate, y, at.x, StatsTextObject);
            }

            // ① 这一帧先试一次。★ 实测：**这一帧几乎总是量不到**（TextMesh 的网格要下一帧才有），
            //    所以上面就按"保底字号"（肯定不压边的那一档）造出来了，别让数字先以别的字号露一帧。
            FitStatNumbers(numbers, y);

            // ② 真正那一次交给它：晚一帧量到网格之后再把三个数字一起缩 + 居中
            CardStatNumberFit fit = root.gameObject.AddComponent<CardStatNumberFit>();
            fit.numbers = numbers;
            fit.textY   = y;
        }

        /// <summary>
        /// 把一组数字按各自那一格缩放 + 居中。**三个一起处理**，理由见下面 baseSize 那一段。
        ///
        /// **返回 false = 这一帧还读不到 TextMesh 网格**（实测：AddComponent 那一帧就是空的，
        /// 见 <see cref="CardStatNumberFit"/>）。这时什么也不动 —— 调用方已经把它们摆在
        /// 形状中心、字号也已经是保底档，晚一帧再量一次就好。
        /// ★ 千万别在这里按经验系数硬算一个字号：那会把"晚一帧量到的那次"覆盖掉一个更差的猜测。
        /// </summary>
        public static bool FitStatNumbers(GameObject[] gos, float y)
        {
            if (gos == null || gos.Length == 0) return false;

            int n = Mathf.Min(gos.Length, CardArt.StatSlots.Length);

            // ① 三个墨迹盒一起量。任何一个还没有网格就整组等下一帧 ——
            //    "三个数字一起定字号"这件事要求它们同一次全部量到，否则先量到的那个
            //    会拿一个只由自己决定的字号，和后来那两个对不上。
            Vector3[] inkCenter = new Vector3[n];
            Vector3[] inkSize   = new Vector3[n];
            float digitInkH = 0f;

            for (int i = 0; i < n; i++)
            {
                if (gos[i] == null) return false;
                if (!InkBox(gos[i], out inkCenter[i], out inkSize[i])) return false;

                // 数字都是**等高**的（0~9 都是齐线数字，没有一个带下伸部），所以任何一个串的
                // 墨迹高度就是这个字体的"数字字面高"。取最大的那个最保险（量到 1e-5 以下就说明网格是空的）。
                digitInkH = Mathf.Max(digitInkH, inkSize[i].y);
            }
            if (digitInkH <= 1e-5f) return false;

            // ② 基准字号 = 三个形状里**最紧的那一格**装**一位数**时的字号。
            //
            //    "一位数的宽高比"不用外部常量，**从这三个串自己反推**：
            //    数字是等宽的（齐线数字，advance 一样），一个 n 位数的墨迹宽 ≈ n × 一位数的墨迹宽，
            //    所以 ratio1 ≈ ratio_n / n —— 串本身是一位数时这个式子就是精确的。
            //    三个里取最小的那个（最保守），算出来的就是"装一位数的上限"。
            float ratio1 = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float ri = inkSize[i].x / Mathf.Max(1e-5f, inkSize[i].y);
                int digits = StatDigitCount(gos[i]);
                ratio1 = Mathf.Min(ratio1, ri / digits);
            }
            if (ratio1 <= 0.01f || ratio1 == float.MaxValue) ratio1 = CardArt.FallbackDigitRatio;

            float baseSize = float.MaxValue;
            for (int i = 0; i < CardArt.StatSlots.Length; i++)
            {
                float maxWorld = CardArt.EffectPxToWorld(
                    CardArt.StatMaxDigitHeightPx(i, ratio1));
                if (maxWorld > 0f) baseSize = Mathf.Min(baseSize, maxWorld / digitInkH);
            }
            if (baseSize <= 0f || baseSize == float.MaxValue) baseSize = FallbackStatSize;
            baseSize = Mathf.Min(baseSize, MaxStatSize);      // 上限：别把版面顶穿

            // ③ 逐格：min(基准, 这一格在**这个串**下的上限)，然后把墨迹盒中心对准格子中心
            for (int i = 0; i < n; i++)
            {
                float ratio    = inkSize[i].x / Mathf.Max(1e-5f, inkSize[i].y);  // 与缩放无关
                float maxPx    = CardArt.StatMaxDigitHeightPx(i, ratio);         // 这一格装得下的最大高度（效果图像素）
                float maxWorld = CardArt.EffectPxToWorld(maxPx);
                float size     = Mathf.Min(baseSize, maxWorld / digitInkH);

                gos[i].transform.localScale = Vector3.one * size;

                // 文字对象的局部轴 → 根节点局部轴的映射由它自己的 localRotation 给出
                // （AddText 里那次 LookRotation(down, forward)：局部 X→根 X、局部 Y→根 Z），
                // 所以"墨迹盒中心偏了多少"要按同一个旋转搬过去，别手写这三个分量。
                Vector2 at = CardArt.EffectPxToLocal(CardArt.StatSlots[i].centerPx);
                Vector3 offset = gos[i].transform.localRotation * (inkCenter[i] * size);
                gos[i].transform.localPosition = new Vector3(at.x, y, at.y) - offset;
            }

            return true;
        }

        /// <summary>
        /// 这个数字文字对象上是**几位数**（最少算 1 位）。
        /// 用来从"n 位数的墨迹宽高比"反推"一位数的宽高比"（见 <see cref="FitStatNumbers"/> ②）。
        /// </summary>
        private static int StatDigitCount(GameObject go)
        {
            TextMesh tm = go != null ? go.GetComponent<TextMesh>() : null;
            if (tm == null || string.IsNullOrEmpty(tm.text)) return 1;
            return Mathf.Max(1, tm.text.Length);
        }

        /// <summary>
        /// 一个文字对象的**墨迹盒**：宽 × 高（折算到 localScale = 1 时的**局部**尺寸）
        /// 和它在文字对象局部坐标里的中心。返回 false = 现在还没有网格（晚一帧再试）。
        ///
        /// 【★ 为什么不去读 MeshFilter.sharedMesh —— 实测那条路在这台机器上永远走不通】
        ///   TextMesh 生成出来的网格**不放在 MeshFilter 的序列化字段里**：
        ///   `sharedMesh` 是 null，而 `mf.mesh` 会**现场新建一个空网格**返回（顶点 0 个）。
        ///   实测：三个数字等了 12 帧、24 张卡全部报"量不到墨迹盒"，
        ///   而同一批卡在屏幕上明明画着数字。
        ///   → 只能走 `Renderer.bounds`（这个一定是对的，它来自真正在渲染的那份网格）。
        ///
        /// 【★ 但 bounds 是**世界** AABB，不能直接用】
        ///   卡是带朝向摆的（手牌是扇形、桌面卡有偏航），同一个墨迹盒转个角度，
        ///   世界 AABB 就被撑大 —— 实测同一串 "12"、同一个字号在不同卡上量出
        ///   12.0 / 12.9 px 两个值。拿它算字号会得到"看卡朝向而定"的结果。
        ///
        /// 【还原的办法】文字网格是一个**平的**矩形（顶点都在 z = 0 上），
        ///   所以它在自己局部坐标里就是一个 2D 盒子 (a, b, 0)。世界 AABB 的三个**边长**满足
        ///       h_i = |R_i0|·a + |R_i1|·b        （R = 这个文字对象的世界旋转，i = x/y/z）
        ///   三个方程、两个未知数 —— 取**条件数最好**的两行解出来即可，结果与朝向无关、精确。
        ///   （正交阵的行两两线性无关，|行列式| 最大的那一对就是最稳的那一对。）
        ///   ★ 这里用的必须是 wb.**size**（边长）而不是 wb.extents（半长）：
        ///     方程两边同为"半长"时才成立的是半长版本 —— 混用会让墨迹盒**少算一半**，
        ///     表现是数字大得压出形状（实测踩过：0.01020 的字号在 26 px 的圆里溢出来）。
        ///
        /// 中心不需要解：盒子关于自己的中心对称，旋转不改变 AABB 的中心，
        ///   所以世界 AABB 的中心就是盒子中心 —— 逆变换回局部坐标即可。
        /// </summary>
        public static bool InkBox(GameObject go, out Vector3 center, out Vector3 size)
        {
            center = Vector3.zero;
            size   = Vector3.zero;
            if (go == null) return false;

            Renderer r = go.GetComponent<Renderer>();
            if (r == null) return false;

            Bounds wb = r.bounds;
            if (wb.size.x <= 1e-7f && wb.size.y <= 1e-7f && wb.size.z <= 1e-7f) return false;

            // 世界 AABB 的三个**边长**，和"世界旋转"的绝对值矩阵
            float[] h = { wb.size.x, wb.size.y, wb.size.z };
            Matrix4x4 m = Matrix4x4.Rotate(r.transform.rotation);
            float[,] a = new float[3, 2];
            for (int i = 0; i < 3; i++)
            {
                a[i, 0] = Mathf.Abs(m[i, 0]);   // 局部 X（墨迹宽）对世界第 i 轴的贡献
                a[i, 1] = Mathf.Abs(m[i, 1]);   // 局部 Y（墨迹高）对世界第 i 轴的贡献
            }

            int bi = 0, bj = 1;
            float bestDet = -1f;
            int[,] pairs = { { 0, 1 }, { 0, 2 }, { 1, 2 } };
            for (int p = 0; p < 3; p++)
            {
                int i = pairs[p, 0], j = pairs[p, 1];
                float det = a[i, 0] * a[j, 1] - a[i, 1] * a[j, 0];
                if (det > bestDet) { bestDet = det; bi = i; bj = j; }
            }
            if (bestDet <= 1e-6f) return false;     // 两行都退化 = 这个旋转量不出来（正常摆位到不了）

            float inkW = (h[bi] * a[bj, 1] - h[bj] * a[bi, 1]) / bestDet;
            float inkH = (a[bi, 0] * h[bj] - a[bj, 0] * h[bi]) / bestDet;
            if (inkW <= 1e-7f || inkH <= 1e-7f) return false;

            // 世界长度 ÷ 这个文字对象自己的缩放 = localScale = 1 时的墨迹盒
            Vector3 s = go.transform.localScale;
            size = new Vector3(inkW / Mathf.Max(1e-6f, Mathf.Abs(s.x)),
                               inkH / Mathf.Max(1e-6f, Mathf.Abs(s.y)),
                               0f);

            center = go.transform.InverseTransformPoint(wb.center);
            return true;
        }

        /// <summary>
        /// 量不到字体网格时的保底字号 —— 故意取**比能装下的更小**的一档：
        /// 这一档是"肯定不压边"的下限，不是"刚好装满"。
        /// （按实测的数字墨迹高 ≈ localScale × 5.26 反推：0.0042 × 5.26 ≈ 0.0221 世界高，
        ///   而最紧的那一格——菱形——装得下约 0.0272，留了两成余量。）
        ///
        /// ★ 它只该出现在"那一帧还没网格"的时候：晚一帧 <see cref="CardStatNumberFit"/>
        ///   会把真正的字号算出来。真的一直算不出来，日志里会有一条警告。
        /// </summary>
        private const float FallbackStatSize = 0.0042f;

        /// <summary>三个数字的字号上限（防止几何算出来的值把版面顶穿）。</summary>
        private const float MaxStatSize = 0.0120f;

        // ── 文本内容 ──────────────────────────────────────────────────

        private static readonly Color InkColor = new Color(0.145f, 0.132f, 0.118f);

        private static Color InkOnPaper() { return InkColor; }

        /// <summary>
        /// 三属性写成三行 —— **只给程序化卡面用**（没有美术贴图时那条退路）。
        ///
        /// 有正式美术的卡面上不再出现这些字：数值牌上本来就有三个装饰形状，
        /// 三个数字直接进那三格（见 <see cref="AddStatNumbers"/>），
        /// "H""盐性"这些字样按用户要求全部去掉。
        ///
        /// 以前是一行 "H 盐性 1 · D 汞性 9 · V 硫性 1"，实机上一行排不下 0.24 的卡宽，
        /// 文字直接横跨到旁边几张卡上去。改成每个属性一行，宽度立刻够用。
        /// </summary>
        private static string StatsText(Ingredient ing)
        {
            StringBuilder sb = new StringBuilder();
            foreach (AttrDef def in AttrCatalog.All())
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(def.Label).Append(' ').Append(ing.attrs.Get(def.id));
            }
            return sb.ToString();
        }

        // ── 文字 ──────────────────────────────────────────────────────

        /// <summary>
        /// 在卡面上放一行文字，平躺在桌面上。
        ///
        /// ★★ 朝向推导（这一步以前写错过，文字整片镜像）★★
        ///
        /// 症状：文字能看出是什么字，但左右翻了个面，且没有上下颠倒。
        /// 这个组合只有一种解释 —— **我们看到了字形的背面**。
        /// 因为"绕竖直轴看反面"恰好是横向翻转、纵向不变；
        /// 而如果是上下颠倒，纵向也会翻。
        ///
        /// 所以：TextMesh 的可读面朝局部 **−Z**（不是 +Z）。
        /// 要让可读面朝上给相机看，就必须让 **局部 +Z 朝下**。
        ///
        /// 再定另外两个轴：局部 +Y 是文字的"上"，要朝远端（屏幕上方向）= 世界 +Z。
        /// 剩下 +X 由右手法则算出来 —— X = Y × Z = (0,0,1) × (0,−1,0) = (1,0,0)，
        /// 正好是世界 +X，也就是屏幕上向右的方向，文字才是正着读的。
        ///
        /// 对照旧代码：旧版用 LookRotation(up, forward)，即 +Z 朝上、+Y 朝远端，
        /// 这时 X 被强制算成世界 −X，于是横向镜像 —— 就是截图里那个现象。
        ///
        /// ★ 如果哪天发现文字整体上下颠倒了，把 LookRotation(Vector3.down, ...)
        ///   里的 down 换成 up 即可（那说明这台机器上 TextMesh 的可读面约定相反）。
        ///
        /// 【为什么是 public】牌组卡片（TableChoiceRig）也要在桌面放中文，
        /// 上面这段朝向推导踩过坑、只有一个正确答案，不能让它有第二份抄本。
        /// </summary>
        public static GameObject AddText(Transform parent, string text, float localZ, float size, Color color)
        {
            return AddText(parent, text, localZ, size, color, CardThick * 0.5f + 0.0022f);
        }

        /// <summary>
        /// 同上，但自己指定文字离卡面的高度。
        ///
        /// ★ 卡牌厚度不是处处都一样的：牌组卡有 0.018，手牌只有 0.008。
        ///   沿用默认高度的话，牌组卡的标题会被埋进卡身里，一个字都看不见 ——
        ///   （踩过：三张牌组卡的色带全是空白的。）所以把 Y 开出来当参数。
        ///
        /// localX 是给正式美术版面用的：美术的名字牌在左上角、数值牌在右侧，
        /// 文字得跟着离开中线。程序化卡面继续传 0（居中）。
        ///
        /// objectName 是给"上层按用途找文字"用的（见 <see cref="NameTextObject"/> /
        /// <see cref="StatsTextObject"/>）：级联要把被压住那张的**数值**藏起来、
        /// **名字**留在露出区，靠的就是它，而不是靠猜"两个 Text 谁是谁"。
        ///
        /// ★★ sortingOrder **不用传**：默认值就是 <see cref="CardTextSortingOrder"/>
        ///    （"排在卡面之后"）。这是卡面文字唯一的出口 —— 谁都不该在这里另传一个数，
        ///    理由见那个常量那一大段（牌组选择界面下半屏一个字都看不见，就是漏了它）。
        ///
        /// 返回新建的文字对象：调用方要按**真实墨迹盒**再缩一次 / 再居中时用得上
        /// （见 <see cref="FitStatNumber"/>）。不需要就照旧当它是 void 用。
        /// </summary>
        public static GameObject AddText(Transform parent, string text, float localZ, float size,
                                         Color color, float localY, float localX = 0f,
                                         string objectName = "Text",
                                         int sortingOrder = CardTextSortingOrder)
        {
            GameObject go = new GameObject(string.IsNullOrEmpty(objectName) ? "Text" : objectName);
            go.transform.SetParent(parent, false);

            go.transform.localPosition = new Vector3(localX, localY, localZ);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            go.transform.localScale = Vector3.one * size;

            TextMesh tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 48;                 // 配合 localScale 缩放，实际字号由 size 决定
            tm.color = color;
            tm.characterSize = 1f;
            tm.richText = false;

            Font f = CjkFont();
            if (f != null)
            {
                tm.font = f;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = f.material;
            }

            // ★ **所有**卡面文字都排在卡面之后 —— 见 CardTextSortingOrder 那一大段
            MeshRenderer rend = go.GetComponent<MeshRenderer>();
            if (rend != null) rend.sortingOrder = sortingOrder;

            return go;
        }

        /// <summary>卡面**名字**那一行文字的对象名（上层按用途找它：级联要保证它一直露着）。</summary>
        public const string NameTextObject = "NameText";

        /// <summary>
        /// 卡面**数值**文字的对象名 —— 正式美术卡面上是**三个**（H/D/V 三个数字，
        /// 见 <see cref="AddStatNumbers"/>），程序化卡面是一个（那三行字）。
        ///
        /// ★ 所以它**不是唯一的**：要一起显隐请走 <see cref="SetStatsVisible"/>，
        ///   不要用 `transform.Find(名字)` 只拿到第一个。
        /// </summary>
        public const string StatsTextObject = "StatsText";

        /// <summary>
        /// 把一张卡上**全部**数值文字一起显示 / 隐藏。
        ///
        /// 【为什么要有这个出口】级联要用它把"被压住那几张"的数值藏起来
        ///   （理由见 <see cref="CardTextSortingOrder"/> 那段：文字是 ZTest Always，
        ///   几何上盖不住，只能由布局决定显不显示）。
        ///   而正式美术卡面上的数值是**三个**同样叫 StatsText 的对象 ——
        ///   老写法 `transform.Find("StatsText").GetComponent&lt;Renderer&gt;()`
        ///   只会拿到第一个，另外两个数字照样浮在压着它的那张卡上。
        ///   一处实现，省得以后谁又去 Find 一次。
        /// </summary>
        public static void SetStatsVisible(Transform cardRoot, bool visible)
        {
            if (cardRoot == null) return;

            for (int i = 0; i < cardRoot.childCount; i++)
            {
                Transform child = cardRoot.GetChild(i);
                if (child == null || child.name != StatsTextObject) continue;

                Renderer r = child.GetComponent<Renderer>();
                if (r != null) r.enabled = visible;
            }
        }

        /// <summary>
        /// **卡面上所有文字**的渲染次序：排在卡面之后。这是全工程唯一一处设定，
        /// 由 <see cref="AddText"/> 的默认参数生效 —— 新加文字什么都不用传。
        ///
        /// ★★ 它修的是同一类问题的两种表现 ★★
        ///
        /// 【症状一（上一轮）】**桌面视角下所有卡的名字牌都是空的**，
        ///   而同一张卡上的 H/D/V 却清清楚楚。不是摆位问题 ——
        ///   还没做级联之前（上一轮）的截图 docs/verify/black-table-fix/05 / 10 / 14
        ///   三张里，名字牌就同样是空的。
        ///
        /// 【症状二（本轮，用户新截图）】**牌组选择界面下半屏那三张牌一个字的没有** ——
        ///   标题、食材行、模块行、开局刀片行全不见，而上面一排的标题看得见。
        ///   同一批卡、同一屏，只是**一部分**被盖住。
        ///
        /// 【根因：两个透明物体谁盖谁，是按"包围盒中心离相机多远"定的】
        ///   卡面走 <see cref="MakeUnlit"/> → `Sprites/Default`，也就是 **Transparent 队列（3000）**；
        ///   文字用字体材质（GUI/Text Shader），队列也是 3000。
        ///   同一队列里，Unity 先按 **sortingOrder** 排、同 order 再按包围盒中心距离排
        ///   （远的先画、近的后画）。于是"谁盖谁"就取决于机位和文字在卡上的位置：
        ///     · 桌面视角（相机在近侧、43° 俯角）：**卡面中心比名字更靠近相机** ——
        ///       名字在卡远端（`CardArt.NameOnPlate.z` = +0.136）、卡面中心在卡心 →
        ///       卡面后画、把名字整片盖住 ✗；
        ///     · 牌组卡的文字全在近端（localZ = −0.075 ~ −0.215），
        ///       按"近的应该后画"本该可见 —— 可那是**默认机位**下的算法；
        ///       用户那张是偏俯视：视线几乎竖直，比较的就不再是 z 而是高度 y，
        ///       而卡面（y = 卡厚 + 0.0006）和文字（y = 卡厚 + 0.0022）只差 1.6 毫米，
        ///       哪个更近完全由机位和卡身尺寸的小数决定 —— 于是同一屏里
        ///       **一部分卡的文字沉到卡面下面、另一部分浮在上面**，看着毫无规律。
        ///   两个症状一个根因：**把文字和卡面丢给"距离"去排，就是让它随机**。
        ///
        /// 【修法：把次序写死，不让距离参与】所有卡面文字统一 sortingOrder = 1，
        ///   卡面（<see cref="MakeUnlit"/> 造的那些）全是默认的 0 ——
        ///   于是"文字一定在卡面之后画"成了常量事实，跟机位、卡身尺寸、卡在屏幕哪一块都无关。
        ///   这一条**不能只给名字**：只给名字就是上一轮的写法，本轮用户那张截图就是它的后果。
        ///
        /// 【被压住的卡怎么办（这条是硬约束，别改）】
        ///   文字材质是 **ZTest Always**（GUI/Text Shader 写死的），深度缓冲挡不住它 ——
        ///   所以"被上面那张卡压住的数值会透出来"这件事只能**由布局决定显不显示**：
        ///   见 TableRulesV21 级联那一节（被压住的卡只留名字，数值用
        ///   <see cref="SetStatsVisible"/> 显式关掉）。现在三个数字都落在卡面以内、
        ///   不再像老版三行字那样"比卡还宽"，几何上盖得住；但 ZTest Always 依旧，
        ///   所以那条布局规则一个字都不能少。
        ///   名字则天生安全：它在卡的**露出区**里，压着它的那张卡根本不在那一块。
        ///
        /// 【为什么不去动卡面材质（★ 试过，别再试）】卡面是 `Sprites/Default`：**ZWrite Off**
        ///   （圆角与边缘的半透明靠混合抠出来，见 CardArt 里修脏边那一大段）。把它的队列压到
        ///   Geometry（2000）会落进不透明批次，而卡身（Body 立方体、Standard、ZWrite On）也在 2000 ——
        ///   不透明批次按"近→远"排，卡面（y 更高、更近）会先画、卡身随后把卡面整个盖掉
        ///   （卡面自己不写深度）；反过来给卡面开 ZWrite 抢在前面，圆角处的半透明边就变成
        ///   硬边/黑边，还会挡掉圆角外的桌面 —— 正是 CardArt 花大力气修掉的那圈脏边。
        ///   上一轮实测过一次"改卡面队列"：第 6 张牌组卡的卡面整个没了。
        ///   所以卡面材质 / ZWrite / Blend **一个字节都不动**，只动文字的次序。
        /// </summary>
        public const int CardTextSortingOrder = 1;
    }
}
