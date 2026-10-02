using System;
using System.Collections.Generic;

namespace GameJam.Data
{
    /// <summary>
    /// 回合状态：一关之内的过程数据。
    ///
    /// 【和选择记录的分工】
    ///   TurnState    管"现在场上是什么"（手牌 / 刀片 / 杯内 / 得分）
    ///   SelectionLog 管"玩家做过什么选择"
    /// 两者互不依赖 —— 回合状态推进不用知道玩家当初怎么选的。
    ///
    /// 【效果组合挂在哪】
    /// activeEffects 是本局累积的效果组合（牌组模块 + 关卡规则 + 已投食材）。
    /// 它作用在任何一个 AttrSet 上，不关心那个集合属于谁。
    /// </summary>
    [Serializable]
    public class TurnState
    {
        /// <summary>当前回合数，从 1 开始</summary>
        public int turnNumber = 1;

        /// <summary>手牌里的食材</summary>
        public List<Ingredient> hand = new List<Ingredient>();

        /// <summary>
        /// 手牌里的变速模块。
        ///
        /// 【Day 3 起模块也进手牌】
        /// 之前模块是"带上就自动生效"的被动加成，直接并进 activeEffects。
        /// 现在改成一局开始时进手牌，玩家每回合挑一个投放，投出去才生效。
        /// 所以手牌实际上是"食材 + 模块"两类东西 —— 界面按类型标签区分。
        ///
        /// 单独一个列表而不是混进 hand：两者的类型不同（Ingredient / SpeedModule），
        /// 混在一起就得给 List 换个能装两种类型的类型，所有取值处都要改。
        /// 分两个列表 + 界面上合并显示，够用而且改动面小。
        /// </summary>
        public List<SpeedModule> modules = new List<SpeedModule>();

        /// <summary>已经投放过的模块，按投放顺序留档（界面上要显示"已应用"）。</summary>
        public List<SpeedModule> appliedModules = new List<SpeedModule>();

        /// <summary>当前刀片（也是一个 Ingredient）</summary>
        public Ingredient blade;

        /// <summary>杯内食材</summary>
        public List<Ingredient> cup = new List<Ingredient>();

        /// <summary>当前得分</summary>
        public int score;

        /// <summary>本关目标分</summary>
        public int targetScore;

        /// <summary>本局累积的效果组合（关卡规则 + 已投放的模块 + 食材自带效果）</summary>
        public EffectGroup activeEffects = new EffectGroup("本局效果");

        /// <summary>手牌（食材 + 模块）是否已经全空 —— 关卡结束的判据。</summary>
        public bool IsHandEmpty
        {
            get
            {
                bool noIngredients = (hand == null || hand.Count == 0);
                bool noModules     = (modules == null || modules.Count == 0);
                return noIngredients && noModules;
            }
        }

        /// <summary>手牌总项数 = 食材数 + 模块数。</summary>
        public int HandCount
        {
            get
            {
                int n = hand != null ? hand.Count : 0;
                if (modules != null) n += modules.Count;
                return n;
            }
        }

        // ── 生命周期 ──────────────────────────────────────────────────

        /// <summary>开始一关。等价于 PrepareLoadout，保留旧名字方便调用方不动。</summary>
        public void StartLevel(LevelData level, Deck deck, Ingredient defaultBlade = null)
        {
            PrepareLoadout(level, deck, defaultBlade);
        }

        /// <summary>
        /// 准备开局配置 —— 牌组里指定的那张牌装到刀片槽，其余全部进手牌。
        ///
        /// 【规则来自策划文档】
        ///   牌组「硫硝爆燃」= 外星合金 / 水 / 硝石 / 硫磺
        ///   开局外星合金当刀片（H=20），手牌 = 水、硝石、硫磺
        /// 也就是"初始刀片是牌组里的一张牌，不是额外的第六张"。
        ///
        /// 【兜底】牌组为空时用 fallbackBlade（配置里的铁块），手牌留空。
        /// 这样"铁块"作为系统默认刀座始终存在，但正常的牌组不会用到它。
        /// </summary>
        public void PrepareLoadout(LevelData level, Deck deck, Ingredient fallbackBlade = null)
        {
            Reset();
            targetScore = level != null ? level.targetScore : 0;

            // 本局效果 = 关卡规则。
            // 牌组的模块**不再**在这里自动并进来 —— 它们要进手牌等玩家自己投。
            activeEffects = new EffectGroup("本局效果");
            if (level != null) activeEffects.Append(level.rules);

            if (hand == null) hand = new List<Ingredient>();
            if (modules == null) modules = new List<SpeedModule>();

            // 牌组为空（或没配）→ 只放兜底刀片
            if (deck == null || deck.ingredients == null || deck.ingredients.Count == 0)
            {
                blade = fallbackBlade != null ? fallbackBlade.Clone() : null;
                return;
            }

            int bladeIndex = deck.InitialBladeIndex();
            Ingredient initial = bladeIndex >= 0 ? deck.ingredients[bladeIndex] : null;

            // 刀片：牌组指定的那张 → 没找到才用兜底
            if (initial != null)             blade = initial.Clone();
            else if (fallbackBlade != null)  blade = fallbackBlade.Clone();
            else                             blade = null;

            // 手牌 = 除初始刀片外的全部牌
            for (int i = 0; i < deck.ingredients.Count; i++)
            {
                if (i == bladeIndex) continue;
                if (deck.ingredients[i] != null) hand.Add(deck.ingredients[i].Clone());
            }

            // 牌组的变速模块也进手牌（Day 3 规则：模块要玩家自己投，不是自动生效）
            if (deck.modules != null)
            {
                for (int i = 0; i < deck.modules.Count; i++)
                {
                    if (deck.modules[i] != null) modules.Add(deck.modules[i].Clone());
                }
            }
        }

