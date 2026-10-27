using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay3FirstBattleTests
    {
        public static void Run()
        {
            GatedShieldStillPerformsShieldForObservers();
            AppliedShieldStillFiresObserversOnce();
            MusicNoteSocketEffectSkipsNeighborTargeting();
            ActionCostSpendsTempoOncePerAbility();
            PlayerSkillObservesOpponentHealthLoss();
        }

        // 0276c46b frame 227: Bloodhound ("when the opponent loses health,
        // charge your items") is a player skill whose trigger subject is the
        // opponent. It must fire from the player's own damage.
        private static void PlayerSkillObservesOpponentHealthLoss()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.4,
                PlayerMaxHp = 500, EnemyMaxHp = 500, EnemyStartHp = 100 };
            CardDef weapon = new CardDef { Id = "observer-weapon", Cooldown = 1 };
            weapon.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "enemyHero" } });
            config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 0 });
            CardDef target = new CardDef { Id = "charged-item", Cooldown = 30 };
            config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 1 });
            CardDef bloodhound = new CardDef { Id = "skl_bloodhound", Cooldown = 0, Passive = true };
            bloodhound.LiveAttributes["Custom_0"] = 0;
            TriggerDef observe = new TriggerDef { Event = "onPlayerAttributeChanged", Priority = "Immediate",
                AttributeFilter = "Health", ChangeType = "Loss",
                SkillRule = new NativeSkillRule { PlayerOwner = "enemy",
                    Prerequisites = Json.Parse(@"[{""$type"":""TPrerequisiteCardCount"",""Amount"":1,""Comparison"":""Equal"",
                        ""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":{""$type"":""TCardConditionalAttribute"",""Attribute"":""Custom_0"",
                        ""ComparisonOperator"":""Equal"",""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":0}}}},
                        {""$type"":""TPrerequisitePlayer"",""Subject"":{""$type"":""TTargetPlayerRelative"",""Conditions"":{""$type"":""TPlayerConditionalAttribute"",
                        ""Attribute"":""Health"",""ComparisonOperator"":""LessThan"",""ComparisonValue"":{""$type"":""TReferenceValuePlayerAttribute"",
                        ""AttributeType"":""HealthMax"",""DefaultValue"":0,""Modifier"":{""ModifyMode"":""Multiply"",""ShouldRound"":true,
                        ""Value"":{""$type"":""TFixedValue"",""Value"":0.5}},""Target"":{""$type"":""TTargetPlayerRelative"",""Conditions"":null,
                        ""TargetMode"":""Opponent""}}},""TargetMode"":""Opponent""}}]") } };
            observe.Effects.Add(new Effect { Kind = "charge", Seconds = 1,
                Target = new Target { Kind = "allSelfItems" } });
            bloodhound.Triggers.Add(observe);
            config.PlayerSkills.Add(bloodhound);
            CardDef chargeTarget = new CardDef { Id = "observer-charge-target", Cooldown = 30 };
            config.Cards[chargeTarget.Id] = chargeTarget;
            config.Player.Items.Add(new LoadoutItem { CardId = chargeTarget.Id, Slot = 2 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.True(result.Events.Exists(delegate(BattleEvent e) {
                return e.Kind == "charge" && e.TargetItemId == chargeTarget.Id;
            }));
        }

        // 0276c46b frame 80: Wings' "adjacent items start Flying" picked slot
        // 4/8 in the replay. Slot 5 is a TCardMusicNoteSocketEffect, which
        // occupies a slot but is never a board target nor an adjacency anchor.
        private static void MusicNoteSocketEffectSkipsNeighborTargeting()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.4,
                PlayerMaxHp = 500, EnemyMaxHp = 500 };
            CardDef left = new CardDef { Id = "left-item", Cooldown = 30 };
            CardDef note = new CardDef { Id = "music-note", Cooldown = 30 };
            note.NativeTemplate = Json.Parse(@"{""$type"":""TCardMusicNoteSocketEffect""}");
            CardDef wings = new CardDef { Id = "wings-item", Cooldown = 1 };
            wings.Effects.Add(new Effect { Kind = "fly", Priority = "Medium",
                Target = new Target { Kind = "randomAdjacentSelf", PositionMode = "Neighbor", NotFlyingOnly = true } });
            CardDef right = new CardDef { Id = "right-item", Cooldown = 30 };
            foreach (CardDef card in new[] { left, note, wings, right }) config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = left.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = note.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = wings.Id, Slot = 2 });
            config.Player.Items.Add(new LoadoutItem { CardId = right.Id, Slot = 3 });
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> flights = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "fly"; });
            // Both real neighbors are candidates; the socket effect is not.
            Assert.Equal(1, flights.Count);
            if (flights.Count == 1)
                Assert.True(flights[0].TargetItemId == left.Id || flights[0].TargetItemId == right.Id);
            Assert.False(result.Events.Exists(delegate(BattleEvent e) { return e.Kind == "fly" && e.TargetItemId == note.Id; }));
        }

        // 0276c46b frame 82: Tuba's paid abilities spend Tempo once per
        // ability, not per target, and emit one actionCost record.
        private static void ActionCostSpendsTempoOncePerAbility()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.4,
                PlayerMaxHp = 500, EnemyMaxHp = 500 };
            CardDef grant = new CardDef { Id = "tempo-grant" };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(new Effect { Kind = "tempo", Amount = new NumExpr { Value = 5 },
                Target = new Target { Kind = "selfHero" } });
            grant.Triggers.Add(start);
            CardDef paid = new CardDef { Id = "paid-item", Cooldown = 1 };
            paid.LiveAttributes["Custom_1"] = 0;
            paid.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_1", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" },
                CostAttribute = "Tempo", CostAmount = new NumExpr { Value = 2 } });
            config.Cards[grant.Id] = grant;
            config.Cards[paid.Id] = paid;
            config.Player.Items.Add(new LoadoutItem { CardId = grant.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = paid.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> costs = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "actionCost"; });
            Assert.Equal(1, costs.Count);
            if (costs.Count == 1) { Assert.Near(1.0, costs[0].Time); Assert.Near(2, costs[0].Value.Value); Assert.Equal(paid.Id, costs[0].TargetItemId); }
            Assert.True(result.Events.Exists(delegate(BattleEvent e) {
                return e.Kind == "modifyAttribute" && e.Note == "Custom_1" && e.SourceItemId == paid.Id;
            }));
        }

        // 0276c46b frames 58-60: Light Drone's "every 3rd use, Shield 40"
        // ability carries a prerequisite (Custom_0 == 2). On the first two
        // uses the shield is not applied, yet Sharp Corners still receives
        // TTriggerOnCardPerformedShield and gains +6 Damage on the next frame.
        // The prerequisite gates the amount, not the performed-shield event.
        private static void GatedShieldStillPerformsShieldForObservers()
        {
            BattleConfig config = ShieldConfig(0);
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "shield"; });
            Assert.Equal(0, shields.Count);
            List<BattleEvent> watched = WatcherEvents(result);
            Assert.Equal(1, watched.Count);
            if (watched.Count == 1) Assert.Near(1.05, watched[0].Time);
        }

        // When the prerequisite is already satisfied the shield applies and the
        // observer must still fire exactly once, not twice.
        private static void AppliedShieldStillFiresObserversOnce()
        {
            BattleConfig config = ShieldConfig(2);
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "shield"; });
            Assert.Equal(1, shields.Count);
            if (shields.Count == 1) Assert.Near(1.0, shields[0].Time);
            List<BattleEvent> watched = WatcherEvents(result);
            Assert.Equal(1, watched.Count);
            if (watched.Count == 1) Assert.Near(1.05, watched[0].Time);
        }

        private static List<BattleEvent> WatcherEvents(BattleResult result)
        {
            return result.Events.FindAll(delegate(BattleEvent e) {
                return e.Kind == "modifyAttribute" && e.Note == "Custom_1" && e.SourceItemId == "shield-watcher";
            });
        }

        private static BattleConfig ShieldConfig(int openingCounter)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.6,
                PlayerMaxHp = 500, EnemyMaxHp = 500 };
            CardDef drone = new CardDef { Id = "gated-shield", Cooldown = 1 };
            drone.LiveAttributes["Custom_0"] = openingCounter;
            drone.Effects.Add(new Effect { Kind = "modifyAttribute", Priority = "Immediate", Attribute = "Custom_0",
                Operation = "Add", Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            drone.Effects.Add(new Effect { Kind = "shield", Priority = "Medium",
                Amount = new NumExpr { Value = 40 }, Target = new Target { Kind = "selfHero" },
                Prerequisites = Json.Parse(@"[{""$type"":""TPrerequisiteCardCount"",""Amount"":1,""Comparison"":""Equal"",
                    ""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":{""$type"":""TCardConditionalAttribute"",""Attribute"":""Custom_0"",
                    ""ComparisonOperator"":""Equal"",""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":2}}}}]") });
            CardDef watcher = new CardDef { Id = "shield-watcher", Cooldown = 30 };
            watcher.LiveAttributes["Custom_1"] = 0;
            TriggerDef onShield = new TriggerDef { Event = "onShield", Priority = "Low" };
            onShield.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_1", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            watcher.Triggers.Add(onShield);
            config.Cards[drone.Id] = drone;
            config.Cards[watcher.Id] = watcher;
            config.Player.Items.Add(new LoadoutItem { CardId = drone.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = watcher.Id, Slot = 1 });
            return config;
        }
    }
}
