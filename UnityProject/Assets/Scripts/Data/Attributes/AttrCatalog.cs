using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 属性定义表。全局唯一一份，所有属性定义都注册在这里。
    ///
    /// 数值策划改范围只改这个文件，界面、校验、效果全部自动跟着走。
    ///
    /// 【顺序 = AttrId 的下标】Defs[i] 必须对应 (AttrId)i，
    /// 加属性时两边一起改（AttrId 追加 → 这里按同样顺序追加）。
    /// </summary>
    public static class AttrCatalog
    {
        /// <summary>属性总数。AttrSet 的数组长度就是它。</summary>
        public static readonly int Count = 3;

        private static readonly AttrDef[] Defs =
        {
            new AttrDef(
                AttrId.Salt, "H", "盐性", "固体倾向",
                "耐久、生命值。H 归零，固态原材变粉末；刀片 H 归零爆刀。",
                0, 99),

            new AttrDef(
                AttrId.Mercury, "D", "汞性", "液体倾向",
                "溶解力、流动性、催化力。D 越高，粉末越快变溶液；溶液润滑刀片、催化粉末。",
                0, 99),

            new AttrDef(
                AttrId.Sulfur, "V", "硫性", "气体倾向",
                "挥发性、膨胀力、压力。V 越高，溶液越快变气体；气体压力削刀片。",
                0, 99),
        };

        /// <summary>按 ID 取定义。</summary>
        public static AttrDef Get(AttrId id)
        {
            int i = (int)id;
            if (i < 0 || i >= Defs.Length) return null;
            return Defs[i];
        }

        /// <summary>遍历所有属性定义，按 ID 顺序。</summary>
        public static IEnumerable<AttrDef> All()
        {
            for (int i = 0; i < Defs.Length; i++) yield return Defs[i];
        }

        /// <summary>显示名（炼金名），找不到就退回枚举名。</summary>
        public static string DisplayName(AttrId id)
        {
            AttrDef d = Get(id);
            return d != null ? d.displayName : id.ToString();
        }

        /// <summary>字母代号，例如 "H"。</summary>
        public static string Letter(AttrId id)
        {
            AttrDef d = Get(id);
            return d != null ? d.letter : "";
        }

        /// <summary>短标签 "H 盐性"。</summary>
        public static string Label(AttrId id)
        {
            AttrDef d = Get(id);
            return d != null ? d.Label : id.ToString();
        }

        /// <summary>按字母代号反查属性 —— 配置表里写 "H"/"D"/"V" 时用得上。</summary>
        public static bool TryParse(string letterOrName, out AttrId id)
        {
            id = AttrId.Salt;
            if (string.IsNullOrEmpty(letterOrName)) return false;

            string key = letterOrName.Trim().ToUpperInvariant();
            for (int i = 0; i < Defs.Length; i++)
            {
                AttrDef d = Defs[i];
                if (d == null) continue;
                if (d.letter.ToUpperInvariant() == key ||
                    d.displayName == letterOrName.Trim() ||
                    d.id.ToString().ToUpperInvariant() == key)
                {
                    id = d.id;
                    return true;
                }
            }
            return false;
        }
    }
}
