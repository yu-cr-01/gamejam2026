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
    ///       └─ StatsText          TextMesh：三属性，每个属性一行
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
                sub      = (c.ingredient != null && c.ingredient.attrs != null)
                           ? StatsText(c.ingredient) : null;
            }

            PlayCard card = BuildCard(c.name, accent, dominant, sub, parent, CardArt.ElementOf(c));
            card.Setup(c, home, euler);
            return card;
        }

        /// <summary>
        /// 两类卡共用的建模部分。
        /// 只负责"长什么样"，绑定数据交给调用方 —— 食材和模块的绑定接口不一样。
        ///
        /// artElement 是正式美术的元素皮肤名（见 CardArt）；给 null 或美术缺图时
        /// 整张卡退回程序化卡面，连文字版面一起退 —— 两套版面不能混着用。
        /// </summary>
        private static PlayCard BuildCard(string title, Color accent, AttrId dominant,
                                          string subText, Transform parent, string artElement)
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
            //   正式美术 —— 名字落在左上角的名字牌里、三属性落在右侧数值牌上（位置量自美术效果图）
            if (artFace)
            {
                float y = CardThick * 0.5f + 0.0022f;
                AddText(root.transform, title, CardArt.NameOnPlate.z, CardArt.NameOnPlateSize,
                        CardArt.InkOnPlate, y, CardArt.NameOnPlate.x,
                        NameTextObject, NameTextSortingOrder);

                if (!string.IsNullOrEmpty(subText))
                    AddText(root.transform, subText, CardArt.StatsOnPlate.z, CardArt.StatsOnPlateSize,
                            CardArt.InkOnPlate, y, CardArt.StatsOnPlate.x, StatsTextObject);
            }
            else
            {
                AddText(root.transform, title, NameZ, NameSize, ProceduralArt.InkOn(accent),
                        CardThick * 0.5f + 0.0022f, 0f,
                        NameTextObject, NameTextSortingOrder);

                // 副文本：食材写三属性，模块写效果描述
                if (!string.IsNullOrEmpty(subText))
                    AddText(root.transform, subText, StatsZ, StatsSize, InkOnPaper(),
                            CardThick * 0.5f + 0.0022f, 0f, StatsTextObject);
            }

            // 卡身和卡面都要跟着悬停/拖动变亮，所以两个渲染器都绑上
            card.BindRenderer(bodyRenderer, faceRenderer);
            return card;
        }

        // ── 文本内容 ──────────────────────────────────────────────────

        private static readonly Color InkColor = new Color(0.145f, 0.132f, 0.118f);

        private static Color InkOnPaper() { return InkColor; }

        /// <summary>
        /// 三属性写成三行。
        ///
        /// 以前是一行 "H 盐性 1 · D 汞性 9 · V 硫性 1"，实机上一行排不下 0.24 的卡宽，
        /// 文字直接横跨到旁边几张卡上去了。改成每个属性一行，宽度立刻够用。
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
        public static void AddText(Transform parent, string text, float localZ, float size, Color color)
        {
            AddText(parent, text, localZ, size, color, CardThick * 0.5f + 0.0022f);
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
        /// objectName / sortingOrder 是给"上层按用途找文字"用的（见
        /// <see cref="NameTextObject"/> / <see cref="StatsTextObject"/> 与
        /// <see cref="NameTextSortingOrder"/>）：级联要把被压住那张的**数值**藏起来、
        /// **名字**留在露出区，靠的就是这两个参数，而不是靠猜"两个 Text 谁是谁"。
        /// </summary>
        public static void AddText(Transform parent, string text, float localZ, float size,
                                   Color color, float localY, float localX = 0f,
                                   string objectName = "Text", int sortingOrder = 0)
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

            // ★ 名字要比卡面后画 —— 见 NameTextSortingOrder 那段（"名字在桌面视角看不见"的根因）
            MeshRenderer rend = go.GetComponent<MeshRenderer>();
            if (rend != null) rend.sortingOrder = sortingOrder;
        }

        /// <summary>卡面**名字**那一行文字的对象名（上层按用途找它：级联要保证它一直露着）。</summary>
        public const string NameTextObject = "NameText";

        /// <summary>卡面**三属性**（H/D/V）那几行文字的对象名。</summary>
        public const string StatsTextObject = "StatsText";

        /// <summary>
        /// **名字**文字的渲染次序：排在卡面之后。
        ///
        /// ★★ 它修的是"桌面视角下名字看不见"★★
        ///
        /// 【症状】**桌面视角下所有卡的名字牌都是空的**，而同一张卡上的 H/D/V 却清清楚楚。
        ///   这不是摆位问题 —— 还没做级联之前（上一轮）的截图
        ///   docs/verify/black-table-fix/05 / 10 / 14 三张里，名字牌就同样是空的。
        ///
        /// 【根因：两个透明物体谁盖谁，是按"包围盒中心离相机多远"定的】
        ///   卡面走 <see cref="MakeUnlit"/> → `Sprites/Default`，也就是 **Transparent 队列（3000）**；
        ///   文字用字体材质（GUI/Text Shader），队列也是 3000。
        ///   同一队列里 Unity 按渲染器包围盒中心的距离排序（远的先画、近的后画），于是：
        ///     · 桌面视角（相机在近侧、43° 俯角）：**卡面中心比名字更靠近相机** ——
        ///       名字在卡远端（`CardArt.NameOnPlate.z` = +0.136），卡面中心在卡心 →
        ///       卡面后画、把名字整片盖住 ✗；
        ///     · H/D/V 在卡近端（`StatsOnPlate.z` = −0.087）→ 比卡面中心更近 → 后画 → 一直可见 ✓；
        ///     · 俯视（相机在正上方）时文字 y = 0.0062 > 卡面 y = 0.0046 → 文字更近 → 后画 → 可见 ✓。
        ///   三个现象对得上，这条判断才算"根因"而不是"猜的"。
        ///
        /// 【为什么只抬名字、不抬 H/D/V】★ 这一条是实测出来的：
        ///   把**所有**文字都抬到卡面之后，被压住那几张的 H/D/V 就"透"到了压着它的卡身上
        ///   （级联的实测截图里三行数值浮在上一张卡的插画上）。原因是两条叠在一起：
        ///     ① 文字材质是 **ZTest Always**（GUI/Text Shader 写死的），深度缓冲挡不住它；
        ///     ② 数值牌的文字**比卡还宽**（CardArt 的版面如此），压在它上面的那张卡
        ///        在几何上盖不住"露到卡外的那一截"。
        ///   所以数值必须**由布局来决定显不显示**（见 TableRulesV21 级联那一节：
        ///   被压住的卡只留名字），而不是靠排序去抢 —— 抢不干净。
        ///   名字则天生安全：它在卡的**露出区**里，压着它的那张卡根本不在那一块。
        ///
        /// 【为什么不去动卡面材质】卡面是 `Sprites/Default`：**ZWrite Off**（圆角与边缘的
        ///   半透明靠混合抠出来，见 CardArt 里修脏边那一大段）。把它的队列压到 Geometry（2000）
        ///   会落进不透明批次，而卡身（Body 立方体，Standard、ZWrite On）也在 2000 ——
        ///   不透明批次按"近→远"排，卡面（y 更高、更近）会先画、卡身随后把卡面整个盖掉
        ///   （卡面自己不写深度）；反过来给卡面开 ZWrite 抢在前面，圆角处的半透明边就变成
        ///   硬边/黑边，还会挡掉圆角外的桌面 —— 正是 CardArt 花大力气修掉的那圈脏边。
        ///   两条都是"修好名字、弄坏卡面"，所以卡面一个字节都不动。
        /// </summary>
        private const int NameTextSortingOrder = 1;
    }
}
