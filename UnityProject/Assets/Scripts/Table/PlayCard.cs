using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    /// <summary>
    /// 一张 3D 卡牌。
    ///
    /// 【这张卡身上有什么】
    ///   - BoxCollider：在子物体 Body 上（立方体自带，会跟着缩放走），供射线拾取
    ///   - Rigidbody：在根节点，默认 isKinematic，走脚本控制；按 G 放开物理后受重力、和桌子真实碰撞
    ///   - 平滑移动：位置用指数插值逼近目标点，不会瞬移
    ///
    /// 【为什么碰撞体在子物体上】
    /// 卡牌本体是个被缩放到 (0.24, 0.008, 0.335) 的立方体。
    /// 文字如果挂在它下面会被这个非等比缩放拉变形，
    /// 所以结构拆成：根节点(scale=1, 脚本+刚体) → Body(立方体, 碰撞体) / Label(文字)。
    ///
    /// 【职责边界】它只管"自己怎么动、长什么样"，
    /// 不知道谁在拖它、也不知道桌面有几个槽 —— 那些在 TableInteraction / TableBoard 里。
    /// </summary>
    public class PlayCard : MonoBehaviour
    {
        /// <summary>对应的食材数据（模块牌这里是 null）</summary>
        public Ingredient data;

        /// <summary>
        /// 对应的变速模块数据（食材牌这里是 null）。
        ///
        /// 【为什么是两个字段而不是一个 object】
        /// Ingredient 和 SpeedModule 是两个互不相干的 [Serializable] 类，没有共同基类。
        /// 塞进 object 会丢掉类型安全，每处取值都得强转 + 判空 + 猜类型。
        /// 两个字段加一个 IsModule 判断，读写反而更干净。
        /// </summary>
        public SpeedModule module;

        /// <summary>这张牌是变速模块还是食材。</summary>
        public bool IsModule { get { return module != null; } }

        /// <summary>界面上显示的名字（食材名 / 模块名）。</summary>
        public string DisplayName
        {
            get
            {
                if (module != null) return module.name;
                return data != null ? data.name : "?";
            }
        }

        /// <summary>类型标签 —— HUD 和检视面板靠它区分两类牌。</summary>
        public string TypeTag { get { return IsModule ? "变速模块" : "食材"; } }

        /// <summary>所在卡槽索引。-1 表示不在槽位上（在手牌 / 被拿在手里）</summary>
        public int slotIndex = -1;

        /// <summary>手牌原位 —— 放不出去时回到这里</summary>
        public Vector3 homePosition;
        public Vector3 homeEuler;

        public bool IsHovered  { get; private set; }
        public bool IsDragging { get; private set; }

        private Rigidbody rb;

        // 需要跟着悬停/拖动一起变亮的渲染器。
        // 一张卡现在有两个：Body（卡身边缘）和 Face（卡面贴图）。
        // 各自的"原始颜色"要分开记 —— 卡身是深色、卡面是白色贴图，
        // 共用一个基准色会把卡面乘暗。
        private Renderer[] tintRenderers = new Renderer[0];
        private Color[]    tintBase      = new Color[0];

        private Vector3    targetPos;
        private Quaternion targetRot;
        private float      followSpeed = 16f;

        private const float LiftHover = 0.030f;   // 悬停抬高
        private const float LiftDrag  = 0.080f;   // 拖动抬高
        private const float TiltDragX = 16f;      // 拖动时朝相机前倾的角度

        // ── 初始化 ────────────────────────────────────────────────────

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// 由 CardFactory 指定要参与高亮的渲染器。
        /// 卡身、卡面都是子物体，用 GetComponentInChildren 猜不可靠，所以显式绑。
        /// </summary>
        public void BindRenderer(Renderer body, params Renderer[] extras)
        {
            int extraCount = extras != null ? extras.Length : 0;

            tintRenderers = new Renderer[1 + extraCount];
            tintBase      = new Color[1 + extraCount];

            tintRenderers[0] = body;
            tintBase[0]      = ReadBaseColor(body);

            for (int i = 0; i < extraCount; i++)
            {
                tintRenderers[i + 1] = extras[i];
                tintBase[i + 1]      = ReadBaseColor(extras[i]);
            }
        }

        private static Color ReadBaseColor(Renderer r)
        {
            if (r == null || r.material == null) return Color.white;
            Color c = r.material.color;
            c.a = 1f;
            return c;
        }

        /// <summary>绑定食材数据并放到手牌原位。</summary>
        public void Setup(Ingredient ing, Vector3 home, Vector3 euler)
        {
            data = ing;
            module = null;
            SetHome(home, euler);
            Teleport(home, euler);
            gameObject.name = "Card_" + (ing != null ? ing.name : "?");
        }

        /// <summary>绑定变速模块数据并放到手牌原位。</summary>
        public void SetupModule(SpeedModule mod, Vector3 home, Vector3 euler)
        {
            module = mod;
            data = null;
            SetHome(home, euler);
            Teleport(home, euler);
            gameObject.name = "Module_" + (mod != null ? mod.name : "?");
        }

        /// <summary>
        /// 重设"手牌原位"。
        ///
        /// 手牌会因为投放而少人，剩下的牌得重新居中排一遍 ——
        /// 所以原位不能只在开局定一次。这里**不瞬移**，只改目标点，
        /// 让剩下的牌自己滑过去（瞬移会让每次投放都闪一下）。
        /// </summary>
        public void SetHome(Vector3 home, Vector3 euler)
        {
            homePosition = home;
            homeEuler    = euler;

            if (consuming) return;   // 正飞向罐子的牌不参与重排

            targetPos = home;
            targetRot = Quaternion.Euler(euler);
        }

        // ── 目标控制 ──────────────────────────────────────────────────

        /// <summary>平滑落位到指定世界坐标（吸附到卡槽时用）。</summary>
        public void SnapTo(Vector3 pos)
        {
            targetPos = pos;
            targetRot = Quaternion.Euler(homeEuler);
        }

        /// <summary>回到手牌原位。</summary>
        public void ReturnHome()
        {
            targetPos = homePosition;
            targetRot = Quaternion.Euler(homeEuler);
        }

        /// <summary>拖动中：由交互层每帧喂目标点。</summary>
        public void DragTo(Vector3 pos)
        {
            targetPos = pos;
        }

        public void SetHover(bool on) { IsHovered = on; }

        public void BeginDrag()
        {
            IsDragging = true;
            if (rb != null) rb.isKinematic = true;   // 拖动时脚本接管，避免物理打架
        }

        public void EndDrag() { IsDragging = false; }

        /// <summary>开关物理。开启后受重力，会和桌子 / 其它卡真实碰撞。</summary>
        public void SetPhysics(bool on)
        {
            if (rb == null) return;
            rb.isKinematic = !on;
            if (on)
            {
                rb.useGravity = true;
                IsDragging = false;
            }
        }

        public bool PhysicsOn { get { return rb != null && !rb.isKinematic; } }

        /// <summary>把卡瞬间挪到某处（初始化 / 复位用，不做插值）。</summary>
        public void Teleport(Vector3 pos, Vector3 euler)
        {
            targetPos = pos;
            targetRot = Quaternion.Euler(euler);
            transform.position = pos;
            transform.rotation = targetRot;
        }

        // ── 投进杯子 ──────────────────────────────────────────────────

        private bool    consuming;
        private Vector3 consumeTarget;

        /// <summary>
        /// 被投进杯子：飞向罐口、边飞边缩小，到点自毁。
        ///
        /// 【为什么不是直接 Destroy】
        /// 直接删的话，玩家看到的是"按下确认，牌凭空消失"——
        /// 而"牌被机器吸进去"这一段恰恰是确认键唯一的反馈。
        /// </summary>
        public void ConsumeInto(Vector3 target, float lifeSeconds)
        {
            consuming     = true;
            consumeTarget = target;
            IsDragging    = false;
            IsHovered     = false;

            if (rb != null) rb.isKinematic = true;

            // 飞行途中不能再被射线拾取，否则会被鼠标半路"抓"回来
            Collider col = GetComponentInChildren<Collider>();
            if (col != null) col.enabled = false;

            Destroy(gameObject, Mathf.Max(0.1f, lifeSeconds));
        }

        /// <summary>正在飞向罐子（表里已经在数它离场了）。</summary>
        public bool IsConsuming { get { return consuming; } }

        // ── 每帧 ──────────────────────────────────────────────────────

        void Update()
        {
            if (consuming)
            {
                // 比平时快得多的速度扑向罐口，同时缩到几乎看不见。
                // 插值系数不能叫 k —— 下面那个作用域里已经有一个 k 了，
                // C# 不允许内层作用域重名。
                float kc = 1f - Mathf.Exp(-11f * Time.deltaTime);
                transform.position   = Vector3.Lerp(transform.position, consumeTarget, kc);
                transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot, kc);
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * 0.10f, kc);
                return;
            }

            if (rb != null && !rb.isKinematic)
            {
                // 物理接管中：位置交给 Rigidbody，只更新高亮
                UpdateTint();
                return;
            }

            Vector3 want = targetPos;
            if (IsDragging)     want.y += LiftDrag;
            else if (IsHovered) want.y += LiftHover;

            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);   // 帧率无关的指数逼近

            transform.position = Vector3.Lerp(transform.position, want, k);

            Quaternion wantRot = IsDragging
                ? Quaternion.Euler(homeEuler.x + TiltDragX, homeEuler.y, homeEuler.z)
                : targetRot;

            transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, k);

            UpdateTint();
        }

        private void UpdateTint()
        {
            if (tintRenderers == null || tintRenderers.Length == 0) return;

            float mul = IsDragging ? 1.30f
                      : IsHovered  ? 1.15f
                                   : 1f;
            float k = 1f - Mathf.Exp(-18f * Time.deltaTime);

            for (int i = 0; i < tintRenderers.Length; i++)
            {
                Renderer r = tintRenderers[i];
                if (r == null || r.material == null) continue;

                Color want = tintBase[i] * mul;
                want.a = 1f;    // 圆角靠贴图自己的 alpha 抠，这里不能动 alpha
                r.material.color = Color.Lerp(r.material.color, want, k);
            }
        }
    }
}
