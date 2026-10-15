using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>Loads the reference-built mesh asset; no runtime primitive modeling.</summary>
    public sealed class BlenderModelRig : MonoBehaviour
    {
        public const string ModelPath = "Art/Juicer/Models/blender_reference";
        private JuicerRig owner;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Transform> blades = new List<Transform>();
        private Vector3 restPosition;

        public static BlenderModelRig Attach(JuicerRig owner)
        {
            GameObject source = Resources.Load<GameObject>(ModelPath);
            if (source == null) return null;
            GameObject instance = Instantiate(source, owner.transform, false);
            instance.name = "ReferenceBlenderModel";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            BlenderModelRig rig = instance.AddComponent<BlenderModelRig>();
            rig.owner = owner;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds();
            bool found = false;
            var cache = new Dictionary<string, Material>();
            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    Bounds mb = filter.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 point = instance.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string key = slots[i] != null ? slots[i].name.Split(' ')[0] : "Enamel";
                    Material material;
                    if (!cache.TryGetValue(key, out material))
                    {
                        material = MakeMaterial(key);
                        cache.Add(key, material);
                        rig.materials.Add(material);
                    }
                    slots[i] = material;
                }
                renderer.sharedMaterials = slots;
                if (renderer.name.Contains("Glass"))
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (renderer.name.StartsWith("Blade") && renderer.name != "BladeHub")
                    rig.blades.Add(renderer.transform);
            }
            foreach (Collider hit in instance.GetComponentsInChildren<Collider>()) hit.enabled = false;
            if (found)
            {
                float scale = 0.81f / Mathf.Max(bounds.size.y, 0.0001f);
                // OBJ importer mirrors the X axis; restore the reference's right-side handle.
                instance.transform.localScale = new Vector3(-scale, scale, scale);
                instance.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);
            }
            rig.restPosition = instance.transform.localPosition;
            Debug.Log("[BlenderModelRig] Loaded reference 3D mesh: " + renderers.Length + " parts.");
            return rig;
        }

        private static Material MakeMaterial(string key)
        {
            Shader painted = Resources.Load<Shader>("Art/Juicer/Models/IllustratedProp");
            if (painted != null && key != "Glass")
            {
                Material inkWash = new Material(painted);
                Color color = Color.white;
                switch (key)
                {
                    case "GlassEdge": color = new Color(0.06f, 0.07f, 0.075f); break;
                    case "Ink": color = new Color(0.023f, 0.027f, 0.030f); break;
                    case "Rubber": color = new Color(0.045f, 0.05f, 0.055f); break;
                    case "Juice": color = new Color(0.48f, 0.035f, 0.025f); break;
                    case "JuiceTop": color = new Color(0.63f, 0.07f, 0.045f); break;
                    case "Steel": color = new Color(0.29f, 0.34f, 0.35f); break;
                    default: inkWash.mainTexture = Resources.Load<Texture2D>("Art/Juicer/Models/enamel_brush"); break;
                }
                inkWash.color = color;
                // Draw the illustrated red fill after the paper-tinted shell so it stays burgundy.
                if (key == "Juice" || key == "JuiceTop") inkWash.renderQueue = 3020;
                inkWash.SetFloat("_OutlineWidth", key == "Enamel" ? 0.007f : 0.003f);
                return inkWash;
            }
            switch (key)
            {
                case "Glass":
                    Material glass = CardFactory.MakeUnlit(Resources.Load<Texture2D>("Art/Juicer/Models/glass_paper"));
                    glass.color = new Color(1f, 1f, 1f, 0.39f);
                    return glass;
                case "GlassEdge": return JuicerRig.MakeLit(new Color(0.40f, 0.56f, 0.58f), 0.65f, 0.15f);
                case "Ink": return JuicerRig.MakeLit(new Color(0.045f, 0.055f, 0.06f), 0.24f, 0.04f);
                case "Rubber": return JuicerRig.MakeLit(new Color(0.065f, 0.07f, 0.065f), 0.12f, 0f);
                case "Juice": return JuicerRig.MakeLit(new Color(0.62f, 0.045f, 0.025f), 0.30f, 0f);
                case "JuiceTop": return JuicerRig.MakeLit(new Color(0.82f, 0.10f, 0.045f), 0.45f, 0f);
                case "Steel": return JuicerRig.MakeLit(new Color(0.64f, 0.66f, 0.61f), 0.50f, 0.55f);
                default:
                    Material material = JuicerRig.MakeLit(new Color(0.93f, 0.96f, 0.97f), 0.33f, 0.12f);
                    material.mainTexture = Resources.Load<Texture2D>("Art/Juicer/Models/enamel_brush");
                    return material;
            }
        }

        private void LateUpdate()
        {
            bool running = owner != null && owner.IsStamping;
            transform.localPosition = restPosition + (running
                ? new Vector3(Mathf.Sin(Time.time * 74f) * 0.002f, 0f, Mathf.Cos(Time.time * 61f) * 0.001f)
                : Vector3.zero);
            if (running)
                foreach (Transform blade in blades) blade.RotateAround(transform.TransformPoint(new Vector3(0f, 0.39f, 0f)), transform.up, Time.deltaTime * 960f);
        }

        private void OnDestroy()
        {
            foreach (Material material in materials) if (material != null) CardFactory.DestroySafe(material);
        }
    }
}
