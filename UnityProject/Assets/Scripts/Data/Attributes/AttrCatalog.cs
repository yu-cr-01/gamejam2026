using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 属性定义表。全局唯一一份，所有属性定义都注册在这里。
    ///
    /// 数值策划改范围只改这个文件，界面、校验、效果全部自动跟着走。
    /// </summary>
    public static class AttrCatalog
    {
        /// <summary>属性总数。AttrSet 的数组长度就是它。</summary>
        public static readonly int Count = 6;

        private static readonly AttrDef[] Defs =
        {
            new AttrDef(AttrId.Hardness,    "硬度",  0,  10),
            new AttrDef(AttrId.Temperature, "温度", -20, 300, "℃"),
            new AttrDef(AttrId.Acidity,     "酸性",  0,  10),
            new AttrDef(AttrId.Sugar,       "糖分",  0,  10),
            new AttrDef(AttrId.Oil,         "油脂",  0,  10),
            new AttrDef(AttrId.Water,       "水分",  0,  10),
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

        /// <summary>显示名，找不到就退回枚举名。</summary>
        public static string DisplayName(AttrId id)
        {
            AttrDef d = Get(id);
            return d != null ? d.displayName : id.ToString();
        }
    }
}
