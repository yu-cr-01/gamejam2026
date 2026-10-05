using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using GameJam.Data;
using GameJam.Prototype;

namespace GameJam.EditorTools
{
    /// <summary>
    /// 美术接入自检 —— 美术换图 / 加元素之后跑一遍，确认「图在、能读、拼得出、映射对」。
    ///
    /// 【为什么要有这个】
    ///   卡面是**运行期拼**的（CardArt：通用卡底 + 元素插画 + 名字牌 + 数值牌）。
    ///   少一张图、贴图没开 Read/Write、元素名写错，在编辑器里都不会报编译错，
    ///   要等到发牌那一刻才在 Console 里冒一条 warning —— 很容易漏。
    ///   这里一次把这些都查了，顺带把「哪张牌用哪套皮肤」整张表打出来，
    ///   映射改错了当场就能看见（比如铁块跑到 ice 上去了）。
    ///
    /// 【用法】
    ///   菜单：工具 → 检查美术接入
    ///   命令行：
    ///     Unity.exe -projectPath &lt;工程&gt; -executeMethod
    ///       GameJam.EditorTools.ArtCheck.Check -quit
    ///   任何一项缺失/不可读 → 退出码 1（方便挂到 CI 或提交前脚本上）。
    /// </summary>
    public static class ArtCheck
    {
        private static readonly string[] CardPieces =
        {
            "card_common_bg", "card_name_bg", "card_number_bg",
        };

        private static readonly string[] Elements =
        {
            "ice", "water", "vagour", "extraterrestrialalloy",
        };

        private static readonly string[] JuicerFrames =
        {
            BlenderArt.IdleFrameName,
            "gameblender_move_01_img", "gameblender_move_02_img", "gameblender_move_03_img",
        };

        [MenuItem("工具/检查美术接入")]
        public static void CheckFromMenu()
        {
            Run(false);
        }

        /// <summary>命令行入口 —— 有问题就返回非 0。</summary>
        public static void Check()
        {
            EditorApplication.Exit(Run(true) ? 0 : 1);
        }

        private static bool Run(bool quiet)
        {
            StringBuilder sb = new StringBuilder();
            int problems = 0;

            sb.AppendLine("===== 卡面切片（" + CardArt.ArtRoot + "）");
            problems += ReportTexture(sb, CardArt.ArtRoot + "card_common_bg", true);
            problems += ReportTexture(sb, CardArt.ArtRoot + "card_name_bg", true);
            problems += ReportTexture(sb, CardArt.ArtRoot + "card_number_bg", true);

            foreach (string e in Elements)
            {
                problems += ReportTexture(sb, CardArt.ArtRoot + "card_" + e + "_img", true);
                problems += ReportTexture(sb, CardArt.ArtRoot + "card_" + e + "_bg", false);
            }

            sb.AppendLine();
            sb.AppendLine("===== 卡面拼图（CardArt，运行期真正走的那条路）");
            string dumpDir = System.Environment.GetEnvironmentVariable("DSH_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dumpDir)) dumpDir = System.IO.Path.GetTempPath();
            System.IO.Directory.CreateDirectory(dumpDir);

            foreach (string e in Elements)
            {
                Texture2D face = CardArt.Face(e);
                if (face == null)
                {
                    sb.AppendLine("  " + e.PadRight(24) + " 拼不出来（会退回程序化卡面）");
                    problems++;
                    continue;
                }

                Color32 c = face.GetPixels32()[(face.height / 2) * face.width + 8];
                sb.AppendLine("  " + e.PadRight(24) + " " + face.width + "x" + face.height +
                              "  左侧取样像素 RGBA(" + c.r + "," + c.g + "," + c.b + "," + c.a + ")");

                // 把拼好的卡面写出来 —— 出了图才能跟美术的效果图对，别只信日志
                try
                {
                    string path = System.IO.Path.Combine(dumpDir, "face_" + e + ".png");
                    System.IO.File.WriteAllBytes(path, face.EncodeToPNG());
                    sb.AppendLine("       → 已写出 " + path);
                }
                catch (System.Exception ex)
                {
                    sb.AppendLine("       → 写出失败：" + ex.Message);
                }
            }

            sb.AppendLine();
            sb.AppendLine("===== 破壁机立绘（" + BlenderArt.ArtRoot + "）");
            foreach (string f in JuicerFrames)
                problems += ReportTexture(sb, BlenderArt.ArtRoot + f, true);   // 剪影要读 alpha，必须可读

            sb.AppendLine();
            sb.AppendLine("===== 元素映射（改 CardArt.ElementOf 后看这张表）");
            List<Deck> decks = GameConfig.Decks();
            HashSet<string> seen = new HashSet<string>();
            foreach (Deck d in decks)
            {
                if (d == null || d.ingredients == null) continue;
                foreach (Ingredient ing in d.ingredients)
                {
                    if (ing == null || !seen.Add(ing.id)) continue;
                    string element = CardArt.ElementOf(ing.id, ing, false);
                    sb.AppendLine("  " + ing.id.PadRight(20) + " " + ing.name.PadRight(8) +
                                  " → " + (element == null ? "（程序化）" : element));
                }
            }
            sb.AppendLine("  变速模块（任意）        → （程序化，刻意不给皮肤）");

            string report = sb.ToString();
            Debug.Log("[美术自检]\n" + report);
            if (!quiet) Debug.Log("[美术自检] 问题项：" + problems);
            return problems == 0;
        }

        private static int ReportTexture(StringBuilder sb, string path, bool mustBeReadable)
        {
            Texture2D t = Resources.Load<Texture2D>(path);
            if (t == null)
            {
                sb.AppendLine("  [缺] " + path);
                return 1;
            }

            bool readable = t.isReadable;
            int problems = 0;

            // 和磁盘上 PNG 的原始尺寸对一下 —— 导入设置一旦把图缩了（npotScale / maxTextureSize），
            // 卡面版面（按 136x182 量的）和立绘比例会一起失真，而这一步不会报任何错。
            Vector2Int src = PngHeaderSize("Assets/Resources/" + path + ".png");

            sb.AppendLine("  [有] " + path.PadRight(46) + " " + t.width + "x" + t.height +
                          "  readable=" + readable);

            if (src.x > 0 && (src.x != t.width || src.y != t.height))
            {
                sb.AppendLine("       ★ 被导入设置缩放了：原图 " + src.x + "x" + src.y +
                              " → 导入后 " + t.width + "x" + t.height +
                              "（检查 npotScale / maxTextureSize）");
                problems++;
            }

            if (mustBeReadable && !readable)
            {
                sb.AppendLine("       ↑ 这张要在运行期 GetPixels32() 拼图，必须开 Read/Write" +
                              "（导入设置见 Editor/ArtImportSettings.cs，右键 Reimport 一次即可）");
                problems++;
            }
            return problems;
        }

        /// <summary>直接读 PNG 头里的 IHDR 尺寸（避免再依赖一个贴图导入器）。读不到返回 (0,0)。</summary>
        private static Vector2Int PngHeaderSize(string assetPath)
        {
            try
            {
                using (System.IO.FileStream fs = System.IO.File.OpenRead(assetPath))
                {
                    byte[] b = new byte[24];
                    if (fs.Read(b, 0, 24) < 24) return Vector2Int.zero;
                    int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                    int h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                    return new Vector2Int(w, h);
                }
            }
            catch
            {
                return Vector2Int.zero;
            }
        }
    }
}
