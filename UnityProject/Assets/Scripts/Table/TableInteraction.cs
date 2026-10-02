using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 桌面交互：射线拾取 → 拖动 → 松手吸附。
    ///
    /// 【流程】
    ///   鼠标悬停  → 射线打到卡 → 卡抬起 + 变亮
    ///   按下左键  → 拿起，卡脱离原卡槽，跟着鼠标在一个水平面上走
    ///   拖动中    → 实时高亮最近的空卡槽
    ///   松开左键  → 吸附半径内有空槽就落位，否则回手牌
    ///
    /// 【拖动平面】用一个水平面求交，而不是直接把卡放到鼠标位置 ——
    /// 相机是斜俯视，直接放会让卡在近处埋进桌子、远处飘起来。
    ///
    /// 【物理模式】按 G 切换。开启后卡受重力，会和桌子及彼此真实碰撞（演示碰撞效果）。
    /// 关闭时走脚本控制，手感更可控。
    /// </summary>
    public class TableInteraction : MonoBehaviour
    {
        public Camera     cam;
        public CameraRig  rig;
        public TableBoard board;
        public Transform  cardsRoot;

        /// <summary>卡槽指示块，下标与 board.slots 一一对应（由 TableSetup 填入）</summary>
        public Renderer[] slotMarkers;

        /// <summary>吸附半径（米）</summary>
        public float snapRadius = 0.13f;

        public PlayCard Hovered   { get; private set; }
        public PlayCard Dragging  { get; private set; }
        public bool     PhysicsOn { get; private set; }

        /// <summary>
        /// 右键检视中的那张牌（null = 没开检视面板）。
        /// 这里只管"现在在看哪张"，具体画成什么样是 TableHud 的事。
        /// </summary>
        public PlayCard Inspected { get; private set; }

        private Plane  dragPlane;
        private int    hotSlot = -1;
        private float  markerAlpha;      // 角标当前透明度（拖动时淡入、松手后淡出）
        private float  markerApplied = -1f;

        // ── 转视角拖动 / 右键单击 的区分 ──────────────────────────────
        private Vector3 rightDownPos;
        private Vector3 middleDownPos;
        private bool    rightDragging;
        private bool    middleDragging;

        /// <summary>超过这个像素距离才算"拖动"，否则算"单击"。</summary>
        private const float LookDragThreshold = 6f;

        /// <summary>转头灵敏度（度 / 鼠标单位）。</summary>
        private const float LookSensitivity = 3.2f;

        /// <summary>卡槽角标的颜色。贴图只出白色形状，颜色在这里染 —— TableSetup 建槽时也要用。</summary>
        public static readonly Color MarkerIdle = new Color(0.30f, 0.34f, 0.42f);
        public static readonly Color MarkerHot  = new Color(0.35f, 0.95f, 0.60f);

        // ─────────────────────────────────────────────────────────────

        void Update()
        {
            if (cam == null || board == null) return;

            HandlePhysicsToggle();

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // 转视角时不刷新悬停 —— 一边转头一边有牌弹起来很干扰。
            // （rightDragging / middleDragging 是上一帧的值，差一帧看不出来）
            if (Dragging == null && !rightDragging && !middleDragging)
            {
                SetHovered(RaycastCard(ray));

                if (Input.GetMouseButtonDown(0) && Hovered != null && !PhysicsOn)
                    BeginDrag(Hovered, ray);
            }
            else if (Dragging == null)
            {
                SetHovered(null);
            }

            if (Dragging != null)
            {
                if (Input.GetMouseButtonUp(0)) EndDrag();
                else                           MoveDrag(ray);
            }

            HandleCameraAndInspect(ray);
            UpdateSlotMarkers();
        }

        /// <summary>
        /// 从外面指定要检视哪张牌（传 null 关闭）。
        /// 右键那条路自己会用，留着给 HUD 按钮 / 编辑器工具复用。
        /// </summary>
        public void Inspect(PlayCard card) { Inspected = card; }

        /// <summary>
        /// 相机自由转头 + 右键检视。这两件事都挂在右键上，靠**移动距离**区分。
        ///
        ///   右键只按下不移动 → 单击 → 检视这张牌
        ///   右键按住走一段 → 拖动 → 转视角（并且不再触发检视）
        ///
        /// 阈值 6 像素。这是 3D / 建模软件的通用做法，玩家不用记规则。
        /// 中键拖动也转视角 —— 它不和任何东西冲突，鼠标有中键的话更顺手。
        ///
        /// 转头是**位置固定、只转朝向**：CameraRig.Rotate 只改 pitch/yaw。
        /// 走的是 TableInteraction 而不是单独的脚本，因为鼠标状态只能有一处权威，
        /// 两个脚本各读各的 Input 迟早会打架。
        /// </summary>
        private void HandleCameraAndInspect(Ray ray)
        {
            // 检视的那张牌被清掉了（比如回主菜单重建桌面）就自动收起
            if (Inspected != null && Inspected.data == null) Inspected = null;

            bool looking = false;

            // ── 中键拖动 ──
            if (Input.GetMouseButtonDown(2))
            {
                middleDownPos = Input.mousePosition;
                middleDragging = false;
            }
            if (Input.GetMouseButton(2))
            {
                if (!middleDragging &&
                    (Input.mousePosition - middleDownPos).magnitude > LookDragThreshold)
                    middleDragging = true;

                looking |= middleDragging;
            }
            if (Input.GetMouseButtonUp(2)) middleDragging = false;

            // ── 右键：先按下，看接下来是单击还是拖动 ──
            if (Input.GetMouseButtonDown(1))
            {
                rightDownPos = Input.mousePosition;
                rightDragging = false;
            }
            if (Input.GetMouseButton(1))
            {
                if (!rightDragging &&
                    (Input.mousePosition - rightDownPos).magnitude > LookDragThreshold)
                    rightDragging = true;

                looking |= rightDragging;
            }
            if (Input.GetMouseButtonUp(1))
            {
                // 从按下到松开都没怎么动 → 当成单击，走检视
                if (!rightDragging && Dragging == null)
                {
                    PlayCard hit = RaycastCard(ray);
                    Inspected = (hit != null && hit != Inspected) ? hit : null;
                }
                rightDragging = false;
            }

            if (looking) ApplyLook();

            if (Input.GetKeyDown(KeyCode.Escape)) Inspected = null;
        }

        private void ApplyLook()
        {
            if (rig == null) return;
            rig.Rotate(Input.GetAxis("Mouse X") * LookSensitivity,
                       Input.GetAxis("Mouse Y") * LookSensitivity);
        }

        private void HandlePhysicsToggle()
        {
            if (!Input.GetKeyDown(KeyCode.G)) return;

            PhysicsOn = !PhysicsOn;
            SetAllCardsPhysics(PhysicsOn);

            if (PhysicsOn && Dragging != null)
            {
                // 进物理模式时把正拖着的卡放下
                Dragging.EndDrag();
                board.Clear(Dragging);
                Dragging = null;
            }
        }

        private void SetAllCardsPhysics(bool on)
        {
            if (cardsRoot == null) return;
            PlayCard[] all = cardsRoot.GetComponentsInChildren<PlayCard>();
            for (int i = 0; i < all.Length; i++) all[i].SetPhysics(on);
        }

        // ── 射线 ──────────────────────────────────────────────────────

        private PlayCard RaycastCard(Ray ray)
        {
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 50f)) return null;

            // 碰撞体在子物体 Body 上，所以往父级找逻辑组件
            return hit.collider.GetComponentInParent<PlayCard>();
        }

        private void SetHovered(PlayCard card)
        {
            if (Hovered == card) return;
            if (Hovered != null) Hovered.SetHover(false);
            Hovered = card;
            if (Hovered != null) Hovered.SetHover(true);
        }

        // ── 拖拽 ──────────────────────────────────────────────────────

        private void BeginDrag(PlayCard card, Ray ray)
        {
            Dragging = card;

            // 脱离原卡槽，把这个位置让出来
            if (board != null) board.Clear(card);

            card.BeginDrag();

            // 拖动平面：过卡牌当前高度的水平面
            dragPlane = new Plane(Vector3.up, new Vector3(0f, card.transform.position.y, 0f));
        }

        private void MoveDrag(Ray ray)
        {
            float dist;
            if (!dragPlane.Raycast(ray, out dist)) return;

            Vector3 p = ray.GetPoint(dist);
            p.y = 0.022f;                    // 略微离开桌面，避免和桌面穿插
            Dragging.DragTo(p);
        }

        private void EndDrag()
        {
            PlayCard card = Dragging;
            Dragging = null;
            if (card == null) return;

            card.EndDrag();

            int slot = board.FindNearestFreeSlot(card.transform.position, snapRadius);
            if (slot >= 0 && board.Place(slot, card))
            {
                card.SnapTo(board.SlotPosition(slot));
            }
            else
            {
                card.ReturnHome();           // 附近没有空槽 → 回手牌
            }
        }

        // ── 卡槽高亮 ──────────────────────────────────────────────────

        private void UpdateSlotMarkers()
        {
            if (slotMarkers == null || slotMarkers.Length == 0) return;

            int want = (Dragging != null)
                ? board.FindNearestFreeSlot(Dragging.transform.position, snapRadius)
                : -1;

            // ★ 角标平时整体隐形，只有拿起牌要放的时候才淡入。
            //   每个槽是 4 个 L 形角标，8 个槽就是 32 个 —— 常亮的话整张桌子全是碎线，
            //   比原来那种色块还吵。拖动时才出现，桌面平时是干净的。
            float targetAlpha = (Dragging != null) ? 0.95f : 0f;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            markerAlpha = Mathf.Lerp(markerAlpha, targetAlpha, k);

            bool slotChanged = (want != hotSlot);
            hotSlot = want;

            // 位置没换、透明度也基本到位就不用碰材质了
            if (!slotChanged && Mathf.Abs(markerAlpha - markerApplied) < 0.004f) return;
            markerApplied = markerAlpha;

            for (int i = 0; i < slotMarkers.Length && i < board.SlotCount; i++)
            {
                Renderer r = slotMarkers[i];
                if (r == null || r.material == null) continue;

                // ★ 用 Renderer.enabled 开关，**不依赖 shader 的 alpha 混合**。
                //   上一版只把 color.a 设成 0，结果角标照样显示 ——
                //   说明材质用的根本不是能透明混合的 shader（Shader.Find 回退了），
                //   alpha 被直接忽略。开关渲染器就没这个问题。
                r.enabled = markerAlpha > 0.02f;

                Color c = (i == hotSlot) ? MarkerHot : MarkerIdle;
                c.a = markerAlpha;          // shader 支持透明的话还能顺便有个淡入
                r.material.color = c;
            }
        }

        /// <summary>外部（HUD）用：当前该显示哪张卡的详情。</summary>
        public PlayCard FocusCard
        {
            get { return Dragging != null ? Dragging : Hovered; }
        }
    }
}
