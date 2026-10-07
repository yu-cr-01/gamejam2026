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

        /// <summary>
        /// 桌面上的回合循环。
        /// 放进投放区 / 拖回来的牌要报给它，它才知道"现在待投放的是哪张"。
        /// </summary>
        public TableTurnLoop turnLoop;

        /// <summary>
        /// 吸附余量：卡槽框之外再放宽这么多，落进来也算命中。
        ///
        /// 【为什么用两个轴分开的余量，而不是一个半径】
        /// 卡槽是 0.255 × 0.35 的长方形，槽间距是 0.30 × 0.40。
        /// 余量按轴给，才能保证"横着差一点"和"竖着差一点"被同样宽容地对待 ——
        /// 用一个圆半径去套长方形，长边方向永远比短边难命中。
        ///
        /// 取值让相邻卡槽的判定区刚好连成一片（x 方向半间距 0.15 < 可达 0.228，
        /// z 方向半间距 0.20 < 可达 0.305），于是**整片格子区没有死区**，
        /// 往那片区域随便一丢就能吸上。手牌区在 z=−0.58，离最近的可达边界
        /// 还有 0.24，所以放回手牌不会误吸。
        /// </summary>
        public float snapSlackX = 0.100f;
        public float snapSlackZ = 0.130f;

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

        // ── 左键"单击 vs 拖拽"的区分（v2.1 的"点一下也能上桌"靠它）─────────
        // 按下时记位置，抬起时量位移：小于阈值算单击。
        private Vector2  leftPressPos;
        private PlayCard pressedCard;

        /// <summary>
        /// 左键从按下到抬起，位移不超过这个像素数就算**单击**（否则算拖拽）。
        ///
        /// 【为什么是 8】比手抖大（2~3 像素的抖动不会被误判成拖），
        ///   比"真想拖到投放区"小得多（那至少几十像素）；
        ///   和右键那条"转头 / 检视"的 6 像素同一个量级，玩家不用记两套手感。
        /// </summary>
        private const float ClickSlack = 8f;

        /// <summary>卡槽角标的颜色。贴图只出白色形状，颜色在这里染 —— TableSetup 建槽时也要用。</summary>
        public static readonly Color MarkerIdle = new Color(0.30f, 0.34f, 0.42f);
        public static readonly Color MarkerHot  = new Color(0.35f, 0.95f, 0.60f);

        /// <summary>
        /// "手牌区"的分界线（世界 z）。桌面素材被拖到 z 小于它就 = 往玩家自己这边收回来。
        ///
        /// 【为什么是一条 z 线，而不是"手牌卡的包围盒"】
        ///   手牌会随张数重排（LayoutHand），空手牌时更是**一张卡都没有**可以当参照物 ——
        ///   拿卡的位置当判据会在"最后一张牌"上直接失效。
        ///   两个区域的 z 是固定的：槽位那一排在 z = −0.20（矩形纵深 0.35，后沿 −0.375），
        ///   手牌那一排在 z ≈ −0.58（见 TableTurnLoop.HandSlot）。
        ///   取两者中间偏玩家一侧的 −0.45：既不会和槽位判定区重叠
        ///   （槽位吸附的可达边界是 −0.375 − snapSlackZ 0.13 ≈ −0.505，
        ///   而那一下是**桌面卡**的收回判定，两者本来就不会同时命中），
        ///   也不需要玩家把手牌拖得很往下才算数。
        /// </summary>
        public const float HandZoneZ = -0.45f;

        /// <summary>这个落点算不算"玩家想把它收回手牌"。</summary>
        public static bool InHandZone(Vector3 world) { return world.z <= HandZoneZ; }

        // ─────────────────────────────────────────────────────────────

        void Update()
        {
            if (cam == null || board == null) return;

            HandlePhysicsToggle();

            // ★ 鼠标压在 v2.1 面板（半透明、可拖动的那种）上时，别让点击穿透到桌面。
            //   拾取走的是 Physics.Raycast，它看不见 IMGUI 面板 —— 不挡的话，
            //   拖面板会顺手把底下的牌选中/拖走。
            //   只挡"新发起的交互"：正在拖的牌、正在转的视角都要让它走完，
            //   否则鼠标划过面板的那一刻牌会卡在半空、镜头也会顿住。
            if (TableHud.PointerOverPanel && Dragging == null && !rightDragging && !middleDragging)
            {
                SetHovered(null);
                return;
            }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // 转视角时不刷新悬停 —— 一边转头一边有牌弹起来很干扰。
            // （rightDragging / middleDragging 是上一帧的值，差一帧看不出来）
            if (Dragging == null && !rightDragging && !middleDragging)
            {
                SetHovered(RaycastCard(ray));

                // 只有选牌阶段能拖动。模拟中 / 结算中手里的牌不动，
                // 免得玩家在这两张界面之间还能把牌丢来丢去、状态对不上。
                // 悬停和右键检视不受限制 —— 那两件事任何时候都该能用。
                bool mayDrag = (turnLoop == null) || turnLoop.CanInteract;

                if (Input.GetMouseButtonDown(0)) HandleLeftClick(ray, mayDrag);
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

            // v2.1：把"启动目标"的高亮压回去（必须排在悬停刷新之后，鼠标悬停才盖得住它）
            if (turnLoop != null && turnLoop.rulesV21 != null && TableSettings.UseRulesV21)
                turnLoop.rulesV21.ApplySelectionHighlight();

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
            if (Inspected != null && Inspected.card == null) Inspected = null;

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
        }

        private void ApplyLook()
        {
            if (rig == null) return;

            // 灵敏度走设置面板，不再是写死的常量
            float s = TableSettings.LookSensitivity;
            rig.Rotate(Input.GetAxis("Mouse X") * s,
                       Input.GetAxis("Mouse Y") * s);
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
            // ★ 目标素材的高亮由 TableRulesV21 每帧压回去（见它 ApplySelectionHighlight 的说明）。
            //   这里的判断是"别把那份高亮撤掉"：鼠标从目标卡移开时 Hovered 变成 null，
            //   如果照常 SetHover(false)，目标卡就不像被选中了。
            bool keepTarget = (Hovered != null && Hovered != card && IsPreviewTarget(Hovered));

            if (Hovered == card)
            {
                if (card != null) card.SetHover(true);
                return;
            }

            if (Hovered != null && !keepTarget) Hovered.SetHover(false);

            Hovered = card;
            if (Hovered != null) Hovered.SetHover(true);
        }

        /// <summary>这张卡是不是规则侧的"启动目标"（不在 v2.1 模式下永远是 false）。</summary>
        private bool IsPreviewTarget(PlayCard card)
        {
            if (card == null || turnLoop == null || turnLoop.rulesV21 == null) return false;
            if (!TableSettings.UseRulesV21) return false;
            return turnLoop.rulesV21.IsPreviewTarget(card);
        }

        /// <summary>
        /// 左键按下：分两种情况。
        ///
        ///   点在**桌面素材**上 → 选它当"启动破壁机"的目标（正文 §三.2：启动要选一张桌面素材）
        ///   点在**手牌**上（素材 / 法术）→ 先把它拿起来，**出牌在松手那一下判**
        ///
        /// 【为什么手牌要等到松手】按下那一刻还不知道这一下是"单击"还是"往槽里拖"：
        ///   在按下就出牌的话，拖进「上桌位 / 附魔位」这两条入口当场作废
        ///   （牌已经没了，还拖什么）。所以三条入口的统一判决在
        ///   <see cref="DropCard"/> 里：点 = 出牌，落槽 = 出牌，拖到空地 = 回手牌。
        ///
        /// 【桌面素材为什么可以按下就选中】它不是"打牌"，只是选个目标，
        ///   没有第二种手势要区分，早一帧生效手感更跟手。
        ///
        /// 【为什么鼠标分派必须在这一处】鼠标状态只有这一处权威
        ///   （见 HandleCameraAndInspect 顶部那段说明）。放去 HUD 或 TableTurnLoop 里，
        ///   就得再读一次 Input.GetMouseButtonDown，迟早出现"点一下触发两件事"。
        /// </summary>
        private void HandleLeftClick(Ray ray, bool mayDrag)
        {
            if (PhysicsOn) return;              // 物理模式下点击不管用（和原来的拖动一致）

            PlayCard hit = Hovered;

            // ① 桌面素材 → 也是**先拿起来，松手才判**
            //    【为什么从"按下即选中"改成"松手判"】v2.1 现在有两种手势落在同一张卡上：
            //      单击 = 选它当启动目标；往下拖到玩家这一侧 = 收回手牌（见 DropCard）。
            //      按下那一刻分不出是哪一种 —— 和手牌那几张牌的道理完全一样，
            //      所以判决点只能挪到松手那一下（RouteCardClick 里那段老说明还成立：
            //      "早一帧生效"是当时唯一的诉求，而那时桌面卡只有一种手势）。
            TableRulesV21 rules = turnLoop != null ? turnLoop.rulesV21 : null;
            if (rules != null && TableSettings.UseRulesV21 && hit != null && rules.IsTableCard(hit))
            {
                if (mayDrag) BeginDrag(hit, ray);
                return;
            }

            // ② 手牌（素材 / 法术）→ 拿起来拖动；是"点"还是"拖"由松手时的位移判（见 DropCard）
            if (mayDrag && hit != null) BeginDrag(hit, ray);
        }

        /// <summary>
        /// 单击一张卡的**按下**分派：桌面素材 → 选为启动目标。
        /// **返回 true 表示这一下已经被消费掉了**，调用方不该再做别的。
        ///
        /// 【谁还在用它】只有 <see cref="ClickCard"/>（探针 / 工具那条"我指定这一张，点它一下"的路）。
        ///   玩家那条路（HandleLeftClick）现在**不在这里**选中桌面卡了 ——
        ///   桌面卡多了一种手势（拖回手牌），按下那一刻分不出是"点"还是"拖"，
        ///   所以选中挪到了松手判决 <see cref="DropCard"/> 里（那里也调这个方法，口径唯一）。
        ///
        /// 【手牌为什么在这里返回 false】手牌的"打出去"要等松手才能判（点 / 落槽两种手势），
        ///   统一在 <see cref="DropCard"/> → <see cref="TableTurnLoop.PlayCardV21"/> 那一条路上。
        ///   以前这里是"点手牌法术 → 立刻附魔"，和 EndDrag 那条"拖素材进槽 → 摆着等确认"
        ///   各记各的状态，同一次操作被两边碰过就会分叉（规则说上了桌、3D 卡还在手牌位）。
        /// </summary>
        private bool RouteCardClick(PlayCard hit)
        {
            TableRulesV21 rules = turnLoop != null ? turnLoop.rulesV21 : null;
            if (rules == null || !TableSettings.UseRulesV21 || hit == null) return false;

            // 桌面素材 → 选中当目标（不受 CanInteract 限制：任何时候都该能看目标是谁）
            if (rules.IsTableCard(hit))
            {
                rules.OnTableCardClicked(hit);
                return true;
            }

            return false;
        }

        // ── 拖拽 ──────────────────────────────────────────────────────

        private void BeginDrag(PlayCard card, Ray ray)
        {
            Dragging = card;

            // 记下"按下"这一刻的鼠标位置和是哪张牌 —— 抬起时靠它们判断
            // 这一下是单击还是拖拽（见 EndDrag / TryClickPlace）
            leftPressPos = Input.mousePosition;
            pressedCard  = card;

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

            // ── 这一下是"单击"还是"拖拽"？──
            // 【为什么在抬起时才判】按下那一刻没法知道玩家想点还是想拖：
            //   按下就上桌的话，拖拽这条路当场作废（牌已经不在手上了）。
            //   所以按下照旧进拖拽，抬起时量**从按下到抬起的鼠标位移**：
            //   ≤ ClickSlack 像素 = 单击，> 就是拖拽。
            bool isClick = ((Vector2)Input.mousePosition - leftPressPos).magnitude <= ClickSlack;
            pressedCard = null;

            DropCard(card, isClick);
        }

        // ── 松手：这张牌该去哪（唯一判决）──────────────────────────────

        /// <summary>
        /// 松手时"这张牌该去哪"的**唯一判决**。返回 true = 这一下已经处理完了。
        ///
        /// 【v2.1 只认一件事：这张牌有没有被"打出去"】
        ///   · 单击　　　　　　　　→ 打出去
        ///   · 落进上桌位 / 附魔位 → 打出去（**落槽即结算**）
        ///   · 拖到空地上　　　　　→ 回手牌原位（不算出牌）
        ///   前两条走的是同一个函数 <see cref="TableTurnLoop.PlayCardV21"/>，
        ///   所以"点手牌"和"拖进槽"效果逐字一致 —— 这就是那个
        ///   "牌散开浮着、面板张数对不上"的根治点：
        ///   原来落槽那条只是 board.Place + Stage（把牌摆在槽上等确认），
        ///   点击那条却会真的出牌，两条路各记各的状态，同一次操作被两边碰过就分叉。
        ///
        /// 【落槽即结算 ⇒ 槽不会被占住】这里**不再** board.Place：
        ///   牌一进槽就生效（素材上桌 → 3D 卡由 SyncTableVisuals 摆到桌面槽位；
        ///   法术附魔 → 飞进罐口），槽位始终是空的，所以不存在"槽已满"这种状态。
        ///
        /// 【旧流程一个字都不改】投放区仍然是"先摆好、再按确认"的那个中间态。
        /// </summary>
        public bool DropCard(PlayCard card, bool isClick)
        {
            if (card == null) return false;

            int slot = (board != null)
                ? board.FindDropTarget(card.transform.position, snapSlackX, snapSlackZ)
                : -1;

            // ══ v2.1：点 / 落槽 = 出牌 ══════════════════════════════════
            if (turnLoop != null && turnLoop.V21)
            {
                // ★★ 桌面素材：这一下有三种可能，**先按桌面卡判**，别让它掉进下面的出牌分支 ★★
                //    单击（位移 ≤ ClickSlack）→ 选它当启动目标（老行为，一个字没变）
                //    拖到玩家这一侧（z ≤ HandZoneZ）→ 收回手牌（本回合、且还没启动过才行）
                //    拖回桌面别处 → 放回它自己的位置，规则状态一点不动
                //    【为什么必须放在最前面】它是一张**已经在桌面上**的卡，
                //    下面那段是按"手上的牌打出去"写的（落槽 = 出牌），
                //    桌面卡掉进去会被当成"再出一次牌"，那是另一个 bug。
                TableRulesV21 rules = turnLoop.rulesV21;
                if (rules != null && rules.IsTableCard(card))
                {
                    if (isClick)
                    {
                        rules.OnTableCardClicked(card);   // 选中 + notice（玩家那条路走的是这里）
                        card.ReturnHome();
                        return true;
                    }

                    if (InHandZone(card.transform.position) && rules.WithdrawToHand(card)) return true;

                    card.ReturnHome();     // 拖回桌面别处 = 放回它自己的格位
                    return true;
                }

                // 放错了槽（素材拖进附魔位、法术拖进上桌位）：说清楚，牌回手牌原位
                if (slot >= 0 && !turnLoop.CanStageInto(slot, card))
                {
                    card.ReturnHome();
                    turnLoop.RejectSlot(card, slot);
                    return true;
                }

                if (isClick || slot >= 0)
                {
                    if (turnLoop.PlayCardV21(card)) return true;

                    // 打不出去（阶段不对 / 已经不是手牌了）→ 至少别把它丢在半路
                    card.ReturnHome();
                    return true;
                }

                // 拖到空地上 = 反悔，回手牌原位
                card.ReturnHome();
                turnLoop.Release(card);
                return true;
            }

            // ══ 旧流程：投放区，原样保留 ══════════════════════════════════

            // 槽位有归属（素材槽 / 法术槽），放错了直接退回手牌并说明原因 ——
            // 让牌停在错误的槽里，玩家会以为放对了。
            if (slot >= 0 && turnLoop != null && !turnLoop.CanStageInto(slot, card))
            {
                card.ReturnHome();
                turnLoop.RejectSlot(card, slot);
                return true;
            }

            if (slot >= 0 && board != null && board.Place(slot, card))
            {
                card.SnapTo(board.SlotPosition(slot));

                // 进了投放区 → 只是"摆好"，还没投出去。真正落子在 HUD 的确认键上。
                if (turnLoop != null) turnLoop.Stage(card);
                return true;
            }

            card.ReturnHome();

            // 拖回手牌等于反悔，把待投放状态一起撤掉
            if (turnLoop != null) turnLoop.Release(card);
            return true;
        }

        // ── 单击 = 出牌（v2.1）────────────────────────────────────────

        /// <summary>
        /// 把"鼠标单击这张卡"整条路走一遍。
        ///
        /// 【玩家那条和探针那条必须做同一件事】玩家走 HandleLeftClick（按下）+ EndDrag（松手）；
        ///   探针不能模拟鼠标，只能把"从鼠标射线认出是哪张卡"换成"由探针指定哪张"，
        ///   剩下的一步不差：
        ///     桌面卡 → 选目标（RouteCardClick）
        ///     手牌   → 松手判决 DropCard(card, isClick:true) → TableTurnLoop.PlayCardV21
        ///   —— 和"把牌拖进上桌位 / 附魔位"是同一个函数。
        /// </summary>
        public bool ClickCard(PlayCard card)
        {
            if (PhysicsOn || card == null) return false;

            if (RouteCardClick(card)) return true;

            // 旧流程不动（它的"点一下"没有语义，出牌走投放区 + 确认）
            if (turnLoop == null || !turnLoop.V21) return false;

            return DropCard(card, true);
        }

        // ── 卡槽高亮 ──────────────────────────────────────────────────

        private void UpdateSlotMarkers()
        {
            if (slotMarkers == null || slotMarkers.Length == 0) return;

            int want = (Dragging != null)
                ? board.FindDropTarget(Dragging.transform.position, snapSlackX, snapSlackZ)
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
