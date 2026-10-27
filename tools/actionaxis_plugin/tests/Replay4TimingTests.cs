using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4TimingTests
    {
        public static void Run()
        {
            OwnImmediateAbilityRunsAtReadyFrame();
            NativeUseObserverSerializesAcrossDifferentWeapons();
            AmmoAuraRefreshesOnReadyFrameWithoutLosingScheduledCasts();
            ForcedNativeUseSpendsAmmoBeforeNextFrameEffects();
            ReadyImmediateRunsBeforeSameFrameMedium();
            MulticastImmediateCountersLatchFirstCastConditions();
        }

        private static void MulticastImmediateCountersLatchFirstCastConditions()
        {
            // e88da4e7 / d78389e0: Gatling's two Immediate increments both
            // occur on frame 119; its first-cast cooldown bonus runs on 120.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 6.3 };
            CardDef card = new CardDef { Id = "gatling-phases", Cooldown = 6 };
            card.LiveAttributes["Custom_2"] = 0;
            card.Effects.Add(new Effect { Kind = "multicast", Count = 2 });
            card.Effects.Add(new Effect { Kind = "modifyAttribute", Priority = "Immediate", Attribute = "Custom_2", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            card.Effects.Add(new Effect { Kind = "modifyAttribute", Priority = "High", Attribute = "PercentCooldownReduction", Operation = "Add",
                Amount = new NumExpr { Value = 50 }, Target = new Target { Kind = "selfItem" },
                Prerequisites = Json.Parse(@"[{""$type"":""TPrerequisiteCardCount"",""Amount"":1,""Comparison"":""Equal"",
                ""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":{""$type"":""TCardConditionalAttribute"",""Attribute"":""Custom_2"",
                ""ComparisonOperator"":""Equal"",""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":0}}}}]") });
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            ItemSnapshot ready = result.ItemSnapshots.Find(delegate(ItemSnapshot s) { return Math.Abs(s.Time - 5.95) < 1e-9; });
            Assert.Near(2, ready.Attributes["Custom_2"]);
            List<BattleEvent> reductions = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "modifyAttribute" && e.Note == "PercentCooldownReduction"; });
            Assert.Equal(1, reductions.Count);
            if (reductions.Count == 1) Assert.Near(6.0, reductions[0].Time);
        }

        private static void ReadyImmediateRunsBeforeSameFrameMedium()
        {
            // d4d6ac10: Angle Grinder Drone starts its own flight before an
            // already queued Ornithopter action can select a non-flying target.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.10 };
            CardDef medium = new CardDef { Id = "medium-fly", Cooldown = 1 };
            CardDef immediate = new CardDef { Id = "immediate-fly", Cooldown = 1.05 };
            medium.Effects.Add(new Effect { Kind = "fly", Priority = "Medium",
                Target = new Target { Kind = "allSelfItems", ItemId = immediate.Id, NotFlyingOnly = true } });
            immediate.Effects.Add(new Effect { Kind = "fly", Priority = "Immediate",
                Target = new Target { Kind = "selfItem", NotFlyingOnly = true } });
            config.Cards[medium.Id] = medium; config.Cards[immediate.Id] = immediate;
            config.Player.Items.Add(new LoadoutItem { CardId = medium.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = immediate.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent flight = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "fly" && ev.TargetItemId == immediate.Id; });
            Assert.True(flight != null);
            if (flight != null) { Assert.Near(1.0, flight.Time); Assert.Equal(immediate.Id, flight.SourceItemId); }
        }

        // b60fa630: Repeater is forced on frame 61 (including Ammo loss),
        // and its Medium damage executes on frame 62.
        private static void ForcedNativeUseSpendsAmmoBeforeNextFrameEffects()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 1.2, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef source = new CardDef { Id = "forced-source", Cooldown = 1 };
            source.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "enemyHero" } });
            CardDef repeater = new CardDef { Id = "repeater", Cooldown = 10, AmmoMax = 3 };
            repeater.LiveAttributes["Custom_0"] = 0;
            repeater.Effects.Add(new Effect { Kind = "modifyAttribute", Priority = "Immediate", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            repeater.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "enemyHero" } });
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceItemId = source.Id } };
            trigger.Effects.Add(new Effect { Kind = "forceUse", Target = new Target { Kind = "selfItem" } });
            repeater.Triggers.Add(trigger);
            config.Cards[source.Id] = source;
            config.Cards[repeater.Id] = repeater;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = repeater.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent forced = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "forceUse"; });
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == repeater.Id; });
            BattleEvent immediate = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute" && ev.SourceItemId == repeater.Id; });
            Assert.True(forced != null && damage != null && immediate != null);
            if (forced != null) Assert.Near(1.05, forced.Time);
            if (immediate != null) Assert.Near(1.05, immediate.Time);
            if (damage != null) Assert.Near(1.1, damage.Time);
            ItemSnapshot snapshot = result.ItemSnapshots.Find(delegate(ItemSnapshot item) { return item.ItemId == repeater.Id && Math.Abs(item.Time - 1.05) < 1e-9; });
            Assert.True(snapshot != null);
            if (snapshot != null) Assert.Equal(2, snapshot.Ammo);
        }

        // b1621 Dive Weights: spending Ammo updates the aura in frame 119,
        // while the five casts retain the pre-spend Multicast snapshot.
        private static void AmmoAuraRefreshesOnReadyFrameWithoutLosingScheduledCasts()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 1.55, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef card = new CardDef { Id = "ammo-multicast", Cooldown = 1, AmmoMax = 2,
                NativeAttributesAreLive = false };
            card.LiveAttributes["Multicast"] = 1;
            card.NativeRuntimeAuras.Add(Json.Parse("{\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"Multicast\",\"Operation\":\"Add\",\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Value\":{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"Ammo\",\"DefaultValue\":0,\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}}}"));
            card.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "enemyHero" } });
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleContext opening = BattleEngine.InitBattle(config);
            Assert.Equal(2, opening.Items[0].Ammo);
            Assert.Equal(2, opening.Items[0].MulticastBonus);
            BattleResult result = BattleEngine.Simulate(config);
            ItemSnapshot ready = result.ItemSnapshots.Find(delegate(ItemSnapshot snapshot) {
                return snapshot.ItemId == card.Id && Math.Abs(snapshot.Time - 0.95) < 1e-9;
            });
            Assert.True(ready != null);
            if (ready != null) { Assert.Equal(1, ready.Ammo); Assert.Near(2, ready.Attributes["Multicast"]); }
            List<BattleEvent> casts = result.Events.FindAll(delegate(BattleEvent ev) { return ev.SourceItemId == card.Id && ev.Kind == "damage"; });
            Assert.Equal(3, casts.Count);
            if (casts.Count == 3) { Assert.Near(1, casts[0].Time); Assert.Near(1.25, casts[1].Time); Assert.Near(1.5, casts[2].Time); }
        }

        // b57a: Paper Airplane toggles at frame 99, while its Medium
        // dummy counter and Fiery burn execute at frame 100.
        private static void OwnImmediateAbilityRunsAtReadyFrame()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 5.35, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef plane = new CardDef { Id = "paper-airplane", Cooldown = 5 };
            plane.Effects.Add(new Effect { Kind = "flyToggle", Priority = "Immediate", NativeAbilityId = "0",
                Target = new Target { Kind = "selfItem" } });
            plane.Effects.Add(new Effect { Kind = "burn", Priority = "Medium", NativeAbilityId = "e1",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "enemyHero" } });
            plane.Effects.Add(new Effect { Kind = "multicast", Count = 2 });
            config.Cards[plane.Id] = plane;
            config.Player.Items.Add(new LoadoutItem { CardId = plane.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> flight = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "fly" || ev.Kind == "flyStop"; });
            List<BattleEvent> burn = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "burn"; });
            Assert.Equal(2, flight.Count);
            Assert.Equal(2, burn.Count);
            if (flight.Count == 2) { Assert.Near(4.95, flight[0].Time); Assert.Near(4.95, flight[1].Time); }
            if (burn.Count == 2) { Assert.Near(5, burn[0].Time); Assert.Near(5.25, burn[1].Time); }
        }

        // b1621: Tempering is one ability execution queue. Uses from
        // different weapons cannot run the same observer inside 250 ms.
        private static void NativeUseObserverSerializesAcrossDifferentWeapons()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                MaxTime = 1.7, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            for (int i = 0; i < 3; i++)
            {
                CardDef weapon = new CardDef { Id = "weapon-" + i, Cooldown = 1 + i * 0.1 };
                weapon.Tags.Add("Weapon");
                config.Cards[weapon.Id] = weapon;
                config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = i });
            }
            CardDef tempering = new CardDef { Id = "tempering", Passive = true };
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "Medium",
                SkillRule = new NativeSkillRule { SourceOwner = "self" } };
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 3 }, Target = new Target { Kind = "triggerSourceItem" } });
            tempering.Triggers.Add(trigger);
            TriggerDef immediate = new TriggerDef { Event = "onUse", Priority = "Immediate",
                SkillRule = new NativeSkillRule { SourceOwner = "self" } };
            immediate.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "CritChance", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "triggerSourceItem" } });
            tempering.Triggers.Add(immediate);
            TriggerDef independent = new TriggerDef { Event = "onUse", Priority = "Medium",
                SkillRule = new NativeSkillRule { SourceOwner = "self" } };
            independent.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "ShieldApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "triggerSourceItem" } });
            tempering.Triggers.Add(independent);
            config.PlayerSkills.Add(tempering);
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> growth = result.Events.FindAll(delegate(BattleEvent ev) { return ev.SourceItemId == tempering.Id && ev.Kind == "modifyAttribute" && ev.Note == "DamageAmount"; });
            Assert.Equal(3, growth.Count);
            if (growth.Count == 3)
            {
                Assert.Near(1.05, growth[0].Time); Assert.Near(1.3, growth[1].Time); Assert.Near(1.55, growth[2].Time);
                for (int i = 0; i < 3; i++) Assert.Equal("weapon-" + i, growth[i].TargetItemId);
            }
            List<BattleEvent> counters = result.Events.FindAll(delegate(BattleEvent ev) { return ev.SourceItemId == tempering.Id && ev.Kind == "modifyAttribute" && ev.Note == "CritChance"; });
            List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent ev) { return ev.SourceItemId == tempering.Id && ev.Kind == "modifyAttribute" && ev.Note == "ShieldApplyAmount"; });
            Assert.Equal(3, counters.Count);
            Assert.Equal(3, shields.Count);
            for (int i = 0; i < 3 && i < counters.Count && i < shields.Count; i++)
            {
                Assert.Near(1 + i * 0.1, counters[i].Time);
                Assert.Near(1.05 + i * 0.25, shields[i].Time);
            }
            // The queue belongs to each BattleContext, never the shared config.
            BattleResult again = BattleEngine.Simulate(config);
            List<BattleEvent> repeated = again.Events.FindAll(delegate(BattleEvent ev) { return ev.SourceItemId == tempering.Id && ev.Kind == "modifyAttribute" && ev.Note == "DamageAmount"; });
            Assert.Equal(3, repeated.Count);
            if (repeated.Count == 3) Assert.Near(1.05, repeated[0].Time);
        }
    }
}
