using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 相机机位管理：注册若干"命名视角"，然后在它们之间平滑过渡，
    /// 另外支持**位置固定的自由转头**。
    ///
    /// 【两种相机模式】
    ///   预设机位（桌面 / 手牌 / 俯视）—— 位置和朝向一起切。
    ///   自由转头（三脚架模式）—— **位置锁死，只放开朝向**。
    ///     相当于把相机架在三脚架上原地转头，而不是自由飞行。
    ///     所以 Rotate() 只改 pitch/yaw，绝不碰 position。
    ///
    /// 【为什么不用 Cinemachine】
    /// Cinemachine 要装包；而视角切换的原型需求只是"从 A 平滑到 B"，
    /// 用 Vector3.Lerp + Quaternion.Slerp 十几行就够了，还少一个依赖。
    /// 以后要做镜头运镜、跟随、震屏再考虑上 Cinemachine。
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        /// <summary>自由视角的机位名。</summary>
        public const string FreeView = "free";

        /// <summary>俯仰上下限。放太开会钻到桌子底下，收太紧又看不清手牌特写。</summary>
        private const float PitchMin = -14f;
        private const float PitchMax = 86f;

        private class View
        {
            public Vector3    position;
            public Quaternion rotation;
        }

        public Camera cam;

        private readonly Dictionary<string, View> views = new Dictionary<string, View>();

        private string currentName = "";
        private View   from;
        private View   to;
        private float  t = 1f;          // 0..1，1 表示已到位
        private float  duration = 0.6f;

        // ── 自由转头状态 ──────────────────────────────────────────────
        private bool  freeLook;
        private float yaw;
        private float pitch;

        /// <summary>
        /// 自由转头的活动范围：以进入自由转头时的朝向为轴，张开一个圆锥。
        /// 120° 就是「中心方向两侧各 60°」，转到锥面就顶住，不会再转过去看到背后。
        /// </summary>
        public float freeLookConeAngle = 120f;

        /// <summary>圆锥的中心轴（单位向量）。进入自由转头时按当时的朝向定下来。</summary>
        private Vector3 coneAxis = Vector3.forward;

        public string CurrentView { get { return currentName; } }
        public bool   IsFreeLook  { get { return freeLook; } }

        /// <summary>注册一个机位。lookAt 是相机要看向的世界坐标点。</summary>
        public void Register(string name, Vector3 position, Vector3 lookAt)
        {
            View v = new View();
            v.position = position;
            v.rotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);
            views[name] = v;
        }

        /// <summary>
        /// 按一个**躺在桌面上的包围盒**算机位：从盒子正前方、抬高 tiltDeg 度看过去，
        /// 距离取"横向刚好装下"和"纵深刚好装下"里更远的那个，再乘 margin 留边。
        ///
        /// 【为什么要算而不是写死坐标】
        ///   手牌数量是会变的（开局 4 张、换刀片后可能 5 张），固定机位在牌多的时候
        ///   会把最外侧的牌切出画面。按实际范围算，几张牌都装得下。
        ///
        /// 【纵深为什么乘 sin(tilt)】
        ///   俯视时地面上的纵深在屏幕上被压扁了，压扁系数就是 sin(tilt) ——
        ///   不乘的话算出来的距离会偏大，镜头拉得太远。
        ///
        /// go = false 时只更新机位不切过去（发牌途中不该抢镜头）。
        /// </summary>
        public void FrameTableBounds(string name, Bounds box, float tiltDeg, float margin = 1.1f, bool go = false)
        {
            if (cam == null) return;

            float tilt = Mathf.Clamp(tiltDeg, 10f, 85f) * Mathf.Deg2Rad;
            float halfV = Mathf.Tan(Mathf.Clamp(cam.fieldOfView, 5f, 120f) * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * Mathf.Max(0.2f, cam.aspect);

            float needX = box.extents.x * margin;
            float needZ = box.extents.z * margin;

            float distX = needX / halfH;
            float distZ = needZ * Mathf.Sin(tilt) / halfV;
            float dist  = Mathf.Max(Mathf.Max(distX, distZ), 0.25f);

            Vector3 dir = new Vector3(0f, Mathf.Sin(tilt), -Mathf.Cos(tilt));   // 中心 → 相机
            Vector3 target = box.center;
            Vector3 pos = target + dir * dist;

            Register(name, pos, target);

            if (go) GoTo(name);
        }

        /// <summary>
        /// 按**必须看见的点集**算机位：先把点集在画面里居中，再沿视线退到刚好全部装下。
        ///
        /// 【为什么不能再用 FrameTableBounds】
        ///   那个算式把整个包围盒当成"在同一个深度上"，而手牌那一排恰恰是**离相机最近**
        ///   的那一层：按盒心深度估出来的距离偏小，牌就贴到（甚至切出）画面下边缘 ——
        ///   用户拍到的"手牌最下面那张只露上半截"就是这么来的。
        ///   这里改成**用同一套投影量到合格为止**（先翻倍找上界、再二分），
        ///   摆位和量尺不会各算各的。
        ///
        /// 【为什么必须每次按当前宽高比重算】
        ///   可视范围随**宽高比**变：水平半角 = 垂直半角 × aspect。
        ///   同一个机位在 16:9 装得下，竖屏窗口里横向就装不下（这正是"编辑器 Free Aspect
        ///   能出图、打包版 1600×900 也能出图"必须成立的那一条）。所以这个方法算的是
        ///   "当前 cam.aspect 下"的机位，比例变了要再算一次 —— 调用点在 TableSetup.Update。
        ///
        /// 【为什么"居中"这一条不能省】
        ///   只退后不居中 = 让画面中心继续对着桌子中段那块空地，手牌仍然沉在下边缘以外，
        ///   要把它拉进来就得多退一大截（实测多退 46%）。居中只挪画面的中心，
        ///   一个世界坐标都不动。
        ///
        /// margin 的语义和 <see cref="FrameTableBounds"/> 一致：1.1 = 每个方向至少留 1/1.1 半屏。
        /// go = true 时顺带切过去（重建机位一般不切，见调用点的说明）。
        /// </summary>
        public bool FramePoints(string name, IList<Vector3> points, float tiltDeg,
                               float margin = 1.1f, bool go = false)
        {
            if (cam == null || points == null || points.Count == 0) return false;

            float tilt  = Mathf.Clamp(tiltDeg, 10f, 85f) * Mathf.Deg2Rad;
            float limit = 1f / Mathf.Max(1.01f, margin);

            float halfV = Mathf.Tan(Mathf.Clamp(cam.fieldOfView, 5f, 120f) * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * Mathf.Max(0.2f, cam.aspect);

            // 视线方向由俯角唯一决定；相机上方 ⟂ 它，所以"沿 dir 挪"只改距离、不改上下和左右。
            Vector3 dir = new Vector3(0f, Mathf.Sin(tilt), -Mathf.Cos(tilt));   // 注视点 → 相机
            Vector3 up  = new Vector3(0f, Mathf.Cos(tilt), Mathf.Sin(tilt));    // 画面上方

            // ① 居中：点集在"画面右 / 画面上"两个方向上的中值，就是相机在这两个方向上的坐标。
            //    （dir 在 YZ 平面内，所以"画面右"永远是 +X —— 取景不需要跟着转头。）
            float minX = float.MaxValue, maxX = float.MinValue;
            float minU = float.MaxValue, maxU = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                float x = points[i].x;
                float u = Vector3.Dot(points[i], up);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }

            // base 放在 z = 0 平面上，只要它在右 / 上两个方向上的分量对就行：
            //   相机 = base + dir * dist（dir ⟂ up，所以 dist 一变，上下分量不动）。
            Vector3 basePos = new Vector3((minX + maxX) * 0.5f, (minU + maxU) * 0.5f / up.y, 0f);

            // ② 退后到全部装下：先翻倍找"一定装得下"的上界，再二分收紧
            float lo = 0.05f, hi = 1f;
            int guard = 0;
            while (!PointsInside(points, basePos + dir * hi, dir, up, halfV, halfH, limit))
            {
                lo = hi;
                hi *= 2f;
                // 12 次翻倍 = 4096 米，正常内容（整张桌子）到不了；到得了就是参数给错了，
                // 这时候宁可保留旧机位，也不要摆出一台"退到天边"的相机。
                if (++guard > 12) return false;
            }
            for (int i = 0; i < 32; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (PointsInside(points, basePos + dir * mid, dir, up, halfV, halfH, limit)) hi = mid;
                else lo = mid;
            }

            Vector3 pos = basePos + dir * hi;

            // 注视点取视线轴上的任一点 —— Register 只认方向，写在轴上（而不是内容中心）
            // 是因为内容中心一般不在轴上，写它会连带把视线方向也改掉（变成"斜着看"）。
            Register(name, pos, pos - dir);

            if (go) GoTo(name);
            return true;
        }

        /// <summary>
        /// 某个机位下，这批点投到画面里的范围（NDC：−1..1 是画面内，0 是画面中心）。
        ///
        /// 【为什么自检要拿机位来量，而不是拿 cam 当前的投影】
        ///   取景是"算出来的机位"在保证的事，而相机此刻可能正被玩家拖到别的角度上
        ///   （自由转头）。要证明"桌面视角装得下"，就得量**那个机位**。
        ///   这里和 <see cref="FramePoints"/> 共用同一套投影 —— 摆位和量尺不能各算各的。
        ///
        /// 机位不存在 / 点集为空 / 有点在相机背后（投影没有意义）时返回 false。
        /// </summary>
        public bool ViewNdcRange(string name, IList<Vector3> points, out Vector2 lo, out Vector2 hi)
        {
            lo = new Vector2(float.MaxValue, float.MaxValue);
            hi = new Vector2(float.MinValue, float.MinValue);

            View v;
            if (cam == null || points == null || points.Count == 0) return false;
            if (!views.TryGetValue(name, out v)) return false;

            float halfV = Mathf.Tan(Mathf.Clamp(cam.fieldOfView, 5f, 120f) * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * Mathf.Max(0.2f, cam.aspect);

            Vector3 right = v.rotation * Vector3.right;
            Vector3 up    = v.rotation * Vector3.up;
            Vector3 fwd   = v.rotation * Vector3.forward;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 rel = points[i] - v.position;
                float depth = Vector3.Dot(rel, fwd);
                if (depth <= 1e-4f) return false;

                Vector2 ndc = new Vector2(Vector3.Dot(rel, right) / (depth * halfH),
                                          Vector3.Dot(rel, up)    / (depth * halfV));
                lo = Vector2.Min(lo, ndc);
                hi = Vector2.Max(hi, ndc);
            }
            return true;
        }

        /// <summary>
        /// 点集是不是**全部**落在 |ndc| ≤ limit 里（且都在相机前）。
        ///
        /// 【为什么不用解析解】闭式解当然写得出来，但那样就有两份投影公式要同步
        ///   （一份算机位、一份给自检），而这两份一旦走岔，"日志说装得下、画面里切着"
        ///   就会同时成立。二分只是把一个便宜的比较重复几十次 —— 重算机位是
        ///   "比例变了 / 手牌变了"才发生的事，不在每帧路径上。
        /// </summary>
        private static bool PointsInside(IList<Vector3> pts, Vector3 pos, Vector3 dir, Vector3 up,
                                         float halfV, float halfH, float limit)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 rel = pts[i] - pos;
                float depth = Vector3.Dot(rel, -dir);        // dir 是"注视点 → 相机"，所以 −dir 才是前向
                if (depth <= 1e-4f) return false;
                if (Mathf.Abs(rel.x) > limit * halfH * depth) return false;
                if (Mathf.Abs(Vector3.Dot(rel, up)) > limit * halfV * depth) return false;
            }
            return true;
        }

        /// <summary>立刻切到某个机位（不做过渡）。</summary>
        public void SnapTo(string name)
        {
            View v;
            if (!views.TryGetValue(name, out v)) return;
            currentName = name;
            t = 1f;
            to = null;
            SyncFreeLookFrom(v.rotation, name == FreeView);
            Apply(v);
        }

        /// <summary>平滑切到某个机位。</summary>
        public void GoTo(string name, float seconds = 0.6f)
        {
            View v;
            if (!views.TryGetValue(name, out v)) return;

            if (currentName == name && t >= 1f) return;   // 已经在那了

            from = CurrentViewPose();
            to = v;
            t = 0f;
            duration = Mathf.Max(0.01f, seconds);
            currentName = name;
            SyncFreeLookFrom(v.rotation, name == FreeView);
        }

        // ── 自由转头 ──────────────────────────────────────────────────

        /// <summary>
        /// 进入自由转头，锚定在相机**当前**的位置和朝向上。
        /// 从任何预设机位拖一下鼠标就能进，不用先切到"自由视角"。
        /// </summary>
        public void EnterFreeLook()
        {
            if (cam == null || freeLook) return;
            SyncFreeLookFrom(cam.transform.rotation, true);
            currentName = FreeView;
        }

        /// <summary>
        /// 转头。dx / dy 是鼠标增量（度）。
        ///
        /// **只改朝向、绝不改位置** —— 位置固定正是"自由转头"和"自由飞行"的区别。
        /// 拖动会立刻取消正在进行的机位过渡，否则 Update 会每帧把朝向插值覆盖回去。
        /// 转出来的方向会被**圆锥约束**夹一次（见 ClampToCone），
        /// 所以怎么拖都转不出活动范围。
        /// </summary>
        public void Rotate(float dx, float dy)
        {
            if (cam == null) return;
            if (!freeLook) EnterFreeLook();

            to = null;      // 取消过渡，把朝向的控制权交还给鼠标
            t = 1f;

            yaw += dx;
            pitch = Mathf.Clamp(pitch - dy, PitchMin, PitchMax);

            Vector3 dir = ClampToCone(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
            Quaternion final = Quaternion.LookRotation(dir, Vector3.up);
            cam.transform.rotation = final;

            // ★ 把夹取之后**实际的**朝向写回 yaw / pitch。
            //   不写回的话，鼠标继续往锥外推时 yaw/pitch 会一路累加，
            //   等你想往回转，得先把那一段"空转"抵消掉才动得起来 ——
            //   手感就是顶到边之后卡住、然后突然猛跳一大截。
            Vector3 e = final.eulerAngles;
            yaw = e.y;
            pitch = NormalizePitch(e.x);
        }

        /// <summary>
        /// 把朝向夹进圆锥里。
        ///
        /// 【为什么不能靠分别夹 yaw 和 pitch】
        /// 分别夹两个角得到的是一个**矩形**活动区
        /// （yaw ∈ [−60,60] 且 pitch ∈ [−60,60]），
        /// 它的四个角离中心有 85°，比要求的 60° 远得多 —— 斜着拖就转出范围了。
        /// 圆锥是圆形的边界，必须对**最终方向向量**做角度检查。
        /// </summary>
        private Vector3 ClampToCone(Vector3 dir)
        {
            float half = Mathf.Clamp(freeLookConeAngle, 1f, 179f) * 0.5f;
            float ang = Vector3.Angle(coneAxis, dir);

            if (ang <= half) return dir;

            // 和轴正好反向时 Slerp 的插值平面退化了，直接退回轴
            if (ang > 179f) return coneAxis;

            // 在 axis 与 dir 张成的平面内沿球面插值到正好 half 度 ——
            // 效果是"贴着锥面滑动"，而不是把方向硬拽回轴心
            return Vector3.Slerp(coneAxis, dir, half / ang).normalized;
        }

        /// <summary>把 yaw / pitch 从某个朝向同步过来。enable=false 时顺便退出自由转头。</summary>
        private void SyncFreeLookFrom(Quaternion rot, bool enable)
        {
            freeLook = enable;
            if (!enable) return;

            Vector3 e = rot.eulerAngles;
            yaw = e.y;
            pitch = NormalizePitch(e.x);

            // 圆锥的轴 = 进入自由转头那一刻的朝向。
            // 也就是"你正看着哪儿，就以哪儿为中心，最多再偏 60 度"。
            coneAxis = (rot * Vector3.forward).normalized;
        }

        /// <summary>eulerAngles 是 0..360，转成 −180..180 再夹到俯仰范围内。</summary>
        private static float NormalizePitch(float x)
        {
            if (x > 180f) x -= 360f;
            return Mathf.Clamp(x, PitchMin, PitchMax);
        }

        /// <summary>在两个机位之间来回切（重复按同一个键时用）。</summary>
        public void Toggle(string a, string b, float seconds = 0.6f)
        {
            GoTo(currentName == a ? b : a, seconds);
        }

        private View CurrentViewPose()
        {
            View v = new View();
            if (cam != null)
            {
                v.position = cam.transform.position;
                v.rotation = cam.transform.rotation;
            }
            return v;
        }

        private void Apply(View v)
        {
            if (cam == null || v == null) return;
            cam.transform.position = v.position;
            cam.transform.rotation = v.rotation;
        }

        private void Update()
        {
            if (cam == null || to == null) return;
            if (t >= 1f) return;

            t += Time.deltaTime / duration;
            if (t > 1f) t = 1f;

            // SmoothStep 让起停更自然，不然线性插值看着很"机械"
            float k = t * t * (3f - 2f * t);

            cam.transform.position = Vector3.Lerp(from.position, to.position, k);
            cam.transform.rotation = Quaternion.Slerp(from.rotation, to.rotation, k);
        }
    }
}
