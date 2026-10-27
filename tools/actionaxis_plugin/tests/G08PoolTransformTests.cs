using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 变形池供体：Blender→Slushee、Potion Potion→2 Small Potions。
    // 供体由测试显式注入 SpawnPool；缺失时引擎告警并保留原卡，不猜本地近似。
    public static class G08PoolTransformTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        private const string BlenderId = "0b13735f-7382-460d-a054-710e1a2a3799";
        private const string PotionPotionId = "bef0a78b-b3b4-4e7b-85d3-7c42aca6379f";
        private const string SlusheeId = "8307efa3-97b6-4645-a3cb-de4930880ef4";
        private const string RegenPotionId = "16b0d645-3a47-45a1-be2a-1e3ee311f33e";
        private const string DrowsdropId = "a655fb50-b9e1-41ec-b658-07459014f96c";

        private const string SmallTemplate = @"{""$type"":""TCardItem"",""InternalName"":""Small Gem"",""Size"":""Small"",
            ""Tags"":[],""HiddenTags"":[],
            ""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":2000,""DamageAmount"":10},""AbilityIds"":[],""AuraIds"":[]},
                       ""Silver"":{""Attributes"":{""DamageAmount"":20},""AbilityIds"":[],""AuraIds"":[]}},
            ""Abilities"":{},""Auras"":{}}";

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g08_pool_transforms.json"));
            DonorRulesAreCalibrated();
            BlenderTransformsAdjacentSmallIntoSlushee();
            PotionPotionCreatesTwoSmallPotions();
        }

        private static LiveCardEntry Real(string name, int slot, string tier)
        {
            JsonValue row = _fixtures["cards:" + name], template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry Synthetic(string id, string template, int slot, string tier)
        {
            JsonValue parsed = Json.Parse(template);
            return new LiveCardEntry
            {
                TemplateId = id, Template = parsed, Tier = tier, Socket = slot, Size = "Small",
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(parsed, tier)
            };
        }

        private static BattleConfig Board(params LiveCardEntry[] cards)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, 10).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static CardDef Donor(string name, string tier)
        {
            LiveCardEntry entry = Real(name, 0, tier);
            MappedBoard board = CardMapper.Build(new List<LiveCardEntry> { entry }, EmptyCatalog, 10000, 10000, 10);
            CardDef card = board.Config.Cards[board.Config.Player.Items[0].CardId];
            Assert.True(card.NativeTemplate != null && card.NativeTemplate.Kind == JsonKind.Object);
            return card;
        }

        private static InternalItem Find(BattleContext ctx, string baseItemId)
        {
            foreach (InternalItem item in ctx.Items)
                if (item.Card != null && item.Card.BaseItemId == baseItemId) return item;
            return null;
        }

        private static void DonorRulesAreCalibrated()
        {
            foreach (string name in new[] { "Recycling Bin", "[DEBUG] Recycling Bin", "Hacksaw", "Potion Potion", "[DEBUG] Start of Fight Transform" })
            {
                CardDef card = Donor(name, name == "Hacksaw" ? "Gold" : "Silver");
                Assert.True(NativeTransform.DonorRule(card) != null);
            }
        }

        private static void BlenderTransformsAdjacentSmallIntoSlushee()
        {
            BattleConfig config = Board(
                Synthetic("small", SmallTemplate, 0, "Bronze"),
                Real("Blender", 1, "Silver"));
            config.SpawnPool.Add(Donor("Slushee", "Silver"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem small = Find(ctx, "small");
            InternalItem blender = Find(ctx, BlenderId);
            Assert.True(small != null && blender != null);
            BattleEngine.ForceUse(ctx, blender, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            Assert.True(small.Destroyed && small.Disabled);
            small = ctx.Items.Find(delegate(InternalItem item) { return !item.Destroyed && item.Slot == small.Slot; });
            Assert.Equal(SlusheeId, small.Card.BaseItemId);
            Assert.Equal("Silver", small.Card.Tier);
            Assert.Near(6.0, small.CooldownMax);
        }

        private static void PotionPotionCreatesTwoSmallPotions()
        {
            BattleConfig config = Board(Real("Potion Potion", 0, "Silver"));
            config.SpawnPool.Add(Donor("Regeneration Potion", "Silver"));
            config.SpawnPool.Add(Donor("Drowsdrop", "Silver"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem potion = Find(ctx, PotionPotionId);
            Assert.True(potion != null);
            BattleEngine.ForceUse(ctx, potion, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            int count = 0;
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Card == null) continue;
                if (item.Card.BaseItemId != RegenPotionId && item.Card.BaseItemId != DrowsdropId) continue;
                count++;
                Assert.Equal("Silver", item.Card.Tier);
                Assert.Equal("Small", item.Card.Size);
            }
            Assert.Equal(2, count);
        }
    }
}
