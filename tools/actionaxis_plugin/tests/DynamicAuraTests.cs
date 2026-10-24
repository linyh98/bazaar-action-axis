using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class DynamicAuraTests
    {
        public static void Run()
        {
            SkylinerActuallyCastsTwice();
            CountsBothFlightTransitionsAndSides();
            WindbreakerRetainsCombatGains();
            MultipliersAreReversibleAndDoNotCompound();
            LiveAttributesAreNotCountedTwice();
            DependentAurasFollowChangedAttributes();
            CalibratedLiveAurasFollowAddedTags();
            DirectCooldownAuraTracksChanges();
            UncertainAurasAreExplicitlyUnsupported();
            MonsterSkillAuraRemainsAlongsideDynamicCardAura();
            FlatSkillGainPrecedesCardMultiplier();
            NativeRecordedFlatAndMultiplyOrder();
            LiveFractionalMultiplierUsesIntegerBase();
            NativeRecordedFractionalMultiplierTruncates();
        }

        private static JsonValue Template(string name)
        {
            foreach (JsonValue card in Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_dynamic_auras.json"))["cards"].AsArray())
                if (NativeSkillSupport.Text(card, "InternalName") == name) return card;
            throw new Exception("Missing fixture " + name);
        }

        private static BattleConfig Board(string name, string tier = "Gold", bool live = false, Dictionary<string, int> overrides = null)
        {
            JsonValue template = Template(name);
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, tier);
            if (overrides != null) foreach (KeyValuePair<string, int> pair in overrides) attrs[pair.Key] = pair.Value;
            LiveCardEntry entry = new LiveCardEntry { TemplateId = NativeSkillSupport.Text(template, "Id"), Template = template, Tier = tier,
                Size = NativeSkillSupport.Text(template, "Size"), Socket = 0, AttributesAreLive = live, Attributes = attrs };
            BattleConfig config = CardMapper.Build(new[] { entry }, CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 10.4).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal(0, warnings.Count);
            return config;
        }

        private static void State(BattleContext ctx, InternalItem item, bool flying, double time = 1)
        {
            Effects.ResolveEffect(ctx, new Effect { Kind = flying ? "fly" : "flyStop", Count = 1, Target = new Target { Kind = "selfItem" } }, item, time, 0, null);
        }

        private static void Change(BattleContext ctx, InternalItem item, string attribute, string operation, double value, double seconds = 0)
        {
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = attribute, Operation = operation,
                Amount = new NumExpr { Type = "const", Value = value }, Seconds = seconds, Target = new Target { Kind = "selfItem" } }, item, 1, 0, null);
        }

        private static void SkylinerActuallyCastsTwice()
        {
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                BattleConfig config = Board("Skyliner");
                CardDef card = config.Cards[config.Player.Items[0].CardId];
                TriggerDef fly = new TriggerDef { Event = "atCombatStart" };
                fly.Effects.Add(new Effect { Kind = "fly", Target = new Target { Kind = "selfItem" }, Count = 1 });
                card.Triggers.Add(fly);
                if (side == Side.Enemy) { config.Enemy = config.Player; config.Player = new Loadout(); }
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                Assert.Near(300, side == Side.Player ? result.Totals.Player.Damage : result.Totals.Enemy.Damage);
                int casts = 0;
                foreach (BattleEvent ev in result.Events) if (ev.Kind == "activate" && ev.SourceCardId == card.Id) casts++;
                Assert.Equal(2, casts);
            }
        }

        private static void CountsBothFlightTransitionsAndSides()
        {
            BattleConfig config = Board("Skyliner");
            foreach (string id in new[] { "friend", "opponent" })
            {
                config.Cards[id] = new CardDef { Id = id, Cooldown = 50 };
                (id == "friend" ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = id, Slot = 5 });
            }
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem sky = ctx.Items[0], friend = ctx.Items[1], enemy = ctx.Items[2];
            Assert.Equal(0, sky.MulticastBonus);
            State(ctx, sky, true); Assert.Equal(1, sky.MulticastBonus);
            for (int i = 0; i < 6; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Equal(1, sky.MulticastBonus);
            State(ctx, friend, true); Assert.Equal(2, sky.MulticastBonus);
            State(ctx, enemy, true); Assert.Equal(2, sky.MulticastBonus);
            State(ctx, sky, false); Assert.Equal(1, sky.MulticastBonus);
            State(ctx, friend, false); Assert.Equal(0, sky.MulticastBonus);
            Change(ctx, sky, "Multicast", "Add", 1);
            State(ctx, sky, true); Assert.Equal(2, sky.MulticastBonus);
            State(ctx, sky, false); Assert.Equal(1, sky.MulticastBonus);
        }

        private static void WindbreakerRetainsCombatGains()
        {
            BattleConfig config = Board("Windbreaker", "Silver");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            Assert.Near(25, ItemAttributes.Get(item, "ShieldApplyAmount"));
            State(ctx, item, true);
            Assert.Near(50, ItemAttributes.Get(item, "ShieldApplyAmount"));
            Triggers.Drain(ctx, 1); // The native onFly passive grants permanent +25.
            Assert.Near(100, ItemAttributes.Get(item, "ShieldApplyAmount"));
            State(ctx, item, false);
            Assert.Near(50, ItemAttributes.Get(item, "ShieldApplyAmount"));
            Change(ctx, item, "ShieldApplyAmount", "Add", 5);
            State(ctx, item, true);
            Assert.Near(110, ItemAttributes.Get(item, "ShieldApplyAmount"));
            Change(ctx, item, "ShieldApplyAmount", "Add", 7, 2);
            Assert.Near(124, ItemAttributes.Get(item, "ShieldApplyAmount"));
            ItemAttributes.Expire(ctx, 3); NativeDynamicAuras.Refresh(ctx);
            Assert.Near(110, ItemAttributes.Get(item, "ShieldApplyAmount"));
            State(ctx, item, false);
            Assert.Near(55, ItemAttributes.Get(item, "ShieldApplyAmount"));
        }

        private static void MultipliersAreReversibleAndDoNotCompound()
        {
            BattleConfig config = Board("Wrench");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            State(ctx, item, true); Assert.Near(20, ItemAttributes.Get(item, "DamageAmount"));
            Change(ctx, item, "DamageAmount", "Add", 5);
            Assert.Near(30, ItemAttributes.Get(item, "DamageAmount"));
            Change(ctx, item, "DamageAmount", "Multiply", 2);
            Assert.Near(60, ItemAttributes.Get(item, "DamageAmount"));
            State(ctx, item, false); Assert.Near(30, ItemAttributes.Get(item, "DamageAmount"));
            State(ctx, item, true);
            Change(ctx, item, "DamageAmount", "Set", 40);
            Assert.Near(80, ItemAttributes.Get(item, "DamageAmount"));
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(80, ItemAttributes.Get(item, "DamageAmount"));
            State(ctx, item, false); Assert.Near(40, ItemAttributes.Get(item, "DamageAmount"));
            foreach (Effect effect in item.Card.Effects) Effects.ResolveEffect(ctx, effect, item, 4, 0, null);
            Assert.Near(40, ctx.Totals.Player.Damage);
            Assert.Equal(10, item.Card.LiveAttributes["DamageAmount"]); // Shared template remains unchanged.
        }

        private static void LiveAttributesAreNotCountedTwice()
        {
            BattleConfig config = Board("Windbreaker", "Silver", true, new Dictionary<string, int> { { "Flying", 1 }, { "ShieldApplyAmount", 130 } });
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count); // Repeated mapping cannot reapply/lose metadata.
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            Assert.Near(65, ItemAttributes.Get(item, "ShieldApplyAmount"));
            State(ctx, item, true); Assert.Near(130, ItemAttributes.Get(item, "ShieldApplyAmount"));
            State(ctx, item, false); Assert.Near(65, ItemAttributes.Get(item, "ShieldApplyAmount"));
            config = Board("Skyliner", "Gold", true, new Dictionary<string, int> { { "Flying", 1 }, { "Multicast", 2 } });
            ctx = BattleEngine.InitBattle(config); item = ctx.Items[0];
            Assert.Equal(0, item.MulticastBonus);
            State(ctx, item, true); Assert.Equal(1, item.MulticastBonus);
            State(ctx, item, false); Assert.Equal(0, item.MulticastBonus);
        }

        private static void DependentAurasFollowChangedAttributes()
        {
            BattleConfig config = Board("Red Lantern", "Silver");
            CardDef weapon = new CardDef { Id = "weapon", Size = "Small", Cooldown = 5, NativeAttributesAreLive = false };
            // CanCrit 是原生能力标记，不再由“已有暴击率”推断。
            weapon.HiddenTags.Add("CanCrit");
            weapon.LiveAttributes["DamageAmount"] = 10;
            weapon.LiveAttributes["CritChance"] = 5;
            config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = ctx.Items[0], target = ctx.Items[1];
            Assert.Near(25, ItemAttributes.Get(target, "CritChance"));
            State(ctx, source, true); Assert.Near(45, ItemAttributes.Get(target, "CritChance"));
            Change(ctx, source, "Custom_0", "Add", 5);
            Assert.Near(55, ItemAttributes.Get(target, "CritChance"));
            State(ctx, source, false); Assert.Near(30, ItemAttributes.Get(target, "CritChance"));
            Assert.Near(0.30, target.CritBonus);
        }

        private static void DirectCooldownAuraTracksChanges()
        {
            BattleConfig config = Board("Wrench");
            CardDef card = config.Cards[config.Player.Items[0].CardId];
            JsonValue aura = Template("Wrench")["Auras"]["3"];
            aura["Action"].AsObject()["AttributeType"] = Json.Parse("\"CooldownMax\"");
            aura["Action"]["Value"].AsObject()["Value"] = Json.Parse("0.5");
            card.NativeRuntimeAuras = new List<JsonValue> { aura };
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            item.Cooldown = 3;
            State(ctx, item, true); Assert.Near(2, item.CooldownMax); Assert.Near(1, item.Cooldown);
            for (int i = 0; i < 5; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Near(1, item.Cooldown);
            State(ctx, item, false); Assert.Near(4, item.CooldownMax); Assert.Near(3, item.Cooldown);
        }

        private static void CalibratedLiveAurasFollowAddedTags()
        {
            JsonValue gun = JsonValue.Null, spider = JsonValue.Null;
            foreach (JsonValue template in Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_kyver_cards.json")).AsArray())
            {
                if (NativeSkillSupport.Text(template, "InternalName") == "Soldering Gun") gun = template;
                if (NativeSkillSupport.Text(template, "InternalName") == "Trained Spider") spider = template;
            }
            List<LiveCardEntry> entries = new List<LiveCardEntry>();
            foreach (JsonValue template in new[] { gun, spider })
            {
                Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, "Bronze");
                if (template == gun) attrs["Multicast"] = 2; // Live gun already includes one Friend neighbor.
                entries.Add(new LiveCardEntry { TemplateId = NativeSkillSupport.Text(template, "Id"), Template = template, Tier = "Bronze",
                    Socket = entries.Count, Size = "Small", AttributesAreLive = true, Attributes = attrs });
            }
            BattleConfig config = CardMapper.Build(entries, CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 10).Config;
            config.NativeMechanics = true;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            Assert.Equal(0, NativeItemMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem weapon = ctx.Items[0], friend = ctx.Items[1];
            Assert.Equal(1, weapon.MulticastBonus);
            Effects.ResolveEffect(ctx, new Effect { Kind = "addTags", Tags = new List<string> { "Tool" },
                Target = new Target { Kind = "selfItem" } }, friend, 1, 0, null);
            Assert.Equal(2, weapon.MulticastBonus);
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Equal(2, weapon.MulticastBonus);
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Target = new Target { Kind = "selfItem" } }, friend, 2, 0, null);
            Assert.Equal(0, weapon.MulticastBonus);
        }

        private static void UncertainAurasAreExplicitlyUnsupported()
        {
            JsonValue template = Template("Party Float");
            // 冷却缩减光环已支持；此处把操作改成不可逆的 Set，继续验证 fail-closed。
            foreach (KeyValuePair<string, JsonValue> aura in template["Auras"].AsObject())
                aura.Value["Action"].AsObject()["Operation"] = JsonValue.FromString("Set");
            LiveCardEntry entry = new LiveCardEntry { TemplateId = NativeSkillSupport.Text(template, "Id"), Template = template,
                Tier = "Gold", AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold") };
            BattleConfig config = CardMapper.Build(new[] { entry }, CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 30).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.True(warnings.Count > 0);
            Assert.True(config.Cards[config.Player.Items[0].CardId].DataSource != "native-template");
        }

        private static void MonsterSkillAuraRemainsAlongsideDynamicCardAura()
        {
            BattleConfig config = Board("Skyliner");
            config.Enemy = config.Player;
            config.Player = new Loadout();
            JsonValue skill = Json.Parse(@"{
                ""$type"":""TCardSkill"",""Tiers"":{""Gold"":{""Attributes"":{},""AbilityIds"":[],""AuraIds"":[""0""],""TooltipIds"":[]}},""Abilities"":{},""Auras"":{
                  ""0"":{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,""Action"":{""$type"":""TAuraActionCardModifyAttribute"",""AttributeType"":""Multicast"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":1},""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",""Conditions"":null}}}
                }}");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry { TemplateId = "monster-multi", Template = skill, Tier = "Gold" } }, null, Side.Enemy);
            Assert.Equal(1, mapped.CombatCount);
            Assert.Equal(1, config.EnemySkills.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            Assert.Equal(1, item.MulticastBonus);
            State(ctx, item, true); Assert.Equal(2, item.MulticastBonus);
            State(ctx, item, false); Assert.Equal(1, item.MulticastBonus);
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Equal(1, item.MulticastBonus);
        }

        private static void FlatSkillGainPrecedesCardMultiplier()
        {
            foreach (bool live in new[] { false, true })
            {
                BattleConfig config = Board("Wrench", "Gold", live, live ? new Dictionary<string, int> { { "Flying", 1 }, { "DamageAmount", 34 } } : null);
                config.Enemy = config.Player;
                config.Player = new Loadout();
                JsonValue skill = Json.Parse(@"{
                  ""$type"":""TCardSkill"",""Tiers"":{""Gold"":{""Attributes"":{},""AbilityIds"":[],""AuraIds"":[""0""],""TooltipIds"":[]}},""Abilities"":{},""Auras"":{
                    ""0"":{""ActiveIn"":""HandOnly"",""Prerequisites"":null,""Action"":{""$type"":""TAuraActionCardModifyAttribute"",""AttributeType"":""DamageAmount"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":7},""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",""Conditions"":null}}}
                  }}");
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry { TemplateId = "monster-damage", Template = skill, Tier = "Gold" } }, null, Side.Enemy);
                Assert.Equal(1, mapped.CombatCount);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem item = ctx.Items[0];
                Assert.Near(17, ItemAttributes.Get(item, "DamageAmount"));
                State(ctx, item, true);
                Assert.Near(34, ItemAttributes.Get(item, "DamageAmount"));
                for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
                Assert.Near(34, ItemAttributes.Get(item, "DamageAmount"));
                State(ctx, item, false);
                Assert.Near(17, ItemAttributes.Get(item, "DamageAmount"));
                State(ctx, item, true);
                Change(ctx, item, "DamageAmount", "Add", 5);
                Assert.Near(44, ItemAttributes.Get(item, "DamageAmount"));
                State(ctx, item, false);
                Assert.Near(22, ItemAttributes.Get(item, "DamageAmount"));
            }
        }

        private static void NativeRecordedFlatAndMultiplyOrder()
        {
            JsonValue fixture = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_aura_add_multiply.json"));
            foreach (bool live in new[] { false, true })
            foreach (bool reverse in new[] { false, true })
            {
                List<LiveCardEntry> entries = new List<LiveCardEntry>();
                foreach (string key in reverse ? new[] { "landscraper", "piggles" } : new[] { "piggles", "landscraper" })
                {
                    JsonValue template = fixture["templates"][key], opening = fixture["opening"][key];
                    string tier = NativeSkillSupport.Text(opening, "tier");
                    Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, tier);
                    if (key == "landscraper")
                    {
                        attrs["SellPrice"] = (int)opening["capturedSellPrice"].AsDouble();
                        attrs["Custom_2"] = (int)opening["capturedCustom2"].AsDouble();
                        if (live) attrs["ShieldApplyAmount"] += attrs["SellPrice"];
                    }
                    else if (live) attrs["ShieldApplyAmount"] = (int)opening["capturedShield"].AsDouble();
                    entries.Add(new LiveCardEntry { TemplateId = NativeSkillSupport.Text(template, "Id"), Template = template, Tier = tier,
                        Socket = key == "piggles" ? (reverse ? 3 : 0) : (reverse ? 0 : 1), Size = NativeSkillSupport.Text(template, "Size"), AttributesAreLive = live, Attributes = attrs });
                }
                BattleConfig config = CardMapper.Build(entries, CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 10).Config;
                config.NativeMechanics = true;
                Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem piggles = ctx.Items.Find(delegate(InternalItem i) { return i.Card.Name == "Piggles Protectors"; });
                InternalItem lands = ctx.Items.Find(delegate(InternalItem i) { return i.Card.Name == "Landscraper"; });
                Assert.Near(fixture["opening"]["piggles"]["capturedShield"].AsDouble(), ItemAttributes.Get(piggles, "ShieldApplyAmount"));
                foreach (JsonValue frame in fixture["nativeFrames"].AsArray())
                {
                    Assert.Near(frame["shieldBefore"].AsDouble(), ItemAttributes.Get(piggles, "ShieldApplyAmount"));
                    Change(ctx, lands, "SellPrice", "Add", frame["sellPriceAfter"].AsDouble() - frame["sellPriceBefore"].AsDouble());
                    Assert.Near(frame["shieldAfter"].AsDouble(), ItemAttributes.Get(piggles, "ShieldApplyAmount"));
                    for (int i = 0; i < 4; i++) NativeDynamicAuras.Refresh(ctx);
                    Assert.Near(frame["shieldAfter"].AsDouble(), ItemAttributes.Get(piggles, "ShieldApplyAmount"));
                }
            }
        }

        private static void LiveFractionalMultiplierUsesIntegerBase()
        {
            BattleConfig config = Board("Wrench", "Gold", true, new Dictionary<string, int> { { "Flying", 1 }, { "DamageAmount", 385 } });
            CardDef card = config.Cards[config.Player.Items[0].CardId];
            JsonValue aura = Template("Wrench")["Auras"]["3"];
            aura["Action"]["Value"].AsObject()["Value"] = JsonValue.FromNumber(1.5);
            card.NativeRuntimeAuras = new List<JsonValue> { aura };
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            Assert.Near(257, ItemAttributes.Get(item, "DamageAmount"));
            State(ctx, item, true); Assert.Near(385, ItemAttributes.Get(item, "DamageAmount"));
            State(ctx, item, false); Assert.Near(257, ItemAttributes.Get(item, "DamageAmount"));
            Assert.Equal(0, ctx.Warnings.Count);

            // A reduction can map several integer inputs to the same result.
            // Without a matching template reference, signal that uncertainty.
            config = Board("Wrench", "Gold", true, new Dictionary<string, int> { { "Flying", 1 }, { "DamageAmount", 8 } });
            card = config.Cards[config.Player.Items[0].CardId];
            aura = Template("Wrench")["Auras"]["3"];
            aura["Action"]["Value"].AsObject()["Value"] = JsonValue.FromNumber(0.5);
            card.NativeRuntimeAuras = new List<JsonValue> { aura };
            ctx = BattleEngine.InitBattle(config); item = ctx.Items[0];
            Assert.True(ctx.Warnings.Count > 0);
            Assert.Near(Math.Truncate(item.AuraBaseAttributes["DamageAmount"]), item.AuraBaseAttributes["DamageAmount"]);
            State(ctx, item, true); Assert.Near(8, ItemAttributes.Get(item, "DamageAmount"));
        }

        private static void NativeRecordedFractionalMultiplierTruncates()
        {
            JsonValue fixture = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_aura_fractional_multiply.json"));
            // The replay executed historical AuraId 3 (1.5), not the updated
            // current Diamond tier's AuraId 4 (2). Isolate that recorded aura;
            // this test does not claim full Heated action/lifecycle support.
            JsonValue aura = fixture["templates"]["dishwasher"]["Auras"][NativeSkillSupport.Text(fixture["opening"], "observedAuraId")];
            NativeDynamicAuras.Validate(aura);
            foreach (bool live in new[] { false, true })
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                CardDef source = new CardDef { Id = "dishwasher", NativeAttributesAreLive = false, Passive = true };
                source.NativeRuntimeAuras.Add(aura);
                CardDef target = new CardDef { Id = "lollipop", Tier = "Diamond", NativeAttributesAreLive = live,
                    NativeTemplate = fixture["templates"]["giantLollipop"], Tags = new List<string> { "Weapon", "Food" } };
                target.LiveAttributes["DamageAmount"] = live ? 385 : (int)fixture["opening"]["damage"].AsDouble();
                target.LiveAttributes["Heated"] = live ? 1 : 0;
                config.Cards[source.Id] = source;
                config.Cards[target.Id] = target;
                config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
                config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 3 });
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem item = ctx.Items[1];
                Assert.Near(257, item.AuraBaseAttributes["DamageAmount"]);
                if (live) Change(ctx, item, "Heated", "Set", 0);
                Assert.Near(257, ItemAttributes.Get(item, "DamageAmount"));
                foreach (JsonValue frame in fixture["nativeFrames"].AsArray())
                {
                    Assert.Near(frame["damageBefore"].AsDouble(), ItemAttributes.Get(item, "DamageAmount"));
                    Change(ctx, item, "Heated", "Set", frame["heatedAfter"].AsDouble());
                    Assert.Near(frame["damageAfter"].AsDouble(), ItemAttributes.Get(item, "DamageAmount"));
                    for (int i = 0; i < 4; i++) NativeDynamicAuras.Refresh(ctx);
                    Assert.Near(frame["damageAfter"].AsDouble(), ItemAttributes.Get(item, "DamageAmount"));
                }
                Assert.Equal(0, ctx.Warnings.Count);
            }
        }
    }
}
