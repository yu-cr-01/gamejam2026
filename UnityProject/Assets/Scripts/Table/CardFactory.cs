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
    ///       ├─ PlayCard           卡牌逻辑
    ///       ├─ Body               立方体：网格 + BoxCollider，localScale = (0.24, 0.008, 0.335)
    ///       ├─ NameText          TextMesh：名字
    ///       └─ StatsText         TextMesh：属性
    ///
    /// 为什么不把文字直接挂在 Body 下面：Body 是非等比缩放（0.24 / 0.008 / 0.335），
    /// 文字挂上去会被拉得完全变形。所以文字挂在根节点上，自己控制位置。
    ///
    /// 射线拾取打在 Body 的碰撞体上，用 GetComponentInParent&lt;PlayCard&gt;() 拿到逻辑。
    /// </summary>
    public static class CardFactory
    {
        public const float CardWidth  = 0.24f;
        public const float CardThick  = 0.008f;
        public const float CardDepth  = 0.335f;

        private static readonly Color PaperColor = new Color(0.92f, 0.90f, 0.84f);  // 纸色
        private static readonly Color InkColor   = new Color(0.16f, 0.15f, 0.14f);  // 墨色

        private static Font   cjkFont;
        private static Shader stdShader;

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

        private static Shader StdShader()
        {
            if (stdShader == null) stdShader = Shader.Find("Standard");
            if (stdShader == null) stdShader = Shader.Find("Diffuse");   // 兜底
            return stdShader;
        }

        // ── 造卡 ──────────────────────────────────────────────────────

        public static PlayCard Create(Ingredient ing, Transform parent, Vector3 home, Vector3 euler)
        {
            string title = ing != null ? ing.name : "?";

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

            // ② 卡面本体：立方体自带 MeshRenderer + BoxCollider
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(CardWidth, CardThick, CardDepth);

            Renderer bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null && StdShader() != null)
            {
                bodyRenderer.material = new Material(StdShader());
                bodyRenderer.material.color = PaperColor;
                if (bodyRenderer.material.HasProperty("_Glossiness"))
                    bodyRenderer.material.SetFloat("_Glossiness", 0.06f);
            }

            // ③ 文字：挂在根节点上（scale=1），避免被 Body 的非等比缩放拉变形
            AddText(root.transform, title, 0.085f, 0.030f, InkColor);
            if (ing != null && ing.attrs != null)
                AddText(root.transform, ing.attrs.DescribeNonZero(), -0.030f, 0.016f, InkColor * 1.6f);

            card.BindRenderer(bodyRenderer);
            card.Setup(ing, home, euler);
            return card;
        }

        /// <summary>
        /// 在卡面上放一行文字。
        ///
        /// 朝向说明：文字默认朝 +Z。要让它平躺在桌面上、且从相机方向读着不倒，
        /// 需要「局部 +Z → 世界上方」且「局部 +Y → 世界 +Z（远端）」。
        /// LookRotation(forward, upwards) 正好表达这两件事。
        /// </summary>
        private static void AddText(Transform parent, string text, float localZ, float size, Color color)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent, false);

            go.transform.localPosition = new Vector3(0f, CardThick * 0.5f + 0.0015f, localZ);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            go.transform.localScale = Vector3.one * size;

            TextMesh tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 48;                 // 配合 localScale 缩放，实际字号由 size 决定
            tm.color = color;
            tm.characterSize = 1f;

            Font f = CjkFont();
            if (f != null)
            {
                tm.font = f;
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = f.material;
            }
        }
    }
}
