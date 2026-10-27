using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G08 native reconciliation: the transform copies the donor template at the
    // source card's tier and keeps the transformed card's elapsed cooldown.
    // Replay evidence:
    // 07f044ee Mirror Gold -> The Big One Gold (old 2950/3000 -> new 13950/14000)
    // 40bcb27a Hologram Gold -> Gamma Ray Gold (donor was Silver, poison 4->6)
    // 084bd840 Virus Silver -> Virus Silver on both boards
    public static class G08TransformTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        private const string MirrorId = "3172cda6-0ebd-4663-a6d6-f72a3d7c4b2c";
        private const string PrinterId = "723880db-8a97-4833-a9a1-80c75cde4c17";
        private const string HologramId = "1d354dcf-8736-4bab-9dc1-7d9054d6c4d4";
        private const string VirusId = "020a0ec0-21e6-41af-899f-063573ba9ca5";
        private const string GammaRayId = "0229faa7-2eec-4746-9134-ade4f3aebe45";
        private const string GreenScreenId = "62c10400-c215-4edb-a410-5f253b8b863a";
        private const string BlenderId = "0b13735f-7382-460d-a054-710e1a2a3799";
        private const string GrabinatorId = "bd46ceff-a181-49f2-ac60-a769a062cb6d";
        private const string SlusheeId = "8307efa3-97b6-4645-a3cb-de4930880ef4";

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g08_transforms.json"));
            MirrorCopyUsesSourceTierAndElapsedCooldown();
            MirrorDoesNotCopyDonorEnchant();
            HologramCopyUsesSourceTier();
            QuicksilverCopyUsesSourceTier();
            PrinterCopiesUseSourceTierAndKeepSlots();
            PrinterCopiesRealEventPosterAtSourceTier();
            VirusCopiesUseSourceTierOnBothSides();
            GreenScreenCopiesLeftMediumWithEnchant();
            BlenderTransformsNeighborIntoFixedSlushee();
            GrabinatorCopiesEnemyLeftMedium();
            GrabinatorTransformsOnEnemyDestroyed();
            MirrorDoesNotDropLegendaryDonor();
            HologramDoesNotCopyAnotherHologram();
            QuicksilverDoesNotCopyItselfOrLegendary();
            OffFieldTransformPoolsStayFailClosed();
        }

        private static LiveCardEntry Real(string name, int slot, string tier)
        {
            JsonValue row = _fixtures[name], template = row["template"];
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

        private const string MediumTemplate = @"{""$type"":""TCardItem"",""InternalName"":""Medium Gem"",""Size"":""Medium"",
            ""Tags"":[],""HiddenTags"":[],
            ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":14000,""DamageAmount"":100},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]},
                       ""Diamond"":{""Attributes"":{""DamageAmount"":200},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]}},
            ""Abilities"":{},""Auras"":{}}";

        private const string SmallTemplate = @"{""$type"":""TCardItem"",""InternalName"":""Small Gem"",""Size"":""Small"",
            ""Tags"":[],""HiddenTags"":[],
            ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":2000,""DamageAmount"":10},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]},
                       ""Diamond"":{""Attributes"":{""DamageAmount"":20},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]}},
            ""Abilities"":{},""Auras"":{}}";

        private const string TieredSmallTemplate = @"{""$type"":""TCardItem"",""InternalName"":""Tiered Small"",""Size"":""Small"",
            ""Tags"":[],""HiddenTags"":[],
            ""Tiers"":{""Silver"":{""Attributes"":{""CooldownMax"":3000,""DamageAmount"":10},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]},
                       ""Gold"":{""Attributes"":{""DamageAmount"":20},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]},
                       ""Diamond"":{""Attributes"":{""DamageAmount"":30},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]}},
            ""Abilities"":{},""Auras"":{}}";

        private static LiveCardEntry Synthetic(string id, string template, int slot, string tier, string size)
        {
            JsonValue parsed = Json.Parse(template);
            return new LiveCardEntry
            {
                TemplateId = id,
                Template = parsed,
                Tier = tier,
                Socket = slot,
                Size = size,
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(parsed, tier)
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

        private static InternalItem Find(BattleContext ctx, string baseItemId)
        {
            foreach (InternalItem item in ctx.Items)
                if (item.Card != null && item.Card.BaseItemId == baseItemId) return item;
            return null;
        }

        private static InternalItem Replacement(BattleContext ctx, InternalItem old)
        {
            if (!old.Destroyed) return old;
            Assert.True(old.Disabled && old.History.RemovedAt.HasValue);
            InternalItem current = ctx.Items.Find(delegate(InternalItem item) {
                return !item.Destroyed && item.Side == old.Side && item.Slot == old.Slot;
            });
            Assert.True(current != null && current.Id != old.Id);
            return current;
        }

        private static int Attr(InternalItem item, string name)
        {
            int value;
            item.Card.LiveAttributes.TryGetValue(name, out value);
            return value;
        }

        private static CardDef FixtureCard(string name, string tier, string id = null)
        {
            JsonValue row = _fixtures[name], template = row["template"];
            CardDef card = new CardDef
            {
                Id = string.IsNullOrEmpty(id) ? "fixture:" + name : id,
                BaseItemId = row["id"].AsString(),
                Name = name,
                Size = template["Size"].AsString(),
                Tier = tier,
                NativeTemplate = template,
                NativeAttributesAreLive = false,
                ParseStatus = "structured",
                DataSource = "native-template"
            };
            card.LiveAttributes = MonsterTemplateExtractor.TierAttributes(template, tier);
            card.NativeAuraSeedAttributes = new Dictionary<string, int>(card.LiveAttributes);
            int cooldown;
            if (card.LiveAttributes.TryGetValue("CooldownMax", out cooldown)) card.Cooldown = cooldown / 1000.0;
            card.Passive = card.Cooldown <= 0;
            return card;
        }

        private static void MirrorCopyUsesSourceTierAndElapsedCooldown()
        {
            BattleConfig config = Board(
                Synthetic("medium", MediumTemplate, 0, "Diamond", "Medium"),
                Real("Mirror", 2, "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, "medium");
            InternalItem mirror = Find(ctx, MirrorId);
            Assert.True(donor != null && mirror != null);

            // Native replay: Mirror 2950/3000 -> The Big One 13950/14000.
            mirror.Cooldown = 2.95;
            mirror.CooldownMax = 3.0;
            donor.Cooldown = 10.53;
            donor.CooldownMax = 13.58;

            BattleEngine.ForceUse(ctx, mirror, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            mirror = Replacement(ctx, mirror);

            Assert.Equal("medium", mirror.Card.BaseItemId);
            Assert.Equal("Gold", mirror.Card.Tier);
            Assert.Equal(100, Attr(mirror, "DamageAmount"));
            Assert.Near(14.0, mirror.CooldownMax);
            Assert.Near(13.95, mirror.Cooldown);
            Assert.Equal("Diamond", donor.Card.Tier);
            Assert.Equal(200, Attr(donor, "DamageAmount"));
        }

        private static void MirrorDoesNotCopyDonorEnchant()
        {
            // ba635047: Mirror copies a Turbo drone, but the new drone has no
            // HasteAmount/HasteTargets and no Turbo ability in the native replay.
            string template = MediumTemplate.Replace("\"Abilities\":{},\"Auras\":{}",
                "\"Enchantments\":{\"Turbo\":{\"Attributes\":{\"HasteAmount\":1000,\"HasteTargets\":2},\"Abilities\":{},\"Auras\":{},\"HiddenTags\":[]}},\"Abilities\":{},\"Auras\":{}");
            LiveCardEntry donorEntry = Synthetic("turbo-medium", template, 0, "Diamond", "Medium");
            donorEntry.Enchantment = "Turbo";
            BattleConfig config = Board(donorEntry, Real("Mirror", 2, "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, "turbo-medium");
            InternalItem mirror = Find(ctx, MirrorId);
            Assert.Equal("Turbo", donor.Card.EnchantmentName);
            BattleEngine.ForceUse(ctx, mirror, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            mirror = Replacement(ctx, mirror);
            Assert.True(string.IsNullOrEmpty(mirror.Card.EnchantmentName));
            Assert.True(!ItemAttributes.Has(mirror, "HasteAmount"));
            Assert.Equal("Turbo", donor.Card.EnchantmentName);
        }

        private static void HologramCopyUsesSourceTier()
        {
            BattleConfig config = Board(
                Real("Gamma Ray", 0, "Silver"),
                Real("Hologram Projector", 1, "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, GammaRayId);
            InternalItem hologram = Find(ctx, HologramId);
            Assert.True(donor != null && hologram != null);

            Effect transform = null;
            foreach (TriggerDef trigger in hologram.Card.Triggers)
                if (trigger.Event == "atCombatStart")
                    foreach (Effect effect in trigger.Effects) transform = effect;
            Assert.True(transform != null);
            Effects.ResolveEffect(ctx, transform, hologram, 0, 1, null);
            BattleEngine.FlushPendingItems(ctx);
            hologram = Replacement(ctx, hologram);

            // Replay 40bcb27a: donor Silver poison 4, Hologram Gold copy poison 6.
            Assert.Equal(GammaRayId, hologram.Card.BaseItemId);
            Assert.Equal("Gold", hologram.Card.Tier);
            Assert.Equal(6, Attr(hologram, "PoisonApplyAmount"));
            Assert.Equal(3, Attr(hologram, "Custom_0"));
            Assert.Equal("Silver", donor.Card.Tier);
            Assert.Equal(4, Attr(donor, "PoisonApplyAmount"));
        }

        private static void QuicksilverCopyUsesSourceTier()
        {
            BattleConfig config = Board(
                Synthetic("tiered", TieredSmallTemplate, 0, "Gold", "Small"),
                Real("Quicksilver", 1, "Silver"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, "tiered");
            InternalItem quicksilver = Find(ctx, "4fb9fa4a-6250-4132-a7cb-9a4b874b0bdc");
            Assert.True(donor != null && quicksilver != null);

            BattleEngine.ForceUse(ctx, quicksilver, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            quicksilver = Replacement(ctx, quicksilver);

            Assert.Equal("tiered", quicksilver.Card.BaseItemId);
            Assert.Equal("Silver", quicksilver.Card.Tier);
            Assert.Equal(10, Attr(quicksilver, "DamageAmount"));
            Assert.Equal("Gold", donor.Card.Tier);
            Assert.Equal(20, Attr(donor, "DamageAmount"));
        }

        private static void PrinterCopiesUseSourceTierAndKeepSlots()
        {
            BattleConfig config = Board(
                Synthetic("small", SmallTemplate, 0, "Gold", "Small"),
                Real("3D Printer", 1, "Diamond"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, "small");
            InternalItem printer = Find(ctx, PrinterId);
            Assert.True(donor != null && printer != null);

            BattleEngine.ForceUse(ctx, printer, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            printer = Replacement(ctx, printer);

            Assert.Equal(5, ctx.Items.Count);
            Assert.Equal("small", printer.Card.BaseItemId);
            Assert.Equal("Diamond", printer.Card.Tier);
            Assert.Equal(20, Attr(printer, "DamageAmount"));
            Assert.True(printer.History.At(0.5) == null);
            Assert.Equal("small", printer.History.At(1).Card.BaseItemId);

            HashSet<int> slots = new HashSet<int>();
            HashSet<string> ids = new HashSet<string>();
            int copies = 0;
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Card == null || item.Card.BaseItemId != "small") continue;
                copies++;
                Assert.True(slots.Add(item.Slot));
                Assert.True(ids.Add(item.Id));
                if (item == donor) { Assert.Equal("Gold", item.Card.Tier); continue; }
                Assert.Equal("Diamond", item.Card.Tier);
                Assert.Near(2.0, item.CooldownMax);
            }
            Assert.Equal(4, copies);
        }

        private static void PrinterCopiesRealEventPosterAtSourceTier()
        {
            BattleConfig config = Board(
                Real("Event Poster", 0, "Gold"),
                Real("3D Printer", 1, "Diamond"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, "47c2b54e-188b-4ec8-83d6-db089eed0a40");
            InternalItem printer = Find(ctx, PrinterId);
            Assert.True(donor != null && printer != null);
            Assert.Equal(1, Attr(donor, "ChargeTargets"));

            BattleEngine.ForceUse(ctx, printer, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            printer = Replacement(ctx, printer);

            Assert.Equal("47c2b54e-188b-4ec8-83d6-db089eed0a40", printer.Card.BaseItemId);
            Assert.Equal("Diamond", printer.Card.Tier);
            Assert.Equal(2, Attr(printer, "ChargeTargets"));
            Assert.Equal("Gold", donor.Card.Tier);
            Assert.Equal(1, Attr(donor, "ChargeTargets"));
            int copies = 0;
            foreach (InternalItem item in ctx.Items)
                if (item.Card != null && item.Card.BaseItemId == "47c2b54e-188b-4ec8-83d6-db089eed0a40") copies++;
            Assert.Equal(4, copies);
        }

        private static void VirusCopiesUseSourceTierOnBothSides()
        {
            BattleConfig config = Board(
                Real("Virus", 0, "Silver"),
                Synthetic("small", SmallTemplate, 1, "Gold", "Small"));
            CardDef enemySmall = new CardDef
            {
                Id = "enemySmall", BaseItemId = "enemySmall", Name = "Enemy Small",
                Size = "Small", Tier = "Gold", Cooldown = 2
            };
            enemySmall.LiveAttributes["CooldownMax"] = 2000;
            config.Cards[enemySmall.Id] = enemySmall;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemySmall.Id, Slot = 0 });

            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem virus = Find(ctx, VirusId);
            InternalItem playerTarget = Find(ctx, "small");
            InternalItem enemyTarget = ctx.Items.Find(delegate(InternalItem item) { return item.Side == Side.Enemy; });
            Assert.True(virus != null && playerTarget != null && enemyTarget != null);

            BattleEngine.ForceUse(ctx, virus, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            playerTarget = Replacement(ctx, playerTarget);
            enemyTarget = Replacement(ctx, enemyTarget);

            Assert.Equal(VirusId, playerTarget.Card.BaseItemId);
            Assert.Equal("Silver", playerTarget.Card.Tier);
            Assert.Equal(VirusId, enemyTarget.Card.BaseItemId);
            Assert.Equal("Silver", enemyTarget.Card.Tier);
        }

        private static void GreenScreenCopiesLeftMediumWithEnchant()
        {
            BattleConfig config = Board(
                Real("Mirror", 0, "Gold"),
                Real("Green Screen", 2, "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, MirrorId);
            InternalItem green = Find(ctx, GreenScreenId);
            Assert.True(donor != null && green != null);
            Assert.True(string.IsNullOrEmpty(donor.Card.EnchantmentName));

            BattleEngine.ForceUse(ctx, green, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            green = Replacement(ctx, green);
            Assert.Equal("", string.Join(" | ", ctx.Warnings.ToArray()));

            Assert.Equal(MirrorId, green.Card.BaseItemId);
            Assert.Equal("Gold", green.Card.Tier);
            Assert.True(!string.IsNullOrEmpty(green.Card.EnchantmentName));
            Assert.True(string.IsNullOrEmpty(donor.Card.EnchantmentName));
        }

        private static void BlenderTransformsNeighborIntoFixedSlushee()
        {
            BattleConfig config = Board(
                Synthetic("small", SmallTemplate, 0, "Gold", "Small"),
                Real("Blender", 1, "Silver"));
            config.NativeTemplates[SlusheeId] = _fixtures["Slushee"]["template"];
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem target = Find(ctx, "small");
            InternalItem blender = Find(ctx, BlenderId);
            Assert.True(target != null && blender != null);

            BattleEngine.ForceUse(ctx, blender, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            target = Replacement(ctx, target);
            blender = Replacement(ctx, blender);

            Assert.Equal(SlusheeId, target.Card.BaseItemId);
            Assert.Equal("Silver", target.Card.Tier);
            Assert.Near(6.0, target.CooldownMax);
        }

        private static void GrabinatorCopiesEnemyLeftMedium()
        {
            BattleConfig config = Board(Real("Grabinator", 0, "Gold"));
            CardDef enemy = FixtureCard("Mirror", "Gold", "enemy:mirror");
            config.Cards[enemy.Id] = enemy;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id, Slot = 4 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem grabinator = Find(ctx, GrabinatorId);
            Assert.True(grabinator != null);

            Effect transform = null;
            foreach (TriggerDef trigger in grabinator.Card.Triggers)
                if (trigger.Event == "onBeforeDestroy")
                    foreach (Effect effect in trigger.Effects)
                        if (effect.Kind == "transform") transform = effect;
            Assert.True(transform != null);
            Effects.ResolveEffect(ctx, transform, grabinator, 1, 1, null);
            BattleEngine.FlushPendingItems(ctx);
            grabinator = Replacement(ctx, grabinator);

            Assert.Equal(MirrorId, grabinator.Card.BaseItemId);
            Assert.Equal("Gold", grabinator.Card.Tier);
        }

        private static void GrabinatorTransformsOnEnemyDestroyed()
        {
            BattleConfig config = Board(Real("Grabinator", 0, "Gold"));
            CardDef enemy = FixtureCard("Mirror", "Gold", "enemy:mirror");
            config.Cards[enemy.Id] = enemy;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id, Slot = 4 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem grabinator = Find(ctx, GrabinatorId);
            InternalItem enemyItem = ctx.Items.Find(delegate(InternalItem item) { return item.Side == Side.Enemy; });
            Assert.True(grabinator != null && enemyItem != null);
            grabinator.Card.LiveAttributes["Custom_0"] = 1;

            ctx.TriggerQueue.Enqueue(new TriggerPayload
            {
                Event = "onBeforeDestroy", SourceSide = Side.Enemy, SourceItemId = enemyItem.Id,
                TargetSide = Side.Enemy, TargetItemId = enemyItem.Id,
                ActorSide = Side.Player, ActorItemId = grabinator.Id, ChainId = 1, Depth = 0
            });
            Triggers.DrainEvent(ctx, 1, "onBeforeDestroy");
            BattleEngine.FlushPendingItems(ctx);
            grabinator = Replacement(ctx, grabinator);

            Assert.Equal(MirrorId, grabinator.Card.BaseItemId);
        }

        private static void MirrorDoesNotDropLegendaryDonor()
        {
            BattleConfig config = Board(
                Synthetic("legendary", MediumTemplate, 0, "Legendary", "Medium"),
                Real("Mirror", 2, "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = Find(ctx, "legendary");
            InternalItem mirror = Find(ctx, MirrorId);
            Assert.True(donor != null && mirror != null);

            BattleEngine.ForceUse(ctx, mirror, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            mirror = Replacement(ctx, mirror);

            Assert.Equal("legendary", mirror.Card.BaseItemId);
            Assert.Equal("Gold", mirror.Card.Tier);
        }

        private static void HologramDoesNotCopyAnotherHologram()
        {
            BattleConfig config = Board(
                Real("Hologram Projector", 1, "Gold"),
                Real("Hologram Projector", 2, "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem hologram = Find(ctx, HologramId);
            Assert.True(hologram != null);

            Effect transform = null;
            foreach (TriggerDef trigger in hologram.Card.Triggers)
                if (trigger.Event == "atCombatStart")
                    foreach (Effect effect in trigger.Effects) transform = effect;
            Assert.True(transform != null);
            Effects.ResolveEffect(ctx, transform, hologram, 0, 1, null);
            BattleEngine.FlushPendingItems(ctx);
            hologram = Replacement(ctx, hologram);

            Assert.Equal(HologramId, hologram.Card.BaseItemId);
        }

        private static void QuicksilverDoesNotCopyItselfOrLegendary()
        {
            BattleConfig config = Board(
                Synthetic("legendary", TieredSmallTemplate, 0, "Legendary", "Small"),
                Real("Quicksilver", 1, "Silver"),
                Real("Quicksilver", 2, "Silver"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem quicksilver = Find(ctx, "4fb9fa4a-6250-4132-a7cb-9a4b874b0bdc");
            Assert.True(quicksilver != null);

            BattleEngine.ForceUse(ctx, quicksilver, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            quicksilver = Replacement(ctx, quicksilver);

            Assert.Equal("4fb9fa4a-6250-4132-a7cb-9a4b874b0bdc", quicksilver.Card.BaseItemId);
        }

        private static void OffFieldTransformPoolsStayFailClosed()
        {
            // 场外随机卡池没有可重建的候选集/权重；当前 live 模板仍应在映射
            // 入口告警并整卡回退，不能猜本地近似或固定供体。
            foreach (string name in new[] { "Hacksaw", "Potion Potion", "Recycling Bin" })
            {
                BattleConfig config = CardMapper.Build(
                    new List<LiveCardEntry> { Real(name, 0, "Gold") }, EmptyCatalog, 10000, 10000, 10).Config;
                config.NativeMechanics = true;
                List<string> warnings = NativeCardAbilityMapper.Apply(config);
                Assert.True(warnings.Count > 0);
            }
        }
    }
}
