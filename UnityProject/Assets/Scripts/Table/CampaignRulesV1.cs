using System;
using System.Collections.Generic;
using UnityEngine;
using GameJam.Data;

namespace GameJam.Prototype
{
    [Serializable]
    public class TutorialDeckV1
    {
        public string id;
        public string title;
        public string subtitle;
        public string[] materialIds;
        public string spellId;
    }

    [Serializable]
    public class CampaignLevelV1
    {
        public string id;
        public string npcName;
        public string objective;
        public string specialCondition;
        public int scoreNumerator = 1;
        public int scoreDenominator = 1;
        public int shapeChangeScoreBonus;
        public string[] startingSpellIds;
        public int startingCold;
        public string intro;
        public string taunt;
        public string urge;
        public string praise;
        public string farewell;
    }

    [Serializable]
    public class CampaignProfessionV1
    {
        public string id;
        public string name;
        public int tier;
        public string description;
    }

    [Serializable]
    public class CampaignHiddenRoomsV1
    {
        public int mercuryBaseChance = 10;
        public int mercuryChancePerBlank = 10;
        public int mercuryChanceCap = 100;
        public int sacrificeChanceCap = 100;
        public int sacrificeBlankReward = 5;
        public string[] mercuryMaterialIds;
        public CampaignProfessionV1[] tierOneProfessions;
        public CampaignProfessionV1[] tierTwoProfessions;
    }

    [Serializable]
    public class CampaignConfigV1
    {
        public TutorialDeckV1[] tutorialDecks;
        public CampaignLevelV1[] levels;
        public CampaignHiddenRoomsV1 hiddenRooms;
        public string tutorialIntro;
        public string[] tableCallouts;
        public string tutorialPersonality;
        public string tutorialBlade;
        public string tutorialAttributes;
        public string tutorialTarget;
        public string tutorialFourthStart;
        public string tutorialShopGreeting;
        public string tutorialPassed;
        public string tutorialFailed;
        public string hiddenRoomNoProfession;
        public string[] mercuryReflections;
        public string[] sacrificeReflections;
        public string[] shopGreetings;
    }

    [Serializable]
    public class CampaignProgressV1
    {
        public bool tutorialCompleted;
        public int blankCardsSinceMercury;
        public int sacrificedHardness;
        public List<string> specialProfessionIds = new List<string>();
    }

    /// <summary>战役配置与跨关卡隐藏房进度。房间概率进度独立于商店可消费的空白卡库存。</summary>
    public static class CampaignRulesV1
    {
        private const string ResourcePath = "Config/campaign_v1";
        private const string ProgressKey = "gamejam.campaign.v1.progress";
        private static CampaignConfigV1 config;
        private static CampaignProgressV1 progress;

        public static CampaignConfigV1 Config
        {
            get { EnsureLoaded(); return config; }
        }

        public static CampaignProgressV1 Progress
        {
            get { EnsureLoaded(); return progress; }
        }

        public static CampaignLevelV1 Level(int index)
        {
            CampaignLevelV1[] all = Config.levels;
            return all != null && index >= 0 && index < all.Length ? all[index] : null;
        }

        public static Choice TutorialDeckChoice()
        {
            Choice choice = new Choice(GameConfig.DeckPickId, "選擇你的初始素材", 1)
                .WithHint("三副牌組各有四張素材和一張法術，之後再選一張素材作為刀片核心")
                .WithView(ChoiceView.DeckCards);

            TutorialDeckV1[] decks = Config.tutorialDecks;
            if (decks == null) return choice;
            for (int i = 0; i < decks.Length; i++)
            {
                TutorialDeckV1 definition = decks[i];
                Deck deck = BuildDeck(definition);
                if (deck == null) continue;
                choice.AddOptions(new ChoiceOption(deck.id, deck.name, ChoiceOptionKind.Deck, deck.id)
                    .WithSubtitle(definition.subtitle ?? "")
                    .WithDeck(deck));
            }
            return choice;
        }

        public static int MercuryChance
        {
            get
            {
                CampaignHiddenRoomsV1 rooms = Config.hiddenRooms;
                int baseChance = rooms != null ? rooms.mercuryBaseChance : 10;
                int perBlank = rooms != null ? rooms.mercuryChancePerBlank : 10;
                int cap = rooms != null ? rooms.mercuryChanceCap : 100;
                return Mathf.Clamp(baseChance + Progress.blankCardsSinceMercury * perBlank, 0, Mathf.Max(0, cap));
            }
        }

        public static int SacrificeChance
        {
            get
            {
                int cap = Config.hiddenRooms != null ? Config.hiddenRooms.sacrificeChanceCap : 100;
                return Mathf.Clamp(Progress.sacrificedHardness, 0, Mathf.Max(0, cap));
            }
        }

