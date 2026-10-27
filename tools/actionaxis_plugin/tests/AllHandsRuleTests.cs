using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G01：AllHands 是双方手牌并集，主动、被动、光环、数值查询必须共享同一集合。
    // 槽位占用目标只存在于 TCardSocketEffect，实时棋盘没有该实例，必须 fail-closed。
    public static class AllHandsRuleTests
    {
        private static JsonValue _data;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _data = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_all_hands.json"));
            ActiveTargetsBothSides();
            TriggerSubjectsIncludeEnemyUses();
            AuraBuffsBothSides();
            ValueReferenceCountsBothSides();
            RandomAllHandsFiltersConditions();
            OccupyingTargetsShareContract();
        }

        // Enervating Sigil：暴怒后减速所有冷却大于 0 的物品，敌我两侧都在目标集内。
        private static void ActiveTargetsBothSides()
        {
            BattleConfig config = Board(6, Real("Enervating Sigil", 0, "Silver"),
                Fake("g01:slow-ally", 1, 5000, "[]"), Fake("g01:passive-ally", 2, 0, "[]"),
                Fake("g01:slow-enemy", 0, 5000, "[]"));
            MoveToEnemy(config, "g01:slow-enemy");
            BattleContext ctx = BattleEngine.InitBattle(config);
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onPlayerEnraged", SourceSide = Side.Player });
            Triggers.DrainNativeFrame(ctx, 1);
            List<BattleEvent> slows = Events(ctx, "slow");
            Assert.Equal(2, slows.Count);
            Assert.True(slows.Exists(delegate(BattleEvent ev) { return ev.TargetSide == Side.Player; }));
            Assert.True(slows.Exists(delegate(BattleEvent ev) { return ev.TargetSide == Side.Enemy; }));
            Assert.False(slows.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == At(ctx, Side.Player, 2).Id; }));
            Assert.False(slows.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == At(ctx, Side.Player, 0).Id; }));
        }

        // Wolf Mask：触发主体 AllHands + ExcludeSelf 只接受“其他物品”，
        // 且并集包含敌方物品的使用。自己使用时不能进入目标集。
        private static void TriggerSubjectsIncludeEnemyUses()
        {
            BattleConfig solo = Board(3, Real("Wolf Mask", 0, "Silver"));
            BattleContext soloCtx = BattleEngine.InitBattle(solo);
            BattleEngine.ForceUse(soloCtx, At(soloCtx, Side.Player, 0), 0.1, 0);
            Assert.Near(0, soloCtx.Player.Rage);
            Assert.Equal(0, Events(soloCtx, "rage").Count);

            BattleConfig config = Board(3, Real("Wolf Mask", 0, "Silver"), Fake("g01:enemy", 0, 5000, "[]"));
            MoveToEnemy(config, "g01:enemy");
            BattleContext ctx = BattleEngine.InitBattle(config);
            BattleEngine.ForceUse(ctx, At(ctx, Side.Enemy, 0), 0.1, 0);
            List<BattleEvent> rage = Events(ctx, "rage");
            Assert.Equal(1, rage.Count);
            Assert.Near(30, ctx.Player.Rage);
            Assert.Equal(Side.Player, rage[0].TargetSide);

            BattleConfig allyBoard = Board(3, Real("Wolf Mask", 0, "Silver"), Fake("g01:ally", 1, 5000, "[]"));
            BattleContext allyCtx = BattleEngine.InitBattle(allyBoard);
            BattleEngine.ForceUse(allyCtx, At(allyCtx, Side.Player, 1), 0.1, 0);
            Assert.Near(30, allyCtx.Player.Rage);
        }

        // 原生 AllHands 光环（以 Deadly 的 Tech 暴击光环为契约）同时作用于
        // 自己、友方和敌方 Tech 物品；真实 Trojan 因模板残留 AbilityId 尚未映射。
        private static void AuraBuffsBothSides()
        {
            string aura = "{\"Id\":\"0\",\"ActiveIn\":\"HandOnly\",\"Prerequisites\":null," +
                "\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"CritChance\",\"Operation\":\"Add\"," +
                "\"Value\":{\"$type\":\"TFixedValue\",\"Value\":40},\"Target\":{\"$type\":\"TTargetCardSection\"," +
                "\"TargetSection\":\"AllHands\",\"ExcludeSelf\":false,\"Conditions\":{\"$type\":\"TCardConditionalTag\"," +
                "\"Tags\":[\"Tech\"],\"Operator\":\"Any\"}}}}";
            BattleConfig config = Board(3, AuraFake("g01:aura-source", 0, "[\"Tech\"]", aura),
                Fake("g01:tech-ally", 1, 5000, "[\"Tech\"]"), Fake("g01:plain-ally", 2, 5000, "[]"),
                Fake("g01:tech-enemy", 0, 5000, "[\"Tech\"]"));
            MoveToEnemy(config, "g01:tech-enemy");
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(0.4, At(ctx, Side.Player, 0).CritBonus);
            Assert.Near(0.4, At(ctx, Side.Player, 1).CritBonus);
            Assert.Near(0.4, At(ctx, Side.Enemy, 0).CritBonus);
            Assert.Near(0, At(ctx, Side.Player, 2).CritBonus);
        }

        // TReferenceValueCardCount 的 AllHands 目标与行动目标同口径：双方并集 + 条件过滤。
        private static void ValueReferenceCountsBothSides()
        {
            BattleConfig config = Board(3, Fake("g01:count-ally", 0, 5000, "[]"), Fake("g01:count-passive", 1, 0, "[]"),
                Fake("g01:count-enemy-a", 0, 5000, "[]"), Fake("g01:count-enemy-b", 1, 7000, "[]"));
            MoveToEnemy(config, "g01:count-enemy-a");
            MoveToEnemy(config, "g01:count-enemy-b");
            BattleContext ctx = BattleEngine.InitBattle(config);
            NumExpr all = SkillMapper.MapValue(CountAllHands(false), new Dictionary<string, double>(), null);
            NumExpr other = SkillMapper.MapValue(CountAllHands(true), new Dictionary<string, double>(), null);
            InternalItem source = At(ctx, Side.Player, 0);
            Assert.Near(3, Effects.EvaluateNum(all, ctx, source, ctx.Player));
            Assert.Near(2, Effects.EvaluateNum(other, ctx, source, ctx.Player));
        }

        // Dam：随机/全体 AllHands 目标与 Size 条件同时作用于敌我；大尺寸与自身不进入集合。
        private static void RandomAllHandsFiltersConditions()
        {
            BattleConfig config = Board(30, Real("Dam", 0, "Gold"),
                Fake("g01:small-ally", 1, 5000, "[]"), Fake("g01:large-ally", 2, 5000, "[]", "Large"),
                Fake("g01:small-enemy", 0, 5000, "[]"));
            MoveToEnemy(config, "g01:small-enemy");
            BattleContext ctx = BattleEngine.InitBattle(config);
            BattleEngine.ForceUse(ctx, At(ctx, Side.Player, 0), 0.1, 0);
            List<BattleEvent> disables = Events(ctx, "disable");
            string smallAlly = At(ctx, Side.Player, 1).Id, largeAlly = At(ctx, Side.Player, 2).Id, smallEnemy = At(ctx, Side.Enemy, 0).Id;
            Assert.Equal(3, disables.Count);
            Assert.True(disables.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == smallAlly; }));
            Assert.True(disables.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == smallEnemy; }));
            Assert.True(disables.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == At(ctx, Side.Player, 0).Id; }));
            Assert.False(disables.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == largeAlly; }));
        }

        // 槽位占用目标只由 TCardSocketEffect 拥有；映射入口共享同一契约，
        // 实际命中同槽物品由 G01SocketEffectTests 验证。
        private static void OccupyingTargetsShareContract()
        {
            string[] evidence = { "[Stove] Socket Effect", "[Cooler] Socket Effect" };
            foreach (string name in evidence)
            {
                JsonValue template = _data[name]["template"];
                Assert.Equal("TCardSocketEffect", NativeSkillSupport.Type(template));
                JsonValue target = template["Auras"]["0"]["Action"]["Target"];
                Assert.Equal("TTargetCardOccupying", NativeSkillSupport.Type(target));
            }

            Assert.Equal("occupyingItem", SkillMapper.MapCardTarget(Json.Parse("{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}")).Kind);
            Assert.True(SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}}")).SkillRule.SourceOccupying);
            NativeDynamicAuras.Validate(Json.Parse(
                "{\"ActiveIn\":\"HandOnly\",\"Prerequisites\":null,\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\"," +
                "\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":1}," +
                "\"Target\":{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}}}"));
        }

        private static JsonValue CountAllHands(bool excludeSelf)
        {
            return Json.Parse("{\"$type\":\"TReferenceValueCardCount\",\"Target\":{\"$type\":\"TTargetCardSection\"," +
                "\"TargetSection\":\"AllHands\",\"ExcludeSelf\":" + (excludeSelf ? "true" : "false") +
                ",\"Conditions\":{\"$type\":\"TCardConditionalAttribute\",\"Attribute\":\"CooldownMax\"," +
                "\"ComparisonOperator\":\"GreaterThan\",\"ComparisonValue\":{\"$type\":\"TFixedValue\",\"Value\":0}}}}");
        }

        private static BattleConfig Board(double maxTime, params LiveCardEntry[] cards)
        {
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, maxTime);
            mapped.Config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(mapped.Config);
            warnings.AddRange(NativeItemMapper.Apply(mapped.Config));
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return mapped.Config;
        }

        private static LiveCardEntry Real(string name, int slot, string tier, string enchant = null)
        {
            JsonValue template = _data[name]["template"];
            LiveCardEntry entry = new LiveCardEntry {
                TemplateId = template["Id"].AsString(), Template = template, Socket = slot, Tier = tier,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
            if (enchant != null)
            {
                entry.Enchantment = enchant;
                entry.EnchantmentDefinition = template["Enchantments"][enchant];
                foreach (KeyValuePair<string, JsonValue> attr in entry.EnchantmentDefinition["Attributes"].AsObject())
                    entry.Attributes[attr.Key] = attr.Value.AsInt();
            }
            return entry;
        }

        private static LiveCardEntry Fake(string id, int slot, int cooldownMs, string tags, string size = "Small")
        {
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"" + size + "\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CooldownMax\":" + cooldownMs +
                "},\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = slot, Size = size,
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
        }

        private static LiveCardEntry AuraFake(string id, int slot, string tags, string aura)
        {
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"Small\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Gold\":{\"Attributes\":{\"CooldownMax\":6000}," +
                "\"AbilityIds\":[],\"AuraIds\":[\"0\"],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{\"0\":" + aura + "}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry {
                TemplateId = id, Template = template, Tier = "Gold", Socket = slot, Size = "Small",
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold")
            };
        }

        private static void MoveToEnemy(BattleConfig config, string baseItemId)
        {
            string id = IdOf(config, baseItemId);
            LoadoutItem entry = config.Player.Items.Find(delegate(LoadoutItem item) { return item.CardId == id; });
            config.Player.Items.Remove(entry);
            entry.Slot = 0;
            config.Enemy.Items.Add(entry);
        }

        private static string IdOf(BattleConfig config, string baseItemId)
        {
            foreach (KeyValuePair<string, CardDef> pair in config.Cards)
                if (pair.Value.BaseItemId == baseItemId) return pair.Key;
            throw new ArgumentException("棋盘缺少 " + baseItemId);
        }

        private static InternalItem At(BattleContext ctx, Side side, int slot)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.Side == side && item.Slot == slot; });
        }

        private static List<BattleEvent> Events(BattleContext ctx, string kind)
        {
            return ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == kind; });
        }
    }
}
