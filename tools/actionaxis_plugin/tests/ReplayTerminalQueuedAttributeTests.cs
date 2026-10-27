using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayTerminalQueuedAttributeTests
    {
        public static void Run()
        {
            NextFrameQueuedReactionCommitsSilentlyAndOnce();
            FartherOrInvalidQueuedReactionsAreRejected();
            NonAttributeAndRandomEffectsDoNotRunAfterDeath();
            ReactionPresentInBothQueuesCommitsOnce();
            LaterSerialReactionIsNotFlushed();
            QueuedGrowthCannotReviveThroughHeroAura();
        }

        private static BattleContext Context(out InternalItem item)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef brick = new CardDef { Id = "terminal-queued-brick", Passive = true };
            brick.LiveAttributes["ShieldApplyAmount"] = 854;
            brick.LiveAttributes["Custom_0"] = 40;
            config.Cards[brick.Id] = brick;
            config.Enemy.Items.Add(new LoadoutItem { CardId = brick.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            item = ctx.Items[0];
            ctx.Now = 16;
            ctx.Player.Hp = 0;
            ctx.Ended = true;
            ctx.Events.Add(new BattleEvent { Kind = "death", Time = 16, Side = Side.Player });
            return ctx;
        }

        private static TriggerDef AttributeTrigger()
        {
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "High" };
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "ShieldApplyAmount", Operation = "Add",
                Amount = new NumExpr { Type = "cardAttribute", Attribute = "Custom_0" },
                Target = new Target { Kind = "selfItem" } });
            return trigger;
        }

        private static DeferredNativeTrigger Queue(BattleContext ctx, InternalItem item, TriggerDef trigger, double due)
        {
            DeferredNativeTrigger entry = new DeferredNativeTrigger {
                Match = new DeferredTriggerMatch { Owner = item, Trigger = trigger },
                Payload = new TriggerPayload { Event = "onUse", SourceSide = Side.Enemy,
                    SourceItemId = "accepted-copper-ed", ChainId = 1 },
                DueAt = due, Generation = item.Generation };
            ctx.DeferredNativeTriggers.Add(entry);
            return entry;
        }

        private static void NextFrameQueuedReactionCommitsSilentlyAndOnce()
        {
            InternalItem brick;
            BattleContext ctx = Context(out brick);
            Queue(ctx, brick, AttributeTrigger(), 16.05);
            int events = ctx.Events.Count;
            int stats = ctx.EffectStats.Count;
            NativeTerminalAttributes.Commit(ctx, 16);
            NativeTerminalAttributes.Commit(ctx, 16);
            Assert.Near(894, ItemAttributes.Get(brick, "ShieldApplyAmount"));
            Assert.Near(0, ctx.Player.Hp);
            Assert.Equal(events, ctx.Events.Count);
            Assert.Equal(stats, ctx.EffectStats.Count);
            Assert.Equal(0, ctx.TriggerQueue.Count);
            Assert.Equal(0, ctx.DeferredNativeTriggers.Count);
            Assert.False(ctx.TerminalAttributesOnly);
        }

        private static void FartherOrInvalidQueuedReactionsAreRejected()
        {
            foreach (string rejection in new[] { "farther", "generation", "destroyed", "disabled" })
            {
                InternalItem brick;
                BattleContext ctx = Context(out brick);
                Queue(ctx, brick, AttributeTrigger(), rejection == "farther" ? 16.10 : 16.05);
                if (rejection == "generation") brick.Generation++;
                if (rejection == "destroyed") brick.Destroyed = true;
                if (rejection == "disabled") brick.Disabled = true;
                int events = ctx.Events.Count;
                NativeTerminalAttributes.Commit(ctx, 16);
                Assert.Near(854, ItemAttributes.Get(brick, "ShieldApplyAmount"));
                Assert.Equal(events, ctx.Events.Count);
            }
        }

        private static void NonAttributeAndRandomEffectsDoNotRunAfterDeath()
        {
            InternalItem brick;
            BattleContext ctx = Context(out brick);
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "High" };
            trigger.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Value = 100 },
                Target = new Target { Kind = "enemyHero" } });
            trigger.Effects.Add(new Effect { Kind = "heal", Amount = new NumExpr { Value = 100 },
                Target = new Target { Kind = "enemyHero" } });
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "ShieldApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "randomSelfItem" } });
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "ShieldApplyAmount", Operation = "Add",
                Amount = new NumExpr { Type = "range", Min = 1, Max = 100 }, Target = new Target { Kind = "selfItem" } });
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "ShieldApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 100 }, CountValue = new NumExpr { Type = "range", Min = 1, Max = 2 },
                Target = new Target { Kind = "selfItem" } });
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "ShieldApplyAmount", Operation = "Add",
                Amount = new NumExpr { Value = 100 }, Target = new Target { Kind = "adjacentSelf", PositionMode = "Neighbor" } });
            Queue(ctx, brick, trigger, 16.05);
            int events = ctx.Events.Count;
            int stats = ctx.EffectStats.Count;
            NativeTerminalAttributes.Commit(ctx, 16);
            Assert.Near(854, ItemAttributes.Get(brick, "ShieldApplyAmount"));
            Assert.Near(0, ctx.Player.Hp);
            Assert.Equal(events, ctx.Events.Count);
            Assert.Equal(stats, ctx.EffectStats.Count);
            Assert.Equal(0, ctx.TriggerQueue.Count);
        }

        private static void ReactionPresentInBothQueuesCommitsOnce()
        {
            InternalItem brick;
            BattleContext ctx = Context(out brick);
            DeferredNativeTrigger entry = Queue(ctx, brick, AttributeTrigger(), 16.05);
            entry.Payload.DeferredMatches = new List<DeferredTriggerMatch> { entry.Match };
            ctx.TriggerQueue.Enqueue(entry.Payload);
            NativeTerminalAttributes.Commit(ctx, 16);
            NativeTerminalAttributes.Commit(ctx, 16);
            Assert.Near(894, ItemAttributes.Get(brick, "ShieldApplyAmount"));
            Assert.Equal(1, ctx.Events.Count);
        }

        private static void LaterSerialReactionIsNotFlushed()
        {
            InternalItem brick;
            BattleContext ctx = Context(out brick);
            TriggerDef trigger = AttributeTrigger();
            Queue(ctx, brick, trigger, 16.05);
            Queue(ctx, brick, trigger, 16.30).Payload.ChainId = 2;
            NativeTerminalAttributes.Commit(ctx, 16);
            Assert.Near(894, ItemAttributes.Get(brick, "ShieldApplyAmount"));
            Assert.Equal(1, ctx.DeferredNativeTriggers.Count);
        }

        private static void QueuedGrowthCannotReviveThroughHeroAura()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 1000 };
            CardDef card = new CardDef { Id = "terminal-queued-swole", Passive = true };
            card.LiveAttributes["DamageAmount"] = 10;
            config.Cards[card.Id] = card;
            config.Enemy.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            JsonValue template = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g05_full.json"))["Swole"];
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry {
                TemplateId = NativeSkillSupport.Text(template, "Id"), Template = template, Tier = "Gold", TemplateBoard = true,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold") } }, null, Side.Enemy);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            TriggerDef trigger = new TriggerDef { Event = "onUse", Priority = "High" };
            trigger.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "DamageAmount", Operation = "Add",
                Amount = new NumExpr { Value = 6 }, Target = new Target { Kind = "selfItem" } });
            Queue(ctx, item, trigger, 1.05);
            ctx.Enemy.Hp = 0;
            ctx.Ended = true;
            int events = ctx.Events.Count;
            NativeTerminalAttributes.Commit(ctx, 1);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(16, ItemAttributes.Get(item, "DamageAmount"));
            Assert.Near(0, ctx.Enemy.Hp);
            Assert.Near(1010, ctx.Enemy.MaxHp);
            Assert.Equal(events, ctx.Events.Count);
        }
    }
}
