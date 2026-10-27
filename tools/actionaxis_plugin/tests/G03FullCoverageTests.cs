using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G03 全量入口与机制族回归：45 个 primary_group=G03 模板必须全部有结论，
    // 不能只验证几个种子。缺失定义的卡继续 fail-closed，非战斗卡转 OUT。
    public static class G03FullCoverageTests
    {
        private static JsonValue _fixtures;
        private static JsonValue _support;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        private static readonly HashSet<string> DataBlocked = new HashSet<string>
        {
            "1a0c645a-8a0b-424b-bff1-124263589749", // Fortune Cookie
            "6693a3f1-cc7c-4a3a-942f-d2e308b22338", // Sulphur
            "8981892b-ce2a-43dc-a08e-2b5e8f8884fc", // Shard of Obsidian
            "dd789cd1-77d0-422b-968b-7ddb581a700f"  // Wild Quillback
        };

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g03_all.json"));
            _support = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g03_support.json"));
            AllG03TemplatesMapOrAreDataBlocked();
            NonCombatCardSelectionIsOutOfCombat();
            OutOfCombatDataGapStaysVisible();
            OnItemUsedSubjectFilters();
            PerformedActionTriggers();
            TriggerOrDoesNotDoubleCount();
            FightStartedEnchantsMatchingTagsOnly();
            RemainingCombatCards();
        }

        private static JsonValue Template(string name) { return _fixtures[name]["template"]; }
        private static string TemplateId(string name) { return _fixtures[name]["id"].AsString(); }

        private static LiveCardEntry Real(string name, int slot, string tier)
        {
            JsonValue template = Template(name);
            return new LiveCardEntry
            {
                TemplateId = TemplateId(name), Template = template, Tier = tier, Socket = slot,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry Support(string name, int slot, string tier)
        {
            JsonValue template = _support[name]["template"];
            return new LiveCardEntry
            {
                TemplateId = _support[name]["id"].AsString(), Template = template, Tier = tier, Socket = slot,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry Fake(string id, int slot, int cooldownMs, string tags, string size = "Small", int ammoMax = 0)
        {
            string attrs = "{\"CooldownMax\":" + cooldownMs;
            if (ammoMax > 0) attrs += ",\"AmmoMax\":" + ammoMax;
            attrs += "}";
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"" + size +
                "\",\"Tags\":" + tags + ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":" + attrs +
                ",\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = slot, Size = size,
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
                TemplateId = TemplateId(name), InstanceId = "g03-full-" + name,
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

        private static List<string> MapItemWarnings(string id, string tier, Side side)
        {
            JsonValue template = null;
            foreach (KeyValuePair<string, JsonValue> pair in _fixtures.AsObject())
                if (pair.Value["id"].AsString() == id) { template = pair.Value["template"]; break; }
            if (template == null) throw new ArgumentException("夹具缺少 " + id);
            LiveCardEntry entry = new LiveCardEntry
            {
                TemplateId = id, Template = template, Tier = tier, Socket = 0,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { entry }, EmptyCatalog, 10000, 10000, 1);
            if (side == Side.Enemy)
            {
                LoadoutItem loadout = mapped.Config.Player.Items[0];
                mapped.Config.Player.Items.Clear();
                loadout.Slot = 0;
                mapped.Config.Enemy.Items.Add(loadout);
            }
            mapped.Config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(mapped.Config);
            warnings.AddRange(NativeItemMapper.Apply(mapped.Config));
            // 缺少 AbilityId/AuraId 的警告落在 CardDef.MappingWarnings，不能漏收。
            foreach (CardDef card in mapped.Config.Cards.Values) warnings.AddRange(card.MappingWarnings);
            return warnings;
        }

        private static void AllG03TemplatesMapOrAreDataBlocked()
        {
            Assert.Equal(45, _fixtures.Count);
            Assert.Equal(4, DataBlocked.Count);
            int checkedCases = 0;
            foreach (KeyValuePair<string, JsonValue> pair in _fixtures.AsObject())
            {
                string name = pair.Key;
                string id = pair.Value["id"].AsString();
                JsonValue template = pair.Value["template"];
                bool dataBlocked = DataBlocked.Contains(id);
                bool outOfCombat = AllTriggersNonCombat(template);
                foreach (KeyValuePair<string, JsonValue> tier in template["Tiers"].AsObject())
                {
                    string tierName = tier.Key;
                    foreach (Side side in new[] { Side.Player, Side.Enemy })
                    {
                        if (NativeSkillSupport.Type(template) == "TCardSkill")
                        {
                            LiveSkillEntry skill = new LiveSkillEntry
                            {
                                TemplateId = id, InstanceId = "g03-all-" + id, Name = name,
                                Tier = tierName, Template = template, AttributesAreLive = false
                            };
                            SkillMappingResult mapped = SkillMapper.Apply(new BattleConfig(), new[] { skill }, null, side);
                            Assert.Equal(1, mapped.Skills.Count);
                            string status = mapped.Skills[0].Status;
                            Assert.True(status == "simulated" || status == "inactive" || status == "out-of-combat" ||
                                status == "live-applied");
                        }
                        else
                        {
                            List<string> warnings = MapItemWarnings(id, tierName, side);
                            if (dataBlocked)
                                Assert.True(warnings.Count > 0);
                            else if (!outOfCombat)
                                Assert.Equal("", string.Join(" | ", warnings.ToArray()));
                        }
                        checkedCases++;
                    }
                }
            }
            Assert.True(checkedCases >= 45);
        }

        // 所有主动触发都是场外（出售/每日/升级/选牌）时，入口告警不应被当成
        // 战斗缺口；Resonance Crystal 的缺失 Aura 0 属 G00/G11 场外数据。
        private static bool AllTriggersNonCombat(JsonValue template)
        {
            JsonValue abilities = template["Abilities"];
            if (abilities.Kind != JsonKind.Object || abilities.Count == 0) return false;
            foreach (KeyValuePair<string, JsonValue> ability in abilities.AsObject())
                if (!SkillMapper.IsNonCombatTrigger(ability.Value["Trigger"])) return false;
            return true;
        }

        private static void NonCombatCardSelectionIsOutOfCombat()
        {
            foreach (string name in new[] { "Machine Learning", "Renovation" })
            {
                JsonValue template = Template(name);
                foreach (KeyValuePair<string, JsonValue> tier in template["Tiers"].AsObject())
                {
                    foreach (Side side in new[] { Side.Player, Side.Enemy })
                    {
                        LiveSkillEntry skill = new LiveSkillEntry
                        {
                            TemplateId = TemplateId(name), InstanceId = "g03-out-" + name,
                            Name = name, Tier = tier.Key, Template = template, AttributesAreLive = false
                        };
                        SkillMappingResult mapped = SkillMapper.Apply(new BattleConfig(), new[] { skill }, null, side);
                        Assert.Equal(1, mapped.Skills.Count);
                        string status = mapped.Skills[0].Status;
                        Assert.True(status == "out-of-combat" || status == "inactive");
                        Assert.Equal(0, mapped.UnsupportedCount);
                    }
                }
            }
        }

        private static void OutOfCombatDataGapStaysVisible()
        {
            // Resonance Crystal 是纯场外卡，但缺 AuraId 0 不能被静默吞掉；
            // 保留 warning，由 G00/G11 记录，战斗范围按 OUT 处理。
            JsonValue template = Template("Resonance Crystal");
            Assert.True(AllTriggersNonCombat(template));
            List<string> warnings = MapItemWarnings(TemplateId("Resonance Crystal"), "Bronze", Side.Player);
            Assert.True(warnings.Count > 0);
            Assert.True(string.Join(" | ", warnings.ToArray()).Contains("光环定义缺失"));
        }

        private static TriggerPayload UsePayload(InternalItem source)
        {
            return new TriggerPayload
            {
                Event = "onUse", SourceSide = source.Side, SourceItemId = source.Id,
                TargetSide = source.Side, ChainId = 1, Depth = 0
            };
        }

        private static TriggerPayload PerformedPayload(string eventName, InternalItem source, InternalItem target)
        {
            return new TriggerPayload
            {
                Event = eventName, SourceSide = source.Side, SourceItemId = source.Id,
                TargetSide = target == null ? (Side?)null : target.Side,
                TargetItemId = target == null ? null : target.Id, ChainId = 1, Depth = 0
            };
        }

        private static void OnItemUsedSubjectFilters()
        {
            PistolSwordRequiresAmmoSource();
            CaltropsRequiresOpponentSource();
            AttachmentSetToolCombination();
            SkillTagAndSizeFilters();
        }

        private static void PistolSwordRequiresAmmoSource()
        {
            BattleConfig config = Board(1, Real("Pistol Sword", 0, "Gold"),
                Fake("g03-ammo", 1, 5000, "[]", "Small", 2),
                Fake("g03-plain", 2, 5000, "[]"),
                Fake("g03-enemy-ammo", 0, 5000, "[]", "Small", 2));
            MoveToEnemy(config, "g03-enemy-ammo");
            string cardId = IdOf(config, TemplateId("Pistol Sword"));
            AssertTriggerDamage(config, cardId, "g03-ammo", 15, 1);
            AssertTriggerDamage(config, cardId, "g03-plain", 15, 0);
            AssertTriggerDamage(config, cardId, "g03-enemy-ammo", 15, 0);
        }

        private static void CaltropsRequiresOpponentSource()
        {
            BattleConfig config = Board(1, Real("Caltrops", 0, "Diamond"),
                Fake("g03-caltrops-ally", 1, 5000, "[]"), Fake("g03-caltrops-foe", 0, 5000, "[]"));
            MoveToEnemy(config, "g03-caltrops-foe");
            string cardId = IdOf(config, TemplateId("Caltrops"));
            AssertTriggerDamage(config, cardId, "g03-caltrops-foe", 1, 1);
            AssertTriggerDamage(config, cardId, "g03-caltrops-ally", 1, 0);
        }

        private static void AttachmentSetToolCombination()
        {
            BattleConfig config = Board(1, Real("Attachment Set", 0, "Gold"),
                Fake("g03-tool", 1, 5000, "[\"Tool\"]"),
                Fake("g03-tool-weapon", 2, 5000, "[\"Tool\",\"Weapon\"]"),
                Fake("g03-plain", 3, 5000, "[]"));
            string cardId = IdOf(config, TemplateId("Attachment Set"));
            BattleContext toolCtx = BattleEngine.InitBattle(config);
            InternalItem tool = Item(toolCtx, IdOf(config, "g03-tool"));
            Triggers.Enqueue(toolCtx, UsePayload(tool));
            Triggers.DrainNativeFrame(toolCtx, 0.05);
            Assert.Equal(1, toolCtx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == cardId; }));
            Assert.Equal(0, toolCtx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "shield" && ev.SourceItemId == cardId; }));

            BattleContext weaponCtx = BattleEngine.InitBattle(config);
            InternalItem weapon = Item(weaponCtx, IdOf(config, "g03-tool-weapon"));
            Triggers.Enqueue(weaponCtx, UsePayload(weapon));
            Triggers.DrainNativeFrame(weaponCtx, 0.05);
            Assert.Equal(0, weaponCtx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == cardId; }));
            Assert.Equal(1, weaponCtx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "shield" && ev.SourceItemId == cardId; }));

            BattleContext plainCtx = BattleEngine.InitBattle(config);
            InternalItem plain = Item(plainCtx, IdOf(config, "g03-plain"));
            Triggers.Enqueue(plainCtx, UsePayload(plain));
            Triggers.DrainNativeFrame(plainCtx, 0.05);
            Assert.Equal(0, plainCtx.Events.Count(delegate(BattleEvent ev)
                { return (ev.Kind == "damage" || ev.Kind == "shield") && ev.SourceItemId == cardId; }));
        }

        private static void SkillTagAndSizeFilters()
        {
            AssertSkillUseFilter("Relax Bro", "Gold", "g03-small", "Small", "[]", "g03-large", "Large", "[]", "regen", 3);
            AssertSkillUseFilter("Pyromania", "Gold", "g03-large", "Large", "[]", "g03-small", "Small", "[]", "burn", 20);
            AssertSkillUseFilter("Underwater Wonder", "Gold", "g03-aquatic", "Small", "[\"Aquatic\"]", "g03-dry", "Small", "[]", "regen", 2);
            AssertSkillUseFilter("Toxic Friendship", "Gold", "g03-friend", "Small", "[\"Friend\"]", "g03-stranger", "Small", "[]", "poison", 2);
            AssertSkillUseFilter("Healthy Friendship", "Gold", "g03-friend", "Small", "[\"Friend\"]", "g03-stranger", "Small", "[]", "regen", 2);
        }

        private static void AssertSkillUseFilter(string name, string tier, string matchId, string matchSize, string matchTags,
            string missId, string missSize, string missTags, string expectedKind, double expectedValue)
        {
            BattleConfig config = Board(1, Fake(matchId, 0, 5000, matchTags, matchSize), Fake(missId, 1, 5000, missTags, missSize));
            string skillId = MapSkill(config, name, tier, Side.Player);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem match = Item(ctx, IdOf(config, matchId));
            Triggers.Enqueue(ctx, UsePayload(match));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> hits = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == expectedKind && ev.SourceItemId == skillId; });
            Assert.Equal(1, hits.Count);
            Assert.Near(expectedValue, hits[0].Value.Value);

            BattleContext missCtx = BattleEngine.InitBattle(config);
            InternalItem miss = Item(missCtx, IdOf(config, missId));
            Triggers.Enqueue(missCtx, UsePayload(miss));
            Triggers.DrainNativeFrame(missCtx, 0.05);
            Assert.Equal(0, missCtx.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == expectedKind && ev.SourceItemId == skillId; }));
        }

        private static void AssertTriggerDamage(BattleConfig config, string cardId, string sourceBaseId, double expectedValue, int expectedCount)
        {
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = Item(ctx, IdOf(config, sourceBaseId));
            Triggers.Enqueue(ctx, UsePayload(source));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> hits = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "damage" && ev.SourceItemId == cardId; });
            Assert.Equal(expectedCount, hits.Count);
            if (expectedCount > 0) Assert.Near(expectedValue, hits[0].Value.Value);
        }

        private static void PerformedActionTriggers()
        {
            AssertPerformed("Sauna", "Gold", "onBurnApplied", new[] { "heal:30", "regen:4" });
            AssertPerformed("Heat Lover", "Gold", "onBurnApplied", new[] { "regen:2" });
            AssertPerformed("Poison Tyrant", "Gold", "onPoisonApplied", new[] { "regen:2" });
            AssertPerformed("Ouroboros Statue", "Gold", "onPoisonApplied", new[] { "regen:6" });
            AssertPerformed("Aged Cask", "Gold", "onSlow", new[] { "heal:20" });
            AssertPerformed("Tropical Island", "Gold", "onSlow", new[] { "regen:10" });
            AssertPerformed("Proboscis", "Gold", "onSlow", new[] { "damage:12" });
            AssertPerformed("Lightning Rod", "Gold", "onFreeze", new[] { "damage:25" });
            AssertPerformed("Lightning Rod", "Gold", "onSlow", new[] { "damage:25" });
        }

        private static void AssertPerformed(string name, string tier, string eventName, string[] expected)
        {
            JsonValue template = Template(name);
            bool skill = NativeSkillSupport.Type(template) == "TCardSkill";
            BattleConfig config = skill
                ? Board(1, Fake("g03-performed-actor", 0, 5000, "[]"))
                : Board(1, Real(name, 0, tier), Fake("g03-performed-actor", 1, 5000, "[]"));
            string cardId;
            if (skill) cardId = MapSkill(config, name, tier, Side.Player);
            else cardId = IdOf(config, TemplateId(name));
            BattleContext ctx = BattleEngine.InitBattle(config);
            // 治疗事件记录的是实际接受量；先压低生命，避免满血时把 30 治疗记成 0。
            ctx.Player.Hp = 1;
            InternalItem actor = Item(ctx, IdOf(config, "g03-performed-actor"));
            InternalItem target = skill ? null : Item(ctx, cardId);
            Triggers.Enqueue(ctx, PerformedPayload(eventName, actor, target));
            Triggers.DrainNativeFrame(ctx, 0.05);
            foreach (string item in expected)
            {
                string[] parts = item.Split(':');
                string kind = parts[0];
                double value = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                List<BattleEvent> hits = ctx.Events.FindAll(delegate(BattleEvent ev)
                    { return ev.Kind == kind && ev.SourceItemId == cardId; });
                Assert.Equal(1, hits.Count);
                Assert.Near(value, hits[0].Value.Value);
            }
        }

        private static void FightStartedEnchantsMatchingTagsOnly()
        {
            // 使用带完整 Enchantments 定义的原生卡，随机池只剩合法附魔，
            // 目标选择因此可断言；辅助 Drone 来自 native_g03_support.json。
            AssertFightStartedEnchant("Happy Accident",
                Support("Repair Drone", 0, "Gold"), Real("Boosted Saucer", 1, "Silver"));
            AssertFightStartedEnchant("Experimental Chef",
                Real("Pizza", 0, "Silver"), Real("Attachment Set", 1, "Silver"));
        }

        private static void AssertFightStartedEnchant(string name, LiveCardEntry first, LiveCardEntry second)
        {
            BattleConfig config = Board(0.2, first, second, Fake("g03-start-plain", 2, 5000, "[]"));
            string skillId = MapSkill(config, name, "Diamond", Side.Player);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<BattleEvent> enchants = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "enchant" && ev.SourceItemId == skillId; });
            Assert.Equal(2, enchants.Count);
            Assert.True(enchants.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == IdOf(config, first.TemplateId); }));
            Assert.True(enchants.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == IdOf(config, second.TemplateId); }));
            Assert.False(enchants.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == IdOf(config, "g03-start-plain"); }));
        }

        private static void RemainingCombatCards()
        {
            // Tinfoil Hat：仅敌方使用物品时给自己护盾。
            BattleConfig hatConfig = Board(1, Real("Tinfoil Hat", 0, "Diamond"),
                Fake("g03-hat-ally", 1, 5000, "[]"), Fake("g03-hat-foe", 0, 5000, "[]"));
            MoveToEnemy(hatConfig, "g03-hat-foe");
            string hatId = IdOf(hatConfig, TemplateId("Tinfoil Hat"));
            AssertUseEffect(hatConfig, hatId, "g03-hat-foe", "shield", 1, 1);
            AssertUseEffect(hatConfig, hatId, "g03-hat-ally", "shield", 1, 0);

            // Spiked Collar：仅自己使用 Friend 时造成伤害。
            BattleConfig collarConfig = Board(1, Real("Spiked Collar", 0, "Gold"),
                Fake("g03-collar-friend", 1, 5000, "[\"Friend\"]"),
                Fake("g03-collar-plain", 2, 5000, "[]"),
                Fake("g03-collar-foe", 0, 5000, "[\"Friend\"]"));
            MoveToEnemy(collarConfig, "g03-collar-foe");
            string collarId = IdOf(collarConfig, TemplateId("Spiked Collar"));
            AssertUseEffect(collarConfig, collarId, "g03-collar-friend", "damage", 15, 1);
            AssertUseEffect(collarConfig, collarId, "g03-collar-plain", "damage", 15, 0);
            AssertUseEffect(collarConfig, collarId, "g03-collar-foe", "damage", 15, 0);

            // Unwavering：自己使用其他物品时护盾；敌方使用不触发。
            BattleConfig unwaverConfig = Board(1, Fake("g03-unwaver-ally", 0, 5000, "[]"), Fake("g03-unwaver-foe", 0, 5000, "[]"));
            MoveToEnemy(unwaverConfig, "g03-unwaver-foe");
            string unwaverId = MapSkill(unwaverConfig, "Unwavering", "Gold", Side.Player);
            AssertUseEffect(unwaverConfig, unwaverId, "g03-unwaver-ally", "shield", 20, 1);
            AssertUseEffect(unwaverConfig, unwaverId, "g03-unwaver-foe", "shield", 20, 0);

            // Myrrh / Hemlock：自身出手的主动数值。
            AssertSelfActivation("Myrrh", "Gold", "regen", 5);
            AssertSelfActivation("Hemlock", "Gold", "poison", 4);

            // Pizza：Heated 前提控制 Burn；无 Heated 只 Regen。
            AssertPizzaBurn(true, 4);
            AssertPizzaBurn(false, 0);
        }

        private static void AssertUseEffect(BattleConfig config, string sourceId, string actorBaseId, string kind, double value, int expected)
        {
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem actor = Item(ctx, IdOf(config, actorBaseId));
            Triggers.Enqueue(ctx, UsePayload(actor));
            Triggers.DrainNativeFrame(ctx, 0.05);
            List<BattleEvent> hits = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == kind && ev.SourceItemId == sourceId; });
            Assert.Equal(expected, hits.Count);
            if (expected > 0) Assert.Near(value, hits[0].Value.Value);
        }

        private static void AssertSelfActivation(string name, string tier, string kind, double value)
        {
            BattleConfig config = Board(0.3, Real(name, 0, tier));
            config.Rules.StartReady = true;
            string cardId = IdOf(config, TemplateId(name));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<BattleEvent> hits = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == kind && ev.SourceItemId == cardId; });
            Assert.Equal(1, hits.Count);
            Assert.Near(value, hits[0].Value.Value);
        }

        private static void AssertPizzaBurn(bool heated, double expectedBurn)
        {
            BattleConfig config = Board(0.3, Real("Pizza", 0, "Gold"));
            config.Rules.StartReady = true;
            string cardId = IdOf(config, TemplateId("Pizza"));
            config.Cards[cardId].MulticastBonus = 0;
            config.Cards[cardId].HasMulticastOverride = true;
            if (heated) config.Cards[cardId].LiveAttributes["Heated"] = 1;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<BattleEvent> burns = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "burn" && ev.SourceItemId == cardId; });
            Assert.Equal(expectedBurn > 0 ? 1 : 0, burns.Count);
            if (expectedBurn > 0) Assert.Near(expectedBurn, burns[0].Value.Value);
            Assert.Equal(1, result.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "regen" && ev.SourceItemId == cardId; }));
        }

        private static void TriggerOrDoesNotDoubleCount()
        {
            // Lightning Rod 是物品，不是技能；TTriggerOr 展开为 Freeze/Slow 两个触发。
            BattleConfig config = Board(1, Real("Lightning Rod", 0, "Gold"), Fake("g03-or-actor", 1, 5000, "[]"));
            string cardId = IdOf(config, TemplateId("Lightning Rod"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem actor = Item(ctx, IdOf(config, "g03-or-actor"));

            Triggers.Enqueue(ctx, PerformedPayload("onFreeze", actor, null));
            Triggers.DrainNativeFrame(ctx, 0.05);
            Assert.Equal(1, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == cardId; }));

            Triggers.Enqueue(ctx, PerformedPayload("onSlow", actor, null));
            Triggers.DrainNativeFrame(ctx, 0.1);
            Assert.Equal(2, ctx.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == cardId; }));
        }
    }
}
