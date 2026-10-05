using UnityEditor;
using UnityEngine;

namespace GameJam.EditorTools
{
    /// <summary>
    /// Assets/Resources/Art 下的正式美术统一按 UI 贴图导入。
    ///
    /// 【为什么要用 AssetPostprocessor 而不是手改每张图的 Import 设置】
    ///   这批图会随美术迭代反复重导（换图、加元素）。导入设置写在代码里，
    ///   新图拖进来就自动对，不用一个个点、也不会有人漏点。
    ///
    /// 【几个设置的理由】
    ///   mipmapEnabled = false  —— 卡面/立绘都是"屏幕尺寸级"的贴图，
    ///                             开 mip 只会在斜视角下把图案糊掉，还多占 33% 显存
    ///   isReadable             —— **全部开读写**：两处都要在运行期用 CPU 读像素
    ///                             · 卡面：CardArt 用 GetPixels32() 把切片拼成一张卡面
    ///                             · 立绘：BlenderArt 读 alpha 剪影，挤出"3D 版"的厚度
    ///                             不开会直接抛 "Texture is not readable"。
    ///                             代价：立绘四帧 574x672 各约 1.5 MB 常驻，合计 ~6 MB，可接受。
    ///   alphaIsTransparency    —— 透明边缘向里渗色，避免 PNG 半透明边缘出现黑边
    ///   npotScale = None       —— ★ Unity 默认是 ToNearest：非 2 次幂的图会被**缩放**！
    ///                             实测 136x182 的卡底被压成 128x128、574x672 的立绘被压成 512x512，
    ///                             卡面版面（136x182 坐标系）和立绘比例会一起失真。
    ///                             这一条是本批次最容易踩的坑，ArtCheck 会盯着尺寸。
    ///   Uncompressed           —— 都是小图（最大 574x672），压缩省不下多少，
    ///                             但 DXT 的色块会毁掉卡面细边和文字牌
    ///   textureType = Default  —— 代码走 Resources.Load&lt;Texture2D&gt;，
    ///                             不是 Sprite（Quads 用的是材质 mainTexture，不是 Image）
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/Art/";

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(Root)) return;

            TextureImporter ti = assetImporter as TextureImporter;
            if (ti == null) return;

            ti.textureType         = TextureImporterType.Default;
            ti.mipmapEnabled       = false;
            ti.alphaIsTransparency = true;
            ti.npotScale           = TextureImporterNPOTScale.None;   // 别把 136x182 / 574x672 缩成 2 次幂
            ti.wrapMode            = TextureWrapMode.Clamp;
            ti.filterMode          = FilterMode.Bilinear;
            ti.textureCompression  = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize      = 2048;
            ti.sRGBTexture         = true;
            ti.isReadable          = true;    // 卡面拼图 + 立绘剪影都要 CPU 读像素
        }
    }
}
