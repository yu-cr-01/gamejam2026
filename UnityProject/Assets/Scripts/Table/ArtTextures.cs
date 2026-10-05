using UnityEngine;

namespace GameJam.Prototype
{
    /// <summary>
    /// 读贴图像素的小工具 —— 给运行期要拿美术图做处理的地方用（卡面拼图、立绘剪影）。
    ///
    /// 【为什么不能直接 GetPixels32】
    ///   它要求贴图导入时勾了 Read/Write。美术换图、.meta 没跟上、或者忘了勾，
    ///   就会抛 "texture data is either not readable, corrupted or does not exist"。
    ///   踩过一次狠的：异常从 BlenderArt.Attach 冒到 TableSetup.Awake，
    ///   搭桌子半路停住 —— 表现是**桌上一张卡都没有**（发牌/HUD/交互都在它后面）。
    ///
    /// 【策略：能读就直接读，不能读才绕 GPU】
    ///   勾了 Read/Write 的贴图走 GetPixels32 —— 这是原来就在跑的那条路，最快也最稳；
    ///   只有没勾的才 Blit 到 RenderTexture 再 ReadPixels 回读。
    ///   这样"本来能跑的"一行行为都不变，原来会抛异常的那些变成能跑。
    ///
    /// 注意：调用方仍然要接住异常 —— 回读本身也可能失败（比如没有 GPU 上下文），
    /// 该降级就降级，别让它冒到搭场景的代码里。
    /// </summary>
    internal static class ArtTextures
    {
        public static Color32[] Read(Texture2D tex)
        {
            if (tex == null) return null;
            if (tex.isReadable) return tex.GetPixels32();

            return ReadThroughGpu(tex);
        }

        private static Color32[] ReadThroughGpu(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                                                          RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;

                copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply(false, false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) CardFactory.DestroySafe(copy);
            }
        }
    }
}
