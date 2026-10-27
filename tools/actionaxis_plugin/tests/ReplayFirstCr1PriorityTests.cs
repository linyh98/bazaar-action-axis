using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFirstCr1PriorityTests
    {
        public static void Run()
        {
            LaterActivatedHighBuffPrecedesEarlierActivatedMediumDamage();
            SameFrameMediumOrderFollowsActivationWithinPriority();
        }

        // CombatReplays_1 第一场（00a37bf3）帧 80/120：同一帧就绪的多张卡，
        // 原生先结算所有 High 效果，再按激活顺序结算 Medium。
        // 鲨鱼爪的 High 加伤必须先于其它武器的 Medium 伤害。
        private static void LaterActivatedHighBuffPrecedesEarlierActivatedMediumDamage()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef gun = new CardDef { Id = "gun", Cooldown = 1 };
            gun.LiveAttributes["DamageAmount"] = 10;
            gun.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount" },
                Target = new Target { Kind = "enemyHero" } });
            CardDef buffer = new CardDef { Id = "buffer", Cooldown = 1 };
            buffer.LiveAttributes["DamageAmount"] = 5;
            buffer.Effects.Add(new Effect { Kind = "modifyAttribute", Priority = "High",
                Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfItem", ItemId = gun.Id } });
            buffer.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount" },
                Target = new Target { Kind = "enemyHero" } });
            config.Cards[gun.Id] = gun;
            config.Cards[buffer.Id] = buffer;
            config.Player.Items.Add(new LoadoutItem { CardId = gun.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = buffer.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> damage = result.Events.FindAll(
                delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.Equal(2, damage.Count);
            Assert.Equal("gun", damage[0].SourceItemId);
            Assert.Near(20, damage[0].Value.Value);
            Assert.Equal("buffer", damage[1].SourceItemId);
            Assert.Near(5, damage[1].Value.Value);
        }

        // 同一优先级内仍按激活顺序：先激活的 Medium 伤害在前。
        private static void SameFrameMediumOrderFollowsActivationWithinPriority()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef first = new CardDef { Id = "first", Cooldown = 1 };
            first.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Value = 7 }, Target = new Target { Kind = "enemyHero" } });
            CardDef second = new CardDef { Id = "second", Cooldown = 1 };
            second.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Value = 9 }, Target = new Target { Kind = "enemyHero" } });
            config.Cards[first.Id] = first;
            config.Cards[second.Id] = second;
            config.Player.Items.Add(new LoadoutItem { CardId = first.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = second.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> damage = result.Events.FindAll(
                delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.Equal(2, damage.Count);
            Assert.Equal("first", damage[0].SourceItemId);
            Assert.Equal("second", damage[1].SourceItemId);
        }
    }
}
