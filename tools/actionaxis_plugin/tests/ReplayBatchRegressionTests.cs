using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayBatchRegressionTests
    {
        public static void Run()
        {
            ImmediateListenerDoesNotDiscardOtherDeferredListeners();
            OpeningAmmoIsStateInsteadOfReloadEvent();
        }

        private static void OpeningAmmoIsStateInsteadOfReloadEvent()
        {
            foreach (int openingAmmo in new[] { 2, 1 })
            {
                BattleResult result = new BattleResult();
                ItemSnapshot initial = new ItemSnapshot { ItemId = "ammo-card", Time = 0, Ammo = openingAmmo, AmmoMax = 2 };
                initial.Attributes["Ammo"] = openingAmmo;
                result.ItemSnapshots.Add(initial);
                ItemSnapshot later = new ItemSnapshot { ItemId = "ammo-card", Time = 0.05, Ammo = 0, AmmoMax = 2 };
                later.Attributes["Ammo"] = 0;
                result.ItemSnapshots.Add(later);
                System.Reflection.MethodInfo export = typeof(NativeReplayLedgerExport).GetMethod("CardUpdates",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                List<object> rows = (List<object>)export.Invoke(null, new object[] { result });
                int expected = openingAmmo == 2 ? 1 : 2;
                Assert.Equal(expected, rows.Count);
                Dictionary<string, object> first = (Dictionary<string, object>)rows[0];
                Assert.Equal(openingAmmo == 2 ? 1 : 0, Convert.ToInt32(first["frame"]));
                Assert.Equal(2, Convert.ToInt32(first["previous"]));
                Assert.Equal(openingAmmo == 2 ? 0 : 1, Convert.ToInt32(first["current"]));
            }
        }

        private static TriggerDef ShieldTrigger(string priority, double amount, bool skill)
        {
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = priority };
            if (skill) trigger.SkillRule = new NativeSkillRule { SourceOwner = "self" };
            trigger.Effects.Add(new Effect {
                Kind = "shield", Amount = new NumExpr { Value = amount },
                Target = new Target { Kind = "selfHero" }
            });
            return trigger;
        }

        private static void ImmediateListenerDoesNotDiscardOtherDeferredListeners()
        {
            foreach (bool reverse in new[] { false, true })
            {
                CardDef user = new CardDef { Id = "batch-user", Cooldown = 1 };
                CardDef mixed = new CardDef { Id = "batch-mixed", Passive = true };
                mixed.Triggers.Add(ShieldTrigger("Immediate", 2, false));
                mixed.Triggers.Add(ShieldTrigger("Medium", 3, false));
                CardDef observer = new CardDef { Id = "batch-observer", Passive = true };
                observer.Triggers.Add(ShieldTrigger("Medium", 7, false));
                CardDef skill = new CardDef { Id = "batch-skill", Passive = true };
                skill.Triggers.Add(ShieldTrigger("Medium", 11, true));
                BattleConfig config = new BattleConfig {
                    NativeMechanics = true, MaxTime = 1.1, PlayerMaxHp = 1000, EnemyMaxHp = 1000
                };
                config.Cards[user.Id] = user;
                config.Cards[mixed.Id] = mixed;
                config.Cards[observer.Id] = observer;
                config.Player.Items.Add(new LoadoutItem { CardId = user.Id, Slot = 0 });
                config.Player.Items.Add(new LoadoutItem { CardId = reverse ? observer.Id : mixed.Id, Slot = 1 });
                config.Player.Items.Add(new LoadoutItem { CardId = reverse ? mixed.Id : observer.Id, Slot = 2 });
                config.PlayerSkills.Add(skill);
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "shield"; });
                Assert.Equal(4, shields.Count);
                Assert.Near(23, result.Totals.Player.Shield);
                foreach (string source in new[] { mixed.Id, observer.Id, skill.Id })
                {
                    int deferred = shields.FindAll(delegate(BattleEvent ev) {
                        return ev.SourceItemId == source && Math.Abs(ev.Time - 1.05) < 1e-9;
                    }).Count;
                    Assert.Equal(1, deferred);
                }
            }
        }
    }
}
