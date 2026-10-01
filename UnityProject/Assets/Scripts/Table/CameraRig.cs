using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 相机机位管理：注册若干"命名视角"，然后在它们之间平滑过渡。
    ///
    /// 【为什么不用 Cinemachine】
    /// Cinemachine 要装包；而视角切换的原型需求只是"从 A 平滑到 B"，
    /// 用 Vector3.Lerp + Quaternion.Slerp 十几行就够了，还少一个依赖。
    /// 以后要做镜头运镜、跟随、震屏再考虑上 Cinemachine。
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
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

        public string CurrentView { get { return currentName; } }

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
