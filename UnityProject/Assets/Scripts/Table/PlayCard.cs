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
        /// <summary>对应的食材数据</summary>
        public Ingredient data;

        /// <summary>所在卡槽索引。-1 表示不在槽位上（在手牌 / 被拿在手里）</summary>
        public int slotIndex = -1;

        /// <summary>手牌原位 —— 放不出去时回到这里</summary>
        public Vector3 homePosition;
        public Vector3 homeEuler;

        public bool IsHovered  { get; private set; }
        public bool IsDragging { get; private set; }

        private Rigidbody rb;
        private Renderer  bodyRenderer;
        private Color     bodyBaseColor = Color.white;

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
        /// 由 CardFactory 指定卡面渲染器。
        /// 卡牌本体和文字都是子物体，用 GetComponentInChildren 猜不可靠，所以显式绑。
        /// </summary>
        public void BindRenderer(Renderer body)
        {
            bodyRenderer = body;
            if (bodyRenderer != null && bodyRenderer.material != null)
                bodyBaseColor = bodyRenderer.material.color;
        }

        /// <summary>绑定数据并放到手牌原位。</summary>
        public void Setup(Ingredient ing, Vector3 home, Vector3 euler)
        {
            data = ing;
            homePosition = home;
            homeEuler = euler;

            targetPos = home;
            targetRot = Quaternion.Euler(euler);

            transform.position = home;
            transform.rotation = targetRot;

            gameObject.name = "Card_" + (ing != null ? ing.name : "?");
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

        // ── 每帧 ──────────────────────────────────────────────────────

        void Update()
        {
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
            if (bodyRenderer == null || bodyRenderer.material == null) return;

            Color want = IsDragging ? bodyBaseColor * 1.30f
                       : IsHovered  ? bodyBaseColor * 1.15f
                                    : bodyBaseColor;
            want.a = 1f;

            bodyRenderer.material.color =
                Color.Lerp(bodyRenderer.material.color, want, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
    }
}
