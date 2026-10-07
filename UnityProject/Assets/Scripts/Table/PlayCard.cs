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
        /// <summary>
        /// 这张牌的数据。
        ///
        /// 【为什么不再分 data / module 两个字段】
        /// 以前是 `Ingredient data` + `SpeedModule module` + IsModule 判断。
        /// 那个设计把"这张是哪种牌"的判断留在了**表现层**，
        /// 结果同一个判断在四个文件里抄了 15 遍。
        /// 现在统一成 Card —— 3D 卡不需要知道自己是食材还是模块，
        /// 需要的时候问 card.kind 就行。
        /// </summary>
        public Card card;

        /// <summary>这张牌是变速模块。</summary>
        public bool IsModule { get { return card != null && card.IsModule; } }

        /// <summary>这张牌是食材。</summary>
        public bool IsIngredient { get { return card != null && card.IsIngredient; } }

        /// <summary>界面上显示的名字。</summary>
        public string DisplayName { get { return card != null ? card.name : "?"; } }

        /// <summary>类型标签 —— HUD 和检视面板靠它区分两类牌。</summary>
        public string TypeTag { get { return card != null ? card.TypeTag : "?"; } }

        /// <summary>所在卡槽索引。-1 表示不在槽位上（在手牌 / 被拿在手里）</summary>
        public int slotIndex = -1;

        /// <summary>
        /// v2.1：这张 3D 卡对应的**手牌素材**（规则侧的对象）。
        ///
        /// 【为什么要把引用直接挂在卡上，而不是靠名字去找】
        ///   手牌 3D 卡是"照着手牌列表逐张造出来"的，造卡时顺手把引用绑上去是最稳的。
        ///   靠显示名去找曾经踩过：卡面名字里带着 D（`水 D3`），而 D 是会变的 ——
        ///   出牌、形态变化、跨回合都会改，名字一对不上就"这张牌不在手牌里"，
        ///   表现是**点出牌没反应、牌还留在手里**（而且日志里看不出哪里错了）。
        ///   引用不会因为改名而失配。
        ///
        /// 只由 TableRulesV21.RebuildHand 填。旧流程一律是 null，谁都不用管它。
        /// </summary>
        public MaterialCard bindingMaterial;

        /// <summary>v2.1：这张 3D 卡对应的**手牌法术**（规则侧的对象）。</summary>
        public SpellCard bindingSpell;

        /// <summary>
        /// 这张卡已经**排好销毁**了（CardFactory.DestroySafe 盖的章）。
        ///
        /// 【为什么需要这个标记】Object.Destroy 是**帧末**才真删的：
        ///   这一帧剩下的时间里，被销毁的卡还挂在 cardsRoot 下、还能被
        ///   GetComponentsInChildren 找到、也还没变成"假 null"。
        ///   而 TableRulesV21 的残留清扫是按"谁认领"数卡的（手牌 / 刀片 / 桌面标签），
        ///   刚被销毁的卡已经不在 setup.hand 里了 —— 不认这个标记，它就会被当成残留，
        ///   自检每次都报一堆假警（"抓到一张没人认领的卡：水"）。
        ///   认了它，残留清扫就只对**真的漏销毁**的卡报警。
        /// </summary>
        public bool markedForDestroy;

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

        /// <summary>绑定一张牌并放到手牌原位。食材和模块都走这一条。</summary>
        public void Setup(Card c, Vector3 home, Vector3 euler)
        {
            card = c;
            SetHome(home, euler);
            Teleport(home, euler);
            gameObject.name = (c != null ? c.TypeTag : "Card") + "_" + (c != null ? c.name : "?");
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
