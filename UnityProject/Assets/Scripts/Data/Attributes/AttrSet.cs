using System;
using System.Collections.Generic;
using System.Text;

namespace GameJam.Data
{
    /// <summary>
    /// 属性集合：一个食材（或刀片、或任何东西）身上的一整套属性数值。
    ///
    /// 【为什么不用 Dictionary】
    /// Unity 不能序列化 Dictionary。所以用定长数组，下标就是 (int)AttrId。
    /// 好处是访问快、可序列化、顺序稳定。
    ///
    /// 【为什么单独一个类】
    /// 效果要能作用在"一个属性集合"上，而不关心它属于食材还是刀片还是杯子。
    /// 把数值容器抽出来，效果系统就只依赖 AttrSet，不依赖 Ingredient。
    /// </summary>
    [Serializable]
    public class AttrSet
    {
        /// <summary>下标 = (int)AttrId，长度 = AttrCatalog.Count</summary>
        public int[] values;

        public AttrSet()
        {
            values = new int[AttrCatalog.Count];
        }

        public AttrSet(int[] source)
        {
            values = new int[AttrCatalog.Count];
            if (source != null)
            {
                int n = source.Length < values.Length ? source.Length : values.Length;
                for (int i = 0; i < n; i++) values[i] = source[i];
            }
        }

        // ── 读写 ──────────────────────────────────────────────────────

        public int Get(AttrId id)
        {
            int i = (int)id;
            if (i < 0 || i >= values.Length) return 0;
            return values[i];
        }

        /// <summary>写入并夹到合法范围。</summary>
        public void Set(AttrId id, int value)
        {
            int i = (int)id;
            if (i < 0 || i >= values.Length) return;
            AttrDef def = AttrCatalog.Get(id);
            values[i] = def != null ? def.Clamp(value) : value;
        }

        /// <summary>加一个增量（可为负）。</summary>
        public void Add(AttrId id, int delta)
        {
            Set(id, Get(id) + delta);
        }

        /// <summary>设成最小值（"硬度 10，其他 0" 这种写法用得上）。</summary>
        public AttrSet AllTo(int value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                AttrId id = (AttrId)i;
                AttrDef def = AttrCatalog.Get(id);
                values[i] = def != null ? def.Clamp(value) : value;
            }
            return this;
        }

        /// <summary>链式设置，方便写假数据：new AttrSet().With(AttrId.Hardness, 10)</summary>
        public AttrSet With(AttrId id, int value)
        {
            Set(id, value);
            return this;
        }

        public AttrSet Clone()
        {
            return new AttrSet(values);
        }

        // ── 展示 ──────────────────────────────────────────────────────

        /// <summary>只列出非零属性，例如 "硬度 10"、"硬度 8 · 温度 -15 · 水分 10"。</summary>
        public string DescribeNonZero()
        {
            List<string> parts = new List<string>();
            foreach (AttrDef def in AttrCatalog.All())
            {
                int v = Get(def.id);
                if (v != 0) parts.Add(def.displayName + " " + def.Format(v));
            }
            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "（无属性）";
        }

        /// <summary>列出全部属性，例如 "硬度 10 · 温度 0 · 酸性 0 · ..."。</summary>
        public string DescribeAll()
        {
            List<string> parts = new List<string>();
            foreach (AttrDef def in AttrCatalog.All())
            {
                parts.Add(def.displayName + " " + def.Format(Get(def.id)));
            }
            return string.Join(" · ", parts.ToArray());
        }

        public override string ToString() { return DescribeNonZero(); }
    }
}
