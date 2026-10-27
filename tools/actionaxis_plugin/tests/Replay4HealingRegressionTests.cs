using System;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4HealingRegressionTests
    {
        public static void Run()
        {
            FullHealthHealingStillCleansesNativeStatuses();
            NativeCleanseUsesNominalHealAndNativeAttributeRounding();
            RegenAndLegacyHealingDoNotAcquireCleanse();
            RecoveryEventsKeepTheirNativeActionIdentity();
            RegenAndLifestealDoNotPerformHealActions();
            NativeLifestealIncludesShieldDamage();
        }

        private static void NativeLifestealIncludesShieldDamage()
        {
            // ea100da1 frame 140: 40 Shield + 35 HP damage returns 75 HP,
            // not 35. Poison damage on that frame is a separate packet.
            foreach (bool native in new[] { false, true })
            {
                BattleContext ctx = Context(native);
                ctx.Player.Hp = 100;
                ctx.Player.Poison = 5;
                ctx.Enemy.Shield = 40;
                Effects.ApplyDamage(ctx, new DamageSource { Id = ctx.Items[0].Id, CardId = ctx.Items[0].CardId,
                    Side = Side.Player, Lifesteal = 1 }, Side.Enemy, 75, 7, new DamageOptions());
                Assert.Near(native ? 175 : 135, ctx.Player.Hp);
                Assert.Near(5, ctx.Player.Poison);
            }
        }

        private static void RegenAndLifestealDoNotPerformHealActions()
        {
            // d7d560e5: a Regen action grows Nightshade once. Its HP tick is
            // not another performed Heal and must not grow Nightshade again.
            foreach (string kind in new[] { "regen", "lifesteal" })
            {
                BattleContext ctx = Context(true);
                TriggerDef observer = new TriggerDef { Event = "onHeal" };
                observer.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 7 },
                    Target = new Target { Kind = "selfHero" } });
                ctx.Items[0].Card.Triggers.Add(observer);
                Effects.ApplyHeal(ctx, Side.Player, 20, 1, ctx.Items[0].Id, ctx.Items[0].CardId, 1, 0, null, false, kind);
                Triggers.Drain(ctx, 1);
                Assert.Near(0, ctx.Player.Shield);
                Heal(ctx, 20);
                Triggers.Drain(ctx, 5.05);
                Assert.Near(7, ctx.Player.Shield);
            }
        }

        private static BattleContext Context(bool native)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = native, PlayerMaxHp = 400, EnemyMaxHp = 1000 };
            CardDef card = new CardDef { Id = "replay4-healer", Passive = true };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            return BattleEngine.InitBattle(config);
        }

        private static void Heal(BattleContext ctx, double amount)
        {
            Effects.ResolveEffect(ctx, new Effect { Kind = "heal", Amount = new NumExpr { Value = amount },
                Target = new Target { Kind = "selfHero" } }, ctx.Items[0], 5.05, 0, 1);
        }

        private static void FullHealthHealingStillCleansesNativeStatuses()
        {
            // b0202d85 frame 101: nominal heal 50, Health 400->400, Poison 5->2.
            // frame 133: heal 50, Health 398->400, Poison 2->0.
            BattleContext ctx = Context(true);
            ctx.Player.Poison = 5;
            StatusPools.Add(ctx.Player, "poison", Side.Enemy, "poisoner", "poisoner", 5);
            Heal(ctx, 50);
            Assert.Near(400, ctx.Player.Hp);
            Assert.Near(2, ctx.Player.Poison);
            Assert.Near(2, StatusPools.Allocate(ctx.Player, "poison", 2)[0].Amount);
            Assert.Near(2, ctx.Player.PoisonPools[0].Amount);
            ctx.Player.Hp = 398;
            Heal(ctx, 50);
            Assert.Near(400, ctx.Player.Hp);
            Assert.Near(0, ctx.Player.Poison);
            Assert.Equal(0, ctx.Player.PoisonPools.Count);
        }

        private static void NativeCleanseUsesNominalHealAndNativeAttributeRounding()
        {
            // d7d560e5 frame 81: one heal 50 leaves Poison 5->2 AND Burn 10->8.
            // daf62430 frame 133: heal 10 still cleanses Burn 40->39.
            BattleContext ctx = Context(true);
            ctx.Player.Poison = 5;
            ctx.Player.Burn = 10;
            StatusPools.Add(ctx.Player, "burn", Side.Enemy, "burner", "burner", 10);
            Heal(ctx, 50);
            Assert.Near(2, ctx.Player.Poison);
            Assert.Near(8, ctx.Player.Burn);
            Assert.Near(8, ctx.Player.BurnPools[0].Amount);
            ctx.Player.Burn = 40;
            Heal(ctx, 10);
            Assert.Near(39, ctx.Player.Burn);
            double before = ctx.Player.Burn;
            Heal(ctx, 0);
            Assert.Near(before, ctx.Player.Burn);
        }

        private static void RegenAndLegacyHealingDoNotAcquireCleanse()
        {
            BattleContext legacy = Context(false);
            legacy.Player.Poison = 5;
            Heal(legacy, 50);
            Assert.Near(5, legacy.Player.Poison);
            BattleContext native = Context(true);
            native.Player.Poison = 5;
            // The shared HP primitive is also used by regen/lifesteal. Only
            // the explicit native healing action requests the cleanse rule.
            Effects.ApplyHeal(native, Side.Player, 50, 1, "regen", "regen", 1, 0);
            Assert.Near(5, native.Player.Poison);
        }

        private static void RecoveryEventsKeepTheirNativeActionIdentity()
        {
            BattleContext ctx = Context(true);
            ctx.Player.Hp = 100;
            ctx.Player.Poison = 5;
            Effects.ApplyDamage(ctx, new DamageSource { Id = "life-weapon", CardId = "life-weapon",
                Side = Side.Player, Lifesteal = 1 }, Side.Enemy, 20, 1, new DamageOptions());
            BattleEvent recovery = ctx.Events.Find(delegate(BattleEvent e) { return e.Kind == "heal"; });
            Assert.Equal("lifesteal", recovery.Note);
            Assert.Near(5, ctx.Player.Poison);
            Heal(ctx, 50);
            Assert.Near(2, ctx.Player.Poison);
        }
    }
}
