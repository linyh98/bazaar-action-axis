using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G06 全量守卫：136 个主责模板必须进入映射矩阵且不再出现 unsupported；
    // 另对 Rage / PercentDamageReduction / HealthMax 触发 / 动态暴怒时长 /
    // 缺定义降级 / 经济触发做行为回归。
    public static class G06FullCoverageTests
    {
        private const string FixturePath = "tools/actionaxis_plugin/tests/fixtures/native_g06_templates.json";
        private const string Header = "kind\ttable\tid\tname\ttier\ttier_source\tenchantment\tside\tscenario\tattributes_live\tplayer_hero\tenemy_hero\tstatus\tenchantment_status\tlive_attribute_status\tenchantment_warning\tlive_attribute_warning\treason";
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());
        private static JsonValue _allFixtures;

        public static void Run()
        {
            _allFixtures = Json.Parse(File.ReadAllText(FixturePath));
            AllG06TemplatesMapWithoutUnsupported();
            PercentDamageReductionReducesAndExpires();
            HealthMaxGainEmitsPlayerAttributeChange();
            RageModifyActionChangesRageAndCanEnrage();
            BaseRageClearsControlAndAddsCooldownReduction();
            MissingDefinitionsAreSkippedWithWarnings();
            NonCombatGoldAndCardSelectedAreOutOfCombat();
            DynamicEnragedDurationAuraChangesWindow();
        }

        private static void AllG06TemplatesMapWithoutUnsupported()
        {
            JsonValue fixtures = Json.Parse(File.ReadAllText(FixturePath));
            List<JsonValue> rows = new List<JsonValue>(fixtures.AsObject().Values);
            Assert.Equal(136, rows.Count);
            string output = Path.Combine(Path.GetTempPath(), "g06_full_" + Guid.NewGuid().ToString("N") + ".tsv");
            TextWriter original = Console.Out;
            StringWriter captured = new StringWriter();
            Console.SetOut(captured);
            try { Assert.Equal(0, NativeCoverageRunner.Run(rows, output)); }
            finally { Console.SetOut(original); }
            try
            {
                string[] lines = File.ReadAllLines(output);
                Assert.True(lines.Length > 1);
                Assert.Equal(Header, lines[0]);
                HashSet<string> seen = new HashSet<string>();
                List<string> unsupported = new List<string>();
                for (int index = 1; index < lines.Length; index++)
                {
                    if (lines[index].Trim().Length == 0) continue;
                    string[] cells = lines[index].Split('\t');
                    if (cells.Length < 18) continue;
                    seen.Add(cells[2]);
                    if (cells[12] == "unsupported" || cells[12] == "exception")
                        unsupported.Add(cells[3] + " / " + cells[2] + " / " + cells[12] + " / " + cells[17]);
                }
                Assert.Equal(136, seen.Count);
                Assert.Equal(0, unsupported.Count);
                if (unsupported.Count > 0)
                    Assert.Equal("", string.Join(" | ", unsupported.GetRange(0, Math.Min(10, unsupported.Count)).ToArray()));
            }
            finally { File.Delete(output); }
        }

        private static BattleConfig BaseConfig()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000, MaxTime = 5 };
            CardDef player = new CardDef { Id = "g06-player", Cooldown = 100 };
            config.Cards[player.Id] = player;
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id, Slot = 0 });
            CardDef enemy = new CardDef { Id = "g06-enemy", Cooldown = 100 };
            config.Cards[enemy.Id] = enemy;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemy.Id, Slot = 0 });
            return config;
        }

        private static Effect EffectFor(string kind, string attribute, string operation, double value, double seconds)
        {
            return new Effect
            {
                Kind = kind, Attribute = attribute, Operation = operation, Seconds = seconds,
                Amount = new NumExpr { Type = "const", Value = value },
                Target = new Target { Kind = "selfHero" }
            };
        }

        private static void PercentDamageReductionReducesAndExpires()
        {
            BattleConfig config = BaseConfig();
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = ctx.Items[0];
            Effects.ResolveEffect(ctx, EffectFor("playerAttribute", "PercentDamageReduction", "Add", 50, 1), source, 0, 0, null);
            DamageSource enemy = new DamageSource { Side = Side.Enemy, Id = "g06-enemy", CardId = "g06-enemy" };
            ctx.Player.Hp = 1000;
            Effects.ApplyDamage(ctx, enemy, Side.Player, 100, 0.5, new DamageOptions());
            Assert.Near(950, ctx.Player.Hp);
            ctx.Player.Hp = 1000;
            Effects.ApplyDamage(ctx, enemy, Side.Player, 100, 1.1, new DamageOptions());
            Assert.Near(900, ctx.Player.Hp);
        }

        private static LiveSkillEntry Skill(string json)
        {
            JsonValue template = Json.Parse(json);
            return new LiveSkillEntry
            {
                TemplateId = "g06:test-skill", Tier = "Gold", Template = template, AttributesAreLive = false,
                TemplateBoard = true,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold")
            };
        }

        private static LiveSkillEntry SkillFromFixture(string id, string tier)
        {
            JsonValue row = _allFixtures[id];
            JsonValue template = row["template"];
            return new LiveSkillEntry
            {
                TemplateId = id, Tier = tier, Template = template, AttributesAreLive = false,
                TemplateBoard = true,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static void HealthMaxGainEmitsPlayerAttributeChange()
        {
            const string json = "{\"$type\":\"TCardSkill\",\"Size\":\"Medium\",\"Tiers\":{\"Gold\":{\"Attributes\":{},\"AbilityIds\":[\"0\"],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Trigger\":{\"$type\":\"TTriggerOnPlayerAttributeChanged\",\"Subject\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\"},\"AttributeType\":\"HealthMax\",\"ChangeType\":\"Gain\",\"Source\":null},\"Action\":{\"$type\":\"TActionPlayerModifyAttribute\",\"AttributeType\":\"HealthRegen\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":1.0},\"Duration\":null,\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\"}}}}}";
            BattleConfig config = BaseConfig();
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(json) }, null, Side.Player);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Effects.ResolveEffect(ctx, EffectFor("playerAttribute", "HealthMax", "Add", 10, 0), ctx.Items[0], 1, 0, null);
            Triggers.Drain(ctx, 1);
            Assert.Near(1, ctx.Player.Regen);
            List<BattleEvent> changes = ctx.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "playerAttribute" && ev.Note == "HealthMax" && ev.Value.Value >= 10; });
            Assert.True(changes.Count >= 1);
        }

        private static void RageModifyActionChangesRageAndCanEnrage()
        {
            const string json = "{\"$type\":\"TCardSkill\",\"Size\":\"Medium\",\"Tiers\":{\"Gold\":{\"Attributes\":{},\"AbilityIds\":[\"0\"],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Trigger\":{\"$type\":\"TTriggerOnItemUsed\",\"Subject\":{\"$type\":\"TTargetCardSection\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":false,\"Conditions\":null}},\"Action\":{\"$type\":\"TActionPlayerModifyAttribute\",\"AttributeType\":\"Rage\",\"Operation\":\"Subtract\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":5.0},\"Duration\":null,\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Self\"}}}}}";
            BattleConfig config = BaseConfig();
            config.PlayerRageMax = 20;
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(json) }, null, Side.Player);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            ctx.Player.Rage = 20;
            Effects.ResolveEffect(ctx, EffectFor("playerAttribute", "Rage", "Subtract", 5, 0), ctx.Items[0], 1, 0, null);
            Assert.Near(15, ctx.Player.Rage);
            Effects.ResolveEffect(ctx, EffectFor("playerAttribute", "Rage", "Add", 5, 0), ctx.Items[0], 2, 0, null);
            Assert.Near(0, ctx.Player.Rage);
            Assert.True(ctx.Player.EnragedUntil > 0);
        }

        private static void BaseRageClearsControlAndAddsCooldownReduction()
        {
            BattleConfig config = BaseConfig();
            CardDef card = config.Cards["g06-player"];
            card.Cooldown = 10;
            card.LiveAttributes["CooldownMax"] = 10000;
            config.PlayerRageMax = 1;
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            item.SlowUntil = 99;
            item.FrozenUntil = 99;
            ctx.Player.Rage = 1;
            Effects.TryEnrage(ctx, ctx.Player, 0, 0, null);
            Assert.True(item.SlowUntil <= 0);
            Assert.True(item.FrozenUntil <= 0);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(9, item.CooldownMax);
            BattleEngine.TickEnrage(ctx, ctx.Player.EnragedUntil);
            Assert.Near(10, item.CooldownMax);
            Assert.True(item.BaseRageCooldownMax < 0);
        }

        private static void MissingDefinitionsAreSkippedWithWarnings()
        {
            const string json = "{\"$type\":\"TCardItem\",\"Size\":\"Small\",\"Tags\":[],\"Tiers\":{\"Gold\":{\"Attributes\":{\"CooldownMax\":1000,\"DamageAmount\":10},\"AbilityIds\":[\"0\",\"9\"],\"AuraIds\":[\"0\",\"9\"],\"TooltipIds\":[]}},\"Abilities\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Trigger\":{\"$type\":\"TTriggerOnCardFired\"},\"Action\":{\"$type\":\"TActionPlayerDamage\",\"Target\":{\"$type\":\"TTargetPlayerRelative\",\"TargetMode\":\"Opponent\"}}}},\"Auras\":{\"0\":{\"ActiveIn\":\"HandOnly\",\"WorksIn\":\"Anywhere\",\"Action\":{\"$type\":\"TAuraActionCardModifyAttribute\",\"AttributeType\":\"DamageAmount\",\"Operation\":\"Add\",\"Value\":{\"$type\":\"TFixedValue\",\"Value\":1.0},\"Target\":{\"$type\":\"TTargetCardSelf\",\"Conditions\":null}}}}}";
            JsonValue template = Json.Parse(json);
            LiveCardEntry entry = new LiveCardEntry
            {
                TemplateId = "g06:missing-def", Template = template, Tier = "Gold", Socket = 0, Size = "Small",
                AttributesAreLive = false, Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold")
            };
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { entry }, EmptyCatalog, 1000, 1000, 5).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            CardDef card = config.Cards[config.Player.Items[0].CardId];
            Assert.True(card.MappingWarnings.Count > 0);
            Assert.True(string.Join(" | ", card.MappingWarnings.ToArray()).Contains("9"));
            Assert.True(card.Effects.Count > 0);
            Assert.Equal(0, warnings.Count);
        }

        private static void NonCombatGoldAndCardSelectedAreOutOfCombat()
        {
            foreach (string id in new[] { "c6a57594-4358-44de-b888-64216df4aa05", "5ff845f5-6cb5-40b8-957f-8a6ae47c3962" })
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { SkillFromFixture(id, "Gold") }, null, Side.Player);
                Assert.Equal("out-of-combat", mapped.Skills[0].Status);
            }
        }

        private static void DynamicEnragedDurationAuraChangesWindow()
        {
            foreach (string id in new[] { "00e1526a-0eb6-49af-93fd-e5e4aea5f05a", "8b583fac-46a1-4531-8026-efd57232571a" })
            {
                BattleConfig config = BaseConfig();
                config.PlayerRageMax = 1;
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { SkillFromFixture(id, "Diamond") }, null, Side.Player);
                Assert.Equal("simulated", mapped.Skills[0].Status);
                BattleContext ctx = BattleEngine.InitBattle(config);
                double expected = id == "00e1526a-0eb6-49af-93fd-e5e4aea5f05a" ? 4000 : 6000;
                Assert.Near(expected, ctx.Player.EnragedDurationMs);
                ctx.Player.Rage = 1;
                Effects.TryEnrage(ctx, ctx.Player, 0, 0, null);
                Assert.Near(expected / 1000.0, ctx.Player.EnragedUntil);
            }
        }
    }
}
