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
        /// </summary>
        public void Rotate(float dx, float dy)
        {
            if (cam == null) return;
            if (!freeLook) EnterFreeLook();

            to = null;      // 取消过渡，把朝向的控制权交还给鼠标
            t = 1f;

            yaw += dx;
            pitch = Mathf.Clamp(pitch - dy, PitchMin, PitchMax);

            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>把 yaw / pitch 从某个朝向同步过来。enable=false 时顺便退出自由转头。</summary>
        private void SyncFreeLookFrom(Quaternion rot, bool enable)
        {
            freeLook = enable;
            if (!enable) return;

            Vector3 e = rot.eulerAngles;
            yaw = e.y;
            pitch = NormalizePitch(e.x);
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
