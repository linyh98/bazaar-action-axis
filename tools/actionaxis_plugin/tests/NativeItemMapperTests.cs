using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeItemMapperTests
    {
        public static void Run()
        {
            CoreUsesFlyingOnActivation();
            NeighborSourcesAndTargets();
            DrowsdropRequiresFriendlySlowOnItself();
            AviatorsRequiresFriendlyBurnOrDestruction();
            MappingDoesNotMutateLegacyCatalog();
            NativePassivesWaitForFollowingFrame();
            MidReplayFlyingResistanceDoesNotLeakIntoOpening();
        }

        private static BattleConfig Config()
        {
            return new BattleConfig { NativeMechanics = true, PlayerMaxHp = 10000, EnemyMaxHp = 10000, MaxTime = 30 };
        }

        private static CardDef Add(BattleConfig config, string id, int slot, string tier, Side side)
        {
            CardDef card = CardCatalog.Load("src/data/cards.json").Find(id, tier);
            if (card != null) card = card.Clone();
            else card = new CardDef { Id = id, BaseItemId = id, Cooldown = 10 };
            card.Id = id + ":" + SideUtil.Key(side) + ":" + slot;
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
            return card;
        }

        private static InternalItem Item(BattleContext ctx, string template, Side side)
        {
            foreach (InternalItem item in ctx.Items)
                if (item.Card.BaseItemId == template && item.Side == side) return item;
            return null;
        }

        private static void Trigger(BattleContext ctx, string name, InternalItem source, InternalItem target)
        {
            Triggers.Enqueue(ctx, new TriggerPayload { Event = name, SourceSide = source.Side, SourceItemId = source.Id,
                TargetItemId = target == null ? null : target.Id, TargetSide = target == null ? (Side?)null : target.Side });
            Triggers.Drain(ctx, 1);
        }

        private static void CoreUsesFlyingOnActivation()
        {
            BattleConfig config = Config();
            CardDef core = Add(config, NativeItemMapper.LauncherCore, 0, "Silver", Side.Player);
            core.LiveAttributes["FlyingTargets"] = 2;
            core.LiveAttributes["ChargeAmount"] = 1500;
            Add(config, NativeItemMapper.Spider, 2, "Silver", Side.Player);
            Add(config, "unrelated-a", 3, "Bronze", Side.Player);
            Add(config, "unrelated-b", 4, "Bronze", Side.Player);
            Add(config, "unrelated-c", 5, "Bronze", Side.Player);
            NativeItemMapper.Apply(config);
            NativeItemMapper.Apply(config); // Refreshing must not duplicate passive triggers.
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem launcher = Item(ctx, NativeItemMapper.LauncherCore, Side.Player);
            InternalItem spider = Item(ctx, NativeItemMapper.Spider, Side.Player);
            InternalItem other = Item(ctx, "unrelated-a", Side.Player);
            Assert.False(spider.Flying);
            Assert.Equal(0, launcher.Card.Triggers.FindAll(delegate(TriggerDef t) { return t.Event == "atCombatStart"; }).Count);
            Assert.Equal(1, launcher.Card.Triggers.Count);
            launcher.Cooldown = 6;
            Trigger(ctx, "onUse", other, null);
            Assert.Near(6, launcher.Cooldown);
            Trigger(ctx, "onUse", spider, null);
            Assert.Near(4.5, launcher.Cooldown);
            Trigger(ctx, "onUse", launcher, null);
            Assert.Near(4.5, launcher.Cooldown);

            foreach (Effect effect in launcher.Card.Effects) Effects.ResolveEffect(ctx, effect, launcher, 6, 0, null);
            int flying = 0;
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Flying) { flying++; Assert.Near(item.Card.Cooldown - 1.5, item.Cooldown); }
            }
            Assert.Equal(2, flying);
            Assert.False(launcher.Flying);
            Assert.Near(4.5, launcher.Cooldown);
            // Subsequent uses choose new non-flying targets, rather than sampling
            // already-flying cards and discarding the effect.
            foreach (Effect effect in launcher.Card.Effects) Effects.ResolveEffect(ctx, effect, launcher, 12, 0, null);
            flying = 0;
            foreach (InternalItem item in ctx.Items) if (item.Flying) flying++;
            Assert.Equal(4, flying);
            other.Flying = true;
            launcher.Cooldown = 6;
            Trigger(ctx, "onUse", other, null);
            Assert.Near(4.5, launcher.Cooldown);
        }

        private static void NeighborSourcesAndTargets()
        {
            BattleConfig config = Config();
            Add(config, NativeItemMapper.Firefly, 0, "Silver", Side.Player);
            Add(config, NativeItemMapper.Cog, 1, "Silver", Side.Player);
            CardDef passive = Add(config, "passive", 2, "Bronze", Side.Player);
            passive.Cooldown = 0;
            Add(config, "distant", 3, "Bronze", Side.Player);
            Add(config, "opponent", 0, "Bronze", Side.Enemy);
            NativeItemMapper.Apply(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem firefly = Item(ctx, NativeItemMapper.Firefly, Side.Player);
            InternalItem cog = Item(ctx, NativeItemMapper.Cog, Side.Player);
            foreach (Effect effect in cog.Card.Effects) Effects.ResolveEffect(ctx, effect, cog, 1, 0, null);
            Triggers.Drain(ctx, 1);
            Assert.Near(3, firefly.HasteUntil); // Silver cog: 2 seconds.
            Assert.Near(6, firefly.Cooldown); // Neighbor cog caused Haste, charge 1.
            Assert.Near(-1, cog.HasteUntil);
            Assert.Near(-1, Item(ctx, "passive", Side.Player).HasteUntil);
            Trigger(ctx, "onSlow", Item(ctx, "distant", Side.Player), firefly);
            Assert.Near(6, firefly.Cooldown); // Target proximity is insufficient.
            Trigger(ctx, "onHaste", Item(ctx, "opponent", Side.Enemy), firefly);
            Assert.Near(6, firefly.Cooldown);
            Trigger(ctx, "onSlow", cog, Item(ctx, "opponent", Side.Enemy));
            Assert.Near(5, firefly.Cooldown); // Neighbor may Slow an enemy.
        }

        private static void DrowsdropRequiresFriendlySlowOnItself()
        {
            BattleConfig config = Config();
            Add(config, NativeItemMapper.Drowsdrop, 0, "Bronze", Side.Player);
            Add(config, "friendly", 1, "Bronze", Side.Player);
            Add(config, "enemy", 0, "Bronze", Side.Enemy);
            NativeItemMapper.Apply(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem potion = Item(ctx, NativeItemMapper.Drowsdrop, Side.Player);
            InternalItem friend = Item(ctx, "friendly", Side.Player);
            InternalItem enemy = Item(ctx, "enemy", Side.Enemy);
            Assert.True(potion.Card.Tags.Contains("Potion"));
            potion.Ammo = 0;
            Trigger(ctx, "onSlow", potion, enemy);
            Assert.Equal(0, potion.Ammo);
            Trigger(ctx, "onSlow", friend, friend);
            Assert.Equal(0, potion.Ammo);
            Trigger(ctx, "onSlow", enemy, potion);
            Assert.Equal(0, potion.Ammo);
            Trigger(ctx, "onSlow", friend, potion);
            Assert.Equal(1, potion.Ammo);
        }

        private static void AviatorsRequiresFriendlyBurnOrDestruction()
        {
            BattleConfig config = Config();
            Add(config, NativeItemMapper.Aviators, 0, "Bronze", Side.Player);
            Add(config, NativeItemMapper.Firefly, 1, "Silver", Side.Player);
            Add(config, "enemy", 0, "Bronze", Side.Enemy);
            NativeItemMapper.Apply(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem glasses = Item(ctx, NativeItemMapper.Aviators, Side.Player);
            InternalItem firefly = Item(ctx, NativeItemMapper.Firefly, Side.Player);
            Trigger(ctx, "onBurnApplied", firefly, null);
            Assert.Near(4, glasses.Cooldown);
            Trigger(ctx, "onBurnApplied", Item(ctx, "enemy", Side.Enemy), null);
            Assert.Near(4, glasses.Cooldown);
            Trigger(ctx, "onDestroy", firefly, null);
            Assert.Near(3, glasses.Cooldown);
            Trigger(ctx, "onDestroy", Item(ctx, "enemy", Side.Enemy), null);
            Assert.Near(3, glasses.Cooldown);
        }

        private static void MappingDoesNotMutateLegacyCatalog()
        {
            BattleConfig config = Config();
            CardDef original = Add(config, NativeItemMapper.Drowsdrop, 0, "Bronze", Side.Player);
            config.NativeMechanics = false;
            Assert.Equal(0, NativeItemMapper.Apply(config).Count);
            Assert.True(object.ReferenceEquals(original, config.Cards[original.Id]));
            config.NativeMechanics = true;
            original.LiveAttributes["Flying"] = 1; // A mid-replay state must not become an opening buff.
            NativeItemMapper.Apply(config);
            Assert.False(original.Tags.Contains("Potion"));
            Assert.True(config.Cards[original.Id].Tags.Contains("Potion"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.False(ctx.Items[0].Flying);
            Add(config, "unverified-item", 1, "Bronze", Side.Player);
            Assert.Equal(1, NativeItemMapper.Apply(config).Count);
        }

        private static void NativePassivesWaitForFollowingFrame()
        {
            BattleConfig config = Config();
            Add(config, NativeItemMapper.Firefly, 0, "Silver", Side.Player);
            CardDef generator = Add(config, "generator", 1, "Bronze", Side.Player);
            TriggerDef onUse = new TriggerDef { Event = "onUse", Filter = new TriggerFilter { SourceItemId = generator.Id } };
            onUse.Effects.Add(new Effect { Kind = "haste", Seconds = 1, Target = new Target { Kind = "selfItem" } });
            generator.Triggers.Add(onUse);
            NativeItemMapper.Apply(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem firefly = Item(ctx, NativeItemMapper.Firefly, Side.Player);
            InternalItem source = Item(ctx, "generator", Side.Player);
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = source.Id });
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.Near(7, firefly.Cooldown);
            Assert.Equal(1, ctx.TriggerQueue.Count);
            Triggers.DrainNativeFrame(ctx, 1.05);
            Assert.Near(6, firefly.Cooldown);
            Assert.Equal(0, ctx.TriggerQueue.Count);
            BattleEvent last = ctx.Events[ctx.Events.Count - 1];
            Assert.Equal("charge", last.Kind);
            Assert.Near(1.05, last.Time);
        }

        private static void MidReplayFlyingResistanceDoesNotLeakIntoOpening()
        {
            CardCatalog catalog = CardCatalog.Load("src/data/cards.json");
            LiveCardEntry snapshot = new LiveCardEntry {
                TemplateId = NativeItemMapper.Firefly, Tier = "Silver", Size = "Small", Socket = 0
            };
            // These three values were captured together in the middle of combat.
            // Neither the temporary flight nor its derived resistances are opening stats.
            snapshot.Attributes["Flying"] = 1;
            snapshot.Attributes["PercentSlowReduction"] = 50;
            snapshot.Attributes["PercentFreezeReduction"] = 50;
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { snapshot }, catalog, 10000, 10000, 30);
            mapped.Config.NativeMechanics = true;
            Assert.Equal(0, NativeItemMapper.Apply(mapped.Config).Count);
            Add(mapped.Config, "slow-freeze-enemy", 0, "Bronze", Side.Enemy);
            BattleContext ctx = BattleEngine.InitBattle(mapped.Config);
            InternalItem target = Item(ctx, NativeItemMapper.Firefly, Side.Player);
            InternalItem enemy = Item(ctx, "slow-freeze-enemy", Side.Enemy);
            Assert.False(target.Flying);
            Assert.Near(0, target.Card.PercentSlowReduction);
            Assert.Near(0, target.Card.PercentFreezeReduction);
            Assert.Equal(50, target.Card.LiveAttributes["PercentSlowReduction"]); // Diagnostic data is preserved.
            Effects.ResolveEffect(ctx, new Effect { Kind = "slow", Seconds = 4,
                Target = new Target { Kind = "enemyItem", ItemId = target.Id } }, enemy, 0, 0, null);
            Effects.ResolveEffect(ctx, new Effect { Kind = "freeze", Seconds = 4,
                Target = new Target { Kind = "enemyItem", ItemId = target.Id } }, enemy, 0, 0, null);
            Assert.Near(4, target.SlowUntil);
            Assert.Near(4, target.FrozenUntil);
            Effects.ResolveEffect(ctx, new Effect { Kind = "fly", Count = 1,
                Target = new Target { Kind = "selfItem" } }, target, 5, 0, null);
            Effects.ResolveEffect(ctx, new Effect { Kind = "slow", Seconds = 4,
                Target = new Target { Kind = "enemyItem", ItemId = target.Id } }, enemy, 6, 0, null);
            Effects.ResolveEffect(ctx, new Effect { Kind = "freeze", Seconds = 4,
                Target = new Target { Kind = "enemyItem", ItemId = target.Id } }, enemy, 6, 0, null);
            Assert.Near(8, target.SlowUntil);
            Assert.Near(8, target.FrozenUntil);

            // Known permanent/catalog resistances remain intact; do not subtract
            // a blanket 50 from every resistance merely because Flying was read.
            CardDef permanent = catalog.Find(NativeItemMapper.Firefly, "Silver").Clone();
            permanent.PercentSlowReduction = 20;
            permanent.PercentFreezeReduction = 100;
            CardCatalog permanentCatalog = CardCatalog.FromCards(new List<CardDef> { permanent });
            mapped = CardMapper.Build(new List<LiveCardEntry> { snapshot }, permanentCatalog, 10000, 10000, 30);
            mapped.Config.NativeMechanics = true;
            Assert.Equal(1, NativeItemMapper.Apply(mapped.Config).Count);
            CardDef preserved = mapped.Config.Cards[mapped.Config.Player.Items[0].CardId];
            Assert.Near(20, preserved.PercentSlowReduction);
            Assert.Near(100, preserved.PercentFreezeReduction);
            Assert.Near(20, permanent.PercentSlowReduction);
            snapshot.Attributes["PercentSlowReduction"] = 70;
            mapped = CardMapper.Build(new List<LiveCardEntry> { snapshot }, catalog, 10000, 10000, 30);
            mapped.Config.NativeMechanics = true;
            Assert.Equal(1, NativeItemMapper.Apply(mapped.Config).Count);
            Assert.Near(0, mapped.Config.Cards[mapped.Config.Player.Items[0].CardId].PercentSlowReduction);
        }
    }
}
