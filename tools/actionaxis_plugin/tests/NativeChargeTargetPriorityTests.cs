using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 原生 ChargePriorities（BazaarGameShared.Conditionals 静态字段）：
    //   Charge            = [HasCooldown && (NotAnAmmoItem || UsesAndHasAmmo), HasCooldown]
    //   Haste/Slow/Freeze = [HasCooldown, HasNoCooldown]
    // 目标数量受限时先按优先级分层过滤，任一层命中就只在该层里选，全部落空才回落。
    // 光纤（“当你使用己方最左侧物品，充能己方最右侧物品”）因此跳过被动物品：
    // 充能没有冷却的卡不会缩短任何冷却。
    public static class NativeChargeTargetPriorityTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText(
                "tools/actionaxis_plugin/tests/fixtures/native_live_fiber_board.json"));
            RightmostPassiveIsSkipped();
            FallbackChargesPassiveWhenNothingActive();
            EmptyAmmoDropsBelowReadyAmmo();
            UnlimitedChargeKeepsEveryTarget();
            LiveBoardHighlightsActiveRightmost();
        }

        private static JsonValue Template(string name) { return _fixtures[name]["template"]; }

        private static string TemplateId(string name) { return _fixtures[name]["id"].AsString(); }

        private static LiveCardEntry Live(string name, int? socketOverride = null)
        {
            JsonValue row = _fixtures[name];
            JsonValue live = row["live"];
            JsonValue attributes = live["attributes"];
            LiveCardEntry entry = new LiveCardEntry();
            entry.TemplateId = row["id"].AsString();
            entry.Template = row["template"];
            entry.Tier = live["tier"].AsString();
            entry.Size = live["size"].AsString();
            entry.Socket = socketOverride.HasValue ? socketOverride.Value : live["socket"].AsInt();
            entry.AttributesAreLive = true;
            entry.Attributes = new Dictionary<string, int>();
            foreach (KeyValuePair<string, JsonValue> pair in attributes.AsObject())
                entry.Attributes[pair.Key] = (int)pair.Value.AsDouble();
            return entry;
        }

        private static LiveCardEntry Fake(string id, int socket, int cooldownMs, int ammoMax = 0)
        {
            string attributes = "{\"CooldownMax\":" + cooldownMs;
            if (ammoMax > 0) attributes += ",\"AmmoMax\":" + ammoMax;
            attributes += "}";
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"Small\",\"Tags\":[]," +
                "\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":" + attributes +
                ",\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = socket, Size = "Small",
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
        }

        private static BattleConfig Board(params LiveCardEntry[] cards)
        {
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, 1);
            mapped.Config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(mapped.Config);
            warnings.AddRange(NativeItemMapper.Apply(mapped.Config));
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return mapped.Config;
        }

        private static string IdOf(BattleConfig config, string baseItemId)
        {
            foreach (KeyValuePair<string, CardDef> pair in config.Cards)
                if (pair.Value.BaseItemId == baseItemId) return pair.Key;
            throw new ArgumentException("棋盘缺少 " + baseItemId);
        }

        private static InternalItem Item(BattleContext ctx, string uniqueId)
        {
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.Id == uniqueId; });
            if (item == null) throw new ArgumentException("战场缺少 " + uniqueId);
            return item;
        }

        private static TriggerPayload UsePayload(InternalItem source)
        {
            return new TriggerPayload
            {
                Event = "onUse", SourceSide = source.Side, SourceItemId = source.Id,
                TargetSide = source.Side, ChainId = 1, Depth = 0
            };
        }

        private static List<BattleEvent> Charges(BattleContext ctx, InternalItem source)
        {
            return ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.SourceItemId == source.Id; });
        }

        private static void RightmostPassiveIsSkipped()
        {
            BattleConfig config = Board(Fake("leftmost-active", 0, 2000), Fake("second-active", 2, 5000),
                Live("Fiber Optics", 4), Fake("rightmost-passive", 6, 0));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem leftmost = Item(ctx, IdOf(config, "leftmost-active"));
            InternalItem second = Item(ctx, IdOf(config, "second-active"));
            InternalItem fiber = Item(ctx, IdOf(config, TemplateId("Fiber Optics")));

            Triggers.Enqueue(ctx, UsePayload(leftmost));
            Triggers.DrainNativeFrame(ctx, 1.0);

            List<BattleEvent> charges = Charges(ctx, fiber);
            Assert.Equal(1, charges.Count);
            Assert.Equal(second.Id, charges[0].TargetItemId);
            Assert.Near(2.0, charges[0].Value.Value);
        }

        private static void FallbackChargesPassiveWhenNothingActive()
        {
            BattleConfig config = Board(Fake("leftmost-active", 0, 2000), Live("Fiber Optics", 2),
                Fake("passive-a", 4, 0), Fake("passive-b", 6, 0));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem leftmost = Item(ctx, IdOf(config, "leftmost-active"));
            InternalItem passiveB = Item(ctx, IdOf(config, "passive-b"));
            InternalItem fiber = Item(ctx, IdOf(config, TemplateId("Fiber Optics")));

            Triggers.Enqueue(ctx, UsePayload(leftmost));
            Triggers.DrainNativeFrame(ctx, 1.0);

            // 没有可充能目标时原生回落到原始目标集，仍然充能最右侧被动卡。
            List<BattleEvent> charges = Charges(ctx, fiber);
            Assert.Equal(1, charges.Count);
            Assert.Equal(passiveB.Id, charges[0].TargetItemId);
        }

        private static void EmptyAmmoDropsBelowReadyAmmo()
        {
            BattleConfig config = Board(Fake("ammo-empty", 0, 3000, 4), Fake("ammo-ready", 2, 3000, 4),
                Live("Fiber Optics", 4));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem empty = Item(ctx, IdOf(config, "ammo-empty"));
            InternalItem ready = Item(ctx, IdOf(config, "ammo-ready"));
            empty.Ammo = 0;
            ready.Ammo = 4;

            List<InternalItem> pool = new List<InternalItem> { empty, ready };
            List<InternalItem> filtered = Effects.SelectByTargetingPriorities(ctx, pool, "charge", 1);
            Assert.Equal(1, filtered.Count);
            Assert.Equal(ready.Id, filtered[0].Id);

            // 有冷却但没弹药的卡仍留在第二优先级：没有可用弹药目标时它可被充能。
            List<InternalItem> onlyEmpty = new List<InternalItem> { empty };
            List<InternalItem> fallback = Effects.SelectByTargetingPriorities(ctx, onlyEmpty, "charge", 1);
            Assert.Equal(1, fallback.Count);
            Assert.Equal(empty.Id, fallback[0].Id);
        }

        private static void UnlimitedChargeKeepsEveryTarget()
        {
            BattleConfig config = Board(Fake("active", 0, 3000), Fake("passive", 2, 0));
            BattleContext ctx = BattleEngine.InitBattle(config);
            List<InternalItem> pool = new List<InternalItem>(ctx.Items);
            // “充能所有物品”没有目标数量限制，原生不套用优先级过滤（被动卡照样被充能）。
            List<InternalItem> same = Effects.SelectByTargetingPriorities(ctx, pool, "charge", int.MaxValue);
            Assert.Equal(pool.Count, same.Count);
            Assert.True(same.Exists(delegate(InternalItem item) { return item.CooldownMax <= 0; }));
        }

        private static void LiveBoardHighlightsActiveRightmost()
        {
            // 实战棋盘（2026-10-28 16:32 实时快照）：0 冲锋枪、1 气球机器人、3 推进器(被动)、
            // 4 发射核心、6 光纤(被动)、7 特里翼龙。最右为主动卡，使用最左卡后应充能它。
            BattleConfig config = Board(Live("SMG"), Live("Balloon Bot"), Live("Thrusters"),
                Live("Launcher Core"), Live("Fiber Optics"), Live("Terry-Dactyl"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem leftmost = Item(ctx, IdOf(config, TemplateId("SMG")));
            InternalItem terry = Item(ctx, IdOf(config, TemplateId("Terry-Dactyl")));
            InternalItem fiber = Item(ctx, IdOf(config, TemplateId("Fiber Optics")));

            Triggers.Enqueue(ctx, UsePayload(leftmost));
            Triggers.DrainNativeFrame(ctx, 1.0);

            List<BattleEvent> charges = Charges(ctx, fiber);
            Assert.Equal(1, charges.Count);
            Assert.Equal(terry.Id, charges[0].TargetItemId);
        }
    }
}
