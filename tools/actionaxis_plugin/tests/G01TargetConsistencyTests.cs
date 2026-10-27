using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G01：区域/位置目标必须共享同一集合。AllHands 是双方手牌并集，
    // Absolute* 固定到玩家/对手一侧，Positional 只沿原点同侧且最近邻
    // 不能越过不满足条件的牌。主动、被动、光环、数值引用都走同一契约。
    public static class G01TargetConsistencyTests
    {
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            AllHandsPathsAgree();
            AllHandsDynamicAuraAgrees();
            AbsoluteSectionsUseFixedSide();
            AbsoluteSectionsWorkForEnemyOwners();
            AbsoluteXMostUsesFixedSide();
            XMostAuraUsesSameTargetSet();
            PositionalPathsAgree();
            NeighborDoesNotCrossIneligibleCard();
            OccupyingContractIsShared();
        }

        // ---- AllHands ----

        private static void AllHandsPathsAgree()
        {
            BattleConfig config = Board(Fake("g01:source", 0, 5000, "[]"),
                Fake("g01:ally-a", 1, 5000, "[\"Tech\"]"), Fake("g01:ally-b", 2, 0, "[]"),
                Fake("g01:enemy-a", 0, 5000, "[]"), Fake("g01:enemy-b", 1, 7000, "[\"Tech\"]", "Large"));
            MoveToEnemy(config, "g01:enemy-a");
            MoveToEnemy(config, "g01:enemy-b");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, Side.Player, 0);

            string all = Section("AllHands", false, null);
            string other = Section("AllHands", true, null);
            string tech = Section("AllHands", false, Tag("Tech"));
            string otherTech = Section("AllHands", true, Tag("Tech"));

            AssertIds(new[] { "g01:source", "g01:ally-a", "g01:ally-b", "g01:enemy-a", "g01:enemy-b" },
                ActiveTargets(ctx, source, all, 0));
            AssertIds(new[] { "g01:ally-a", "g01:ally-b", "g01:enemy-a", "g01:enemy-b" },
                ActiveTargets(ctx, source, other, 0));
            AssertIds(new[] { "g01:ally-a", "g01:enemy-b" }, ActiveTargets(ctx, source, tech, 0));
            AssertIds(new[] { "g01:ally-a", "g01:enemy-b" }, ActiveTargets(ctx, source, otherTech, 0));

            Assert.Near(5, EvaluateCount(ctx, source, all));
            Assert.Near(4, EvaluateCount(ctx, source, other));
            Assert.Near(2, EvaluateTagCount(ctx, source, all));
            Assert.Near(50, EvaluateAggregate(ctx, source, all));

            // 被动主体 AllHands：自己、友方、敌方物品的使用都应命中。
            BattleConfig passive = Board(Fake("g01:source", 0, 5000, "[]"),
                Fake("g01:ally", 1, 5000, "[]"), Fake("g01:enemy", 0, 5000, "[]"));
            MoveToEnemy(passive, "g01:enemy");
            AttachTrigger(passive, "g01:source", "{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + other + "}");
            BattleContext passiveCtx = BattleEngine.InitBattle(passive);
            foreach (InternalItem item in new List<InternalItem>(passiveCtx.Items))
                passiveCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id });
            Triggers.DrainNativeFrame(passiveCtx, 1);
            Assert.Near(2, passiveCtx.Player.Shield);

            // 静态光环 AllHands：双方 Tech 都加 40 暴击，非 Tech 不变。
            string aura = Aura("CritChance", "Add", "40", tech);
            BattleConfig auraConfig = RawBoard(AuraFake("g01:aura", 0, "[\"Tech\"]", aura),
                Fake("g01:tech-ally", 1, 5000, "[\"Tech\"]"), Fake("g01:plain-ally", 2, 5000, "[]"),
                Fake("g01:tech-enemy", 0, 5000, "[\"Tech\"]"));
            MoveToEnemy(auraConfig, "g01:tech-enemy");
            Map(auraConfig);
            BattleContext auraCtx = BattleEngine.InitBattle(auraConfig);
            Assert.Near(0.4, At(auraCtx, Side.Player, 0).CritBonus);
            Assert.Near(0.4, At(auraCtx, Side.Player, 1).CritBonus);
            Assert.Near(0, At(auraCtx, Side.Player, 2).CritBonus);
            Assert.Near(0.4, At(auraCtx, Side.Enemy, 0).CritBonus);
        }

        private static void AllHandsDynamicAuraAgrees()
        {
            BattleConfig config = RawBoard(Fake("g01:dynamic-source", 0, 5000, "[]"),
                Fake("g01:dynamic-ally", 1, 5000, "[]"), Fake("g01:dynamic-enemy", 0, 5000, "[]"));
            MoveToEnemy(config, "g01:dynamic-enemy");
            CardDef source = config.Cards[IdOf(config, "g01:dynamic-source")];
            source.NativeRuntimeAuras.Add(Json.Parse("{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Prerequisites\":null," +
                "\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\"," +
                "\"Value\":{\"$type\":\"TFixedValue\",\"Value\":5},\"Target\":" + Section("AllHands", false, null) + "}}"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(15, ItemAttributes.Get(At(ctx, Side.Player, 0), "DamageAmount"));
            Assert.Near(15, ItemAttributes.Get(At(ctx, Side.Player, 1), "DamageAmount"));
            Assert.Near(15, ItemAttributes.Get(At(ctx, Side.Enemy, 0), "DamageAmount"));
        }

        // ---- Absolute sections ----

        private static void AbsoluteSectionsUseFixedSide()
        {
            BattleConfig config = Board(Fake("g01:absolute-source", 5, 5000, "[]"),
                Fake("g01:player-a", 0, 5000, "[]"), Fake("g01:player-b", 3, 5000, "[]"),
                Fake("g01:enemy-a", 0, 5000, "[\"Tech\"]"), Fake("g01:enemy-b", 2, 5000, "[\"Tech\"]"));
            MoveToEnemy(config, "g01:enemy-a");
            MoveToEnemy(config, "g01:enemy-b");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, Side.Player, 5);

            string playerHand = Section("AbsolutePlayerHand", true, null);
            string opponentHand = Section("AbsoluteOpponentHand", false, null);
            AssertIds(new[] { "g01:player-a", "g01:player-b" }, ActiveTargets(ctx, source, playerHand, 0));
            AssertIds(new[] { "g01:enemy-a", "g01:enemy-b" }, ActiveTargets(ctx, source, opponentHand, 0));
            Assert.Near(2, EvaluateCount(ctx, source, playerHand));
            Assert.Near(2, EvaluateTagCount(ctx, source, opponentHand));
            Assert.Near(20, EvaluateAggregate(ctx, source, opponentHand));

            // 被动主体固定侧：玩家来源的 AbsoluteOpponentHand 只响应敌方使用。
            BattleConfig passive = Board(Fake("g01:absolute-source", 0, 5000, "[]"),
                Fake("g01:player-ally", 1, 5000, "[]"), Fake("g01:enemy-ally", 0, 5000, "[]"));
            MoveToEnemy(passive, "g01:enemy-ally");
            AttachTrigger(passive, "g01:absolute-source", "{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + opponentHand + "}");
            BattleContext passiveCtx = BattleEngine.InitBattle(passive);
            foreach (InternalItem item in new List<InternalItem>(passiveCtx.Items))
                passiveCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id });
            Triggers.DrainNativeFrame(passiveCtx, 1);
            Assert.Near(1, passiveCtx.Player.Shield);

            // 被动 Target 固定侧：玩家来源只接受玩家手牌作为行动受体。
            BattleConfig target = Board(Fake("g01:absolute-source", 0, 5000, "[]"),
                Fake("g01:player-target", 1, 5000, "[]"), Fake("g01:enemy-target", 0, 5000, "[]"));
            MoveToEnemy(target, "g01:enemy-target");
            AttachTrigger(target, "g01:absolute-source",
                "{\"$type\":\"TTriggerOnCardPerformedSlow\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Target\":" + playerHand + "}");
            BattleContext targetCtx = BattleEngine.InitBattle(target);
            InternalItem owner = At(targetCtx, Side.Player, 0);
            InternalItem playerTarget = At(targetCtx, Side.Player, 1), enemyTarget = At(targetCtx, Side.Enemy, 0);
            targetCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onSlow", SourceSide = Side.Player, SourceItemId = owner.Id, TargetSide = Side.Player, TargetItemId = playerTarget.Id });
            targetCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onSlow", SourceSide = Side.Player, SourceItemId = owner.Id, TargetSide = Side.Enemy, TargetItemId = enemyTarget.Id });
            Triggers.DrainNativeFrame(targetCtx, 1);
            Assert.Near(1, targetCtx.Player.Shield);

            // 静态光环 AbsoluteOpponentHand：玩家来源只作用于敌方。
            string aura = Aura("DamageAmount", "Add", "5", opponentHand);
            BattleConfig auraConfig = RawBoard(AuraFake("g01:absolute-aura", 0, "[]", aura),
                Fake("g01:aura-ally", 1, 5000, "[]"), Fake("g01:aura-enemy", 0, 5000, "[]"));
            MoveToEnemy(auraConfig, "g01:aura-enemy");
            Map(auraConfig);
            BattleContext auraCtx = BattleEngine.InitBattle(auraConfig);
            Assert.Near(10, ItemAttributes.Get(At(auraCtx, Side.Player, 1), "DamageAmount"));
            Assert.Near(15, ItemAttributes.Get(At(auraCtx, Side.Enemy, 0), "DamageAmount"));

            // 相对 OpponentHand 随拥有者翻转，不能误加到自己一侧。
            string relativeOpponent = Section("OpponentHand", false, null);
            BattleConfig relative = RawBoard(AuraFake("g01:relative-aura", 0, "[]", Aura("DamageAmount", "Add", "5", relativeOpponent)),
                Fake("g01:relative-ally", 1, 5000, "[]"), Fake("g01:relative-enemy", 0, 5000, "[]"));
            MoveToEnemy(relative, "g01:relative-enemy");
            Map(relative);
            BattleContext relativeCtx = BattleEngine.InitBattle(relative);
            Assert.Near(10, ItemAttributes.Get(At(relativeCtx, Side.Player, 1), "DamageAmount"));
            Assert.Near(15, ItemAttributes.Get(At(relativeCtx, Side.Enemy, 0), "DamageAmount"));

            // 前提计数 AbsolutePlayerHand：以敌方卡为拥有者时仍统计玩家手牌。
            NativeSkillRule sideRule = new NativeSkillRule { Prerequisites = Json.Parse("[" + Prereq(playerHand, "Equal", 3) + "]") };
            Assert.True(NativeSkillSupport.PrerequisitesMatch(ctx, At(ctx, Side.Enemy, 0), sideRule));
        }

        private static void AbsoluteSectionsWorkForEnemyOwners()
        {
            BattleConfig config = Board(Fake("g01:enemy-source", 5, 5000, "[]"),
                Fake("g01:player-a", 0, 5000, "[]"), Fake("g01:enemy-a", 0, 5000, "[]"));
            MoveToEnemy(config, "g01:enemy-source");
            MoveToEnemy(config, "g01:enemy-a");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, Side.Enemy, 5);

            string playerHand = Section("AbsolutePlayerHand", false, null);
            AssertIds(new[] { "g01:player-a" }, ActiveTargets(ctx, source, playerHand, 0));
            Assert.Near(1, EvaluateCount(ctx, source, playerHand));

            // 敌方拥有的 AbsolutePlayerHand 被动只响应玩家物品。
            BattleConfig passive = Board(Fake("g01:enemy-source", 2, 5000, "[]"),
                Fake("g01:player-ally", 1, 5000, "[]"), Fake("g01:enemy-ally", 0, 5000, "[]"));
            MoveToEnemy(passive, "g01:enemy-source");
            MoveToEnemy(passive, "g01:enemy-ally");
            AttachTrigger(passive, "g01:enemy-source", "{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + playerHand + "}");
            BattleContext passiveCtx = BattleEngine.InitBattle(passive);
            foreach (InternalItem item in new List<InternalItem>(passiveCtx.Items))
                passiveCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id });
            Triggers.DrainNativeFrame(passiveCtx, 1);
            Assert.Near(1, passiveCtx.Enemy.Shield);
        }

        private static void AbsoluteXMostUsesFixedSide()
        {
            BattleConfig config = Board(Fake("g01:xmost-source", 5, 5000, "[]"),
                Fake("g01:player-left", 0, 5000, "[]"), Fake("g01:player-mid", 3, 5000, "[]"),
                Fake("g01:enemy-left", 0, 5000, "[]"));
            MoveToEnemy(config, "g01:enemy-left");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, Side.Player, 5);
            string leftMostPlayer = "{\"$type\":\"TTargetCardXMost\",\"TargetSection\":\"AbsolutePlayerHand\",\"TargetMode\":\"LeftMostCard\",\"Conditions\":null}";
            AssertIds(new[] { "g01:player-left" }, ActiveTargets(ctx, source, leftMostPlayer, 1));
        }

        private static void XMostAuraUsesSameTargetSet()
        {
            string leftMostSelf = "{\"$type\":\"TTargetCardXMost\",\"TargetSection\":\"SelfHand\",\"TargetMode\":\"LeftMostCard\"," +
                "\"ExcludeSelf\":true,\"Conditions\":null}";
            BattleConfig config = RawBoard(AuraFake("g01:xmost-aura", 0, "[]", Aura("DamageAmount", "Add", "5", leftMostSelf)),
                Fake("g01:xmost-left", 1, 5000, "[]"), Fake("g01:xmost-right", 3, 5000, "[]"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(10, ItemAttributes.Get(At(ctx, Side.Player, 0), "DamageAmount"));
            Assert.Near(15, ItemAttributes.Get(At(ctx, Side.Player, 1), "DamageAmount"));
            Assert.Near(10, ItemAttributes.Get(At(ctx, Side.Player, 3), "DamageAmount"));
            Assert.Near(1, EvaluateCount(ctx, At(ctx, Side.Player, 0), leftMostSelf));
            Assert.Near(15, EvaluateAggregate(ctx, At(ctx, Side.Player, 0), leftMostSelf));

            BattleConfig dynamic = RawBoard(Fake("g01:xmost-dynamic", 0, 5000, "[]"),
                Fake("g01:xmost-dynamic-left", 1, 5000, "[]"), Fake("g01:xmost-dynamic-right", 3, 5000, "[]"));
            CardDef source = dynamic.Cards[IdOf(dynamic, "g01:xmost-dynamic")];
            source.NativeRuntimeAuras.Add(Json.Parse(Aura("DamageAmount", "Add", "5", leftMostSelf)));
            BattleContext dynamicCtx = BattleEngine.InitBattle(dynamic);
            Assert.Near(15, ItemAttributes.Get(At(dynamicCtx, Side.Player, 1), "DamageAmount"));
            Assert.Near(10, ItemAttributes.Get(At(dynamicCtx, Side.Player, 3), "DamageAmount"));
        }

        // ---- Positional ----

        private static void PositionalPathsAgree()
        {
            BattleConfig config = Board(Fake("g01:position-source", 5, 5000, "[]"),
                Fake("g01:left-far", 0, 5000, "[\"Tech\"]"), Fake("g01:left-near", 3, 5000, "[\"Tech\"]"),
                Fake("g01:right-near", 7, 5000, "[]"), Fake("g01:right-far", 9, 5000, "[]"),
                Fake("g01:enemy-right", 8, 5000, "[]"));
            MoveToEnemy(config, "g01:enemy-right");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, Side.Player, 5);

            string right = Position("AllRightCards", true, "Self");
            string left = Position("AllLeftCards", false, "Self");
            AssertIds(new[] { "g01:position-source", "g01:right-near", "g01:right-far" },
                ActiveTargets(ctx, source, right, 0));
            AssertIds(new[] { "g01:left-far", "g01:left-near" }, ActiveTargets(ctx, source, left, 0));
            Assert.Near(3, EvaluateCount(ctx, source, right));
            Assert.Near(2, EvaluateTagCount(ctx, source, left));
            Assert.Near(30, EvaluateAggregate(ctx, source, right));

            // 被动主体与主动目标同集合：右侧含自身的三张牌使用都命中。
            BattleConfig passive = Board(Fake("g01:position-source", 5, 5000, "[]"),
                Fake("g01:right-near", 7, 5000, "[]"), Fake("g01:right-far", 9, 5000, "[]"),
                Fake("g01:left-near", 3, 5000, "[]"));
            AttachTrigger(passive, "g01:position-source", "{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + right + "}");
            BattleContext passiveCtx = BattleEngine.InitBattle(passive);
            foreach (InternalItem item in new List<InternalItem>(passiveCtx.Items))
                passiveCtx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id });
            Triggers.DrainNativeFrame(passiveCtx, 1);
            Assert.Near(3, passiveCtx.Player.Shield);

            // 光环 AllRightCards + IncludeOrigin 作用于同集合。
            string aura = Aura("DamageAmount", "Add", "5", right);
            BattleConfig auraConfig = RawBoard(AuraFake("g01:position-aura", 5, "[]", aura),
                Fake("g01:aura-right", 7, 5000, "[]"), Fake("g01:aura-left", 3, 5000, "[]"));
            Map(auraConfig);
            BattleContext auraCtx = BattleEngine.InitBattle(auraConfig);
            Assert.Near(15, ItemAttributes.Get(At(auraCtx, Side.Player, 5), "DamageAmount"));
            Assert.Near(15, ItemAttributes.Get(At(auraCtx, Side.Player, 7), "DamageAmount"));
            Assert.Near(10, ItemAttributes.Get(At(auraCtx, Side.Player, 3), "DamageAmount"));

            // TriggerSource 原点：行动来源在 5 时，位置计数仍以触发来源 5 为原点。
            InternalItem triggerSource = At(ctx, Side.Player, 5);
            NumExpr expr = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardAttributeAggregate\",\"AttributeType\":\"DamageAmount\",\"Target\":" +
                Position("AllRightCards", true, "TriggerSource") + "}"), new Dictionary<string, double>(), null);
            Assert.Near(30, Effects.EvaluateNum(expr, ctx, source, ctx.Player, triggerSource.Id));
        }

        private static void NeighborDoesNotCrossIneligibleCard()
        {
            BattleConfig config = Board(Fake("g01:neighbor-source", 5, 5000, "[]"),
                Fake("g01:neighbor-large", 6, 5000, "[]", "Large"), Fake("g01:neighbor-small", 7, 5000, "[]"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, Side.Player, 5);
            string neighborSmall = "{\"$type\":\"TTargetCardPositional\",\"Origin\":\"Self\",\"TargetMode\":\"Neighbor\"," +
                "\"IncludeOrigin\":false,\"Conditions\":" + Size("Small") + "}";
            Assert.Equal(0, ActiveTargets(ctx, source, neighborSmall, 0).Count);
            Assert.Near(0, EvaluateCount(ctx, source, neighborSmall));

            // 无尺寸条件时最近邻就是 Large，不能跳到下一张 Small。
            string neighbor = Position("Neighbor", false, "Self");
            AssertIds(new[] { "g01:neighbor-large" }, ActiveTargets(ctx, source, neighbor, 0));
        }

        // ---- Occupying ----

        private static void OccupyingContractIsShared()
        {
            string occupying = "{\"$type\":\"TTargetCardOccupying\",\"Conditions\":null}";
            Assert.Equal("occupyingItem", SkillMapper.MapCardTarget(Json.Parse(occupying)).Kind);
            Assert.True(SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":" + occupying + "}")).SkillRule.SourceOccupying);
            Assert.True(SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnCardPerformedSlow\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Target\":" + occupying + "}")).SkillRule.TargetOccupying);
            Assert.True(SkillMapper.MapTrigger(Json.Parse("{\"$type\":\"TTriggerOnCardTransformed\",\"Subject\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"Source\":" + occupying + "}")).SkillRule.ActorOccupying);
            Assert.True(SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardCount\",\"Target\":" + occupying + "}"), new Dictionary<string, double>(), null) != null);
            Assert.True(SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"DamageAmount\",\"Target\":" + occupying + "}"), new Dictionary<string, double>(), null) != null);
            Assert.True(SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardTagCount\",\"Target\":" + occupying + "}"), new Dictionary<string, double>(), null) != null);
            Assert.True(SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardAttributeAggregate\",\"AttributeType\":\"DamageAmount\",\"Target\":" + occupying + "}"), new Dictionary<string, double>(), null) != null);
            Assert.True(NativeSkillSupport.PrerequisitesSupported(Json.Parse("[" + Prereq(occupying, "Equal", 1) + "]")));
            NativeDynamicAuras.Validate(Json.Parse("{\"ActiveIn\":\"HandOnly\",\"Prerequisites\":null," +
                "\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\"," +
                "\"Value\":{\"$type\":\"TFixedValue\",\"Value\":1},\"Target\":" + occupying + "}}"));
        }

        // ---- helpers ----

        private static string Section(string section, bool excludeSelf, string condition)
        {
            return "{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"" + section + "\",\"ExcludeSelf\":" +
                (excludeSelf ? "true" : "false") + ",\"Conditions\":" + (condition ?? "null") + "}";
        }

        private static string Position(string mode, bool includeOrigin, string origin)
        {
            return "{\"$type\":\"TTargetCardPositional\",\"Origin\":\"" + origin + "\",\"TargetMode\":\"" + mode +
                "\",\"IncludeOrigin\":" + (includeOrigin ? "true" : "false") + ",\"Conditions\":null}";
        }

        private static string Tag(string tag)
        {
            return "{\"$type\":\"TCardConditionalTag\",\"Tags\":[\"" + tag + "\"],\"Operator\":\"Any\"}";
        }

        private static string Size(string size)
        {
            return "{\"$type\":\"TCardConditionalSize\",\"Sizes\":[\"" + size + "\"],\"IsNot\":false}";
        }

        private static string Aura(string attribute, string operation, string value, string target)
        {
            return "{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Prerequisites\":null," +
                "\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"" + attribute +
                "\",\"Operation\":\"" + operation + "\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":" + value +
                "},\"Target\":" + target + "}}";
        }

        private static string Prereq(string target, string comparison, int amount)
        {
            return "{\"$type\":\"TPrerequisiteCardCount\",\"Subject\":" + target + ",\"Comparison\":\"" +
                comparison + "\",\"Amount\":" + amount + "}";
        }

        private static List<string> ActiveTargets(BattleContext ctx, InternalItem source, string target, int count)
        {
            Target mapped = SkillMapper.MapCardTarget(Json.Parse(target));
            int useCount = count > 0 ? count :
                mapped.Kind != null && mapped.Kind.StartsWith("all", StringComparison.Ordinal) ? int.MaxValue : 1;
            ctx.Events.Clear();
            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "modifyAttribute", Attribute = "Custom_probe", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = mapped, Count = useCount
            }, source, 0, 0, null);
            List<string> ids = new List<string>();
            foreach (BattleEvent ev in ctx.Events)
            {
                if (ev.Kind != "modifyAttribute" || ev.Note != "Custom_probe") continue;
                InternalItem targetItem = ctx.Items.Find(delegate(InternalItem item) { return item.Id == ev.TargetItemId; });
                string id = targetItem == null ? ev.TargetItemId : targetItem.Card.BaseItemId ?? targetItem.Id;
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        private static double EvaluateCount(BattleContext ctx, InternalItem source, string target)
        {
            NumExpr expr = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardCount\",\"Target\":" + target + "}"),
                new Dictionary<string, double>(), null);
            return Effects.EvaluateNum(expr, ctx, source, ctx.Player);
        }

        private static double EvaluateTagCount(BattleContext ctx, InternalItem source, string target)
        {
            NumExpr expr = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardTagCount\",\"Target\":" + target + "}"),
                new Dictionary<string, double>(), null);
            return Effects.EvaluateNum(expr, ctx, source, ctx.Player);
        }

        private static double EvaluateAggregate(BattleContext ctx, InternalItem source, string target)
        {
            NumExpr expr = SkillMapper.MapValue(Json.Parse("{\"$type\":\"TReferenceValueCardAttributeAggregate\",\"AttributeType\":\"DamageAmount\",\"Target\":" + target + "}"),
                new Dictionary<string, double>(), null);
            return Effects.EvaluateNum(expr, ctx, source, ctx.Player);
        }

        private static void AssertIds(string[] expected, List<string> actual)
        {
            if (expected.Length != actual.Count || !actual.TrueForAll(delegate(string id) { return Array.IndexOf(expected, id) >= 0; }))
                Console.WriteLine("  target ids <" + string.Join(",", actual.ToArray()) + ">");
            Assert.Equal(expected.Length, actual.Count);
            foreach (string id in expected) Assert.True(actual.Contains(id));
        }

        private static void AttachTrigger(BattleConfig config, string baseItemId, string trigger)
        {
            CardDef card = config.Cards[IdOf(config, baseItemId)];
            TriggerDef mapped = SkillMapper.MapTrigger(Json.Parse(trigger));
            mapped.Effects.Add(new Effect { Kind = "shield", Amount = new NumExpr { Value = 1 } });
            card.Triggers = new List<TriggerDef> { mapped };
        }

        private static BattleConfig Board(params LiveCardEntry[] cards)
        {
            return RawBoard(cards);
        }

        private static BattleConfig RawBoard(params LiveCardEntry[] cards)
        {
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, 30);
            mapped.Config.NativeMechanics = true;
            return mapped.Config;
        }

        private static void Map(BattleConfig config)
        {
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            warnings.AddRange(NativeItemMapper.Apply(config));
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
        }

        private static LiveCardEntry Fake(string id, int slot, int cooldownMs, string tags, string size = "Small")
        {
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"" + size + "\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CooldownMax\":" + cooldownMs +
                ",\"DamageAmount\":10},\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = slot, Size = size,
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
        }

        private static LiveCardEntry AuraFake(string id, int slot, string tags, string aura)
        {
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"Small\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Gold\":{\"Attributes\":{\"CooldownMax\":6000,\"DamageAmount\":10}," +
                "\"AbilityIds\":[],\"AuraIds\":[\"0\"],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{\"0\":" + aura + "}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = "Gold", Socket = slot, Size = "Small",
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold")
            };
        }

        private static void MoveToEnemy(BattleConfig config, string baseItemId)
        {
            string id = IdOf(config, baseItemId);
            LoadoutItem entry = config.Player.Items.Find(delegate(LoadoutItem item) { return item.CardId == id; });
            config.Player.Items.Remove(entry);
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
    }
}