        public static void RecordBlankCards(int count)
        {
            if (count <= 0) return;
            Progress.blankCardsSinceMercury += count;
            SaveProgress();
        }

        public static void RecordSacrifice(int hardness)
        {
            if (hardness <= 0) return;
            Progress.sacrificedHardness += hardness;
            SaveProgress();
        }

        public static bool RollMercuryRoom()
        {
            if (MercuryChance <= 0) return false;
            bool triggered = UnityEngine.Random.Range(0, 100) < MercuryChance;
            if (triggered)
            {
                Progress.blankCardsSinceMercury = 0;
                SaveProgress();
            }
            return triggered;
        }

        public static bool RollSacrificeRoom()
        {
            if (SacrificeChance <= 0) return false;
            bool triggered = UnityEngine.Random.Range(0, 100) < SacrificeChance;
            if (triggered)
            {
                Progress.sacrificedHardness = 0;
                SaveProgress();
            }
            return triggered;
        }

        public static List<string> MercuryMaterialIds()
        {
            CampaignHiddenRoomsV1 rooms = Config.hiddenRooms;
            List<string> ids = new List<string>();
            if (rooms != null && rooms.mercuryMaterialIds != null)
                ids.AddRange(rooms.mercuryMaterialIds);
            return ids;
        }

        public static List<CampaignProfessionV1> DrawProfessions(int tier, int count)
        {
            List<CampaignProfessionV1> pool = new List<CampaignProfessionV1>();
            CampaignHiddenRoomsV1 rooms = Config.hiddenRooms;
            CampaignProfessionV1[] source = rooms == null ? null :
                (tier == 1 ? rooms.tierOneProfessions : rooms.tierTwoProfessions);
            if (source != null)
                for (int i = 0; i < source.Length; i++)
                    if (source[i] != null && source[i].tier == tier && !string.IsNullOrEmpty(source[i].id))
                        pool.Add(source[i]);

            List<CampaignProfessionV1> result = new List<CampaignProfessionV1>();
            while (pool.Count > 0 && result.Count < count)
            {
                int index = UnityEngine.Random.Range(0, pool.Count);
                result.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return result;
        }

        public static void RecordSpecialProfession(CampaignProfessionV1 profession)
        {
            if (profession == null || string.IsNullOrEmpty(profession.id)) return;
            if (Progress.specialProfessionIds == null) Progress.specialProfessionIds = new List<string>();
            // 重复获取保留为多个记录，便于职业效果以后按层数叠加。
            Progress.specialProfessionIds.Add(profession.id);
            SaveProgress();
        }

        public static void ResetProgress()
        {
            progress = new CampaignProgressV1();
            SaveProgress();
        }

        public static void SaveProgress()
        {
            EnsureLoaded();
            PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(progress));
            PlayerPrefs.Save();
        }

        private static Deck BuildDeck(TutorialDeckV1 definition)
        {
            if (definition == null || definition.materialIds == null || definition.materialIds.Length == 0) return null;
            Deck deck = new Deck(definition.id, definition.title);
            for (int i = 0; i < definition.materialIds.Length; i++)
            {
                Ingredient material = CardSpecs.Material(definition.materialIds[i]);
                if (material == null)
                {
                    Debug.LogWarning("[CampaignV1] 初始牌组缺少素材：" + definition.materialIds[i]);
                    return null;
                }
                deck.ingredients.Add(material.Clone());
            }

            Spell spell = CardSpecs.Spell(definition.spellId);
            if (spell != null)
                deck.modules.Add(new SpeedModule("tutorial_spell_" + spell.id, spell.name, new EffectGroup("教程初始法术")));
            else
                Debug.LogWarning("[CampaignV1] 初始牌组缺少法术：" + definition.spellId);
            return deck;
        }

        private static void EnsureLoaded()
        {
            if (progress == null)
            {
                string json = PlayerPrefs.GetString(ProgressKey, "");
                progress = string.IsNullOrEmpty(json) ? new CampaignProgressV1() :
                    JsonUtility.FromJson<CampaignProgressV1>(json);
                if (progress == null) progress = new CampaignProgressV1();
                if (progress.specialProfessionIds == null) progress.specialProfessionIds = new List<string>();
            }

            if (config != null) return;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null)
            {
                try { config = JsonUtility.FromJson<CampaignConfigV1>(asset.text); }
                catch (Exception e) { Debug.LogError("[CampaignV1] 配置解析失败：" + e.Message); }
            }
            if (config == null) config = new CampaignConfigV1();
            if (config.hiddenRooms == null) config.hiddenRooms = new CampaignHiddenRoomsV1();
        }
    }
}
