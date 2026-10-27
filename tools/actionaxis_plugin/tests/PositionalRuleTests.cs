using System;
using System.IO;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class PositionalRuleTests
    {
        private static JsonValue _data;
        public static void Run()
        {
            _data = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_weaponized_core.json"));
            WeaponizedCoreWorksOnBothSidesAndTiers();
            LeftUseMovesActualActivationEarlier();
            PositionCountsAndOrigins();
            PositionTriggersAndAuraAgree();
            RealNeighborGrowthHitsEveryEligibleItem();
            GoldenCopiesKeepPriceAura();
            SparseTierCritAuraDoesNotThrow();
            UnsupportedOriginStillFailsClosed();
        }

        private static LiveCardEntry Entry(string name, int slot, string tier, string enchant = null)
        {
            JsonValue row = _data[name], template = row["template"];
            LiveCardEntry entry = new LiveCardEntry { TemplateId = row["id"].AsString(), Template = template, Socket = slot,
                Tier = tier, Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier), Enchantment = enchant };
            if (enchant != null)
            {
                entry.EnchantmentDefinition = template["Enchantments"][enchant];
                foreach (KeyValuePair<string, JsonValue> attr in entry.EnchantmentDefinition["Attributes"].AsObject())
                    entry.Attributes[attr.Key] = attr.Value.AsInt();
            }
            return entry;
        }

        private static BattleConfig Board(Side side, params LiveCardEntry[] entries)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(entries), CardCatalog.FromCards(new List<CardDef>()), 100000, 100000, 10).Config;
            config.NativeMechanics = true;
            if (side == Side.Enemy) { config.Enemy = config.Player; config.Player = new Loadout(); }
            return config;
        }

        private static CardDef Peer(BattleConfig config, Side side, int slot, bool weapon = true)
        {
            CardDef card = new CardDef { Id = "peer:" + side + ":" + slot, Passive = true, NativeAttributesAreLive = false };
            if (weapon) card.Tags.Add("Weapon");
            card.Tags.AddRange(new[] { "Poison", "Burn" });
            card.HiddenTags.AddRange(new[] { "Poison", "Burn" });
            card.LiveAttributes["DamageAmount"] = 10;
            card.LiveAttributes["PoisonApplyAmount"] = 10;
            card.LiveAttributes["BurnApplyAmount"] = 10;
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
            return card;
        }

        private static void Map(BattleConfig config)
        {
            Assert.Equal("", string.Join(" | ", NativeCardAbilityMapper.Apply(config).ToArray()));
        }

        private static InternalItem At(BattleContext ctx, Side side, int slot)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.Side == side && item.Slot == slot; });
        }

        private static BattleConfig CoreBoard(string tier, Side side)
        {
            BattleConfig config = Board(side, Entry("Weaponized Core", 3, tier));
            Peer(config, side, 0, false);
            Peer(config, side, 1, true);
            Peer(config, side, 5, false);
            Peer(config, side, 7, true);
            Peer(config, side, 9, true);
            Peer(config, SideUtil.Opposing(side), 0, true);
            Map(config);
            return config;
        }

        private static void WeaponizedCoreWorksOnBothSidesAndTiers()
        {
            foreach (string tier in new[] { "Silver", "Gold", "Diamond", "Legendary" })
                foreach (Side side in new[] { Side.Player, Side.Enemy })
                {
                    BattleContext ctx = BattleEngine.InitBattle(CoreBoard(tier, side));
                    InternalItem core = At(ctx, side, 3);
                    double gain = core.Card.LiveAttributes["Custom_0"];
                    for (int cast = 1; cast <= 2; cast++)
                    {
                        BattleEngine.ForceUse(ctx, core, cast, 0);
                        Assert.Near(20 + gain * cast, ItemAttributes.Get(core, "DamageAmount"));
                        Assert.Near(10 + gain * cast, ItemAttributes.Get(At(ctx, side, 7), "DamageAmount"));
                        Assert.Near(10 + gain * cast, ItemAttributes.Get(At(ctx, side, 9), "DamageAmount"));
                        Assert.Near(10, ItemAttributes.Get(At(ctx, side, 1), "DamageAmount"));
                        Assert.Near(10, ItemAttributes.Get(At(ctx, side, 5), "DamageAmount"));
                    }
                    core.Cooldown = 6;
                    foreach (InternalItem source in new[] { At(ctx, side, 0), At(ctx, side, 1), core, At(ctx, side, 9), At(ctx, SideUtil.Opposing(side), 0) })
                        ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = source.Side, SourceItemId = source.Id });
                    Triggers.DrainNativeFrame(ctx, 3);
                    // 原生同一（充能者,目标）的充能 250ms 串行：同帧两次使用只
                    // 立即结算一次，第二次延后到 上次结算+0.25s。
                    Assert.Near(5, core.Cooldown);
                    Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "charge"; }).Count);
                    Assert.Equal(1, ctx.DeferredCharges.Count);
                }
        }

        private static void LeftUseMovesActualActivationEarlier()
        {
            BattleConfig config = CoreBoard("Gold", Side.Player);
            config.MaxTime = 4.2;
            CardDef left = config.Cards["peer:Player:0"];
            left.Passive = false;
            left.Cooldown = 2;
            left.LiveAttributes["CooldownMax"] = 2000;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string core = config.Player.Items.Find(delegate(LoadoutItem item) { return item.Slot == 3; }).CardId;
            List<BattleEvent> casts = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "activate" && ev.SourceItemId == core; });
            Assert.Equal(1, casts.Count);
            if (casts.Count > 0) Assert.Near(4.1, casts[0].Time);
            Assert.Equal(2, result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "charge" && ev.TargetItemId == core; }).Count);
        }

        private static string Position(string mode, bool include, string origin = "Self")
        {
            return "{\"$type\":\"TTargetCardPositional\",\"Origin\":\"" + origin + "\",\"TargetMode\":\"" + mode +
                "\",\"IncludeOrigin\":" + (include ? "true" : "false") + ",\"Conditions\":null}";
        }

        private static void PositionCountsAndOrigins()
        {
            BattleContext ctx = BattleEngine.InitBattle(CoreBoard("Gold", Side.Player));
            InternalItem source = At(ctx, Side.Player, 3), origin = At(ctx, Side.Player, 7);
            foreach (string mode in new[] { "AllLeftCards", "AllRightCards", "LeftCard", "RightCard", "Neighbor" })
                foreach (bool include in new[] { false, true })
                {
                    string raw = Position(mode, include, "TriggerSource");
                    NumExpr count = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardCount\",\"Target\":" + raw + "}"), new Dictionary<string, double>(), null);
                    int expected = mode == "AllLeftCards" ? 4 : mode == "Neighbor" ? 2 : 1;
                    if (include) expected++;
                    Assert.Near(expected, Effects.EvaluateNum(count, ctx, source, ctx.Player, origin.Id));
                    Target target = SkillMapper.MapCardTarget(Json.Parse(raw));
                    Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Custom_test", Operation = "Add",
                        Amount = new NumExpr { Value = 1 }, Target = target, Count = int.MaxValue }, source, 0, 0, null, origin.Id);
                    int changed = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute" && ev.Note == "Custom_test"; }).Count;
                    Assert.Equal(expected, changed);
                    ctx.Events.Clear();
                }
            At(ctx, Side.Player, 5).Destroyed = true;
            Assert.True(NativeSkillSupport.PositionalMatch(ctx, source, origin, "RightCard"));
            Assert.False(NativeSkillSupport.PositionalMatch(ctx, source, At(ctx, Side.Enemy, 0), "AllLeftCards"));
        }

        private static void PositionTriggersAndAuraAgree()
        {
            foreach (string mode in new[] { "AllLeftCards", "AllRightCards" })
            {
                BattleConfig config = CoreBoard("Gold", Side.Player);
                string coreId = config.Player.Items.Find(delegate(LoadoutItem item) { return item.Slot == 3; }).CardId;
                CardDef card = config.Cards[coreId];
                card.NativeRuntimeAuras.Add(Json.Parse("{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":5},\"Target\":" + Position(mode, true) + "}}"));
                TriggerDef trigger = SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + Position(mode, true) + "}"));
                trigger.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 1 } });
                card.Triggers = new List<TriggerDef> { trigger };
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem core = At(ctx, Side.Player, 3);
                int matched = 0;
                foreach (InternalItem item in ctx.Items)
                {
                    bool match = NativeSkillSupport.PositionalMatch(ctx, core, item, mode, true);
                    Assert.Near((item == core ? 20 : 10) + (match ? 5 : 0), ItemAttributes.Get(item, "DamageAmount"));
                    if (match) matched++;
                    ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id });
                }
                Triggers.Drain(ctx, 1);
                Assert.Near(matched, ctx.Player.Shield);
            }
        }

        private static void RealNeighborGrowthHitsEveryEligibleItem()
        {
            foreach (string name in new[] { "Gamma Ray", "Omega Ray", "Kusarigama" })
            {
                BattleConfig config = Board(Side.Player, Entry(name, 3, "Silver"));
                Peer(config, Side.Player, 0);
                Peer(config, Side.Player, 1);
                Peer(config, Side.Player, 6);
                Peer(config, Side.Player, 9);
                Map(config);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem source = At(ctx, Side.Player, 3);
                TriggerDef trigger = source.Card.Triggers.Find(delegate(TriggerDef item)
                    { return item.Effects.Exists(delegate(Effect effect) { return effect.Kind == "modifyAttribute"; }); });
                Assert.True(trigger != null);
                if (trigger == null) continue;
                InternalItem actor = At(ctx, Side.Player, 0);
                actor.Card.Tags.Add("Core");
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = trigger.Event, SourceSide = Side.Player, SourceItemId = actor.Id });
                Triggers.DrainNativeFrame(ctx, 1);
                List<BattleEvent> events = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute"; });
                Assert.Equal(3, events.Count);
                Assert.True(events.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == source.Id; }));
                Assert.True(events.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == At(ctx, Side.Player, 1).Id; }));
                Assert.True(events.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == At(ctx, Side.Player, 6).Id; }));
            }
        }

        private static void GoldenCopiesKeepPriceAura()
        {
            foreach (bool mirror in new[] { false, true })
            {
                int slot = mirror ? 2 : 1;
                BattleConfig config = Board(Side.Player, Entry(mirror ? "Launcher Core" : "Cog", 0, "Gold"),
                    Entry(mirror ? "Mirror" : "3D Printer", slot, "Gold", "Golden"));
                Map(config);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem donor = At(ctx, Side.Player, 0), copier = At(ctx, Side.Player, slot);
                double price = ItemAttributes.Get(donor, "SellPrice");
                BattleEngine.ForceUse(ctx, copier, 1, 0);
                BattleEngine.FlushPendingItems(ctx);
                Assert.Equal("", string.Join(" | ", ctx.Warnings.ToArray()));
                copier = ctx.Items.Find(delegate(InternalItem item) { return !item.Destroyed && item.Side == Side.Player && item.Slot == slot; });
                Assert.Equal("Golden", copier.Card.EnchantmentName);
                Assert.Equal(donor.CardId, copier.CardId);
                // 原生变形副本按副本母版的尺寸+品质重算买卖价（07f044ee：Gold Mirror
                // 复制 Diamond The Big One → 16/8），不再沿用供体实时价；Golden 的
                // SellPrice ×2 光环仍叠在其上。测试供体没有价格字段（0）。
                int baseSell = mirror ? 8 : 4; // Medium/Gold 8，Small/Gold 4
                Assert.Near(baseSell * 2, ItemAttributes.Get(copier, "SellPrice"));
                Assert.Near(price, ItemAttributes.Get(donor, "SellPrice"));
            }
        }

        private static void SparseTierCritAuraDoesNotThrow()
        {
            foreach (string tier in new[] { "Silver", "Gold", "Diamond", "Legendary" })
            {
                BattleConfig config = Board(Side.Player, Entry("Gamma Ray", 0, tier, "Deadly"));
                Map(config);
                BattleContext ctx = BattleEngine.InitBattle(config);
                Assert.Near(0.25, ctx.Items[0].CritBonus);
                Assert.Equal(tier, ctx.Items[0].Card.Tier);
            }
        }

        private static void UnsupportedOriginStillFailsClosed()
        {
            Assert.Throws<FormatException>(delegate { SkillMapper.MapCardTarget(Json.Parse(Position("AllLeftCards", true, "UnknownOrigin"))); });
            Assert.Throws<FormatException>(delegate { SkillMapper.MapCardTarget(Json.Parse(Position("UnknownMode", true))); });
        }
    }
}
