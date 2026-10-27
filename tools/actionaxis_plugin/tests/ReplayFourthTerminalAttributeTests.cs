using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFourthTerminalAttributeTests
    {
        public static void Run()
        {
            AcceptedAttributeReactionAppearsInFinalSnapshotOnly();
            TerminalItemGrowthDoesNotReviveThroughHeroAura();
        }

        private static void AcceptedAttributeReactionAppearsInFinalSnapshotOnly()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.1, EnemyMaxHp = 1 };
            CardDef killer = new CardDef { Id = "terminal-core", Cooldown = 1 };
            killer.LiveAttributes["DamageAmount"] = 10;
            killer.Effects.Add(new Effect { Kind = "damage", Priority = "Medium",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount" }, Target = new Target { Kind = "enemyHero" } });
            CardDef observer = new CardDef { Id = "terminal-alpha", Passive = true };
            TriggerDef accepted = new TriggerDef { Event = "onUse", Priority = "Medium",
                Filter = new TriggerFilter { SourceItemId = killer.Id } };
            accepted.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 6 }, Target = new Target { Kind = "allSelfItems", ItemId = killer.Id } });
            // No post-death attacks or guessed random attribute targets.
            accepted.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "enemyHero" } });
            accepted.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "randomSelfItem", ItemId = killer.Id } });
            accepted.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                CountValue = new NumExpr { Type = "range", Min = 1, Max = 2 },
                Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "allSelfItems", ItemId = killer.Id } });
            accepted.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add", Count = 1,
                Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "adjacentSelf", PositionMode = "Neighbor" } });
            accepted.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "DamageAmount",
                    CardTarget = new Target { Kind = "randomSelfItem" } },
                Target = new Target { Kind = "allSelfItems", ItemId = killer.Id } });
            observer.Triggers.Add(accepted);
            CardDef neighbor = new CardDef { Id = "terminal-neighbor", Passive = true };
            neighbor.LiveAttributes["DamageAmount"] = 10;
            config.Cards[killer.Id] = killer; config.Cards[observer.Id] = observer; config.Cards[neighbor.Id] = neighbor;
            config.Player.Items.Add(new LoadoutItem { CardId = killer.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = observer.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = neighbor.Id, Slot = 2 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.True(result.EndedByDeath);
            Assert.Near(1, result.EndTime);
            ItemSnapshot last = result.ItemSnapshots.Last(delegate(ItemSnapshot s) { return s.ItemId == killer.Id; });
            Assert.Near(16, last.Attributes["DamageAmount"]);
            Assert.Near(16, result.ItemSnapshots.Last(delegate(ItemSnapshot s) { return s.ItemId == neighbor.Id; }).Attributes["DamageAmount"]);
            Assert.Equal(1, result.Events.Count(delegate(BattleEvent e) { return e.Kind == "damage"; }));
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent e) { return e.Kind == "modifyAttribute"; }));
        }

        private static void TerminalItemGrowthDoesNotReviveThroughHeroAura()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 1000 };
            CardDef card = new CardDef { Id = "terminal-swole", Passive = true };
            card.LiveAttributes["DamageAmount"] = 10;
            config.Cards[card.Id] = card;
            config.Enemy.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            JsonValue template = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g05_full.json"))["Swole"];
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry {
                TemplateId = NativeSkillSupport.Text(template, "Id"), Template = template, Tier = "Gold", TemplateBoard = true,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold") } }, null, Side.Enemy);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == card.Id; });
            Assert.Near(1010, ctx.Enemy.MaxHp);
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "Medium" };
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 6 }, Target = new Target { Kind = "selfItem" } });
            ctx.TriggerQueue.Enqueue(new TriggerPayload { DeferredMatches = new List<DeferredTriggerMatch> {
                new DeferredTriggerMatch { Owner = item, Trigger = trigger } } });
            ctx.Enemy.Hp = 0;
            ctx.Ended = true;
            NativeTerminalAttributes.Commit(ctx, 1);
            NativeDynamicAuras.Refresh(ctx);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(16, ItemAttributes.Get(item, "DamageAmount"));
            Assert.Near(0, ctx.Enemy.Hp);
            Assert.Near(1010, ctx.Enemy.MaxHp);
        }
    }
}
