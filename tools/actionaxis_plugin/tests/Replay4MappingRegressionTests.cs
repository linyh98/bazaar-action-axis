using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // Exact 2026-10-26 native templates used by the failing replay batch.
    public static class Replay4MappingRegressionTests
    {
        private static JsonValue Data { get { return Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_replay4_mapping.json")); } }

        public static void Run()
        {
            AttributeConditionRequiresAttribute();
            CarvingForkDoesNotCreateDamageAttribute();
            SocketTemplatesMapThroughPublicEntry();
            LiveDayAurasKeepSnapshotAndOtherRules();
            UnknownDayWithoutSnapshotStillWarns();
            BandPairsGrantTempo();
            CompleteNativeItemDoesNotReportMissingRule();
            RedButtonChecksBothBoards();
            UnboundedFlightToggleReachesBothNeighbors();
            SocketEffectsDoNotBlockVisibleNeighbors();
            LiveSkillAuraFollowsItsChangingCounter();
            NativeCooldownChangesUpdateAttributeAndEffectiveMax();
        }

        private static void NativeCooldownChangesUpdateAttributeAndEffectiveMax()
        {
            BattleConfig rounding = new BattleConfig { NativeMechanics = true };
            CardDef rounded = new CardDef { Id = "native-cooldown-rounding", Cooldown = 6 };
            rounding.Cards[rounded.Id] = rounded;
            rounding.Player.Items.Add(new LoadoutItem { CardId = rounded.Id, Slot = 0 });
            BattleContext roundedContext = BattleEngine.InitBattle(rounding);
            Effects.ResolveEffect(roundedContext, new Effect { Kind = "modifyAttribute", Attribute = "PercentCooldownReduction", Operation = "Add",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfItem" } }, roundedContext.Items[0], 4, 0, 1);
            // fc2cb9ab frame 80: 6000 -> 5399, not 5400. The game computes
            // the percentage factor as Single, then truncates milliseconds.
            Assert.Near(5.399, roundedContext.Items[0].CooldownMax);
            CardDef anchored = new CardDef { Id = "captured-cooldown-rounding", Cooldown = 3.64, NativeAttributesAreLive = true };
            anchored.LiveAttributes["CooldownMax"] = 3640;
            anchored.LiveAttributes["PercentCooldownReduction"] = 9;
            anchored.LiveAttributes["FlatCooldownReduction"] = 0;
            anchored.NativeRuntimeAuras.Add(Json.Parse(@"{""Action"":{""$type"":""TAuraActionCardModifyAttribute"",
                ""AttributeType"":""PercentCooldownReduction"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":9},
                ""Target"":{""$type"":""TTargetCardSelf""}}}"));
            BattleConfig capturedConfig = new BattleConfig { NativeMechanics = true };
            capturedConfig.Cards[anchored.Id] = anchored;
            capturedConfig.Player.Items.Add(new LoadoutItem { CardId = anchored.Id, Slot = 0 });
            BattleContext anchoredContext = BattleEngine.InitBattle(capturedConfig);
            Assert.Near(3.64, anchoredContext.Items[0].CooldownMax);
            NativeDynamicAuras.Refresh(anchoredContext);
            Assert.Near(3.64, anchoredContext.Items[0].CooldownMax);
            // ec24c04d / f76e272d: reductions are persistent native attributes,
            // not a one-time subtraction from the current cooldown bar.
            foreach (bool aura in new[] { false, true })
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                CardDef card = new CardDef { Id = "live-cooldown-reduction", Cooldown = 3.4,
                    NativeAttributesAreLive = true, NativeTemplate = Json.Parse("{}") };
                card.LiveAttributes["CooldownMax"] = 3400;
                card.LiveAttributes["PercentCooldownReduction"] = 15;
                card.LiveAttributes["FlatCooldownReduction"] = 0;
                if (aura) card.NativeRuntimeAuras.Add(Json.Parse(@"{""Action"":{""$type"":""TAuraActionCardModifyAttribute"",
                    ""AttributeType"":""PercentCooldownReduction"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":15},
                    ""Target"":{""$type"":""TTargetCardSelf""}}}"));
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem item = ctx.Items[0];
                Assert.Near(3.4, item.CooldownMax);
                Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "PercentCooldownReduction", Operation = "Add",
                    Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfItem" } }, item, 1, 0, 1);
                Assert.Near(25, ItemAttributes.Get(item, "PercentCooldownReduction"));
                Assert.Near(3.0, item.CooldownMax);
                Assert.Near(3.0, item.Cooldown);
                Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "FlatCooldownReduction", Operation = "Add",
                    Amount = new NumExpr { Value = 200 }, Seconds = 0.5, Target = new Target { Kind = "selfItem" } }, item, 1.1, 0, 2);
                Assert.Near(200, ItemAttributes.Get(item, "FlatCooldownReduction"));
                Assert.Near(2.85, item.CooldownMax);
                ItemAttributes.Expire(ctx, 1.6);
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(0, ItemAttributes.Get(item, "FlatCooldownReduction"));
                Assert.Near(3.0, item.CooldownMax);
            }
        }

        private static void LiveSkillAuraFollowsItsChangingCounter()
        {
            // ec24c04d: Inexorable's live +20 Crit is already baked in, but
            // its counter gains 2 at fight start and the aura must become +22.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 0.05 };
            CardDef weapon = new CardDef { Id = "live-crit-weapon", Passive = true, NativeAttributesAreLive = true };
            weapon.LiveAttributes["CritChance"] = 20;
            config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 0 });
            JsonValue template = Json.Parse(@"{""$type"":""TCardSkill"",""StartingTier"":""Silver"",
                ""Tiers"":{""Silver"":{""Attributes"":{""Custom_0"":2,""Custom_1"":20},""AbilityIds"":[""0""],""AuraIds"":[""1""],""TooltipIds"":[]}},
                ""Abilities"":{""0"":{""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere"",""Priority"":""Immediate"",
                ""Trigger"":{""$type"":""TTriggerOnFightStarted""},""Action"":{""$type"":""TActionCardModifyAttribute"",""AttributeType"":""Custom_1"",""Operation"":""Add"",
                ""Target"":{""$type"":""TTargetCardSelf""},""Value"":{""$type"":""TFixedValue"",""Value"":2}}}},
                ""Auras"":{""1"":{""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere"",""Action"":{""$type"":""TAuraActionCardModifyAttribute"",
                ""AttributeType"":""CritChance"",""Operation"":""Add"",""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand""},
                ""Value"":{""$type"":""TReferenceValueCardAttribute"",""AttributeType"":""Custom_1"",""Target"":{""$type"":""TTargetCardSelf""}}}}}}");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry {
                TemplateId = "live-counter-skill", InstanceId = "counter", Tier = "Silver",
                AttributesAreLive = true, TemplateBoard = true, Template = template } });
            Assert.Equal(0, mapped.Warnings.Count);
            BattleResult result = BattleEngine.Simulate(config);
            ItemSnapshot snapshot = result.ItemSnapshots.Find(delegate(ItemSnapshot s) { return s.ItemId == weapon.Id; });
            Assert.Near(22, snapshot.Attributes["CritChance"]);
            System.Reflection.MethodInfo export = typeof(NativeReplayLedgerExport).GetMethod("CardUpdates",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            List<object> rows = (List<object>)export.Invoke(null, new object[] { result });
            Assert.True(rows.Exists(delegate(object row) {
                Dictionary<string, object> values = (Dictionary<string, object>)row;
                return Convert.ToString(values["instanceId"]) == weapon.Id && Convert.ToString(values["attribute"]) == "CritChance" &&
                    Convert.ToInt32(values["previous"]) == 20 && Convert.ToInt32(values["current"]) == 22;
            }));
        }

        private static void SocketEffectsDoNotBlockVisibleNeighbors()
        {
            // ddc6683c: Cooler socket effects lie between Kitchen Scale and
            // visible items in slot order. They do not occupy a card slot.
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            for (int i = 0; i < 5; i++)
            {
                CardDef card = new CardDef { Id = "socket-neighbor-" + i, Passive = true };
                if (i % 2 == 1) card.NativeTemplate = Json.Parse("{\"$type\":\"TCardSocketEffect\"}");
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = i });
            }
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.True(NativeSkillSupport.PositionalMatch(ctx, ctx.Items[2], ctx.Items[0], "Neighbor"));
            Assert.True(NativeSkillSupport.PositionalMatch(ctx, ctx.Items[2], ctx.Items[4], "Neighbor"));
            Assert.False(NativeSkillSupport.PositionalMatch(ctx, ctx.Items[2], ctx.Items[1], "Neighbor"));
        }

        private static void UnboundedFlightToggleReachesBothNeighbors()
        {
            // fe6522bf: Freefall Simulator has no FlyingTargets attribute. Its
            // positional target means both neighbors, rather than one random one.
            Effect effect = SkillMapper.MapAction(Json.Parse(@"{""$type"":""TActionCardFlyingToggle"",
                ""Target"":{""$type"":""TTargetCardPositional"",""Origin"":""Self"",""TargetMode"":""Neighbor"",""IncludeOrigin"":false}}"),
                new Dictionary<string, double>());
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            for (int i = 0; i < 3; i++)
            {
                CardDef card = new CardDef { Id = "flight-neighbor-" + i, Size = "Small", Passive = true };
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = i });
            }
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effects.ResolveEffect(ctx, effect, ctx.Items[1], 2, 0, 1);
            Assert.True(ctx.Items[0].Flying && ctx.Items[2].Flying);
            Assert.False(ctx.Items[1].Flying);
        }

        public static void AttributeConditionRequiresAttribute()
        {
            JsonValue condition = Json.Parse("{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"DamageAmount\",\"ComparisonOperator\":\"GreaterThanOrEqual\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":0}}");
            InternalItem missing = new InternalItem { Card = new CardDef() };
            Assert.True(!NativeSkillSupport.CardMatches(missing, condition));
            missing.Card.LiveAttributes["DamageAmount"] = 0;
            Assert.True(NativeSkillSupport.CardMatches(missing, condition));
            missing.Card.LiveAttributes.Remove("DamageAmount");
            missing.AttributeDeltas["DamageAmount"] = 6;
            Assert.True(NativeSkillSupport.CardMatches(missing, condition));
            // Sushi Boat checks Heated == 0. Unset binary state flags are false.
            Assert.True(NativeSkillSupport.CardMatches(missing, Json.Parse("{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"Heated\",\"ComparisonOperator\":\"Equal\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":0}}")));
        }

        public static void SocketTemplatesMapThroughPublicEntry()
        {
            foreach (string name in new[] { "[Stove] Socket Effect", "[Cooler] Socket Effect" })
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                CardDef socket = Item(config, name, "Diamond", "socket", 3, false);
                CardDef occupant = new CardDef { Id = "occupant", NativeAttributesAreLive = false, Size = "Medium" };
                config.Cards[occupant.Id] = occupant;
                config.Player.Items.Add(new LoadoutItem { CardId = occupant.Id, Slot = 3 });
                List<string> warnings = NativeCardAbilityMapper.Apply(config);
                Assert.Equal(0, warnings.Count);
                BattleContext ctx = BattleEngine.InitBattle(config);
                string attr = name.Contains("Stove") ? "Heated" : "Chilled";
                Assert.Near(1, ItemAttributes.Get(ctx.Items.Find(delegate(InternalItem i) { return i.CardId == occupant.Id; }), attr));
                Assert.Equal("structured", config.Cards[socket.Id].ParseStatus);
            }
        }

        public static void CarvingForkDoesNotCreateDamageAttribute()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef fork = Item(config, "Carving Fork", "Bronze", "fork", 0, false);
            CardDef zero = new CardDef { Id = "zero-weapon", NativeAttributesAreLive = false };
            zero.LiveAttributes["DamageAmount"] = 0;
            CardDef utility = new CardDef { Id = "utility", NativeAttributesAreLive = false };
            config.Cards[zero.Id] = zero;
            config.Cards[utility.Id] = utility;
            config.Player.Items.Add(new LoadoutItem { CardId = zero.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = utility.Id, Slot = 2 });
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == fork.Id; });
            InternalItem weapon = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == zero.Id; });
            InternalItem tool = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == utility.Id; });
            BattleEngine.ForceUse(ctx, source, 0, 0);
            Assert.Near(ItemAttributes.Get(source, "Custom_0"), ItemAttributes.Get(weapon, "DamageAmount"));
            Assert.True(!ItemAttributes.Has(tool, "DamageAmount"));
            Assert.Near(0, ItemAttributes.Get(tool, "DamageAmount"));
        }

        public static void LiveDayAurasKeepSnapshotAndOtherRules()
        {
            foreach (string name in new[] { "Coral Armor", "Vampire Squid", "Birthday Cake" })
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                CardDef card = Item(config, name, "Bronze", "live-day", 0, true);
                card.LiveAttributes["CritChance"] = 35;
                card.LiveAttributes["ShieldApplyAmount"] = name == "Birthday Cake" ? 35 : 150;
                List<string> warnings = NativeCardAbilityMapper.Apply(config);
                Assert.Equal(0, warnings.Count);
                Assert.Equal("structured", config.Cards[card.Id].ParseStatus);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem item = ctx.Items[0];
                Assert.Near(35, ItemAttributes.Get(item, "CritChance"));
                NativeDynamicAuras.Refresh(ctx);
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(35, ItemAttributes.Get(item, "CritChance"));
                ItemAttributes.AddDelta(item, "CritChance", 7);
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(42, ItemAttributes.Get(item, "CritChance"));
                if (name == "Birthday Cake") Assert.Near(42, ItemAttributes.Get(item, "ShieldApplyAmount"));
                if (name == "Coral Armor") Assert.Near(150, ItemAttributes.Get(item, "ShieldApplyAmount"));
            }
        }

        public static void UnknownDayWithoutSnapshotStillWarns()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef card = Item(config, "Coral Armor", "Bronze", "unknown-day", 0, false);
            Assert.True(NativeCardAbilityMapper.Apply(config).Count > 0);
            Assert.Equal("partial", config.Cards[card.Id].ParseStatus);
        }

        public static void BandPairsGrantTempo()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 0.05 };
            for (int i = 0; i < 3; i++)
            {
                CardDef card = new CardDef { Id = "band-item-" + i, NativeAttributesAreLive = false };
                card.Tags.Add(i == 2 ? "Friend" : "Instrument");
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = i });
            }
            JsonValue row = Data["Band's All Here"];
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry {
                TemplateId = row["id"].AsString(), InstanceId = "band", Name = "Band's All Here", Tier = "Silver",
                Template = row["template"], AttributesAreLive = false, TemplateBoard = true
            } }, null);
            Assert.Equal(0, mapped.UnsupportedCount);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(2, ItemAttributes.Get(ctx.Skills[0], "TempoApplyAmount"));
            Assert.Near(1, ItemAttributes.Get(ctx.Skills[0], "Custom_1"));
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> tempo = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "tempo"; });
            Assert.Equal(1, tempo.Count);
            Assert.Near(2, tempo[0].Value.GetValueOrDefault());
        }

        public static void CompleteNativeItemDoesNotReportMissingRule()
        {
            JsonValue row = Data["Kicks"];
            LiveCardEntry live = new LiveCardEntry { TemplateId = row["id"].AsString(), Template = row["template"], Tier = "Bronze", AttributesAreLive = false };
            MappedBoard mapped = CardMapper.Build(new[] { live }, CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 10);
            mapped.Config.NativeMechanics = true;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(mapped.Config).Count);
            // The local text catalog has no Kicks row; that diagnostic remains
            // visible even though the native template fully maps the combat rule.
            Assert.Equal(1, mapped.Unknown.Count);
            CardDef card = mapped.Config.Cards[mapped.Config.Player.Items[0].CardId];
            Assert.Equal(2, card.Effects.Count);
            Assert.Equal(2, card.Triggers.Count);
        }

        public static void RedButtonChecksBothBoards()
        {
            for (int blockers = 0; blockers < 3; blockers++)
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                CardDef button = Item(config, "Red Button", "Silver", "button", 3, false);
                CardDef neighbor = new CardDef { Id = "large", Size = "Large", Cooldown = 8, NativeAttributesAreLive = false };
                neighbor.LiveAttributes["CooldownMax"] = 8000;
                config.Cards[neighbor.Id] = neighbor;
                config.Player.Items.Add(new LoadoutItem { CardId = neighbor.Id, Slot = 0 });
                for (int i = 0; i < blockers; i++)
                {
                    CardDef blocker = new CardDef { Id = "blocker-" + i, BaseItemId = i == 0 ? "b607f75b-e245-4e4a-809e-899fcdd1e6e5" : "63c3c74e-8a01-44cf-a629-2b12aa842264" };
                    config.Cards[blocker.Id] = blocker;
                    // The first blocker is on the opposing board. Both boards count.
                    (i == 0 ? config.Enemy : config.Player).Items.Add(new LoadoutItem { CardId = blocker.Id, Slot = 7 + i });
                }
                Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem source = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == button.Id; });
                InternalItem target = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == neighbor.Id; });
                target.Cooldown = 8;
                BattleEngine.ForceUse(ctx, source, 0, 0);
                Assert.Near(8 - 4.0 / Math.Pow(2, blockers), target.Cooldown);
                Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "charge"; }).Count);
            }
        }

        private static CardDef Item(BattleConfig config, string name, string tier, string id, int slot, bool live)
        {
            JsonValue row = Data[name], template = row["template"];
            CardDef card = new CardDef { Id = id, BaseItemId = row["id"].AsString(), Name = name, Tier = tier,
                NativeTemplate = template, NativeAttributesAreLive = live };
            foreach (KeyValuePair<string, int> attr in MonsterTemplateExtractor.TierAttributes(template, tier)) card.LiveAttributes[attr.Key] = attr.Value;
            config.Cards[id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = id, Slot = slot });
            return card;
        }
    }
}