        /// <summary>
        /// 把手牌第 index 张换上来当刀片，原刀片回到手牌**同一个位置**。
        ///
        /// 【为什么用"原位替换"而不是"移除 + 追加"】
        /// 移除+追加要分别动两个列表，任何一步异常都会留下
        /// "手牌少一张"或"刀片凭空消失"的脏状态。
        /// 原位替换只有两次赋值，天然不会重复、不会丢失，
        /// 所以铁块被换下后一定出现在手牌里，也能再次被换回去。
        ///
        /// 返回 false 表示这次交换没发生（下标越界 / 手牌空 / 刀片为空）。
        /// </summary>
        public bool SwapBladeWithHand(int index)
        {
            if (hand == null || blade == null) return false;
            if (index < 0 || index >= hand.Count) return false;

            Ingredient picked = hand[index];
            if (picked == null) return false;

            hand[index] = blade;    // 原刀片（可能是铁块，也可能是上一次换下的食材）回手牌原位
            blade = picked;         // 被点的那张成为新刀片
            return true;
        }

        /// <summary>手牌里还能不能换刀片（至少要有一张食材）。</summary>
        public bool CanSwapBlade
        {
            get { return blade != null && hand != null && hand.Count > 0; }
        }

        /// <summary>清空一切。</summary>
        public void Reset()
        {
            turnNumber = 1;
            score = 0;
            targetScore = 0;
            hand = new List<Ingredient>();
            modules = new List<SpeedModule>();
            appliedModules = new List<SpeedModule>();
            cup = new List<Ingredient>();
            blade = null;
            activeEffects = new EffectGroup("本局效果");
        }

        /// <summary>进入下一回合。</summary>
        public void NextTurn()
        {
            turnNumber++;
            cup = new List<Ingredient>();
        }

        // ── 动作 ──────────────────────────────────────────────────────

        /// <summary>
        /// 从手牌打出一张，放进杯子。返回打出的那张。
        ///
        /// ★ 食材自带的 effects 也要并进本局效果 —— 和 PlayModule 对称。
        ///   activeEffects 的注释里一直写着"关卡规则 + 已投放的模块 + 食材自带效果"，
        ///   但这里从来没并过，食材的效果一直是丢的。
        ///   多张牌同时发动时这一点尤其要命：模块叠得起来，食材的却全丢。
        /// </summary>
        public Ingredient PlayFromHand(int index)
        {
            if (hand == null || index < 0 || index >= hand.Count) return null;

            Ingredient ing = hand[index];
            hand.RemoveAt(index);

            if (ing != null)
            {
                cup.Add(ing);

                if (ing.effects != null) activeEffects.Append(ing.effects);
            }
            return ing;
        }

        /// <summary>
        /// 投放一个变速模块：从手牌移除，效果并进本局效果，并留档到已应用列表。
        ///
        /// 【为什么不直接把效果写死在刀片属性上】
        /// 刀片属性是"基础值 + 本局效果"算出来的（BladeResolvedAttrs），
        /// 把模块效果并进 activeEffects，刀片显示的数值会自动跟着变；
        /// 直接改刀片的基础值的话，"换刀片"之后那些加成就会跟着跑到新刀片上，
        /// 这是不对的 —— 模块加的是本局的加工能力，不是某一把刀片的属性。
        /// </summary>
        public SpeedModule PlayModule(int index)
        {
            if (modules == null || index < 0 || index >= modules.Count) return null;

            SpeedModule m = modules[index];
            modules.RemoveAt(index);

            if (m != null)
            {
                if (m.effects != null) activeEffects.Append(m.effects);
                appliedModules.Add(m);
            }
            return m;
        }

        // ── 查询 ──────────────────────────────────────────────────────

        public List<string> HandNamesList()
        {
            List<string> names = new List<string>();
            if (hand != null)
                for (int i = 0; i < hand.Count; i++)
                    if (hand[i] != null) names.Add(hand[i].name);
            return names;
        }

        public string HandNames()
        {
            List<string> n = HandNamesList();
            return n.Count > 0 ? string.Join("、", n.ToArray()) : "（空）";
        }

        public string CupNames()
        {
            List<string> names = new List<string>();
            if (cup != null)
                for (int i = 0; i < cup.Count; i++)
                    if (cup[i] != null) names.Add(cup[i].name);
            return names.Count > 0 ? string.Join("、", names.ToArray()) : "（空）";
        }

        public string BladeName()
        {
            return blade != null ? blade.name : "（未选择）";
        }

        /// <summary>刀片叠加本局效果之后的实际属性。</summary>
        public AttrSet BladeResolvedAttrs()
        {
            if (blade == null) return new AttrSet();
            return blade.ResolveAttrs(activeEffects);
        }

        /// <summary>刀片实际硬度 —— 走的是效果组合，不是读死字段。</summary>
        public int BladeHardness()
        {
            return BladeResolvedAttrs().Get(AttrId.Salt);
        }

        /// <summary>刀片实际属性，带字母标签，例如 "H 盐性 20 · D 汞性 0 · V 硫性 3"。</summary>
        public string BladeAttrLine()
        {
            return BladeResolvedAttrs().DescribeLabeled();
        }
    }
}
