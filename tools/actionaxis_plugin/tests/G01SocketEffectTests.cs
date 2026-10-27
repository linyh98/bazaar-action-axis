using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G01: TTargetCardOccupying resolves the item in the socket-effect owner's slot.
    // The two native rows are [Stove]/[Cooler] Socket Effect. No slot context => fail closed.
    public static class G01SocketEffectTests
    {
        private static JsonValue _data;

        public static void Run()
        {
            _data = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_all_hands.json"));
            ActiveEffectHitsOccupyingItemOnly();
            StaticAuraHitsOccupyingItem();
            DynamicAuraHitsOccupyingItem();
            NumericReferencesHitOccupyingItem();
            PassiveSubjectAndTargetUseOwnerSlot();
            MissingSocketDefinitionFailsClosed();
        }

        private static void ActiveEffectHitsOccupyingItemOnly()
        {
            BattleConfig config = Board("[Stove] Socket Effect", 3);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem socket = Find(ctx, "g01:socket"), item = Find(ctx, "g01:socket-item"), other = Find(ctx, "g01:socket-other");

            Target target = SkillMapper.MapCardTarget(Json.Parse("{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}"));
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Heated", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = target, Count = 1 }, socket, 0, 0, null);
            Assert.Near(1, ItemAttributes.Get(item, "Heated"));
            Assert.Near(0, ItemAttributes.Get(other, "Heated"));
        }

        private static void StaticAuraHitsOccupyingItem()
        {
            BattleConfig config = Board("[Stove] Socket Effect", 3);
            CardDef socket = config.Cards[IdOf(config, "g01:socket")];
            CardDef item = config.Cards[IdOf(config, "g01:socket-item")];
            JsonValue aura = _data["[Stove] Socket Effect"]["template"]["Auras"]["0"];
            NativeCardAbilityMapper.ApplyStaticAuras(config, socket, Side.Player,
                new Dictionary<string, double> { { "Custom_0", 1 } }, new[] { aura }, false);
            int heated;
            config.Cards[item.Id].LiveAttributes.TryGetValue("Heated", out heated);
            Assert.Near(1, heated);
        }

        private static void DynamicAuraHitsOccupyingItem()
        {
            BattleConfig config = Board("[Cooler] Socket Effect", 3);
            CardDef socket = config.Cards[IdOf(config, "g01:socket")];
            socket.NativeRuntimeAuras.Add(_data["[Cooler] Socket Effect"]["template"]["Auras"]["0"]);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(1, ItemAttributes.Get(Find(ctx, "g01:socket-item"), "Chilled"));
            Assert.Near(0, ItemAttributes.Get(Find(ctx, "g01:socket-other"), "Chilled"));
        }

        private static void NumericReferencesHitOccupyingItem()
        {
            BattleConfig config = Board("[Stove] Socket Effect", 3);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem socket = Find(ctx, "g01:socket");
            NumExpr count = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardCount\",\"Target\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}}"), new Dictionary<string, double>(), null);
            Assert.Near(1, Effects.EvaluateNum(count, ctx, socket, ctx.Player));
            NumExpr damage = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"DamageAmount\",\"Target\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}}"), new Dictionary<string, double>(), null);
            Assert.Near(30, Effects.EvaluateNum(damage, ctx, socket, ctx.Player));
        }

        private static void PassiveSubjectAndTargetUseOwnerSlot()
        {
            BattleConfig config = Board("[Stove] Socket Effect", 3);
            TriggerDef subject = SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}}"));
            subject.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 1 } });
            config.Cards[IdOf(config, "g01:socket")].Triggers = new List<TriggerDef> { subject };
            BattleContext ctx = BattleEngine.InitBattle(config);
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = Find(ctx, "g01:socket-item").Id });
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = Find(ctx, "g01:socket-other").Id });
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.Near(1, ctx.Player.Shield);

            BattleConfig targetConfig = Board("[Stove] Socket Effect", 3);
            TriggerDef target = SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnCardPerformedSlow\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Target\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}}"));
            target.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 1 } });
            targetConfig.Cards[IdOf(targetConfig, "g01:socket")].Triggers = new List<TriggerDef> { target };
            BattleContext targetCtx = BattleEngine.InitBattle(targetConfig);
            InternalItem owner = Find(targetCtx, "g01:socket");
            targetCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onSlow", SourceSide = Side.Player, SourceItemId = owner.Id,
                TargetSide = Side.Player, TargetItemId = Find(targetCtx, "g01:socket-item").Id });
            targetCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onSlow", SourceSide = Side.Player, SourceItemId = owner.Id,
                TargetSide = Side.Player, TargetItemId = Find(targetCtx, "g01:socket-other").Id });
            Triggers.DrainNativeFrame(targetCtx, 1);
            Assert.Near(1, targetCtx.Player.Shield);

            NativeSkillRule rule = new NativeSkillRule { Prerequisites = Json.Parse("[{\"$type\":\"TPrerequisiteCardCount\",\"Subject\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null},\"Comparison\":\"Equal\",\"Amount\":1}]") };
            Assert.True(NativeSkillSupport.PrerequisitesMatch(ctx, owner, rule));
        }

        private static void MissingSocketDefinitionFailsClosed()
        {
            BattleConfig config = Board("[Stove] Socket Effect", 3);
            CardDef source = config.Cards[IdOf(config, "g01:socket")];
            source.NativeTemplate = JsonValue.Null;
            BattleContext ctx = BattleEngine.InitBattle(config);
            Target target = SkillMapper.MapCardTarget(Json.Parse("{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}"));
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "Heated", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = target, Count = 1 }, Find(ctx, "g01:socket"), 0, 0, null);
            Assert.Near(0, ItemAttributes.Get(Find(ctx, "g01:socket-item"), "Heated"));
        }

        private static BattleConfig Board(string socketName, int socketSlot)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            JsonValue socketTemplate = _data[socketName]["template"];
            CardDef socket = new CardDef {
                Id = "g01:socket", BaseItemId = "g01:socket", Name = socketName,
                Tier = "Diamond", Size = "Small", Cooldown = 0, Passive = true, NativeAttributesAreLive = false,
                NativeTemplate = socketTemplate
            };
            foreach (KeyValuePair<string, int> attr in MonsterTemplateExtractor.TierAttributes(socketTemplate, "Diamond"))
                socket.LiveAttributes[attr.Key] = attr.Value;
            JsonValue itemTemplate = Json.Parse("{\"$type\":\"TCardItem\",\"InternalName\":\"g01:socket-item\",\"Size\":\"Small\",\"Tags\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CooldownMax\":5000,\"DamageAmount\":30}}}}");
            CardDef item = new CardDef { Id = "g01:socket-item", BaseItemId = "g01:socket-item", Name = "socket item",
                Tier = "Bronze", Size = "Small", Cooldown = 5, NativeAttributesAreLive = false, NativeTemplate = itemTemplate };
            item.LiveAttributes["CooldownMax"] = 5000;
            item.LiveAttributes["DamageAmount"] = 30;
            CardDef other = new CardDef { Id = "g01:socket-other", BaseItemId = "g01:socket-other", Name = "other",
                Tier = "Bronze", Size = "Small", Cooldown = 5, NativeAttributesAreLive = false, NativeTemplate = itemTemplate };
            other.LiveAttributes["CooldownMax"] = 5000;
            other.LiveAttributes["DamageAmount"] = 30;
            config.Cards[socket.Id] = socket;
            config.Cards[item.Id] = item;
            config.Cards[other.Id] = other;
            config.Player.Items.Add(new LoadoutItem { CardId = socket.Id, Slot = socketSlot });
            config.Player.Items.Add(new LoadoutItem { CardId = item.Id, Slot = socketSlot });
            config.Player.Items.Add(new LoadoutItem { CardId = other.Id, Slot = socketSlot + 1 });
            return config;
        }

        private static string IdOf(BattleConfig config, string baseItemId)
        {
            foreach (KeyValuePair<string, CardDef> pair in config.Cards)
                if (pair.Value.BaseItemId == baseItemId || pair.Value.Id == baseItemId) return pair.Key;
            throw new ArgumentException("棋盘缺少 " + baseItemId);
        }

        private static InternalItem Find(BattleContext ctx, string baseItemId)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.Card.BaseItemId == baseItemId; });
        }
    }
}
