using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFirstCr2SchedulingTests
    {
        public static void Run()
        {
            NaturalReadyCastPrecedesLaterRegenObserver();
            ChargeReadyCastPrecedesItsParentUseObserver();
            EarlierObserverStillUpdatesLaterCast();
            HighChargeStillLetsParentUseObserverUpdateNextCast();
            PeriodicRegenReadsBeforeSameFrameMediumObserver();
            EnrageCooldownReductionWaitsForNextReadyFrame();
            SocketEffectsStayOutOfRandomHandTargets();
            SameTickRegenCanRecoverPoisonZeroHealth();
            PoisonZeroHealthWithoutRegenStillEndsBattle();
            RateLimitedObserverKeepsItsReservationBehindEarlierCast();
            PeriodicOverkillAndRegenUseSignedHealth();
            QueuedSelfBuffPrecedesOwnersChargedCast();
            ReplayOrderMergesNaturalAndChargedCasts();
        }

        // 034c3648 frame 142: Bread Knife was already queued when another
        // item's use published Refrigerator's next-frame Medium observer.
        // Both are due together, but the weapon must read the earlier regen.
        private static void NaturalReadyCastPrecedesLaterRegenObserver()
        {
            BattleResult result = BattleEngine.Simulate(RegenConfig(1.05));
            BattleEvent hit = Find(result, "damage", "bread-knife");
            BattleEvent regen = Find(result, "regen", "refrigerator");
            Assert.True(hit != null && regen != null);
            if (hit == null || regen == null) return;
            Assert.Near(1.05, hit.Time);
            Assert.Near(1.05, regen.Time);
            Assert.Near(7, hit.Value.Value);
            Assert.True(result.Events.IndexOf(hit) < result.Events.IndexOf(regen));
        }

        // Frame 143: charge crosses Copper Ed's cooldown through zero before
        // the parent's onUse is published. Copper Ed's queued poison action
        // keeps that position ahead of Gamma Ray's Medium attribute update.
        private static void ChargeReadyCastPrecedesItsParentUseObserver()
        {
            BattleResult result = BattleEngine.Simulate(PoisonChargeConfig("Medium"));
            BattleEvent poison = Find(result, "poison", "copper-ed");
            BattleEvent buff = Find(result, "modifyAttribute", "gamma-ray");
            Assert.True(poison != null && buff != null);
            if (poison == null || buff == null) return;
            Assert.Near(1.05, poison.Time);
            Assert.Near(1.05, buff.Time);
            Assert.Near(3, poison.Value.Value);
            Assert.True(result.Events.IndexOf(poison) < result.Events.IndexOf(buff));
        }

        private static BattleConfig PoisonChargeConfig(string priority)
        {
            BattleConfig config = BaseConfig();
            CardDef charger = new CardDef { Id = "charge-source", Cooldown = 1 };
            charger.Effects.Add(new Effect { Kind = "charge", Priority = priority, Seconds = 2,
                Target = new Target { Kind = "selfItem", ItemId = "copper-ed" } });
            CardDef copper = new CardDef { Id = "copper-ed", Cooldown = 2 };
            copper.LiveAttributes["PoisonApplyAmount"] = 3;
            copper.Effects.Add(new Effect { Kind = "poison", Priority = "Medium",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "PoisonApplyAmount" },
                Target = new Target { Kind = "enemyHero" } });
            CardDef gamma = new CardDef { Id = "gamma-ray", Passive = true };
            TriggerDef observed = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceItemId = charger.Id } };
            observed.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "PoisonApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 5 }, Target = new Target { Kind = "selfItem", ItemId = copper.Id } });
            gamma.Triggers.Add(observed);
            Add(config, charger, copper, gamma);
            return config;
        }

        // b60fa630: Omni Core's High charge is an earlier priority phase.
        // Troll's next-frame cast reads the Medium onUse update from that use.
        // The Medium-charge case above must not reorder this High-phase case.
        private static void HighChargeStillLetsParentUseObserverUpdateNextCast()
        {
            BattleResult result = BattleEngine.Simulate(PoisonChargeConfig("High"));
            BattleEvent poison = Find(result, "poison", "copper-ed");
            BattleEvent buff = Find(result, "modifyAttribute", "gamma-ray");
            Assert.True(poison != null && buff != null);
            if (poison == null || buff == null) return;
            Assert.Near(1.05, poison.Time);
            Assert.Near(1.05, buff.Time);
            Assert.Near(8, poison.Value.Value);
            Assert.True(result.Events.IndexOf(buff) < result.Events.IndexOf(poison));
        }

        // 034c3648 frame 220: the whole-second Poison/Regen tick precedes
        // Refrigerator's new Medium regen. This tick must heal the old pool.
        private static void PeriodicRegenReadsBeforeSameFrameMediumObserver()
        {
            BattleConfig config = RegenConfig(10);
            config.PlayerStartHp = 500;
            config.Cards["regen-driver"].Cooldown = 0.95;
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent tick = result.Events.Find(delegate(BattleEvent ev) {
                return ev.Kind == "dotTick" && ev.Note == "regen" && ev.Side == Side.Player && Math.Abs(ev.Time - 1) < 1e-9;
            });
            BattleEvent regen = Find(result, "regen", "refrigerator");
            Assert.True(tick != null && regen != null);
            if (tick == null || regen == null) return;
            Assert.Near(1, regen.Time);
            Assert.Near(7, tick.Value.Value);
            Assert.True(result.Events.IndexOf(tick) < result.Events.IndexOf(regen));
            BattleHealthSnapshot final = result.HealthSnapshots[result.HealthSnapshots.Count - 1];
            Assert.Near(12, final.Player.Regen);
        }

        // Frames 301-303: Base Rage CDR pushes Honeycomb to zero after the
        // ready check. It remains zero now, resets next frame, acts the next.
        private static void EnrageCooldownReductionWaitsForNextReadyFrame()
        {
            BattleConfig config = BaseConfig();
            CardDef source = new CardDef { Id = "rage-source", Cooldown = 1 };
            source.Effects.Add(new Effect { Kind = "rage", Priority = "Medium", Amount = new NumExpr { Value = 100 },
                Target = new Target { Kind = "selfHero" } });
            CardDef honey = new CardDef { Id = "honeycomb", Cooldown = 1.1 };
            honey.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", Amount = new NumExpr { Value = 10 },
                Target = new Target { Kind = "enemyHero" } });
            Add(config, source, honey);
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent enrage = Find(result, "enrage", null);
            BattleEvent hit = Find(result, "damage", honey.Id);
            Assert.True(enrage != null && hit != null);
            if (enrage != null) Assert.Near(1, enrage.Time);
            if (hit != null) Assert.Near(1.1, hit.Time);
            ItemSnapshot zero = Snapshot(result, honey.Id, 1);
            ItemSnapshot reset = Snapshot(result, honey.Id, 1.05);
            Assert.True(zero != null && reset != null);
            if (zero != null) Assert.Near(0, zero.CooldownRemaining);
            if (reset != null)
            {
                Assert.Near(0.99, reset.CooldownRemaining);
                Assert.Near(0.99, reset.CooldownMax);
            }
        }

        // Frame 283: socket effects occupy a slot but are not ordinary hand
        // targets. Explicit self and occupying-card routing remain available.
        private static void SocketEffectsStayOutOfRandomHandTargets()
        {
            foreach (string type in new[] { "TCardSocketEffect", "TCardMusicNoteSocketEffect" })
            {
                BattleConfig config = BaseConfig();
                CardDef driver = new CardDef { Id = "socket-driver", Passive = true };
                CardDef host = new CardDef { Id = "socket-host", Passive = true };
                CardDef socket = new CardDef { Id = "socket-effect", Passive = true,
                    NativeTemplate = Json.Parse("{\"$type\":\"" + type + "\"}") };
                foreach (CardDef card in new[] { driver, host, socket }) card.LiveAttributes["Custom_0"] = 0;
                Add(config, driver, host, socket);
                config.Player.Items[2].Slot = config.Player.Items[1].Slot;
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem actor = ctx.Items.Find(delegate(InternalItem item) { return item.Id == driver.Id; });
                InternalItem occupying = ctx.Items.Find(delegate(InternalItem item) { return item.Id == host.Id; });
                InternalItem effect = ctx.Items.Find(delegate(InternalItem item) { return item.Id == socket.Id; });
                Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add", Count = 10,
                    Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "randomSelfItem" } }, actor, 0, 0, null);
                Assert.Near(1, ItemAttributes.Get(actor, "Custom_0"));
                Assert.Near(1, ItemAttributes.Get(occupying, "Custom_0"));
                Assert.Near(0, ItemAttributes.Get(effect, "Custom_0"));
                Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                    Amount = new NumExpr { Value = 3 }, Target = new Target { Kind = "selfItem" } }, effect, 0, 0, null);
                Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                    Amount = new NumExpr { Value = 4 }, Target = new Target { Kind = "occupyingItem" } }, effect, 0, 0, null);
                Assert.Near(3, ItemAttributes.Get(effect, "Custom_0"));
                Assert.Near(5, ItemAttributes.Get(occupying, "Custom_0"));
            }
        }

        private static ItemSnapshot Snapshot(BattleResult result, string item, double time)
        {
            return result.ItemSnapshots.Find(delegate(ItemSnapshot snapshot) {
                return snapshot.ItemId == item && Math.Abs(snapshot.Time - time) < 1e-9;
            });
        }

        // Frame 340: enemy Health 430 - Poison 430 + Regen 103 survives.
        // The death check belongs after the frame's whole periodic batch.
        private static void SameTickRegenCanRecoverPoisonZeroHealth()
        {
            BattleResult result = BattleEngine.Simulate(LethalPoisonConfig(true));
            BattleHealthSnapshot frame = result.HealthSnapshots.Find(delegate(BattleHealthSnapshot snapshot) {
                return Math.Abs(snapshot.Time - 1) < 1e-9;
            });
            Assert.True(frame != null);
            if (frame != null) Assert.Near(7, frame.Enemy.Hp);
            Assert.False(result.EndedByDeath);
            Assert.Near(1.05, result.EndTime);
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) {
                return ev.Kind == "dotTick" && ev.Note == "poison" && Math.Abs(ev.Time - 1) < 1e-9;
            });
            BattleEvent regen = result.Events.Find(delegate(BattleEvent ev) {
                return ev.Kind == "dotTick" && ev.Note == "regen" && ev.Side == Side.Enemy && Math.Abs(ev.Time - 1) < 1e-9;
            });
            Assert.True(damage != null && regen != null);
            if (damage != null && regen != null)
            {
                Assert.Near(50, damage.Value.Value);
                Assert.Near(0, damage.HpAfter.Value);
                Assert.Near(7, regen.HpAfter.Value);
                Assert.True(result.Events.IndexOf(damage) < result.Events.IndexOf(regen));
            }
        }

        private static void PoisonZeroHealthWithoutRegenStillEndsBattle()
        {
            BattleResult result = BattleEngine.Simulate(LethalPoisonConfig(false));
            Assert.True(result.EndedByDeath);
            Assert.Near(1, result.EndTime);
            Assert.Equal("player", result.Winner);
            Assert.Near(0, result.HealthSnapshots[result.HealthSnapshots.Count - 1].Enemy.Hp);
        }

        private static BattleConfig LethalPoisonConfig(bool regen)
        {
            BattleConfig config = BaseConfig();
            config.MaxTime = 1.05;
            config.EnemyStartRegen = regen ? 7 : 0;
            // Opening regen ticks once at frame zero, so both boards have
            // precisely 50 HP immediately before the first poison tick.
            config.EnemyStartHp = regen ? 43 : 50;
            CardDef poison = new CardDef { Id = "periodic-poison-source", Cooldown = 0.95 };
            poison.Effects.Add(new Effect { Kind = "poison", Priority = "Medium",
                Amount = new NumExpr { Value = 50 }, Target = new Target { Kind = "enemyHero" } });
            Add(config, poison);
            return config;
        }

        // Frames 295/296 reserve the observer's next slot at 301. A new use
        // at 299 is matched at 300 and delayed to that slot; Bread Knife was
        // already readied at 300, so its 301 cast precedes this new reaction.
        private static void RateLimitedObserverKeepsItsReservationBehindEarlierCast()
        {
            BattleConfig config = RegenConfig(0.5);
            config.MaxTime = 0.55;
            config.Cards["regen-driver"].Cooldown = 0.2;
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent hit = Find(result, "damage", "bread-knife");
            List<BattleEvent> regen = result.Events.FindAll(delegate(BattleEvent ev) {
                return ev.Kind == "regen" && ev.SourceItemId == "refrigerator";
            });
            Assert.True(hit != null);
            Assert.Equal(2, regen.Count);
            if (hit == null || regen.Count != 2) return;
            Assert.Near(0.25, regen[0].Time);
            Assert.Near(0.5, regen[1].Time);
            Assert.Near(0.5, hit.Time);
            Assert.Near(12, hit.Value.Value);
            Assert.True(result.Events.IndexOf(regen[0]) < result.Events.IndexOf(hit));
            Assert.True(result.Events.IndexOf(hit) < result.Events.IndexOf(regen[1]));
            Assert.Near(17, result.HealthSnapshots[result.HealthSnapshots.Count - 1].Player.Regen);
        }

        // 51f83a43: 273 - 512 + 283 = 44 survives. Later,
        // 13 - 564 + 283 is lethal. Clamping poison to zero before regen
        // incorrectly revives the second case and inflates the first case.
        private static void PeriodicOverkillAndRegenUseSignedHealth()
        {
            foreach (bool survives in new[] { true, false })
            {
                BattleConfig config = BaseConfig();
                config.MaxTime = 1.05;
                config.EnemyStartHp = survives ? 273 : 13;
                CardDef source = new CardDef { Id = "signed-periodic-source", Cooldown = 0.95 };
                source.Effects.Add(new Effect { Kind = "poison", Priority = "Medium",
                    Amount = new NumExpr { Value = survives ? 512 : 564 }, Target = new Target { Kind = "enemyHero" } });
                source.Effects.Add(new Effect { Kind = "regen", Priority = "Medium",
                    Amount = new NumExpr { Value = 283 }, Target = new Target { Kind = "enemyHero" } });
                Add(config, source);
                BattleResult result = BattleEngine.Simulate(config);
                BattleHealthSnapshot tick = result.HealthSnapshots.Find(delegate(BattleHealthSnapshot snapshot) {
                    return Math.Abs(snapshot.Time - 1) < 1e-9;
                });
                Assert.True(tick != null);
                if (tick != null) Assert.Near(survives ? 44 : 0, tick.Enemy.Hp);
                Assert.Equal(!survives, result.EndedByDeath);
                Assert.Near(survives ? 1.05 : 1, result.EndTime);
            }
        }

        // 034c3648 frame 343: Gamma Ray's already queued onUse growth belongs
        // to that same card and is applied before its own due active action.
        // Copper Ed's cross-card case above continues to read the old amount.
        private static void QueuedSelfBuffPrecedesOwnersChargedCast()
        {
            BattleConfig config = BaseConfig();
            CardDef charger = new CardDef { Id = "self-growth-charger", Cooldown = 1 };
            CardDef gamma = new CardDef { Id = "self-growth-gamma", Cooldown = 2 };
            charger.Effects.Add(new Effect { Kind = "charge", Priority = "Medium", Seconds = 2,
                Target = new Target { Kind = "selfItem", ItemId = gamma.Id } });
            gamma.LiveAttributes["PoisonApplyAmount"] = 3;
            gamma.Effects.Add(new Effect { Kind = "poison", Priority = "Medium",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "PoisonApplyAmount" },
                Target = new Target { Kind = "enemyHero" } });
            TriggerDef growth = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceItemId = charger.Id } };
            growth.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "PoisonApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 5 }, Target = new Target { Kind = "selfItem" } });
            gamma.Triggers.Add(growth);
            Add(config, charger, gamma);
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent buff = Find(result, "modifyAttribute", gamma.Id);
            BattleEvent poison = Find(result, "poison", gamma.Id);
            Assert.True(buff != null && poison != null);
            if (buff == null || poison == null) return;
            Assert.Near(1.05, buff.Time);
            Assert.Near(1.05, poison.Time);
            Assert.Near(8, poison.Value.Value);
            Assert.True(result.Events.IndexOf(buff) < result.Events.IndexOf(poison));
        }

        // One naturally ready card and two newly charged cards are scheduled
        // from separate readiness passes. Consume frame 21's recorded order
        // once, after combining all three due casts, rather than on a subset.
        private static void ReplayOrderMergesNaturalAndChargedCasts()
        {
            BattleConfig config = BaseConfig();
            config.MaxTime = 1.1;
            config.ReplayRandom = new ReplayRandomTape();
            CardDef natural = new CardDef { Id = "order-natural", Cooldown = 1.05 };
            CardDef charger = new CardDef { Id = "order-charger", Cooldown = 1 };
            CardDef first = new CardDef { Id = "order-charged-first", Cooldown = 2 };
            CardDef second = new CardDef { Id = "order-charged-second", Cooldown = 2 };
            foreach (CardDef target in new[] { first, second })
                charger.Effects.Add(new Effect { Kind = "charge", Priority = "Medium", Seconds = 2,
                    Target = new Target { Kind = "selfItem", ItemId = target.Id } });
            foreach (CardDef weapon in new[] { natural, first, second })
                weapon.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                    Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "enemyHero" } });
            Add(config, natural, charger, first, second);
            config.ReplayRandom.Orders.Add(new ReplayRandomOrder { Frame = 21,
                Sources = new List<string> { second.Id, natural.Id, first.Id } });
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> uses = result.Events.FindAll(delegate(BattleEvent ev) {
                return ev.Kind == "activate" && Math.Abs(ev.Time - 1.05) < 1e-9;
            });
            Assert.Equal(3, uses.Count);
            if (uses.Count == 3)
            {
                Assert.Equal(second.Id, uses[0].SourceItemId);
                Assert.Equal(natural.Id, uses[1].SourceItemId);
                Assert.Equal(first.Id, uses[2].SourceItemId);
            }
            Assert.Equal(1, result.ReplayRandom.OrderApplied);
            Assert.Equal(0, result.ReplayRandom.InvalidChoices);
        }

        // An observer published before the weapon becomes ready still runs
        // first. Moving all passive abilities behind all casts is incorrect.
        private static void EarlierObserverStillUpdatesLaterCast()
        {
            BattleResult result = BattleEngine.Simulate(RegenConfig(1.1));
            BattleEvent hit = Find(result, "damage", "bread-knife");
            BattleEvent regen = Find(result, "regen", "refrigerator");
            Assert.True(hit != null && regen != null);
            if (hit == null || regen == null) return;
            Assert.Near(1.1, hit.Time);
            Assert.Near(1.05, regen.Time);
            Assert.Near(12, hit.Value.Value);
            Assert.True(result.Events.IndexOf(regen) < result.Events.IndexOf(hit));
        }

        private static BattleConfig RegenConfig(double cooldown)
        {
            BattleConfig config = BaseConfig();
            config.PlayerStartRegen = 7;
            CardDef driver = new CardDef { Id = "regen-driver", Cooldown = 1 };
            driver.Effects.Add(new Effect { Kind = "heal", Priority = "Medium",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfHero" } });
            CardDef bread = new CardDef { Id = "bread-knife", Cooldown = cooldown };
            bread.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Type = "playerAttribute", Attribute = "HealthRegen", Side = "self" },
                Target = new Target { Kind = "enemyHero" } });
            CardDef fridge = new CardDef { Id = "refrigerator", Passive = true };
            TriggerDef observed = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceItemId = driver.Id } };
            observed.Effects.Add(new Effect { Kind = "regen", Amount = new NumExpr { Value = 5 },
                Target = new Target { Kind = "selfHero" } });
            fridge.Triggers.Add(observed);
            Add(config, driver, bread, fridge);
            return config;
        }

        private static BattleConfig BaseConfig()
        {
            return new BattleConfig { NativeMechanics = true, MaxTime = 1.15,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
        }

        private static void Add(BattleConfig config, params CardDef[] cards)
        {
            int slot = 0;
            foreach (CardDef card in cards)
            {
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot++ });
            }
        }

        private static BattleEvent Find(BattleResult result, string kind, string source)
        {
            return result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == kind && ev.SourceItemId == source; });
        }
    }
}
