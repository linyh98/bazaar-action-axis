using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeSkillTierTests
    {
        public static void Run()
        {
            JsonValue fixture = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_kyver_monster.json"));
            LiveMonsterEntry monster = MonsterTemplateExtractor.FromJson(fixture["monster"],
                delegate(string id) { return fixture["cards"][id]; });
            LiveSkillEntry original = monster.Skills[0];
            foreach (string tier in new[] { "Bronze", "Silver", "Gold", "Diamond", "Legendary", "Unknown" })
            {
                LiveSkillEntry skill = new LiveSkillEntry { TemplateId = original.TemplateId, Template = original.Template,
                    InstanceId = "counterstrike-tier", Tier = tier, Name = original.Name };
                BattleConfig config = new BattleConfig { NativeMechanics = true };
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Enemy);
                SkillMappingStatus status = mapped.Skills[0];
                Assert.Equal(tier, status.Tier);
                if (tier == "Bronze" || tier == "Silver")
                {
                    Assert.Equal("inactive", status.Status);
                    Assert.True(status.NativeTierHasNoEffects);
                    Assert.Equal(0, mapped.UnsupportedCount);
                    Assert.Equal(0, config.EnemySkills.Count);
                    Assert.Equal(0, mapped.Warnings.Count);
                }
                else if (tier == "Unknown")
                {
                    Assert.Equal("unsupported", status.Status);
                    Assert.Equal(1, mapped.UnsupportedCount);
                }
                else
                {
                    Assert.Equal("simulated", status.Status);
                    Assert.False(status.NativeTierHasNoEffects);
                    Assert.Equal(1, config.EnemySkills.Count);
                    Assert.Near(tier == "Gold" ? 1 : 2, config.EnemySkills[0].Triggers[0].Effects[0].Seconds);
                }
            }
        }
    }
}
