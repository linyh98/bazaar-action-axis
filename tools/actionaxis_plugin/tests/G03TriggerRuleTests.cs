using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G03：指定触发来源/行动受体、次数与前提限制、使用前同步与普通被动下一帧。
    // 种子卡使用 native_g03_cards.json 的完整原生模板；辅助卡只提供棋盘位置/标签。
    public static class G03TriggerRuleTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g03_cards.json"));
            HuntingHawkSourceAndRecipientStayExact();
            HuntingHawkActiveHasteFlightsOnFollowingFrame();
            HuntingHawkAuraFiltersFlyingAndCanCrit();
            BoostedSaucerActorFiltersRepairAndDestroy();
            WolfMaskFirstUseEachFight();
            BloodhoundFirstBelowHalfEachFight();
            FrozenSecretsFirstNRelicUses();
            CounterstrikeFirstEnemyWeaponUse();
            RetoolReloadsTriggerSourceNeighborWithAmmo();
            PropellerHatBeforeUseResolvesOnSameFrame();
        }

        private static JsonValue Template(string name)
        {
            JsonValue row = _fixtures[name];
            if (row.Kind != JsonKind.Object) throw new ArgumentException("夹具缺少 " + name);
            return row["template"];
        }

        private static string TemplateId(string name) { return _fixtures[name]["id"].AsString(); }

        private static LiveCardEntry Real(string name, int slot, string tier)
        {
            JsonValue template = Template(name);
            return new LiveCardEntry
            {
                TemplateId = TemplateId(name),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        // 辅助卡不参与规则断言：只提供冷却/标签/弹药，避免把测试棋盘当成原生模板。
        private static LiveCardEntry Fake(string id, int slot, int cooldownMs, string tags, int ammoMax = 0)
        {
            string attrs = "{\"CooldownMax\":" + cooldownMs;
            if (ammoMax > 0) attrs += ",\"AmmoMax\":" + ammoMax;
            attrs += "}";
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"Small\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":" + attrs +
                ",\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = slot, Size = "Small",
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
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

        private static string MapSkill(BattleConfig config, string name, string tier, Side side)
        {
            LiveSkillEntry skill = new LiveSkillEntry
            {
                TemplateId = TemplateId(name), InstanceId = "g03-" + name,
                Name = name, Tier = tier, Template = Template(name), AttributesAreLive = false
            };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, side);
            Assert.Equal(1, mapped.Skills.Count);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            return mapped.Skills[0].SourceId;
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

        private static InternalItem Skill(BattleContext ctx, string sourceId)
        {
            InternalItem item = ctx.Skills.Find(delegate(InternalItem candidate) { return candidate.Id == sourceId; });
            if (item == null) throw new ArgumentException("技能缺少 " + sourceId);
            return item;
        }

        private static void MoveToEnemy(BattleConfig config, string baseItemId)
        {
            string id = IdOf(config, baseItemId);
            LoadoutItem entry = config.Player.Items.Find(delegate(LoadoutItem item) { return item.CardId == id; });
            config.Player.Items.Remove(entry);
            entry.Slot = 0;
            config.Enemy.Items.Add(entry);
        }

        private static TriggerDef AtStart(Effect effect)
        {
            TriggerDef trigger = new TriggerDef { Event = "atCombatStart" };
            trigger.Effects.Add(effect);
            return trigger;
        }

        private static Effect FlySelf()
        {
            return new Effect { Kind = "fly", Count = 1, Target = new Target { Kind = "selfItem" } };
        }

        private static TriggerPayload HastePayload(InternalItem source, InternalItem target)
        {
            return new TriggerPayload
            {
                Event = "onHaste", SourceSide = source.Side, SourceItemId = source.Id,
                TargetSide = target.Side, TargetItemId = target.Id, ChainId = 1, Depth = 0
            };
        }

        private static TriggerPayload UsePayload(InternalItem source)
        {
            return new TriggerPayload
            {
                Event = "onUse", SourceSide = source.Side, SourceItemId = source.Id,
                TargetSide = source.Side, ChainId = 1, Depth = 0
            };
        }

        private static List<BattleEvent> Events(BattleResult result, string kind, string sourceId)
        {
            return result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == kind && ev.SourceItemId == sourceId; });
        }

        private static void HuntingHawkSourceAndRecipientStayExact()
        {
            // 成功路径：同侧来源 + 同侧受体；只允许被 haste 的那张卡起飞。
            BattleConfig config = Board(1, Real("Hunting Hawk", 0, "Bronze"),
                Fake("g03:ally", 1, 5000, "[]"), Fake("g03:bystander", 2, 5000, "[]"),
                Fake("g03:foe", 0, 5000, "[]"));
            MoveToEnemy(config, "g03:foe");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem hawk = Item(ctx, IdOf(config, TemplateId("Hunting Hawk")));
            InternalItem ally = Item(ctx, IdOf(config, "g03:ally"));
            InternalItem bystander = Item(ctx, IdOf(config, "g03:bystander"));
            InternalItem foe = Item(ctx, IdOf(config, "g03:foe"));

            Triggers.Enqueue(ctx, HastePayload(ally, ally));
            Triggers.DrainNativeFrame(ctx, 0);
            List<BattleEvent> flights = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "fly"; });
            Assert.Equal(1, flights.Count);
            Assert.Equal(hawk.Id, flights[0].SourceItemId);
            Assert.Equal(ally.Id, flights[0].TargetItemId);
            Assert.False(flights.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == bystander.Id; }));

            // 敌方来源不能触发“本方棋盘”的限制。
            BattleContext wrongSource = BattleEngine.InitBattle(config);
            InternalItem wrongAlly = Item(wrongSource, IdOf(config, "g03:ally"));
            InternalItem wrongFoe = Item(wrongSource, IdOf(config, "g03:foe"));
            Triggers.Enqueue(wrongSource, HastePayload(wrongFoe, wrongAlly));
            Triggers.DrainNativeFrame(wrongSource, 0);
            Assert.Equal(0, wrongSource.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "fly"; }));

            // 敌方受体不能触发“本方手牌”的限制。
            BattleContext wrongTarget = BattleEngine.InitBattle(config);
            InternalItem targetAlly = Item(wrongTarget, IdOf(config, "g03:ally"));
            InternalItem targetFoe = Item(wrongTarget, IdOf(config, "g03:foe"));
            Triggers.Enqueue(wrongTarget, HastePayload(targetAlly, targetFoe));
            Triggers.DrainNativeFrame(wrongTarget, 0);
            Assert.Equal(0, wrongTarget.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "fly"; }));

            // 行动受体的 CooldownMax 条件在飞行动作结算时复查。
            BattleContext zeroCooldown = BattleEngine.InitBattle(config);
            InternalItem zeroAlly = Item(zeroCooldown, IdOf(config, "g03:ally"));
            ItemAttributes.AddDelta(zeroAlly, "CooldownMax", -5000);
            Triggers.Enqueue(zeroCooldown, HastePayload(zeroAlly, zeroAlly));
            Triggers.DrainNativeFrame(zeroCooldown, 0);
            Assert.Equal(0, zeroCooldown.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "fly"; }));
        }

        private static void HuntingHawkActiveHasteFlightsOnFollowingFrame()
        {
            BattleConfig config = Board(4.3, Real("Hunting Hawk", 0, "Bronze"),
                Fake("g03:prey", 1, 5000, "[]"), Fake("g03:flyer", 2, 5000, "[]"));
            string flyerId = IdOf(config, "g03:flyer");
            config.Cards[flyerId].Triggers.Add(AtStart(FlySelf()));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);

            string hawkId = IdOf(config, TemplateId("Hunting Hawk"));
            List<BattleEvent> haste = Events(result, "haste", hawkId);
            Assert.Equal(1, haste.Count);
            Assert.Near(4.0, haste[0].Time);
            Assert.False(haste[0].TargetItemId == flyerId);

            // 普通 onHaste 被动读取下一帧，而不是在主动效果同一帧继续结算。
            List<BattleEvent> fly = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "fly" && ev.SourceItemId == hawkId && Math.Abs(ev.Time - 4.05) < 1e-9; });
            Assert.Equal(1, fly.Count);
            Assert.Equal(haste[0].TargetItemId, fly[0].TargetItemId);
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "fly" && ev.SourceItemId == hawkId && Math.Abs(ev.Time - 4.0) < 1e-9; }));
        }

        private static void HuntingHawkAuraFiltersFlyingAndCanCrit()
        {
            BattleConfig config = Board(1, Real("Hunting Hawk", 0, "Bronze"),
                Real("Bellelista", 1, "Gold"), Fake("g03:noncrit", 2, 5000, "[]"),
                Real("Bellelista", 3, "Gold"));
            MoveToEnemy(config, TemplateId("Bellelista"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            string hawkId = IdOf(config, TemplateId("Hunting Hawk"));
            InternalItem hawk = ctx.Items.Find(delegate(InternalItem item) { return item.Id == hawkId; });
            InternalItem belle = ctx.Items.Find(delegate(InternalItem item)
                { return item.Card.BaseItemId == TemplateId("Bellelista") && item.Side == Side.Player; });
            InternalItem enemyBelle = ctx.Items.Find(delegate(InternalItem item)
                { return item.Card.BaseItemId == TemplateId("Bellelista") && item.Side == Side.Enemy; });
            InternalItem noncrit = Item(ctx, IdOf(config, "g03:noncrit"));

            hawk.Flying = true;
            enemyBelle.Flying = true;
            noncrit.Flying = true;
            NativeDynamicAuras.Refresh(ctx);

            Assert.Near(0.10, hawk.CritBonus);
            Assert.Near(0, belle.CritBonus);
            Assert.Near(0, enemyBelle.CritBonus);
            Assert.Near(0, noncrit.CritBonus);
        }

        private static void BoostedSaucerActorFiltersRepairAndDestroy()
        {
            BattleConfig config = Board(1, Real("Boosted Saucer", 0, "Silver"),
                Fake("g03:actor", 1, 5000, "[]"), Fake("g03:target", 2, 5000, "[]"),
                Fake("g03:enemy-actor", 0, 5000, "[]"));
            MoveToEnemy(config, "g03:enemy-actor");
            BattleContext ctx = BattleEngine.InitBattle(config);
            string saucerId = IdOf(config, TemplateId("Boosted Saucer"));
            InternalItem saucer = Item(ctx, saucerId);
            InternalItem actor = Item(ctx, IdOf(config, "g03:actor"));
            InternalItem target = Item(ctx, IdOf(config, "g03:target"));
            InternalItem enemyActor = Item(ctx, IdOf(config, "g03:enemy-actor"));
            Assert.Near(150, ItemAttributes.Get(saucer, "DamageAmount"));

            TriggerPayload repaired = new TriggerPayload
            {
                Event = "onCardRepaired", SourceSide = target.Side, SourceItemId = target.Id,
                TargetSide = target.Side, TargetItemId = target.Id,
                ActorSide = actor.Side, ActorItemId = actor.Id, ChainId = 1, Depth = 0
            };
            Triggers.Enqueue(ctx, repaired);
            Triggers.DrainNativeFrame(ctx, 0);
            Assert.Near(300, ItemAttributes.Get(saucer, "DamageAmount"));

            TriggerPayload enemyRepair = new TriggerPayload
            {
                Event = "onCardRepaired", SourceSide = target.Side, SourceItemId = target.Id,
                TargetSide = target.Side, TargetItemId = target.Id,
                ActorSide = enemyActor.Side, ActorItemId = enemyActor.Id, ChainId = 2, Depth = 0
            };
            Triggers.Enqueue(ctx, enemyRepair);
            Triggers.DrainNativeFrame(ctx, 0);
            Assert.Near(300, ItemAttributes.Get(saucer, "DamageAmount"));

            double before = saucer.Cooldown;
            TriggerPayload destroyed = new TriggerPayload
            {
                Event = "onBeforeDestroy", SourceSide = target.Side, SourceItemId = target.Id,
                TargetSide = target.Side, TargetItemId = target.Id,
                ActorSide = actor.Side, ActorItemId = actor.Id, ChainId = 3, Depth = 0
            };
            Triggers.Enqueue(ctx, destroyed);
            Triggers.DrainEvent(ctx, 0, "onBeforeDestroy");
            Assert.Near(before - 4, saucer.Cooldown);

            double afterSelfActor = saucer.Cooldown;
            TriggerPayload enemyDestroy = new TriggerPayload
            {
                Event = "onBeforeDestroy", SourceSide = target.Side, SourceItemId = target.Id,
                TargetSide = target.Side, TargetItemId = target.Id,
                ActorSide = enemyActor.Side, ActorItemId = enemyActor.Id, ChainId = 4, Depth = 0
            };
            Triggers.Enqueue(ctx, enemyDestroy);
            Triggers.DrainEvent(ctx, 0, "onBeforeDestroy");
            Assert.Near(afterSelfActor, saucer.Cooldown);
        }

        private static void WolfMaskFirstUseEachFight()
        {
            BattleConfig config = Board(1, Real("Wolf Mask", 0, "Silver"),
                Fake("g03:wolf-ally", 1, 5000, "[]"), Fake("g03:wolf-foe", 0, 5000, "[]"));
            MoveToEnemy(config, "g03:wolf-foe");
            BattleContext ctx = BattleEngine.InitBattle(config);
            string wolfId = IdOf(config, TemplateId("Wolf Mask"));
            InternalItem wolf = Item(ctx, wolfId);
            InternalItem ally = Item(ctx, IdOf(config, "g03:wolf-ally"));
            InternalItem foe = Item(ctx, IdOf(config, "g03:wolf-foe"));

            // 自己使用不算“另一张手牌”，第一次必须留给其他卡。
            Triggers.Enqueue(ctx, UsePayload(wolf));
            Triggers.DrainNativeFrame(ctx, 0);
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "rage"; }));

            Triggers.Enqueue(ctx, UsePayload(foe));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> rage = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "rage"; });
            Assert.Equal(1, rage.Count);
            Assert.Near(30, rage[0].Value.Value);
            Assert.Near(1, ItemAttributes.Get(wolf, "Custom_1"));

            Triggers.Enqueue(ctx, UsePayload(ally));
            Triggers.DrainNativeFrame(ctx, 0.1);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "rage"; }));

            BattleContext fresh = BattleEngine.InitBattle(config);
            InternalItem freshFoe = Item(fresh, IdOf(config, "g03:wolf-foe"));
            Triggers.Enqueue(fresh, UsePayload(freshFoe));
            Triggers.DrainNativeFrame(fresh, 0);
            Assert.Equal(1, fresh.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "rage"; }));
        }

        private static void BloodhoundFirstBelowHalfEachFight()
        {
            BattleConfig config = Board(1, Fake("g03:bh-ally", 0, 5000, "[]"), Fake("g03:bh-ally2", 1, 5000, "[]"));
            config.EnemyMaxHp = 1000;
            config.EnemyStartHp = 1000;
            string skillId = MapSkill(config, "Bloodhound", "Silver", Side.Player);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem skill = Skill(ctx, skillId);

            ctx.Enemy.Hp = 600;
            Triggers.Enqueue(ctx, HealthLossPayload(Side.Enemy, 100));
            Triggers.DrainNativeFrame(ctx, 0);
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "charge"; }));

            ctx.Enemy.Hp = 400;
            Triggers.Enqueue(ctx, HealthLossPayload(Side.Enemy, 200));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> charges = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.SourceItemId == skillId; });
            Assert.Equal(2, charges.Count);
            Assert.Near(1, charges[0].Value.Value);
            Assert.Near(1, ItemAttributes.Get(skill, "Custom_0"));

            ctx.Enemy.Hp = 300;
            Triggers.Enqueue(ctx, HealthLossPayload(Side.Enemy, 100));
            Triggers.DrainNativeFrame(ctx, 0.1);
            Assert.Equal(2, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "charge" && ev.SourceItemId == skillId; }));

            BattleConfig fresh = Board(1, Fake("g03:bh-ally", 0, 5000, "[]"), Fake("g03:bh-ally2", 1, 5000, "[]"));
            fresh.EnemyMaxHp = 1000;
            fresh.EnemyStartHp = 1000;
            string freshSkill = MapSkill(fresh, "Bloodhound", "Silver", Side.Player);
            BattleContext freshCtx = BattleEngine.InitBattle(fresh);
            freshCtx.Enemy.Hp = 400;
            Triggers.Enqueue(freshCtx, HealthLossPayload(Side.Enemy, 600));
            Triggers.DrainNativeFrame(freshCtx, 0);
            Assert.Equal(2, freshCtx.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.SourceItemId == freshSkill; }));
        }

        private static TriggerPayload HealthLossPayload(Side side, double amount)
        {
            return new TriggerPayload
            {
                Event = "onPlayerAttributeChanged", SourceSide = side,
                AttributeName = "Health", ChangeType = "Loss", Amount = amount, ChainId = 1, Depth = 0
            };
        }

        private static void FrozenSecretsFirstNRelicUses()
        {
            BattleConfig config = Board(1, Fake("g03:relic", 0, 5000, "[\"Relic\"]"),
                Fake("g03:plain", 1, 5000, "[]"), Fake("g03:freeze-target", 0, 5000, "[]"));
            MoveToEnemy(config, "g03:freeze-target");
            string skillId = MapSkill(config, "Frozen Secrets", "Gold", Side.Player);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem skill = Skill(ctx, skillId);
            InternalItem relic = Item(ctx, IdOf(config, "g03:relic"));
            InternalItem plain = Item(ctx, IdOf(config, "g03:plain"));
            InternalItem target = Item(ctx, IdOf(config, "g03:freeze-target"));

            // 光环 9 只给 tooltip 提供 {aura.9}，不能把 Custom_1 预写进计数。
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(0, ItemAttributes.Get(skill, "Custom_0"));
            Assert.Near(3, ItemAttributes.Get(skill, "Custom_1"));

            Triggers.Enqueue(ctx, UsePayload(plain));
            Triggers.DrainNativeFrame(ctx, 0);
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "freeze"; }));
            Assert.Near(0, ItemAttributes.Get(skill, "Custom_0"));

            for (int i = 0; i < 4; i++)
            {
                Triggers.Enqueue(ctx, UsePayload(relic));
                Triggers.DrainNativeFrame(ctx, 0.05 + i * 0.05);
            }
            // Immediate counters already observed all four uses. The Medium
            // freezes retain those matched prerequisites and execute serially.
            Triggers.DrainNativeFrame(ctx, 0.3);
            Triggers.DrainNativeFrame(ctx, 0.55);
            List<BattleEvent> freezes = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "freeze" && ev.SourceItemId == skillId; });
            Assert.Equal(3, freezes.Count);
            foreach (BattleEvent freeze in freezes)
            {
                Assert.Equal(target.Id, freeze.TargetItemId);
                Assert.Near(1, freeze.Value.Value);
            }
            // 计数按“使用次数”累计；达到上限后第 4 次不再冻结，但计数器仍会再加 1。
            Assert.Near(4, ItemAttributes.Get(skill, "Custom_0"));
        }

        private static void CounterstrikeFirstEnemyWeaponUse()
        {
            BattleConfig config = Board(1, Fake("g03:cs-ally", 0, 5000, "[]"), Fake("g03:cs-ally2", 1, 5000, "[]"),
                Fake("g03:cs-weapon", 0, 5000, "[\"Weapon\"]"), Fake("g03:cs-plain", 1, 5000, "[]"));
            MoveToEnemy(config, "g03:cs-weapon");
            MoveToEnemy(config, "g03:cs-plain");
            string skillId = MapSkill(config, "Counterstrike", "Gold", Side.Player);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem skill = Skill(ctx, skillId);
            InternalItem weapon = Item(ctx, IdOf(config, "g03:cs-weapon"));
            InternalItem plain = Item(ctx, IdOf(config, "g03:cs-plain"));

            Triggers.Enqueue(ctx, UsePayload(plain));
            Triggers.DrainNativeFrame(ctx, 0);
            Assert.Equal(0, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "charge"; }));

            Triggers.Enqueue(ctx, UsePayload(weapon));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> charges = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.SourceItemId == skillId; });
            Assert.Equal(2, charges.Count);
            Assert.Near(1, ItemAttributes.Get(skill, "Custom_0"));

            Triggers.Enqueue(ctx, UsePayload(weapon));
            Triggers.DrainNativeFrame(ctx, 0.1);
            Assert.Equal(2, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "charge" && ev.SourceItemId == skillId; }));

            BattleConfig fresh = Board(1, Fake("g03:cs-ally", 0, 5000, "[]"), Fake("g03:cs-ally2", 1, 5000, "[]"),
                Fake("g03:cs-weapon", 0, 5000, "[\"Weapon\"]"), Fake("g03:cs-plain", 1, 5000, "[]"));
            MoveToEnemy(fresh, "g03:cs-weapon");
            MoveToEnemy(fresh, "g03:cs-plain");
            string freshSkill = MapSkill(fresh, "Counterstrike", "Gold", Side.Player);
            BattleContext freshCtx = BattleEngine.InitBattle(fresh);
            InternalItem freshWeapon = Item(freshCtx, IdOf(fresh, "g03:cs-weapon"));
            Triggers.Enqueue(freshCtx, UsePayload(freshWeapon));
            Triggers.DrainNativeFrame(freshCtx, 0);
            Assert.Equal(2, freshCtx.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.SourceItemId == freshSkill; }));
        }

        private static void RetoolReloadsTriggerSourceNeighborWithAmmo()
        {
            BattleConfig config = Board(1, Fake("g03:tool", 0, 5000, "[\"Tool\"]"),
                Fake("g03:near-ammo", 1, 5000, "[]", 2), Fake("g03:near-noammo", 2, 5000, "[]"),
                Fake("g03:far-ammo", 3, 5000, "[]", 2));
            string skillId = MapSkill(config, "Retool", "Diamond", Side.Player);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem tool = Item(ctx, IdOf(config, "g03:tool"));
            InternalItem near = Item(ctx, IdOf(config, "g03:near-ammo"));
            InternalItem noAmmo = Item(ctx, IdOf(config, "g03:near-noammo"));
            InternalItem far = Item(ctx, IdOf(config, "g03:far-ammo"));
            near.Ammo = 0;
            far.Ammo = 1;
            int noAmmoMax = noAmmo.AmmoMax.HasValue ? noAmmo.AmmoMax.Value : -1;
            Assert.Equal(-1, noAmmoMax);

            Triggers.Enqueue(ctx, UsePayload(tool));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> reloads = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "reload" && ev.SourceItemId == skillId; });
            Assert.Equal(1, reloads.Count);
            Assert.Equal(near.Id, reloads[0].TargetItemId);
            Assert.Equal(2, near.Ammo);
            Assert.Equal(1, far.Ammo);
            Assert.False(noAmmo.AmmoMax.HasValue);
        }

        private static void PropellerHatBeforeUseResolvesOnSameFrame()
        {
            BattleConfig config = Board(4.3, Real("Propeller Hat", 0, "Silver"), Fake("g03:hat-target", 1, 5000, "[]"));
            string hatId = IdOf(config, TemplateId("Propeller Hat"));
            config.Cards[hatId].Triggers.Add(AtStart(FlySelf()));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);

            List<BattleEvent> haste = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "haste" && ev.SourceItemId == hatId && Math.Abs(ev.Time - 4.0) < 1e-9; });
            Assert.Equal(1, haste.Count);
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "haste" && ev.SourceItemId == hatId && Math.Abs(ev.Time - 4.05) < 1e-9; }));

            int hasteIndex = result.Events.IndexOf(haste[0]);
            BattleEvent damage = result.Events.First(delegate(BattleEvent ev)
                { return ev.Kind == "damage" && ev.SourceItemId == hatId && Math.Abs(ev.Time - 4.0) < 1e-9; });
            int damageIndex = result.Events.IndexOf(damage);
            Assert.True(hasteIndex >= 0 && damageIndex > hasteIndex);
            Assert.Near(10, damage.Value.Value);
        }
    }
}
