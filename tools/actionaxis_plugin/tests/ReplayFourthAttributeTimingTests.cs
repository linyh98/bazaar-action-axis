using System;
using System.Collections.Generic;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFourthAttributeTimingTests
    {
        public static void Run()
        {
            HealthLossCountersRunImmediatelyAndPreserveFirstDisable();
            NativeDisablePublishesDestructionAndForcesNextFrameUse();
            DisableObserverRoutingPreservesLegacyAndDestroy();
            DotHealthLossPublishesImmediateCounterBeforeOrdinaryObserver();
            DisabledAmmoCardLosesExternalAuraAndStopsReloading();
            DisabledOwnerCancelsDeferredOrdinaryObservation();
            DeferredReloadBuffRecordsNoopOnDisabledTriggerTarget();
        }

        private static void DeferredReloadBuffRecordsNoopOnDisabledTriggerTarget()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef ramrod = new CardDef { Id = "queued-ramrod", Passive = true };
            TriggerDef reload = new TriggerDef { Event = "onReload", Priority = "Medium", SkillRule = new NativeSkillRule { SourceOwner = "self" } };
            reload.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "CritChance", Operation = "Add",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "triggerTargetItem" } });
            ramrod.Triggers.Add(reload);
            CardDef first = new CardDef { Id = "first-reloaded", Passive = true };
            CardDef grape = new CardDef { Id = "later-disabled-reloaded", Passive = true };
            first.LiveAttributes["CritChance"] = 0; grape.LiveAttributes["CritChance"] = 0;
            CardDef disabler = new CardDef { Id = "queued-buff-disabler", Passive = true };
            foreach (CardDef card in new[] { ramrod, first, grape, disabler }) config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = ramrod.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = first.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = grape.Id, Slot = 2 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = disabler.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem target = ctx.Items.Find(delegate(InternalItem item) { return item.Id == grape.Id; });
            InternalItem actor = ctx.Items.Find(delegate(InternalItem item) { return item.Id == disabler.Id; });
            for (int i = 0; i < 2; i++)
            {
                string id = i == 0 ? first.Id : grape.Id;
                Triggers.Enqueue(ctx, new TriggerPayload { Event = "onReload", SourceSide = Side.Player,
                    SourceItemId = ramrod.Id, TargetSide = Side.Player, TargetItemId = id });
                Triggers.DrainNativeFrame(ctx, i * 0.05);
            }
            Assert.True(ctx.DeferredNativeTriggers.Count > 0);
            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Target = new Target { Kind = "enemyItem", ItemId = grape.Id } }, actor, 0.1, 0, 1);
            Triggers.DrainNativeFrame(ctx, 0.25);
            Assert.Near(0, ItemAttributes.Get(target, "CritChance"));
            List<BattleEvent> recorded = ctx.Events.FindAll(delegate(BattleEvent ev) {
                return ev.Kind == "modifyAttribute" && ev.TargetItemId == grape.Id;
            });
            // Fourth replay frame 171 still contains Ramrod's action, even
            // though disabled Grapeshot's CritChance remains unchanged.
            Assert.Equal(1, recorded.Count);
            if (recorded.Count == 1) { Assert.Near(0.25, recorded[0].Time); Assert.Near(0, recorded[0].Value.Value); }
        }

        private static void DisabledAmmoCardLosesExternalAuraAndStopsReloading()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef driver = new CardDef { Id = "disabled-ammo-driver", Passive = true };
            CardDef grapeshot = new CardDef { Id = "disabled-grapeshot", Passive = true, AmmoMax = 1 };
            grapeshot.LiveAttributes["CritChance"] = 8; // Captured Keen Eye contribution.
            grapeshot.LiveAttributes["DamageAmount"] = 20;
            grapeshot.LiveAttributes["Custom_0"] = 0;
            grapeshot.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", Amount = new NumExpr { Value = 20 }, Target = new Target { Kind = "enemyHero" } });
            TriggerDef reload = new TriggerDef { Event = "onUse", Priority = "Medium",
                SkillRule = new NativeSkillRule { SourceOwner = "self", SourceOther = true } };
            reload.Effects.Add(new Effect { Kind = "reload", AmountValue = 1, Target = new Target { Kind = "selfItem" } });
            grapeshot.Triggers.Add(reload);
            foreach (string name in new[] { "onBeforeDestroy", "onCardDisabled" })
            {
                TriggerDef lifecycle = new TriggerDef { Event = name, Priority = "Medium",
                    SkillRule = new NativeSkillRule { SourceOwner = "self", SourceSelf = true } };
                lifecycle.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                    Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
                grapeshot.Triggers.Add(lifecycle);
            }
            CardDef disabler = new CardDef { Id = "ammo-disabler", Passive = true };
            CardDef keenEye = new CardDef { Id = "ammo-keen-eye", Passive = true, NativeAttributesAreLive = false };
            keenEye.NativeRuntimeAuras.Add(Json.Parse("{\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"CritChance\",\"Operation\":\"Add\",\"Target\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"Conditions\":{\"$type\":\"TCardConditionalCanCrit\",\"IsNot\":false}},\"Value\":{\"$type\":\"TFixedValue\",\"Value\":8}}}"));
            config.Cards[driver.Id] = driver; config.Cards[grapeshot.Id] = grapeshot; config.Cards[disabler.Id] = disabler;
            config.PlayerSkills.Add(keenEye);
            config.Player.Items.Add(new LoadoutItem { CardId = driver.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = grapeshot.Id, Slot = 1 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = disabler.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem ammo = ctx.Items.Find(delegate(InternalItem item) { return item.Id == grapeshot.Id; });
            InternalItem source = ctx.Items.Find(delegate(InternalItem item) { return item.Id == driver.Id; });
            InternalItem actor = ctx.Items.Find(delegate(InternalItem item) { return item.Id == disabler.Id; });
            Assert.Near(8, ItemAttributes.Get(ammo, "CritChance"));
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "CritChance", Operation = "Add",
                Amount = new NumExpr { Value = 10 }, Target = new Target { Kind = "selfItem", ItemId = grapeshot.Id } }, source, 8, 0, 1);
            Assert.Near(18, ItemAttributes.Get(ammo, "CritChance"));
            ammo.Ammo = 0;
            // A use observed before disable can already have a matched reload;
            // disabling its owner must cancel that pending ordinary observer.
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = source.Id });
            Triggers.DrainImmediateOnUse(ctx, 8.1);
            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Target = new Target { Kind = "enemyItem", ItemId = grapeshot.Id } }, actor, 8.15, 0, 2);
            Assert.Near(10, ItemAttributes.Get(ammo, "CritChance"));
            Assert.Near(10, ammo.AttributeDeltas["CritChance"]);
            Triggers.DrainNativeFrame(ctx, 8.2);
            Assert.Near(2, ItemAttributes.Get(ammo, "Custom_0")); // Existing lifecycle observers still run.
            Assert.Equal(0, ammo.Ammo);
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = source.Id });
            Triggers.DrainNativeFrame(ctx, 8.25);
            Assert.Equal(0, ammo.Ammo);
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "reload" && ev.SourceItemId == ammo.Id; }));
            // An explicit forced use of a disabled item retains its active
            // effects; the ordinary reload observer still cannot restart it.
            ammo.Ammo = 1;
            BattleEngine.ForceUse(ctx, ammo, 8.3, 0);
            Assert.True(ctx.Events.Any(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == ammo.Id; }));
            Assert.True(ammo.Disabled);
            Assert.False(ammo.Destroyed);
        }

        private static void DisabledOwnerCancelsDeferredOrdinaryObservation()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef owner = new CardDef { Id = "deferred-disabled-owner", Passive = true };
            owner.LiveAttributes["Custom_0"] = 0;
            TriggerDef observed = new TriggerDef { Event = "onUse", Priority = "Medium" };
            observed.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            owner.Triggers.Add(observed);
            CardDef disabler = new CardDef { Id = "deferred-disabler", Passive = true };
            config.Cards[owner.Id] = owner; config.Cards[disabler.Id] = disabler;
            config.Player.Items.Add(new LoadoutItem { CardId = owner.Id, Slot = 0 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = disabler.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items.Find(delegate(InternalItem current) { return current.Id == owner.Id; });
            InternalItem actor = ctx.Items.Find(delegate(InternalItem current) { return current.Id == disabler.Id; });
            for (int i = 0; i < 2; i++)
            {
                Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = owner.Id });
                Triggers.DrainNativeFrame(ctx, i * 0.05);
            }
            Assert.Near(1, ItemAttributes.Get(item, "Custom_0"));
            Assert.True(ctx.DeferredNativeTriggers.Count > 0);
            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Target = new Target { Kind = "enemyItem", ItemId = owner.Id } }, actor, 0.1, 0, 1);
            Triggers.DrainNativeFrame(ctx, 0.25);
            Assert.Near(1, ItemAttributes.Get(item, "Custom_0"));
        }

        private static void NativeDisablePublishesDestructionAndForcesNextFrameUse()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 8.3,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef driver = new CardDef { Id = "disable-driver", Cooldown = 8.1 };
            CardDef actor = new CardDef { Id = "disable-actor", Passive = true };
            actor.Effects.Add(new Effect { Kind = "damage", Priority = "Medium", NativeAbilityId = "0",
                Amount = new NumExpr { Value = 7 }, Target = new Target { Kind = "enemyHero" } });
            TriggerDef disable = new TriggerDef { Event = "onUse", Priority = "Highest",
                Filter = new TriggerFilter { SourceItemId = driver.Id } };
            disable.Effects.Add(new Effect { Kind = "disable", Target = new Target { Kind = "enemyItem", ItemId = "disabled-target" } });
            TriggerDef destruction = new TriggerDef { Event = "onCardDestroyed", Priority = "Low",
                SkillRule = new NativeSkillRule { SourceOwner = "self", SourceSelf = true } };
            destruction.Effects.Add(new Effect { Kind = "forceUse", Target = new Target { Kind = "selfItem" } });
            actor.Triggers.Add(disable);
            actor.Triggers.Add(destruction);
            CardDef target = new CardDef { Id = "disabled-target", Passive = true };
            config.Cards[driver.Id] = driver; config.Cards[actor.Id] = actor; config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = driver.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = actor.Id, Slot = 1 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent disabled = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "disable"; });
            BattleEvent forced = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "forceUse"; });
            BattleEvent damage = result.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == actor.Id; });
            Assert.True(disabled != null && forced != null && damage != null);
            if (disabled != null) { Assert.Near(8.15, disabled.Time); Assert.Equal(actor.Id, disabled.SourceItemId); }
            if (forced != null) { Assert.Near(8.2, forced.Time); Assert.Equal(actor.Id, forced.TargetItemId); }
            if (damage != null) Assert.Near(8.25, damage.Time);
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "destroy"; }));
        }

        private static void DisableObserverRoutingPreservesLegacyAndDestroy()
        {
            foreach (bool native in new[] { false, true })
                foreach (bool destroy in new[] { false, true })
                {
                    BattleConfig config = new BattleConfig { NativeMechanics = native, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
                    CardDef actor = new CardDef { Id = "observer-actor", Passive = true };
                    actor.LiveAttributes["Custom_0"] = 0;
                    TriggerDef observer = new TriggerDef { Event = "onCardDestroyed", Priority = "Medium",
                        SkillRule = new NativeSkillRule { SourceOwner = "self", SourceSelf = true } };
                    observer.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_0", Operation = "Add",
                        Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
                    actor.Triggers.Add(observer);
                    CardDef target = new CardDef { Id = "observer-target", Passive = true };
                    config.Cards[actor.Id] = actor; config.Cards[target.Id] = target;
                    config.Player.Items.Add(new LoadoutItem { CardId = actor.Id, Slot = 0 });
                    config.Enemy.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 0 });
                    BattleContext ctx = BattleEngine.InitBattle(config);
                    InternalItem acting = ctx.Items.Find(delegate(InternalItem item) { return item.Id == actor.Id; });
                    InternalItem affected = ctx.Items.Find(delegate(InternalItem item) { return item.Id == target.Id; });
                    Effect action = new Effect { Kind = destroy ? "destroy" : "disable", Target = new Target { Kind = "enemyItem", ItemId = target.Id } };
                    Effects.ResolveEffect(ctx, action, acting, 8.15, 0, 1);
                    // A second successful transition is required for another
                    // observer; retrying an already disabled/destroyed target
                    // must leave one event and one pending observation.
                    Effects.ResolveEffect(ctx, action, acting, 8.15, 0, 2);
                    TriggerPayload pending = ctx.TriggerQueue.FirstOrDefault(delegate(TriggerPayload payload) { return payload.Event == "onCardDestroyed"; });
                    Assert.Equal(destroy || native ? 1 : 0, ctx.TriggerQueue.Count(delegate(TriggerPayload payload) { return payload.Event == "onCardDestroyed"; }));
                    if (pending != null) { Assert.Equal(actor.Id, pending.SourceItemId); Assert.Equal(target.Id, pending.TargetItemId); }
                    if (native) Triggers.DrainNativeFrame(ctx, 8.2); else Triggers.Drain(ctx, 8.2);
                    Assert.Near(destroy || native ? 1 : 0, ItemAttributes.Get(acting, "Custom_0"));
                    Assert.True(affected.Disabled);
                    Assert.Equal(destroy, affected.Destroyed);
                    Assert.Equal(destroy ? 1 : 0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "destroy"; }));
                    Assert.Equal(destroy ? 0 : 1, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "disable"; }));
                }
        }

        private static void DotHealthLossPublishesImmediateCounterBeforeOrdinaryObserver()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 0.05,
                PlayerMaxHp = 100, EnemyMaxHp = 100, EnemyStartHp = 75 };
            CardDef fire = new CardDef { Id = "dot-attribute-source", Passive = true };
            TriggerDef opening = new TriggerDef { Event = "atCombatStart" };
            opening.Effects.Add(new Effect { Kind = "burn", Amount = new NumExpr { Value = 30 }, Target = new Target { Kind = "enemyHero" } });
            fire.Triggers.Add(opening);
            CardDef watcher = new CardDef { Id = "dot-attribute-watcher", Passive = true };
            watcher.LiveAttributes["Custom_1"] = 0;
            foreach (bool immediate in new[] { true, false })
            {
                TriggerDef observed = new TriggerDef { Event = "onPlayerAttributeChanged", AttributeFilter = "Health", ChangeType = "Loss",
                    Priority = immediate ? "Immediate" : "Highest", SkillRule = new NativeSkillRule { PlayerOwner = "enemy" } };
                observed.Effects.Add(immediate
                    ? new Effect { Kind = "modifyAttribute", Attribute = "Custom_1", Operation = "Add", Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } }
                    : new Effect { Kind = "shield", Amount = new NumExpr { Value = 7 }, Target = new Target { Kind = "selfHero" } });
                watcher.Triggers.Add(observed);
            }
            config.Cards[fire.Id] = fire; config.Cards[watcher.Id] = watcher;
            config.Player.Items.Add(new LoadoutItem { CardId = fire.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = watcher.Id, Slot = 1 });
            BattleResult result = BattleEngine.Simulate(config);
            BattleEvent counter = result.Events.Find(delegate(BattleEvent ev) { return ev.SourceItemId == watcher.Id && ev.Kind == "modifyAttribute"; });
            BattleEvent shield = result.Events.Find(delegate(BattleEvent ev) { return ev.SourceItemId == watcher.Id && ev.Kind == "shield"; });
            Assert.True(counter != null && shield != null);
            if (counter != null) Assert.Near(0, counter.Time);
            if (shield != null) Assert.Near(0.05, shield.Time);
        }

        private static void HealthLossCountersRunImmediatelyAndPreserveFirstDisable()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                PlayerMaxHp = 100, EnemyMaxHp = 100, EnemyStartHp = 75 };
            CardDef attacker = new CardDef { Id = "fourth-attacker", Passive = true };
            CardDef trollosaur = new CardDef { Id = "fourth-trollosaur", Passive = true };
            trollosaur.LiveAttributes["Custom_1"] = 0;
            JsonValue belowHalf = Json.Parse("{\"$type\":\"TPlayerConditionalAttribute\",\"Attribute\":\"Health\",\"ComparisonOperator\":\"LessThan\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":50}}");
            JsonValue unused = Json.Parse("[{\"$type\":\"TPrerequisiteCardCount\",\"Amount\":1,\"Comparison\":\"Equal\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"Custom_1\",\"ComparisonOperator\":\"Equal\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":0}}}}]");
            TriggerDef count = new TriggerDef { Event = "onPlayerAttributeChanged", AttributeFilter = "Health",
                ChangeType = "Loss", Priority = "Immediate",
                SkillRule = new NativeSkillRule { PlayerOwner = "enemy", PlayerCondition = belowHalf } };
            count.Effects.Add(new Effect { Kind = "modifyAttribute", Attribute = "Custom_1", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem" } });
            TriggerDef disable = new TriggerDef { Event = "onPlayerAttributeChanged", AttributeFilter = "Health",
                ChangeType = "Loss", Priority = "Highest",
                SkillRule = new NativeSkillRule { PlayerOwner = "enemy", PlayerCondition = belowHalf, Prerequisites = unused } };
            disable.Effects.Add(new Effect { Kind = "disable", Target = new Target { Kind = "enemyItem", ItemId = "fourth-small-target" } });
            trollosaur.Triggers.Add(count);
            trollosaur.Triggers.Add(disable);
            CardDef target = new CardDef { Id = "fourth-small-target", Cooldown = 10 };
            config.Cards[attacker.Id] = attacker;
            config.Cards[trollosaur.Id] = trollosaur;
            config.Cards[target.Id] = target;
            config.Player.Items.Add(new LoadoutItem { CardId = attacker.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = trollosaur.Id, Slot = 1 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = target.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = ctx.Items.Find(delegate(InternalItem item) { return item.Id == attacker.Id; });
            InternalItem watcher = ctx.Items.Find(delegate(InternalItem item) { return item.Id == trollosaur.Id; });
            Effect firstLoss = new Effect { Kind = "damage", Amount = new NumExpr { Value = 30 }, Target = new Target { Kind = "enemyHero" } };
            // Fourth replay frame 162: the first loss crosses half Health.
            Triggers.DrainNativeFrame(ctx, 8.1);
            Effects.ResolveEffect(ctx, firstLoss, source, 8.1, 0, 1);
            Assert.Near(45, ctx.Enemy.Hp);
            Assert.Near(1, ItemAttributes.Get(watcher, "Custom_1"));
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "disable"; }));
            // Frame 163: Highest keeps its pre-counter match and executes once.
            Triggers.DrainNativeFrame(ctx, 8.15);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "disable"; }));
            Effects.ResolveEffect(ctx, new Effect { Kind = "damage", Amount = new NumExpr { Value = 5 },
                Target = new Target { Kind = "enemyHero" } }, source, 8.15, 0, 2);
            Assert.Near(2, ItemAttributes.Get(watcher, "Custom_1"));
            Triggers.DrainNativeFrame(ctx, 8.2);
            List<BattleEvent> counters = ctx.Events.FindAll(delegate(BattleEvent ev) {
                return ev.Kind == "modifyAttribute" && ev.SourceItemId == trollosaur.Id && ev.Note == "Custom_1";
            });
            List<BattleEvent> disables = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "disable"; });
            Assert.Equal(2, counters.Count);
            if (counters.Count == 2) { Assert.Near(8.1, counters[0].Time); Assert.Near(8.15, counters[1].Time); }
            Assert.Equal(1, disables.Count);
            if (disables.Count == 1) Assert.Near(8.15, disables[0].Time);
        }
    }
}
