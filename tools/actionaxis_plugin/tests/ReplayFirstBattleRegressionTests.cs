using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFirstBattleRegressionTests
    {
        public static void Run()
        {
            RevivePreservesTheLethalAttackUseReactions();
        }

        private static void RevivePreservesTheLethalAttackUseReactions()
        {
            // b0202d85, frame 540: Marlon deals lethal damage, gains its
            // onUse Crit, then Life Preserver revives the enemy. The charge
            // observer belongs to frame 541 and must survive the revive.
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1,
                PlayerMaxHp = 400, EnemyMaxHp = 400, EnemyStartHp = 15 };
            CardDef attack = new CardDef { Id = "lethal-marlon", Cooldown = 1 };
            attack.LiveAttributes["CritChance"] = 0;
            attack.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Value = 30 }, Target = new Target { Kind = "enemyHero" } });
            TriggerDef ownUse = new TriggerDef { Event = "onUse", Priority = "Immediate",
                Filter = new TriggerFilter { SourceItemId = attack.Id } };
            ownUse.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "CritChance", Operation = "Add",
                Amount = new NumExpr { Value = 20 }, Target = new Target { Kind = "selfItem" } });
            attack.Triggers.Add(ownUse);
            CardDef observer = new CardDef { Id = "charge-observer", Cooldown = 10 };
            TriggerDef use = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceItemId = attack.Id } };
            use.Effects.Add(new Effect { Kind = "charge", Seconds = 1, Target = new Target { Kind = "selfItem" } });
            observer.Triggers.Add(use);
            CardDef preserver = new CardDef { Id = "life-preserver", Passive = true };
            preserver.LiveAttributes["Custom_0"] = 0;
            TriggerDef healthLoss = new TriggerDef { Event = "onPlayerAttributeChanged", Priority = "Immediate",
                AttributeFilter = "Health", ChangeType = "Loss", SkillRule = new NativeSkillRule { PlayerOwner = "self" } };
            healthLoss.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            preserver.Triggers.Add(healthLoss);
            TriggerDef revive = new TriggerDef { Event = "onPlayerDied",
                SkillRule = new NativeSkillRule { PlayerOwner = "self" } };
            revive.Effects.Add(new Effect { Kind = "revive", Amount = new NumExpr { Value = 200 },
                Target = new Target { Kind = "selfHero" } });
            preserver.Triggers.Add(revive);
            foreach (CardDef card in new[] { attack, observer, preserver }) config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = attack.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = observer.Id, Slot = 1 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = preserver.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.False(result.EndedByDeath);
            BattleEvent heal = result.Events.Find(delegate(BattleEvent e) { return e.Kind == "heal" && e.SourceItemId == preserver.Id; });
            Assert.True(heal != null);
            if (heal != null) { Assert.Near(1, heal.Time); Assert.Near(200, heal.Value.Value); }
            BattleEvent crit = result.Events.Find(delegate(BattleEvent e) { return e.Kind == "modifyAttribute" && e.Note == "CritChance"; });
            Assert.True(crit != null);
            if (crit != null) { Assert.Near(1, crit.Time); Assert.Near(20, crit.Value.Value); }
            List<BattleEvent> charges = result.Events.FindAll(delegate(BattleEvent e) { return e.Kind == "charge"; });
            Assert.Equal(1, charges.Count);
            if (charges.Count == 1) Assert.Near(1.05, charges[0].Time);
            BattleEvent lethalHealth = result.Events.Find(delegate(BattleEvent e) {
                return e.Kind == "modifyAttribute" && e.Note == "Custom_0" && e.SourceItemId == preserver.Id;
            });
            Assert.True(lethalHealth != null);
            if (lethalHealth != null) Assert.Near(1, lethalHealth.Time);
        }
    }
}
