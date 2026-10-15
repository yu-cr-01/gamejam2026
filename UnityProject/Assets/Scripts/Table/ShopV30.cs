using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Prototype
{
    [Serializable]
    public class ShopPoolCardV30
    {
        public string id;
        public string rarity;
    }

    [Serializable]
    public class ShopMerchantV30
    {
        public string id;
        public string name;
        public string rollPrompt;
        public ShopPoolCardV30[] pool;
    }

    [Serializable]
    public class ShopRarityValuesV30
    {
        public int common;
        public int rare;
        public int legendary;
    }

    [Serializable]
    public class ShopConfigV30
    {
        public int shopCount;
        public int spellCount;
        public int randomCount;
        public ShopRarityValuesV30 rarityWeights;
        public ShopRarityValuesV30 prices;
        public ShopMerchantV30[] merchants;
    }

    public class ShopOfferV30
    {
        public string id;
        public string name;
        public string rarity;
        public bool spell;
        public int price;
        public bool bought;
        public bool available = true;
    }

    /// <summary>一次商店访问的库存与抽取权重。重复卡牌作为不同货位出售。</summary>
    public class ShopVisitV30
    {
        public ShopMerchantV30 merchant;
        public readonly List<ShopOfferV30> offers = new List<ShopOfferV30>();
        public bool blankCardSpent;
        public int rerollsRemaining;

        private readonly Dictionary<string, double> remainingWeights = new Dictionary<string, double>();
        private readonly System.Random random = new System.Random();

        public static ShopConfigV30 Config { get; private set; }

        public static ShopConfigV30 LoadConfig()
        {
            if (Config != null) return Config;
            TextAsset asset = Resources.Load<TextAsset>("Config/shop_v30");
            if (asset == null)
            {
                Debug.LogError("[ShopV30] 找不到 Resources/Config/shop_v30.json");
                return null;
            }

            Config = JsonUtility.FromJson<ShopConfigV30>(asset.text);
            if (Config == null || Config.merchants == null || Config.merchants.Length != 3 ||
                Config.shopCount != 5 || Config.spellCount != 1 || Config.randomCount != 4 ||
                Config.rarityWeights == null || Config.prices == null)
            {
                Debug.LogError("[ShopV30] 商店配置结构无效；需要3个商人、每店1张法术+4张随机卡");
                Config = null;
                return null;
            }
            return Config;
        }

        public static ShopVisitV30 Begin(int completedTurn)
        {
            ShopConfigV30 cfg = LoadConfig();
            if (cfg == null || completedTurn < 1 || completedTurn > cfg.merchants.Length) return null;

            ShopVisitV30 visit = new ShopVisitV30();
            visit.merchant = cfg.merchants[completedTurn - 1];
            visit.DrawStock();
            return visit;
        }

        public bool CanPayFor(ShopOfferV30 offer, int score)
        {
            return offer != null && offer.available && !offer.bought && score >= offer.price;
        }

        public void MarkBought(ShopOfferV30 offer)
        {
            // 货位是购买单位：同名重复卡可以分别购买，但每个货位只能买一次。
            if (offer != null) offer.bought = true;
        }

        public bool Reroll(bool payBlankCard)
        {
            if (merchant == null) return false;
            if (payBlankCard)
            {
                if (blankCardSpent || rerollsRemaining != 0) return false;
                blankCardSpent = true;
                rerollsRemaining = 2;
            }
            if (rerollsRemaining <= 0) return false;
            rerollsRemaining--;
            DrawStock();
            return true;
        }

        private void DrawStock()
        {
            offers.Clear();
            ShopConfigV30 cfg = LoadConfig();
            if (cfg == null || merchant == null || merchant.pool == null) return;

            List<ShopPoolCardV30> usable = new List<ShopPoolCardV30>();
            List<ShopPoolCardV30> spells = new List<ShopPoolCardV30>();
            for (int i = 0; i < merchant.pool.Length; i++)
            {
                ShopPoolCardV30 item = merchant.pool[i];
                if (item == null || string.IsNullOrEmpty(item.id)) continue;
                string name;
                bool isSpell;
                usable.Add(item);
                if (TryResolve(item.id, out name, out isSpell) && isSpell) spells.Add(item);
                if (!remainingWeights.ContainsKey(item.id))
                    remainingWeights[item.id] = BaseWeight(cfg, item.rarity);
            }

            ShopPoolCardV30 fixedSpell = Pick(spells, cfg);
            if (fixedSpell != null) AddOffer(fixedSpell, cfg);
            for (int i = 0; i < cfg.randomCount; i++)
            {
                ShopPoolCardV30 picked = Pick(usable, cfg);
                if (picked == null) break;
                AddOffer(picked, cfg);
                remainingWeights[picked.id] *= 0.5d;
            }

            if (offers.Count != cfg.shopCount)
                Debug.LogWarning("[ShopV30] " + merchant.name + " 当前可抽卡牌不足：展示 " + offers.Count + "/" + cfg.shopCount +
                                 " 张；配置中的未完成卡牌会在定义补齐后加入抽取。");
        }

        private void AddOffer(ShopPoolCardV30 item, ShopConfigV30 cfg)
        {
            string name;
            bool isSpell;
            bool available = TryResolve(item.id, out name, out isSpell);
            if (!available && item.id == "gold_vapor") name = "金蒸气（属性待定）";
            offers.Add(new ShopOfferV30
            {
                id = item.id,
                name = name,
                rarity = item.rarity,
                spell = isSpell,
                price = Price(cfg, item.rarity),
                bought = false,
                available = available,
            });
        }

        private ShopPoolCardV30 Pick(List<ShopPoolCardV30> pool, ShopConfigV30 cfg)
        {
            if (pool == null || pool.Count == 0) return null;
            double total = 0d;
            for (int i = 0; i < pool.Count; i++)
            {
                double weight;
                if (!remainingWeights.TryGetValue(pool[i].id, out weight))
                    weight = BaseWeight(cfg, pool[i].rarity);
                if (weight > 0d) total += weight;
            }
            if (total <= 0d) return null;

            double ticket = random.NextDouble() * total;
            for (int i = 0; i < pool.Count; i++)
            {
                double weight;
                if (!remainingWeights.TryGetValue(pool[i].id, out weight))
                    weight = BaseWeight(cfg, pool[i].rarity);
                if (weight <= 0d) continue;
                ticket -= weight;
                if (ticket < 0d) return pool[i];
            }
            return pool[pool.Count - 1];
        }

        private static double BaseWeight(ShopConfigV30 cfg, string rarity)
        {
            if (rarity == "common") return Math.Max(0, cfg.rarityWeights.common);
            if (rarity == "rare") return Math.Max(0, cfg.rarityWeights.rare);
            if (rarity == "legendary") return Math.Max(0, cfg.rarityWeights.legendary);
            return 0d;
        }

        private static int Price(ShopConfigV30 cfg, string rarity)
        {
            if (rarity == "common") return cfg.prices.common;
            if (rarity == "rare") return cfg.prices.rare;
            if (rarity == "legendary") return cfg.prices.legendary;
            return int.MaxValue;
        }

        private static bool TryResolve(string id, out string name, out bool spell)
        {
            GameJam.Data.Ingredient material = GameJam.Prototype.CardSpecs.Material(id);
            if (material != null) { name = material.name; spell = false; return true; }
            GameJam.Data.Spell spellCard = GameJam.Prototype.CardSpecs.Spell(id);
            if (spellCard != null) { name = spellCard.name; spell = true; return true; }
            name = id;
            spell = false;
            return false;
        }
    }
}
