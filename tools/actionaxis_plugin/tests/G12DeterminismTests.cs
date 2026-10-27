using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G12 确定性残余的两条原生规则（回放 170666bf 实证）：
    // 1. 同一（充能者,目标）的 CardCharge 串行结算，最小间隔 250ms；
    //    同帧两次使用只立即结算一次，第二次落在 上次结算+0.25s。
    //    全语料 27,629 次充能中相邻同对间隔 1-4 帧为 0 次。
    // 2. WorksIn=CombatOnly 的 SellPrice Add 光环在战斗开始时执行一次，此后不再
    //    叠加（02dae1ed：Abacus 帧0 SellPrice 29→39，其后 5 次开火恒为 39）；
    //    引用其写入属性的同源 Add 光环按“新贡献-旧贡献”增量重算一次
    //    （Heirlooms 结构：开场 SellPrice 1→6、ShieldApplyAmount 2→12）。旧模型
    //    “每次使用叠加”来自已过期的模板数据（现模板把该效果提取为 OnCardFired 能力）。
    public static class G12DeterminismTests
    {
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            SameFrameUsesSerializeChargeSettlements();
            CombatOnlySellPriceAuraAppliesAtCombatStart();
            ImmediateOnUsePassivesSettleInUseFrame();
        }

        // 回放 ebde2082：技能对 TTriggerOnItemUsed 的双能力按 EffectPriority
        // 分帧——Immediate 的 modifyAttribute 与被用物品自身效果同帧（76），
        // Medium 的 Haste/Burn 下一帧（77）；前提按使用帧状态一次判定。
        private static void ImmediateOnUsePassivesSettleInUseFrame()
        {
            const string skillAbilities = "{\"0\":{\"Id\":\"0\",\"Trigger\":{\"$type\":\"TTriggerOnItemUsed\"," +
                "\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":true,\"Conditions\":null}}," +
                "\"ActiveIn\":\"HandOnly\",\"Action\":{\"$type\":\"TActionCardHaste\",\"Value\":null,\"TargetCount\":null," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}},\"Prerequisites\":null,\"Priority\":\"Medium\"," +
                "\"InternalName\":\"g12 medium\",\"InternalDescription\":\"\"}," +
                "\"1\":{\"Id\":\"1\",\"Trigger\":{\"$type\":\"TTriggerOnItemUsed\"," +
                "\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":true,\"Conditions\":null}}," +
                "\"ActiveIn\":\"HandOnly\",\"Action\":{\"$type\":\"TActionCardModifyAttribute\"," +
                "\"Value\":{\"$type\":\"TFixedValue\",\"Value\":1.0},\"AttributeType\":\"Custom_1\",\"Operation\":\"Add\"," +
                "\"Duration\":null,\"TargetCount\":null,\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}," +
                "\"Prerequisites\":null,\"Priority\":\"Immediate\",\"InternalName\":\"g12 immediate\",\"InternalDescription\":\"\"}}";
            JsonValue user = Template("g12-user", "{\"CooldownMax\":4000,\"Multicast\":1}", "{}", "{}", "", "");
            JsonValue skill = Json.Parse("{\"$type\":\"TCardSkill\",\"InternalName\":\"g12 split skill\",\"Size\":\"Small\"," +
                "\"Tags\":[],\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"HasteAmount\":2000,\"HasteTargets\":1},\"AbilityIds\":[\"0\",\"1\"],\"AuraIds\":[],\"TooltipIds\":[]}}," +
                "\"Abilities\":" + skillAbilities + ",\"Auras\":{}}");

            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>
            {
                Entry("g12-user", 0, "Bronze", user, BaseLive(4000))
            }, EmptyCatalog, 100000, 100000, 5.0);
            BattleConfig config = mapped.Config;
            config.NativeMechanics = true;
            Map(config);
            // 技能与其监听的 SelfHand 使用者同侧（ebde2082：怪物技能 + 怪物手牌）。
            LoadoutItem entry = config.Player.Items[0];
            config.Player.Items.Remove(entry);
            config.Enemy.Items.Add(entry);
            SkillMappingResult skillResult = SkillMapper.Apply(config, new List<LiveSkillEntry>
            {
                new LiveSkillEntry { TemplateId = "g12-skill", Template = skill, Tier = "Bronze", Name = "g12 split skill" }
            }, null, Side.Enemy);
            Assert.Equal("", string.Join(" | ", skillResult.Warnings.ToArray()));

            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string userId = IdOf(config, "g12-user");
            List<BattleEvent> modifies = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "modifyAttribute" && ev.SourceItemId != null && ev.SourceItemId != userId;
            });
            List<BattleEvent> hastes = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "haste" && ev.SourceItemId != null && ev.SourceItemId != userId;
            });
            Assert.Equal(1, modifies.Count);
            Assert.Equal(1, hastes.Count);
            // 使用帧 = 80（冷却 4000ms 从帧 0 推进）：Immediate 同帧，Medium 下一帧。
            Assert.Near(4.0, modifies[0].Time);
            Assert.Near(4.05, hastes[0].Time);
        }

        private static LiveCardEntry Entry(string id, int slot, string tier, JsonValue template, Dictionary<string, int> liveAttributes)
        {
            return new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = tier, Socket = slot,
                Size = "Small", AttributesAreLive = true, Attributes = liveAttributes
            };
        }

        private static JsonValue Template(string internalName, string tierAttributes, string abilities, string auras, string abilityIds, string auraIds)
        {
            return Json.Parse("{\"$type\":\"TCardItem\",\"InternalName\":\"" + internalName + "\",\"Size\":\"Small\"," +
                "\"Tags\":[],\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":" + tierAttributes +
                ",\"AbilityIds\":[" + abilityIds + "],\"AuraIds\":[" + auraIds + "],\"TooltipIds\":[]}}," +
                "\"Abilities\":" + abilities + ",\"Auras\":" + auras + "}");
        }

        private static Dictionary<string, int> BaseLive(int cooldownMax)
        {
            Dictionary<string, int> attrs = new Dictionary<string, int>();
            attrs["Cooldown"] = cooldownMax;
            attrs["CooldownMax"] = cooldownMax;
            attrs["Multicast"] = 1;
            return attrs;
        }

        private static string IdOf(BattleConfig config, string baseItemId)
        {
            foreach (KeyValuePair<string, CardDef> pair in config.Cards)
                if (pair.Value.BaseItemId == baseItemId) return pair.Key;
            throw new ArgumentException("棋盘缺少 " + baseItemId);
        }

        private static void Map(BattleConfig config)
        {
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
        }

        // 两件 4s 冷却物品同帧使用：火柴盒型监听者的两次充能分别落在
        // t=4.05 与 t=4.30，第二次充能施加在开火重置后的新冷却上。
        private static void SameFrameUsesSerializeChargeSettlements()
        {
            const string chargeAbility = "{\"1\":{\"Id\":\"1\",\"Trigger\":{\"$type\":\"TTriggerOnItemUsed\"," +
                "\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":true,\"Conditions\":null}}," +
                "\"ActiveIn\":\"HandOnly\",\"Action\":{\"$type\":\"TActionCardCharge\",\"Value\":null,\"TargetCount\":null," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}},\"Prerequisites\":null,\"Priority\":\"Medium\"," +
                "\"InternalName\":\"g12 charge\",\"InternalDescription\":\"\"}}";
            JsonValue matchbox = Template("g12-matchbox",
                "{\"CooldownMax\":5000,\"Multicast\":1,\"ChargeAmount\":1000,\"ChargeTargets\":1}",
                chargeAbility, "{}", "\"1\"", "");
            JsonValue user = Template("g12-user",
                "{\"CooldownMax\":4000,\"Multicast\":1}", "{}", "{}", "", "");

            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>
            {
                Entry("g12-matchbox", 0, "Bronze", matchbox, BaseLive(5000)),
                Entry("g12-user", 1, "Bronze", user, BaseLive(4000)),
                Entry("g12-user", 2, "Bronze", user, BaseLive(4000))
            }, EmptyCatalog, 100000, 100000, 5.5);
            BattleConfig config = mapped.Config;
            config.NativeMechanics = true;
            Map(config);

            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string boxId = IdOf(config, "g12-matchbox");
            List<BattleEvent> charges = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "charge" && ev.TargetItemId == boxId;
            });
            Assert.Equal(2, charges.Count);
            Assert.Near(4.05, charges[0].Time);
            Assert.Near(4.30, charges[1].Time);
            // 第一次充能把 950ms 冷却压到 0：同帧重置、下一帧出手，
            // 第二次充能施加在新冷却 4750ms 上 -> 3750ms。
            List<BattleEvent> activates = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "activate" && ev.SourceItemId == boxId;
            });
            Assert.Equal(1, activates.Count);
            Assert.Near(4.1, activates[0].Time);
            ItemSnapshot frame85 = Snapshot(result, boxId, 4.25);
            ItemSnapshot frame86 = Snapshot(result, boxId, 4.3);
            if (frame85 != null) Assert.Near(4800, frame85.Attributes["Cooldown"]);
            Assert.Near(3750, frame86.Attributes["Cooldown"]);
        }

        private static ItemSnapshot Snapshot(BattleResult result, string itemId, double time)
        {
            return result.ItemSnapshots.Find(delegate(ItemSnapshot snapshot)
            {
                return snapshot.ItemId == itemId && Math.Abs(snapshot.Time - time) < 1e-9;
            });
        }

        // Heirlooms 结构：光环 2（CombatOnly）在战斗开始时 SellPrice +Custom_1；
        // 光环 1 读取 SellPrice×Custom_0，按贡献增量重算 ShieldApplyAmount。
        // 开场一次后两个光环都不再叠加（原生 Abacus/Heirlooms 证据）。
        private static void CombatOnlySellPriceAuraAppliesAtCombatStart()
        {
            const string abilities = "{\"0\":{\"Id\":\"0\",\"Trigger\":{\"$type\":\"TTriggerOnCardFired\"}," +
                "\"ActiveIn\":\"HandOnly\",\"Action\":{\"$type\":\"TActionPlayerShieldApply\",\"ReferenceValue\":null," +
                "\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\",\"Conditions\":null}}," +
                "\"Prerequisites\":null,\"Priority\":\"Medium\",\"InternalName\":\"g12 shield\",\"InternalDescription\":\"\"}}";
            const string auras = "{\"1\":{\"Id\":\"1\",\"ActiveIn\":\"HandAndStash\",\"WorksIn\":\"Anywhere\",\"Prerequisites\":null," +
                "\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"ShieldApplyAmount\",\"Operation\":\"Add\"," +
                "\"Value\":{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"SellPrice\"," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"DefaultValue\":0," +
                "\"Modifier\":{\"ModifyMode\":\"Multiply\",\"Value\":{\"$type\":\"TReferenceValueCardAttribute\"," +
                "\"AttributeType\":\"Custom_0\",\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}," +
                "\"DefaultValue\":0,\"Modifier\":null},\"ShouldRound\":true}}," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}}," +
                "\"2\":{\"Id\":\"2\",\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"CombatOnly\",\"Prerequisites\":null," +
                "\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"SellPrice\",\"Operation\":\"Add\"," +
                "\"Value\":{\"$type\":\"TReferenceValueCardAttribute\",\"AttributeType\":\"Custom_1\"," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null},\"DefaultValue\":0,\"Modifier\":null}," +
                "\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}}}";
            JsonValue heirlooms = Template("g12-heirlooms",
                "{\"CooldownMax\":4000,\"Multicast\":1,\"ShieldApplyAmount\":0,\"Custom_0\":2,\"Custom_1\":10}",
                abilities, auras, "\"0\"", "\"1\",\"2\"");
            Dictionary<string, int> live = BaseLive(4000);
            live["ShieldApplyAmount"] = 2;
            live["Custom_0"] = 2;
            live["Custom_1"] = 5;
            live["SellPrice"] = 1;

            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>
            {
                Entry("g12-heirlooms", 0, "Bronze", heirlooms, live)
            }, EmptyCatalog, 100000, 100000, 16.5);
            BattleConfig config = mapped.Config;
            config.NativeMechanics = true;
            Map(config);

            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string id = IdOf(config, "g12-heirlooms");
            List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "shield" && ev.SourceItemId == id;
            });
            Assert.Equal(4, shields.Count);
            // 开场光环只执行一次：4 次开火的护盾都来自开场重算后的 12。
            foreach (BattleEvent shield in shields) Assert.Near(12, shield.Value.Value);
            foreach (double time in new[] { 0.0, 4.0, 8.0, 12.0, 16.0 })
            {
                ItemSnapshot snapshot = Snapshot(result, id, time);
                Assert.True(snapshot != null);
                if (snapshot == null) continue;
                // 开场 SellPrice 1→6 后恒定，不再逐次累加。
                Assert.Near(6, snapshot.Attributes["SellPrice"]);
                Assert.Near(12, snapshot.Attributes["ShieldApplyAmount"]);
            }
        }
    }
}
