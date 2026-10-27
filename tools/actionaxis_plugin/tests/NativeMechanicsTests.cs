using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeMechanicsTests
    {
        public static void Run()
        {
            NativeDotsMatchCurrentCadenceAndDecay();
            NativeFrameZeroDotsTickImmediately();
            NativeFramesRemainTheTimingAndRoundingReference();
            NativeDotsRespectSeparateShieldRules();
            NativeMulticastUsesFrameDelaysAndContinuousCooldown();
            NativePassiveEffectsWaitOneFrameAndChargesDoNotLoseAnother();
            NativeDurationsStackAndFlyingReducesDebuffs();
            NativeStatusesExpireBeforeCooldownAdvance();
            NativeTargetsFilterBeforeChoosingWithoutMovingAdjacency();
            LegacyRemainsOptIn();
        }

        private static Effect Amount(string kind, double amount, string target)
        {
            return new Effect { Kind = kind, Amount = new NumExpr { Value = amount }, Target = new Target { Kind = target } };
        }

        private static void NativeFramesRemainTheTimingAndRoundingReference()
        {
            JsonValue fixture = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_mechanics_frames.json"));
            JsonValue frames = fixture["frames"];
            double frameSeconds = fixture["frameMilliseconds"].AsDouble() / 1000;
            // Raw cooldown deltas establish the otherwise ambiguous time-zero
            // phase; fire events establish the 250 ms multicast spacing.
            Assert.Near(6000, frames[0]["gunCooldownChange"][1].AsDouble());
            Assert.Near(5950, frames[0]["gunCooldownChange"][2].AsDouble());
            Assert.Near(50, frames[1]["gunCooldownChange"][1].AsDouble());
            Assert.Near(6000, frames[1]["gunCooldownChange"][2].AsDouble());
            CardDef card = new CardDef { Id = "native-gun", Cooldown = 6, HasMulticastOverride = true, MulticastBonus = 2 };
            BattleResult result = BattleEngine.Simulate(Config(card, 6.5));
            List<BattleEvent> casts = result.Events.Where(delegate(BattleEvent ev) { return ev.Kind == "activate"; }).ToList();
            Assert.Equal(3, casts.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(1, frames[2 + i]["gunEvents"].Count);
                Assert.Near(frames[2 + i]["index"].AsDouble() * frameSeconds, casts[i].Time);
            }
            // Frame 240: Poison dealt 30 HP damage and Burn 9 dealt only 4
            // Shield damage. Fully absorbed odd Burn must not leak one HP.
            JsonValue update = frames[6]["opponentUpdate"];
            Assert.Near(9, update[2]["0"][1].AsDouble());
            Assert.Near(4, update[2]["19"][1].AsDouble());
            Assert.Equal(2, update[1].Count);
            Assert.Near(2, update[1][1][1].AsDouble());
            Assert.Near(-4, update[1][1][2].AsDouble());
            BattleContext ctx = BattleEngine.InitBattle(Config(card, 0));
            ctx.Enemy.Shield = update[2]["19"][1].AsDouble();
            Effects.ApplyDamage(ctx, new DamageSource { Side = Side.Player }, Side.Enemy,
                update[2]["0"][1].AsDouble(), 12, new DamageOptions { IsDot = true, DotKind = "burn" });
            Assert.Near(1000, ctx.Enemy.Hp);
            Assert.Near(0, ctx.Enemy.Shield);
            // Samples with no ability events avoid conflating decay with fresh
            // stacks or healing. These are raw changes from a second replay.
            foreach (JsonValue sample in fixture["isolatedBurnDecayFrames"].AsArray())
            {
                double opening = sample["burn"][1].AsDouble();
                BattleResult decay = BattleEngine.Simulate(Config(Start(Amount("burn", opening, "enemyHero")), 1));
                List<BattleEvent> ticks = decay.Events.Where(delegate(BattleEvent ev) { return ev.Kind == "dotTick" && ev.Note == "burn"; }).ToList();
                Assert.Equal(3, ticks.Count);
                Assert.Near(sample["burn"][2].AsDouble(), ticks[1].Value.Value);
            }
        }

        private static BattleConfig Config(CardDef card, double maxTime)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 1000, MaxTime = maxTime };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            return config;
        }

        private static CardDef Start(params Effect[] effects)
        {
            CardDef card = new CardDef { Id = "starter", Passive = true };
            TriggerDef trigger = new TriggerDef { Event = "atCombatStart" };
            trigger.Effects.AddRange(effects);
            card.Triggers.Add(trigger);
            return card;
        }

        private static void NativeFrameZeroDotsTickImmediately()
        {
            // Native frame-0 health records contain the first Poison/Burn/Regen
            // tick for statuses applied by at-combat-start effects (85 replay
            // samples). The attack cadence still uses the 50 ms frame grid.
            CardDef card = Start(Amount("burn", 100, "enemyHero"),
                Amount("poison", 7, "enemyHero"), Amount("regen", 3, "enemyHero"));
            BattleConfig config = Config(card, 0.1);
            config.EnemyStartHp = 500;
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> zero = result.Events.Where(delegate(BattleEvent ev)
            {
                return ev.Kind == "dotTick" && Math.Abs(ev.Time) < 1e-9;
            }).ToList();
            Assert.Equal(3, zero.Count);
            Assert.Equal("poison", zero[0].Note);
            Assert.Near(7, zero[0].Value.Value);
            Assert.Equal("burn", zero[1].Note);
            Assert.Near(100, zero[1].Value.Value);
            Assert.Equal("regen", zero[2].Note);
            Assert.Near(3, zero[2].Value.Value);
        }

        private static void NativeDotsMatchCurrentCadenceAndDecay()
        {
            CardDef card = Start(Amount("burn", 100, "enemyHero"), Amount("poison", 7, "enemyHero"), Amount("regen", 3, "enemyHero"));
            BattleConfig config = Config(card, 2);
            config.EnemyStartHp = 500;
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> burn = result.Events.Where(delegate(BattleEvent ev) { return ev.Kind == "dotTick" && ev.Note == "burn"; }).ToList();
            Assert.Equal(5, burn.Count);
            Assert.Near(0, burn[0].Time);
            Assert.Near(100, burn[0].Value.Value);
            Assert.Near(97, burn[1].Value.Value);
            Assert.Near(95, burn[2].Value.Value);
            Assert.Near(93, burn[3].Value.Value);
            Assert.Near(91, burn[4].Value.Value);
            List<BattleEvent> atSecond = result.Events.Where(delegate(BattleEvent ev) { return ev.Kind == "dotTick" && Math.Abs(ev.Time - 1) < 1e-8; }).ToList();
            Assert.Equal("poison", atSecond[0].Note);
            Assert.Equal("burn", atSecond[1].Note);
            Assert.Equal("regen", atSecond[2].Note);
            Assert.Near(497, result.Totals.Player.Damage);
            Assert.Near(497, result.Totals.Player.EffectiveDamage);
            Assert.Near(12, atSecond.Count > 0 ? result.Events.Last(delegate(BattleEvent ev) { return ev.HpAfter.HasValue && ev.TargetSide == Side.Enemy; }).HpAfter.Value : 0);
            Assert.Near(0.05, Rules.Resolve(new RuleOverrides { Dt = 0.1 }, true).Dt);
        }

        private static void NativeDotsRespectSeparateShieldRules()
        {
            CardDef card = Start(Amount("burn", 3, "enemyHero"), Amount("poison", 4, "enemyHero"), Amount("shield", 10, "enemyHero"));
            BattleResult result = BattleEngine.Simulate(Config(card, 1.5));
            BattleEvent poison = result.Events.First(delegate(BattleEvent ev) { return ev.Kind == "dotTick" && ev.Note == "poison"; });
            Assert.Near(0, poison.Absorbed.Value);
            Assert.Near(996, poison.HpAfter.Value);
            BattleEvent last = result.Events.Last(delegate(BattleEvent ev) { return ev.Kind == "dotTick"; });
            Assert.Near(992, last.HpAfter.Value);
            Assert.Near(8, last.ShieldAfter.Value);
            Assert.Near(8, result.Totals.Player.EffectiveDamage);
            BattleResult partial = BattleEngine.Simulate(Config(Start(Amount("burn", 14, "enemyHero"), Amount("shield", 5, "enemyHero")), 0.5));
            BattleEvent hit = partial.Events.First(delegate(BattleEvent ev) { return ev.Kind == "dotTick"; });
            Assert.Near(0, hit.Time);
            Assert.Near(5, hit.Absorbed.Value);
            Assert.Near(996, hit.HpAfter.Value);
            BattleResult odd = BattleEngine.Simulate(Config(Start(Amount("burn", 9, "enemyHero"), Amount("shield", 4, "enemyHero")), 0.5));
            BattleEvent oddHit = odd.Events.First(delegate(BattleEvent ev) { return ev.Kind == "dotTick"; });
            Assert.Near(0, oddHit.Time);
            Assert.Near(4, oddHit.Absorbed.Value);
            Assert.Near(1000, oddHit.HpAfter.Value);
            BattleResult unshielded = BattleEngine.Simulate(Config(Start(Amount("burn", 1, "enemyHero")), 0.5));
            Assert.Near(1, unshielded.Totals.Player.EffectiveDamage);
        }

        private static void NativeMulticastUsesFrameDelaysAndContinuousCooldown()
        {
            CardDef card = new CardDef { Id = "tricast", Cooldown = 1, HasMulticastOverride = true, MulticastBonus = 2 };
            card.Effects.Add(Amount("damage", 1, "enemyHero"));
            BattleResult result = BattleEngine.Simulate(Config(card, 2.6));
            List<BattleEvent> casts = result.Events.Where(delegate(BattleEvent ev) { return ev.Kind == "activate"; }).ToList();
            Assert.Equal(6, casts.Count);
            Assert.Near(1, casts[0].Time);
            Assert.Near(1.25, casts[1].Time);
            Assert.Near(1.50, casts[2].Time);
            Assert.Near(2, casts[3].Time);
            ItemSnapshot after = result.ItemSnapshots.First(delegate(ItemSnapshot snap) { return Math.Abs(snap.Time - 1.50) < 1e-8; });
            Assert.Near(0.45, after.CooldownRemaining);
            Assert.Near(6, result.Totals.Player.Damage);
        }

        private static void NativeDurationsStackAndFlyingReducesDebuffs()
        {
            CardDef card = new CardDef { Id = "status", Cooldown = 10 };
            BattleContext ctx = BattleEngine.InitBattle(Config(card, 2));
            InternalItem item = ctx.Items[0];
            item.Flying = true;
            foreach (string kind in new[] { "haste", "slow", "freeze" })
            {
                Effect effect = new Effect { Kind = kind, Seconds = 2, Target = new Target { Kind = "selfItem" } };
                Effects.ResolveEffect(ctx, effect, item, 0.5, 0, null);
                Effects.ResolveEffect(ctx, effect, item, 1, 0, null);
            }
            Assert.Near(4.5, item.HasteUntil);
            Assert.Near(2.5, item.SlowUntil);
            Assert.Near(2.5, item.FrozenUntil);
            item.Card.PercentFreezeReduction = 50;
            Effects.ResolveEffect(ctx, new Effect { Kind = "freeze", Seconds = 2 }, item, 1, 0, null);
            Assert.Near(3.5, item.FrozenUntil); // A live 50% reduction and Flying must not halve twice.
        }

        private static void NativePassiveEffectsWaitOneFrameAndChargesDoNotLoseAnother()
        {
            JsonValue frames = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_mechanics_frames.json"))["passiveFrames"];
            Assert.Near(0.05, (frames[1]["index"].AsDouble() - frames[0]["index"].AsDouble()) * 0.05);
            Assert.Near(0.05, (frames[3]["index"].AsDouble() - frames[2]["index"].AsDouble()) * 0.05);
            // Cog haste frame 80 -> Firefly charge frame 81, not frame 80.
            CardDef haste = new CardDef { Id = "haste-source", Cooldown = 1 };
            haste.Effects.Add(new Effect { Kind = "haste", Seconds = 0.1, Target = new Target { Kind = "selfItem", ItemId = "haste-target" } });
            CardDef target = new CardDef { Id = "haste-target", Cooldown = 10 };
            TriggerDef trigger = new TriggerDef { Event = "onHaste", Filter = new TriggerFilter { Self = true } };
            trigger.Effects.Add(new Effect { Kind = "charge", Seconds = 1, Target = new Target { Kind = "selfItem" } });
            target.Triggers.Add(trigger);
            BattleConfig config = Config(haste, 1.05);
            config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent charge = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "charge"; });
            Assert.Near(1.05, charge.Time);
            // Core use frame 101 -> freshly charged Spider use frame 102.
            CardDef charger = new CardDef { Id = "charger", Cooldown = 1 };
            charger.Effects.Add(new Effect { Kind = "charge", Seconds = 1, Target = new Target { Kind = "selfItem", ItemId = "charged" } });
            CardDef charged = new CardDef { Id = "charged", Cooldown = 1.5 };
            config = Config(charger, 1.05);
            config.Cards[charged.Id] = charged;
            config.Player.Items.Add(new LoadoutItem { CardId = charged.Id, Slot = 1 });
            result = BattleEngine.Simulate(config);
            BattleEvent use = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "activate" && ev.SourceCardId == charged.Id; });
            Assert.Near(1.05, use.Time);
        }

        private static void NativeTargetsFilterBeforeChoosingWithoutMovingAdjacency()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            for (int i = 0; i < 4; i++)
            {
                CardDef card = new CardDef { Id = "target" + i, Cooldown = 10 };
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = i });
            }
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = ctx.Items[1];
            ctx.Items[0].Flying = true;
            ctx.Items[3].Flying = true;
            Effects.ResolveEffect(ctx, new Effect { Kind = "charge", Seconds = 1, Target = new Target { Kind = "allSelfItems", ExcludeSelf = true, FlyingOnly = true, AdjacentOnly = true } }, source, 0, 0, null);
            Assert.Near(9, ctx.Items[0].Cooldown);
            Assert.Near(10, ctx.Items[1].Cooldown);
            Assert.Near(10, ctx.Items[2].Cooldown);
            Assert.Near(10, ctx.Items[3].Cooldown);
            Effects.ResolveEffect(ctx, new Effect { Kind = "fly", Count = 4, Target = new Target { Kind = "allSelfItems", ExcludeSelf = true } }, source, 0, 0, null);
            Assert.True(ctx.Items[2].Flying);
            Assert.False(source.Flying);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "fly"; }));
        }

        private static void NativeStatusesExpireBeforeCooldownAdvance()
        {
            // Captured native replay frame 120: Slow 50 -> 0 while Cooldown
            // 2475 -> 2425, so the expiry frame advances at the normal rate.
            CardDef card = Start(new Effect { Kind = "slow", Seconds = 0.1, Target = new Target { Kind = "selfItem" } });
            card.Passive = false;
            card.Cooldown = 10;
            BattleResult result = BattleEngine.Simulate(Config(card, 0.1));
            ItemSnapshot last = result.ItemSnapshots.Last();
            // Opening durations have already spent 50ms at frame zero:
            // one slow tick (25ms) then two normal ticks (50ms each).
            Assert.Near(9.875, last.CooldownRemaining);
            Assert.Near(1, last.CooldownRate);
        }

        private static void LegacyRemainsOptIn()
        {
            CardDef card = new CardDef { Id = "legacy", Cooldown = 1, HasMulticastOverride = true, MulticastBonus = 2 };
            card.Effects.Add(Amount("damage", 1, "enemyHero"));
            BattleConfig config = Config(card, 1);
            config.NativeMechanics = false;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(3, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "activate" && Math.Abs(ev.Time - 1) < 1e-8; }));
            Assert.Near(0.1, Rules.Resolve(config.Rules).Dt);
        }
    }
}
