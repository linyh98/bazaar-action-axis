using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G09: 摧毁前变形、禁用/修理、强制使用与死亡复活。
    // 真实模板来自 native_g09_cards.json；不裁掉触发主体、品质或前提。
    public static class G09LifecycleTests
    {
        private static JsonValue _fixtures;
        private static JsonValue _reviveFixtures;
        private static JsonValue _allFixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g09_cards.json"));
            _reviveFixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g09_revive.json"));
            AbductedCowTransformsInsteadOfDying();
            DroneCrusherTransformsTriggerTargetNotItself();
            DroneCrusherUsesTargetTierAndFreshCooldown();
            DroneCrusherMediumAndLargeSpawnLimitCopies();
            TransformDestroyedNewInstanceStartsAtGenerationOne();
            SameFrameObserverSeesTransformSpawn();
            DisableByDroneCrusherTransformsAfterDisableEvent();
            DroneCrusherIgnoresEnemyDestruction();
            DroneCrusherMissingDonorKeepsDestruction();
            FieryRebirthHealsToFullOnce();
            TwoReviveSkillsDoNotShareGlobalOnce();
            EnemyFieryRebirthRevives();
            MementoMoriSurvivesAtOneAndOnlyOnce();
            LuxuryTentsRevivesForPercentMaxHealth();
            AncientLocketGrantsInvulnerabilityOnDestruction();
            AncientLocketTriggersOnDisable();
            LifePreserverRevivesForFlatHeal();
            ElixirOfImmortalityDisablesThenRevives();
            ArtificialHeartHealsFullAndLosesMaxHp();
            EjectEjectRevivesToHalfAndDisablesVehicle();
            SparringPartnerDoublesMaxHealthOnDeath();
            RepairDroneForceUseRepairsTwoRandomItems();
            ForceUseConsumesAmmoKeepsCooldown();
            ForceUseOverridesDisableAndStopsAtDepth();
            DisableRepairAndDestroyImmunity();
            MutualDestructionResolvesOnce();
            AllG09TemplatesAreTriaged();
        }

        private static LiveCardEntry Real(string name, int slot, string tier = "Gold")
        {
            JsonValue row = _fixtures[name], template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry RealAll(string name, int slot, string tier)
        {
            JsonValue row = AllFixtures()[name], template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveSkillEntry RealAllSkill(string name, string tier)
        {
            JsonValue row = AllFixtures()[name], template = row["template"];
            return new LiveSkillEntry
            {
                InstanceId = "all:" + name + ":" + tier,
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier
            };
        }

        private static LiveCardEntry RealRevive(string name, int slot, string tier = "Gold")
        {
            JsonValue row = _reviveFixtures[name], template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry Custom(string id, int slot, string template)
        {
            JsonValue parsed = Json.Parse(template);
            return new LiveCardEntry
            {
                TemplateId = id,
                Template = parsed,
                Tier = "Gold",
                Socket = slot,
                Size = NativeSkillSupport.Text(parsed, "Size"),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(parsed, "Gold")
            };
        }

        private static BattleConfig Board(params LiveCardEntry[] cards)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, 10).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static CardDef Donor(string name, string tier)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { Real(name, 0, tier) },
                EmptyCatalog, 10000, 10000, 10).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config.Cards[config.Player.Items[0].CardId];
        }

        private static string Name(InternalItem item)
        {
            if (item == null || item.Card == null) return null;
            string name = NativeSkillSupport.Text(item.Card.NativeTemplate, "InternalName");
            return string.IsNullOrEmpty(name) ? item.Card.Name : name;
        }

        private static InternalItem Find(BattleContext ctx, string name)
        {
            foreach (InternalItem item in ctx.Items) if (Name(item) == name) return item;
            return null;
        }

        private static JsonValue AllFixtures()
        {
            if (_allFixtures == null)
                _allFixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g09_all.json"));
            return _allFixtures;
        }

        private static NumExpr Fixed(double value)
        {
            return new NumExpr { Type = "const", Value = value };
        }

        private static Effect Damage(double amount)
        {
            return new Effect { Kind = "damage", Amount = Fixed(amount), Target = new Target { Kind = "enemyHero" } };
        }

        private static CardDef Killer(string id, double cooldown, double damage)
        {
            CardDef card = new CardDef { Id = id, Name = id, Cooldown = cooldown };
            card.Effects.Add(Damage(damage));
            return card;
        }

        private static void AddCard(BattleConfig config, CardDef card, Side side, int slot)
        {
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
        }

        private static void AbductedCowTransformsInsteadOfDying()
        {
            BattleConfig config = Board(Real("Abducted Cow", 1, "Silver"));
            AddCard(config, Killer("killer", 99, 0), Side.Player, 5);
            CardDef other = new CardDef { Id = "other", Name = "Other", Cooldown = 5 };
            other.LiveAttributes["CooldownMax"] = 5000;
            AddCard(config, other, Side.Player, 3);
            config.SpawnPool.Add(Donor("Jerky", "Silver"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem cow = Find(ctx, "Abducted Cow"), source = Find(ctx, "killer"), otherItem = Find(ctx, "Other");
            Assert.True(cow != null && source != null && otherItem != null);
            otherItem.Cooldown = 5;

            // 同阵营行动者摧毁：onBeforeCardDestroyed 同步结算，变形在全部
            // 摧毁前被动结算后才执行，所以充能仍读取原卡数值。
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = cow.Id } }, source, 1, 0, null);
            BattleEngine.FlushPendingItems(ctx);

            // 原生 CardTransformed：旧实例离场，新实例 ID，同槽位、满冷却。
            Assert.True(cow.Destroyed);
            Assert.Equal(1, cow.Generation);
            Assert.Equal("Abducted Cow", Name(cow));
            Assert.True(cow.History != null && cow.History.RemovedAt.HasValue);
            int jerky = 0;
            List<InternalItem> jerkyItems = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
                if (Name(item) == "Jerky") { jerky++; jerkyItems.Add(item); }
            Assert.Equal(2, jerky);
            Assert.Near(3, jerkyItems[0].CooldownMax);
            Assert.Near(3, jerkyItems[1].CooldownMax);
            Assert.False(jerkyItems[0].Destroyed);
            Assert.False(jerkyItems[1].Destroyed);
            int transforms = 0, destroys = 0;
            foreach (BattleEvent ev in ctx.Events)
            {
                if (ev.Kind == "transform") transforms++;
                if (ev.Kind == "destroy") destroys++;
            }
            Assert.Equal(2, transforms);
            Assert.Equal(0, destroys);
            // 牛的摧毁前被动仍然生效：其他物品被充能 3 秒。
            Assert.Near(2, otherItem.Cooldown);
        }

        private static void DroneCrusherTransformsTriggerTargetNotItself()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Gold" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");
            Assert.True(crusher != null && target != null);
            int crusherSlot = crusher.Slot;
            string crusherCard = crusher.CardId;

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);
            BattleEngine.FlushPendingItems(ctx);

            // 真实主体是 TriggerTarget（被摧毁的物品），不是 Drone Crusher 自己。
            Assert.Equal(crusherCard, crusher.CardId);
            Assert.False(crusher.Destroyed);
            Assert.Equal(crusherSlot, crusher.Slot);
            Assert.True(target.Destroyed);
            Assert.Equal(1, target.Generation);
            Assert.Equal("Victim", Name(target));
            Assert.True(target.History != null && target.History.RemovedAt.HasValue);
            InternalItem drone = Find(ctx, "Rocket Drone");
            Assert.True(drone != null);
            Assert.False(drone.Destroyed);
            Assert.Equal(3, drone.Slot);
            Assert.Near(4, drone.CooldownMax);
            // Immediate 的售价累计必须先于 Medium 的变形，读到的仍是原卡售价。
            Assert.Near(7, ItemAttributes.Get(crusher, "Custom_1"));
            Assert.Near(35, ItemAttributes.Get(crusher, "BurnApplyAmount"));
            Assert.Equal(0, ctx.PendingItems.Count);
        }

        private static void DroneCrusherUsesTargetTierAndFreshCooldown()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Diamond" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            config.SpawnPool.Add(Donor("Rocket Drone", "Diamond"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");
            target.Cooldown = 1.5;

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);
            BattleEngine.FlushPendingItems(ctx);

            InternalItem drone = Find(ctx, "Rocket Drone");
            Assert.True(drone != null);
            Assert.Equal("Diamond", drone.Card.Tier);
            // 原生新实例是满冷却，不继承被摧毁卡的剩余进度。
            Assert.Near(4, drone.CooldownMax);
            Assert.Near(4, drone.Cooldown);
        }

        private static void DroneCrusherMediumAndLargeSpawnLimitCopies()
        {
            string[] sizes = { "Medium", "Large" };
            int[] expected = { 2, 3 };
            for (int index = 0; index < sizes.Length; index++)
            {
                BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
                CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5,
                    Size = sizes[index], Tier = "Gold" };
                victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
                AddCard(config, victim, Side.Player, 3);
                config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");
                Assert.True(crusher != null && target != null);

                Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                    Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);
                BattleEngine.FlushPendingItems(ctx);

                // SpawnContext.Limit：Medium=2、Large=3，全部是同一品质的小型 Drone。
                List<InternalItem> drones = new List<InternalItem>();
                HashSet<string> ids = new HashSet<string>();
                foreach (InternalItem item in ctx.Items)
                    if (Name(item) == "Rocket Drone") { drones.Add(item); ids.Add(item.Id); }
                Assert.Equal(expected[index], drones.Count);
                Assert.Equal(expected[index], ids.Count);
                Assert.True(drones.TrueForAll(delegate(InternalItem item) { return !item.Destroyed; }));
                Assert.True(drones.TrueForAll(delegate(InternalItem item) { return item.Card.Tier == "Gold"; }));
                Assert.True(drones.TrueForAll(delegate(InternalItem item) { return Math.Abs(item.Cooldown - 4) < 0.001; }));
                Assert.True(drones.TrueForAll(delegate(InternalItem item) { return Math.Abs(item.CooldownMax - 4) < 0.001; }));
            }
        }

        private static void TransformDestroyedNewInstanceStartsAtGenerationOne()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Gold" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);
            BattleEngine.FlushPendingItems(ctx);
            InternalItem first = null;
            foreach (InternalItem item in ctx.Items) if (Name(item) == "Rocket Drone") first = item;
            Assert.True(first != null);
            Assert.Equal(1, first.Generation);

            // 回放 f176：旧实例本身是 #1 的产物（originalGeneration=1），离场后
            // generation=2；但新实例 itm_HQbWzy7 自己的 generation 仍从 1 开始。
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = first.Id } }, crusher, 2, 0, null);
            BattleEngine.FlushPendingItems(ctx);
            InternalItem second = null;
            foreach (InternalItem item in ctx.Items)
                if (Name(item) == "Rocket Drone" && !item.Destroyed) second = item;
            Assert.True(second != null);
            Assert.True(second.Id != first.Id);
            Assert.True(first.Destroyed);
            Assert.Equal(2, first.Generation);
            Assert.Equal(1, second.Generation);
        }

        private static void SameFrameObserverSeesTransformSpawn()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Gold" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            JsonValue leverage = AllFixtures()["Leverage Momentum"];
            LiveSkillEntry skill = new LiveSkillEntry { InstanceId = "leverage-1", Tier = "Silver",
                TemplateId = leverage["id"].AsString(), Template = leverage["template"] };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Player);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");
            Assert.True(crusher != null && target != null);
            // Leverage Momentum 随机选择未飞行的己方手牌；把 Drone Crusher 设为
            // 已飞行，唯一的候选就是出生帧尚未加入正式列表的新 Drone。
            crusher.Flying = true;

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);

            InternalItem spawned = null;
            foreach (InternalItem item in ctx.PendingItems) if (Name(item) == "Rocket Drone") spawned = item;
            Assert.True(spawned != null);
            // 回放 5.05s：FlyingStart 在同一帧选中新实例 itm_ZNlrJcT。
            Assert.True(spawned.Flying);
            int flyEvents = 0;
            foreach (BattleEvent ev in ctx.Events)
                if (ev.Kind == "fly" && ev.TargetItemId == spawned.Id) flyEvents++;
            Assert.Equal(1, flyEvents);
            BattleEngine.FlushPendingItems(ctx);
            Assert.True(Find(ctx, "Rocket Drone").Flying);
        }

        private static void DisableByDroneCrusherTransformsAfterDisableEvent()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Gold" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");

            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);
            BattleEngine.FlushPendingItems(ctx);
            // 原生禁用的摧毁前链下一帧结算（回放 f100 禁用、f101 变形）。
            Triggers.Drain(ctx, 1.05);
            BattleEngine.FlushPendingItems(ctx);

            // 回放 f100：先 CardDisable(WmLbYJg)，f101 才 CardTransformDestroyed。
            int disableIndex = -1, transformIndex = -1, index = 0;
            foreach (BattleEvent ev in ctx.Events)
            {
                if (ev.Kind == "disable" && ev.TargetItemId == target.Id) disableIndex = index;
                if (ev.Kind == "transform" && ev.TargetItemId != target.Id) transformIndex = index;
                index++;
            }
            Assert.True(disableIndex >= 0);
            Assert.True(transformIndex > disableIndex);
            Assert.True(target.Destroyed);
            Assert.True(Find(ctx, "Rocket Drone") != null);
        }

        private static void DroneCrusherIgnoresEnemyDestruction()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Gold" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            AddCard(config, Killer("enemyKiller", 99, 0), Side.Enemy, 0);
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim"), enemy = Find(ctx, "enemyKiller");
            Assert.True(crusher != null && target != null && enemy != null);

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "enemyItem", ItemId = target.Id } }, enemy, 1, 0, null);

            // “When you destroy”只响应本方行动者；敌方摧毁仍是正常离场。
            Assert.True(target.Destroyed);
            Assert.Equal("Victim", Name(target));
            Assert.Near(0, ItemAttributes.Get(crusher, "Custom_1"));
            int transforms = 0;
            foreach (BattleEvent ev in ctx.Events) if (ev.Kind == "transform") transforms++;
            Assert.Equal(0, transforms);
        }

        private static void DroneCrusherMissingDonorKeepsDestruction()
        {
            BattleConfig config = Board(Real("Drone Crusher", 0, "Silver"));
            CardDef victim = new CardDef { Id = "victim", Name = "Victim", Cooldown = 5, Size = "Small", Tier = "Diamond" };
            victim.LiveAttributes = new Dictionary<string, int> { { "SellPrice", 7 }, { "CooldownMax", 5000 } };
            AddCard(config, victim, Side.Player, 3);
            // 只提供 Gold 供体，Diamond 目标必须 fail-closed：告警并继续正常摧毁。
            config.SpawnPool.Add(Donor("Rocket Drone", "Gold"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem crusher = Find(ctx, "Drone Crusher"), target = Find(ctx, "Victim");

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = target.Id } }, crusher, 1, 0, null);

            Assert.True(target.Destroyed);
            Assert.True(ctx.Warnings.Exists(delegate(string warning) { return warning.Contains("缺少 TransformDestroyed 供体"); }));
        }

        private static void FieryRebirthHealsToFullOnce()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 100, PlayerStartHp = 100,
                EnemyMaxHp = 10000, EnemyStartHp = 10000, MaxTime = 5 };
            AddCard(config, Killer("killer", 1, 200), Side.Enemy, 0);
            LiveSkillEntry skill = new LiveSkillEntry { InstanceId = "fiery-1", Tier = "Legendary",
                TemplateId = _fixtures["Fiery Rebirth"]["id"].AsString(), Template = _fixtures["Fiery Rebirth"]["template"] };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Player);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem skillItem = ctx.Skills.Find(delegate(InternalItem item) { return item.CardId == "skill:fiery-1"; });
            Assert.True(skillItem != null);
            Assert.Near(100, ItemAttributes.Get(skillItem, "HealAmount"));

            BattleResult result = BattleEngine.Simulate(config);
            // 第一次死亡满血复活；Custom_0 使第二次死亡真正结束。
            List<BattleEvent> heals = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "heal" && ev.SourceItemId == skillItem.Id; });
            Assert.Equal(1, heals.Count);
            Assert.Near(100, heals[0].Value.Value);
            Assert.Near(1, heals[0].Time);
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat)
                { return stat.Kind == "revive" && stat.SourceItemId == skillItem.Id; });
            Assert.Equal(1, revives.Count);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
            int counters = 0;
            foreach (BattleEvent ev in result.Events)
                if (ev.Kind == "modifyAttribute" && ev.Note == "Custom_0" && ev.Value.Value > 0) counters++;
            Assert.True(counters >= 1);
        }

        private static void TwoReviveSkillsDoNotShareGlobalOnce()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 100, PlayerStartHp = 100,
                EnemyMaxHp = 10000, EnemyStartHp = 10000, MaxTime = 5 };
            AddCard(config, Killer("killer", 1, 200), Side.Enemy, 0);
            LiveSkillEntry first = new LiveSkillEntry { InstanceId = "fiery-1", Tier = "Legendary",
                TemplateId = _fixtures["Fiery Rebirth"]["id"].AsString(), Template = _fixtures["Fiery Rebirth"]["template"] };
            LiveSkillEntry second = new LiveSkillEntry { InstanceId = "fiery-2", Tier = "Legendary",
                TemplateId = _fixtures["Fiery Rebirth"]["id"].AsString(), Template = _fixtures["Fiery Rebirth"]["template"] };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { first, second }, null, Side.Player);
            Assert.Equal(2, mapped.CombatCount);

            BattleResult result = BattleEngine.Simulate(config);
            // 两张复活技能各自按 Custom_0 判断：第一次死亡都触发，第二次都拒绝。
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat) { return stat.Kind == "revive"; });
            Assert.Equal(2, revives.Count);
            Assert.True(revives[0].SourceItemId != revives[1].SourceItemId);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void EnemyFieryRebirthRevives()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 10000, PlayerStartHp = 10000,
                EnemyMaxHp = 100, EnemyStartHp = 100, MaxTime = 5 };
            AddCard(config, Killer("killer", 1, 200), Side.Player, 0);
            LiveSkillEntry skill = new LiveSkillEntry { InstanceId = "enemy-fiery", Tier = "Legendary",
                TemplateId = _fixtures["Fiery Rebirth"]["id"].AsString(), Template = _fixtures["Fiery Rebirth"]["template"] };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Enemy);
            Assert.Equal("simulated", mapped.Skills[0].Status);

            BattleResult result = BattleEngine.Simulate(config);
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat)
                { return stat.Kind == "revive" && stat.SourceItemId != null && stat.SourceItemId.StartsWith("enemy:", StringComparison.Ordinal); });
            Assert.Equal(1, revives.Count);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void MementoMoriSurvivesAtOneAndOnlyOnce()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealRevive("Memento Mori", 0, "Gold") },
                EmptyCatalog, 100, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 100;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("killer", 1, 200), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat) { return stat.Kind == "revive"; });
            Assert.Equal(1, revives.Count);
            Assert.Near(1, revives[0].Value);
            List<BattleEvent> heals = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "heal"; });
            Assert.Equal(1, heals.Count);
            Assert.Near(1, heals[0].Value.Value);
            // 1 秒无敌覆盖 t=2 的第二击；t=3 第三击才结束。
            Assert.True(result.EndedByDeath);
            Assert.Near(3, result.EndTime);
        }

        private static void LuxuryTentsRevivesForPercentMaxHealth()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealRevive("Luxury Tents", 0, "Gold") },
                EmptyCatalog, 100, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 100;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("killer", 1, 200), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat) { return stat.Kind == "revive"; });
            Assert.Equal(1, revives.Count);
            Assert.True(revives[0].Value > 0);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void AncientLocketGrantsInvulnerabilityOnDestruction()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealRevive("Ancient Locket", 0, "Silver") },
                EmptyCatalog, 100, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 100;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("enemyKiller", 99, 0), Side.Enemy, 0);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem locket = Find(ctx, "Ancient Locket"), enemy = Find(ctx, "enemyKiller");
            Assert.True(locket != null && enemy != null);

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "enemyItem", ItemId = locket.Id } }, enemy, 1, 0, null);
            Assert.True(locket.Destroyed);
            Assert.Near(100, ctx.Player.ActionPercentDamageReduction);
            double before = ctx.Player.Hp;
            Effects.ResolveEffect(ctx, Damage(50), enemy, 2, 0, null);
            Assert.Near(before, ctx.Player.Hp);
            Effects.ResolveEffect(ctx, Damage(50), enemy, 4, 0, null);
            Assert.Near(before - 50, ctx.Player.Hp);
        }

        private static void AncientLocketTriggersOnDisable()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealRevive("Ancient Locket", 0, "Silver") },
                EmptyCatalog, 100, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 100;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("enemyKiller", 99, 0), Side.Enemy, 0);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem locket = Find(ctx, "Ancient Locket"), enemy = Find(ctx, "enemyKiller");

            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Count = 1,
                Target = new Target { Kind = "enemyItem", ItemId = locket.Id } }, enemy, 1, 0, null);
            Triggers.Drain(ctx, 1.05);
            Assert.True(locket.Disabled);
            Assert.False(locket.Destroyed);
            Assert.Near(100, ctx.Player.ActionPercentDamageReduction);
            double before = ctx.Player.Hp;
            Effects.ResolveEffect(ctx, Damage(50), enemy, 2, 0, null);
            Assert.Near(before, ctx.Player.Hp);
            Effects.ResolveEffect(ctx, Damage(50), enemy, 4, 0, null);
            Assert.Near(before - 50, ctx.Player.Hp);
        }

        private static void LifePreserverRevivesForFlatHeal()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealAll("Life Preserver", 0, "Bronze") },
                EmptyCatalog, 1000, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 1000;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("killer", 1, 2000), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            // Bronze Life Preserver 的 ReviveHeal 是固定 200，不是满血。
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat) { return stat.Kind == "revive"; });
            Assert.Equal(1, revives.Count);
            Assert.Near(200, revives[0].Value);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void ElixirOfImmortalityDisablesThenRevives()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealAll("Elixir of Immortality", 0, "Legendary") },
                EmptyCatalog, 1000, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 1000;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("killer", 1, 2000), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            // 首次致死：OnPlayerDied 自禁用 -> 禁用触发 BeforeCardDestroyed -> ReviveHeal 满血。
            // 第二次致死时 Custom_0 已为 1，不再复活。
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat) { return stat.Kind == "revive"; });
            Assert.Equal(1, revives.Count);
            Assert.Near(1000, revives[0].Value);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
            int disables = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "disable"; }).Count;
            Assert.Equal(1, disables);
        }

        private static void ArtificialHeartHealsFullAndLosesMaxHp()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { RealAll("Artificial Heart", 0, "Legendary") },
                EmptyCatalog, 1000, 10000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            config.PlayerStartHp = 1000;
            config.EnemyStartHp = 10000;
            AddCard(config, Killer("killer", 1, 2000), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            // 模板 HealAmount=0 但 tooltip 明确 “Heal to full”；死亡触发顺序为
            // 清毒/清烧 -> 满血 -> MaxHealth -10%，第二次致死才结束。
            List<BattleEvent> heals = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "heal"; });
            Assert.Equal(1, heals.Count);
            // Immediate 的 MaxHealth-10% 先结算，随后的 “Heal to full” 按新上限 900。
            Assert.Near(900, heals[0].Value.Value);
            int maxLoss = 0;
            foreach (BattleEvent ev in result.Events)
                if (ev.Kind == "playerAttribute" && ev.Note == "HealthMax" && ev.Value.Value < 0) maxLoss++;
            Assert.Equal(1, maxLoss);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void EjectEjectRevivesToHalfAndDisablesVehicle()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, PlayerStartHp = 1000,
                EnemyMaxHp = 10000, EnemyStartHp = 10000, MaxTime = 5 };
            CardDef vehicle = new CardDef { Id = "vehicle", Name = "Vehicle", Cooldown = 99, Size = "Small" };
            vehicle.Tags = new List<string> { "Vehicle" };
            AddCard(config, vehicle, Side.Player, 1);
            LiveSkillEntry skill = RealAllSkill("Eject, Eject!", "Diamond");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Player);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            AddCard(config, Killer("killer", 1, 2000), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            // Aura 3 把 ReviveHeal 设为 50% MaxHealth；同一次治疗触发 onCardPerformedHeal
            // 禁用一张 Vehicle。
            List<EffectStat> revives = result.EffectStats.FindAll(delegate(EffectStat stat) { return stat.Kind == "revive"; });
            Assert.Equal(1, revives.Count);
            Assert.Near(500, revives[0].Value);
            int disables = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "disable"; }).Count;
            Assert.Equal(1, disables);
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void SparringPartnerDoublesMaxHealthOnDeath()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, PlayerStartHp = 1000,
                EnemyMaxHp = 10000, EnemyStartHp = 10000, MaxTime = 5 };
            LiveSkillEntry skill = RealAllSkill("Sparring Partner (Skill)", "Legendary");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Player);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            AddCard(config, Killer("killer", 1, 2000), Side.Enemy, 0);

            BattleResult result = BattleEngine.Simulate(config);
            // 首次致死：清毒/清烧、MaxHealth +100%、满血；第二次才结束。
            // 敌方 +Gold 是经济效果，按 G09 口径 OUT，不进入战斗账本。
            int maxGain = 0;
            foreach (BattleEvent ev in result.Events)
                if (ev.Kind == "playerAttribute" && ev.Note == "HealthMax" && ev.Value.Value > 0) maxGain++;
            Assert.Equal(1, maxGain);
            Assert.True(result.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "heal" && ev.Time <= 1.001; }));
            Assert.True(result.EndedByDeath);
            Assert.Near(2, result.EndTime);
        }

        private static void RepairDroneForceUseRepairsTwoRandomItems()
        {
            BattleConfig config = Board(Real("Repair Drone", 0, "Diamond"));
            CardDef first = new CardDef { Id = "first", Name = "First", Cooldown = 5 };
            CardDef second = new CardDef { Id = "second", Name = "Second", Cooldown = 5 };
            AddCard(config, first, Side.Player, 1);
            AddCard(config, second, Side.Player, 2);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem drone = Find(ctx, "Repair Drone"), firstItem = Find(ctx, "First"), secondItem = Find(ctx, "Second");
            Assert.True(drone != null && firstItem != null && secondItem != null);
            // 回放 f176：Repair Drone Diamond 一次开火随机修两张（Ability 1 TargetCount=2）。
            firstItem.Disabled = true;
            secondItem.Disabled = true;
            drone.Cooldown = 1.5;

            BattleEngine.ForceUse(ctx, drone, 1, 0);

            Assert.False(firstItem.Disabled);
            Assert.False(secondItem.Disabled);
            Assert.Equal(2, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "repair"; }).Count);
            Assert.Near(1.5, drone.Cooldown);
        }

        private static void ForceUseConsumesAmmoKeepsCooldown()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 1000, EnemyStartHp = 1000, MaxTime = 5 };
            CardDef target = new CardDef { Id = "target", Name = "Target", Cooldown = 5, AmmoMax = 1 };
            target.Effects.Add(Damage(7));
            AddCard(config, target, Side.Player, 0);
            CardDef force = new CardDef { Id = "force", Name = "Force", Cooldown = 99 };
            force.Effects.Add(new Effect { Kind = "forceUse", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = "target" } });
            AddCard(config, force, Side.Player, 1);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem targetItem = Find(ctx, "Target"), forceItem = Find(ctx, "Force");
            targetItem.Cooldown = 3;
            targetItem.Ammo = 1;
            targetItem.MulticastBonus = 2;

            Effects.ResolveEffect(ctx, forceItem.Card.Effects[0], forceItem, 1, 0, null);

            // 回放确认：ForceUse 不重置冷却、消耗 1 弹药、不展开 Multicast。
            Assert.Near(7, ctx.Totals.Player.Damage);
            Assert.Near(3, targetItem.Cooldown);
            Assert.Equal(0, targetItem.Ammo);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "activate" && ev.SourceItemId == targetItem.Id; }).Count);
            // 0 弹药时再次强制使用空转。
            Effects.ResolveEffect(ctx, forceItem.Card.Effects[0], forceItem, 2, 0, null);
            Assert.Near(7, ctx.Totals.Player.Damage);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "activate" && ev.SourceItemId == targetItem.Id; }).Count);
        }

        private static void ForceUseOverridesDisableAndStopsAtDepth()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 1000, EnemyStartHp = 1000, MaxTime = 5 };
            CardDef target = new CardDef { Id = "target", Name = "Target", Cooldown = 5 };
            target.Effects.Add(Damage(7));
            AddCard(config, target, Side.Player, 0);
            CardDef force = new CardDef { Id = "force", Name = "Force", Cooldown = 99 };
            force.Effects.Add(new Effect { Kind = "forceUse", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = "target" } });
            AddCard(config, force, Side.Player, 1);
            CardDef nested = new CardDef { Id = "nested", Name = "Nested", Cooldown = 99 };
            nested.Effects.Add(new Effect { Kind = "forceUse", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = "nested" } });
            AddCard(config, nested, Side.Player, 2);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem targetItem = Find(ctx, "Target"), forceItem = Find(ctx, "Force"), nestedItem = Find(ctx, "Nested");

            targetItem.Disabled = true;
            Effects.ResolveEffect(ctx, forceItem.Card.Effects[0], forceItem, 1, 0, null);
            // 原生回放：ForceUse 是禁用规则的显式例外，仍执行一次。
            Assert.Near(7, ctx.Totals.Player.Damage);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "forceUse"; }).Count);
            Assert.True(targetItem.Disabled);
            targetItem.Disabled = false;

            Effects.ResolveEffect(ctx, nestedItem.Card.Effects[0], nestedItem, 2, 0, null);
            Assert.Equal(ctx.Rules.MaxTriggerDepth, ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "activate" && ev.SourceItemId == nestedItem.Id; }).Count);
            Assert.True(ctx.Warnings.Exists(delegate(string warning) { return warning.Contains("强制使用嵌套超过上限"); }));
        }

        private static void DisableRepairAndDestroyImmunity()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, EnemyMaxHp = 1000, EnemyStartHp = 1000, MaxTime = 5 };
            CardDef target = new CardDef { Id = "target", Name = "Target", Cooldown = 5 };
            target.Effects.Add(Damage(7));
            AddCard(config, target, Side.Player, 0);
            CardDef breaker = new CardDef { Id = "breaker", Name = "Breaker", Cooldown = 99 };
            AddCard(config, breaker, Side.Player, 1);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem targetItem = Find(ctx, "Target"), breakerItem = Find(ctx, "Breaker");

            Effects.ResolveEffect(ctx, new Effect { Kind = "disable", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = targetItem.Id } }, breakerItem, 1, 0, null);
            Assert.True(targetItem.Disabled);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "disable"; }).Count);
            BattleEngine.ForceUse(ctx, targetItem, 2, 0);
            // 禁用状态不阻止显式 ForceUse；但仍保持禁用，直到修理。
            Assert.Near(7, ctx.Totals.Player.Damage);
            Assert.True(targetItem.Disabled);

            Effects.ResolveEffect(ctx, new Effect { Kind = "repair", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = targetItem.Id } }, breakerItem, 3, 0, null);
            Assert.False(targetItem.Disabled);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "repair"; }).Count);
            BattleEngine.ForceUse(ctx, targetItem, 4, 0);
            Assert.Near(14, ctx.Totals.Player.Damage);

            targetItem.Card.DestroyImmunity = 100;
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "selfItem", ItemId = targetItem.Id } }, breakerItem, 5, 0, null);
            Assert.False(targetItem.Destroyed);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "destroyBlocked" && ev.TargetItemId == targetItem.Id; }).Count);
            Assert.Equal(0, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "destroy"; }).Count);
        }

        private static void MutualDestructionResolvesOnce()
        {
            BattleConfig config = Board(Custom("a", 0, MutualTemplate("A")), Custom("b", 2, MutualTemplate("B")));
            AddCard(config, Killer("enemyKiller", 99, 0), Side.Enemy, 0);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem a = Find(ctx, "A"), b = Find(ctx, "B"), enemy = Find(ctx, "enemyKiller");
            Assert.True(a != null && b != null && enemy != null);

            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Count = 1,
                Target = new Target { Kind = "enemyItem", ItemId = a.Id } }, enemy, 1, 0, null);
            Triggers.DrainNativeFrame(ctx, 1);

            // 同帧互相摧毁：两张都只离场一次，不能递归到事件上限。
            Assert.True(a.Destroyed);
            Assert.True(b.Destroyed);
            Assert.False(enemy.Destroyed);
            Assert.Equal(2, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "destroy"; }).Count);
            Assert.False(ctx.Warnings.Exists(delegate(string warning) { return warning.Contains("event cap"); }));
        }

        private static void AllG09TemplatesAreTriaged()
        {
            JsonValue all = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g09_all.json"));
            HashSet<string> outNames = new HashSet<string>
            {
                "[Crash Site Expedition] Crash Site Expedition Ticket",
                "[Jungle Expedition] Temple Expedition Ticket",
                "[DEBUG] Peacewrought",
                "[DEBUG] Destructible Skill"
            };
            int templates = 0, full = 0, data = 0, partial = 0, outCount = 0;
            List<string> unexpected = new List<string>();
            foreach (KeyValuePair<string, JsonValue> pair in all.AsObject())
            {
                templates++;
                JsonValue template = pair.Value["template"];
                string id = pair.Value["id"].AsString();
                string type = NativeSkillSupport.Text(template, "$type");
                JsonValue tiers = template["Tiers"];
                int tierTotal = 0, tierBlocked = 0;
                foreach (KeyValuePair<string, JsonValue> tierPair in tiers.AsObject())
                {
                    tierTotal++;
                    string tier = tierPair.Key;
                    if (type == "TCardItem")
                    {
                        LiveCardEntry entry = new LiveCardEntry
                        {
                            TemplateId = id, Template = template, Tier = tier, Socket = 0,
                            Size = NativeSkillSupport.Text(template, "Size"), AttributesAreLive = false,
                            Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
                        };
                        BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { entry },
                            EmptyCatalog, 10000, 10000, 10).Config;
                        config.NativeMechanics = true;
                        List<string> warnings = new List<string>(NativeCardAbilityMapper.Apply(config));
                        CardDef mapped = config.Cards[config.Player.Items[0].CardId];
                        if (mapped.ParseStatus != "structured") warnings.Add("ParseStatus=" + mapped.ParseStatus);
                        foreach (string warning in mapped.MappingWarnings)
                            if (warning.Contains("定义缺失")) warnings.Add(warning);
                        if (warnings.Count == 0) continue;
                        tierBlocked++;
                        string text = string.Join(" | ", warnings.ToArray());
                        if (!KnownG09Blocked(pair.Key, text)) unexpected.Add(pair.Key + "/" + tier + ": " + text);
                    }
                    else if (type == "TCardSkill")
                    {
                        LiveSkillEntry entry = new LiveSkillEntry
                        {
                            InstanceId = id + ":" + tier, TemplateId = id, Template = template, Tier = tier
                        };
                        SkillMappingResult result = SkillMapper.Apply(new BattleConfig { NativeMechanics = true },
                            new[] { entry }, null, Side.Player);
                        string status = result.Skills[0].Status;
                        if (status == "simulated" || status == "inactive" || status == "live-applied") continue;
                        tierBlocked++;
                        string text = status + ": " + result.Skills[0].Reason;
                        if (status != "out-of-combat" && !KnownG09Blocked(pair.Key, text))
                            unexpected.Add(pair.Key + "/" + tier + ": " + text);
                    }
                }
                // 与 docs/native-rule-coverage.md 的 G09 分诊一致：
                // 83 全映射 / 4 全入口 DATA / 3 部分 DATA / 4 OUT。
                if (outNames.Contains(pair.Key)) outCount++;
                else if (tierBlocked == 0) full++;
                else if (tierBlocked == tierTotal) data++;
                else partial++;
            }
            Assert.Equal(94, templates);
            Assert.Equal(83, full);
            Assert.Equal(4, data);
            Assert.Equal(3, partial);
            Assert.Equal(4, outCount);
            Assert.Equal("", string.Join("\n", unexpected.ToArray()));
        }

        private static bool KnownG09Blocked(string name, string text)
        {
            // 这些是模板本身缺 AbilityId/AuraId、票券场外经济或 debug-only 规则；
            // 其它任何新增拒绝都必须让测试失败并要求重新分诊。
            if (name == "Bear Trap" && text.Contains("能力定义缺失")) return true;
            if ((name == "Firecrackers" || name == "[DEBUG] Firecrackers" || name == "Hydrogen Tank" ||
                 name == "Seeker Probe" || name == "The Big One") && text.Contains("光环定义缺失")) return true;
            if ((name == "[Crash Site Expedition] Crash Site Expedition Ticket" ||
                 name == "[Jungle Expedition] Temple Expedition Ticket") &&
                (text.Contains("BuyPrice") || text.Contains("动态光环属性") || text.Contains("动态光环目标区域"))) return true;
            if (name == "[DEBUG] Peacewrought" && (text.Contains("TActionGameSpawnCards") || text.Contains("行动"))) return true;
            if (name == "[DEBUG] Repeatable Quest" && text.Contains("能力定义缺失")) return true;
            return false;
        }

        private static string MutualTemplate(string name)
        {
            return @"{""$type"":""TCardItem"",""InternalName"":""" + name + @""",""Size"":""Small"",""Tags"":[],""HiddenTags"":[],
              ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":5000},""AbilityIds"":[""0""],""AuraIds"":[],""TooltipIds"":[]}},
              ""Abilities"":{""0"":{""Trigger"":{""$type"":""TTriggerOnBeforeCardDestroyed"",
                ""Subject"":{""$type"":""TTargetCardSelf"",""Conditions"":null},""Source"":null},
                ""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere"",
                ""Action"":{""$type"":""TActionCardDestroy"",""TargetCount"":null,
                  ""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfBoard"",""ExcludeSelf"":true,""Conditions"":null}},
                ""Prerequisites"":null}},""Auras"":{}}";
        }
    }
}
