using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    public enum NativeSkillTier { Gold }
    public enum NativeSkillAttribute { Custom_0, CritChance }
    public enum NativePlayerAttribute { Health, HealthMax, CritChance }

    public sealed class NativeSkillInstance
    {
        public string Value;
    }

    public sealed class NativeSkillPlayer
    {
        public Dictionary<NativePlayerAttribute, int> Attributes =
            new Dictionary<NativePlayerAttribute, int>();
        public List<object> Skills = new List<object>();
    }

    public sealed class NativeSkillRun
    {
        public NativeSkillPlayer Player;
        public NativeSkillOpponent Opponent;
    }

    public sealed class NativeSkillOpponent
    {
        public object Hero;
    }

    public class NativeSkillCard
    {
        public NativeSkillInstance InstanceId = new NativeSkillInstance { Value = "skill-instance-1" };
        public Guid TemplateId = new Guid("ABCDEF12-3456-7890-ABCD-ABCDEF123456");
        public NativeSkillTier Tier = NativeSkillTier.Gold;
        public Dictionary<NativeSkillAttribute, int> Attributes =
            new Dictionary<NativeSkillAttribute, int>();
        public object Template;
        public string LocalizedTitle;
        public string LocalizedDescription;
        public bool TooltipFailure;
    }

    public sealed class NativeSkillBrokenAttributes
    {
        public string InstanceId { get { return "broken"; } }
        public string TemplateId { get { return "broken-template"; } }
        public string Tier { get { return "Gold"; } }
        public object Attributes { get { throw new InvalidOperationException("attribute read failure"); } }
    }

    public static class LiveSkillReaderTests
    {
        public static void Run()
        {
            try
            {
                ReadsNativeStateAndPolymorphicTemplate();
                PreservesMalformedSkillsAndContinuesReading();
                FallsBackToTemplateText();
                ClearsStaleStateWhenRunUnavailable();
            }
            finally { TheBazaar.Data.Run = null; }
        }

        private static NativeSkillRun SetRun()
        {
            NativeSkillRun run = new NativeSkillRun();
            run.Player = new NativeSkillPlayer();
            run.Player.Attributes[NativePlayerAttribute.Health] = 900;
            run.Player.Attributes[NativePlayerAttribute.HealthMax] = 1075;
            run.Player.Attributes[NativePlayerAttribute.CritChance] = 5;
            run.Opponent = new NativeSkillOpponent { Hero = "Pygmalien" };
            TheBazaar.Data.Run = run;
            return run;
        }

        private static NativeSkillCard Skill()
        {
            NativeSkillCard skill = new NativeSkillCard();
            skill.Attributes[NativeSkillAttribute.Custom_0] = 25;
            skill.LocalizedTitle = "教学时刻";
            skill.LocalizedDescription = "每个技能增加 25 点生命\n当前：\"75\"";
            skill.Template = new BazaarGameShared.Fakes.TCardSkillFake
            {
                InternalName = "Teachable Moment",
                Localization = new BazaarGameShared.Fakes.NativeLocalization
                {
                    Title = new BazaarGameShared.Fakes.NativeText { Text = "Teachable Moment" },
                    Tooltips = new List<BazaarGameShared.Fakes.NativeTooltip>
                    {
                        new BazaarGameShared.Fakes.NativeTooltip
                        {
                            Content = new BazaarGameShared.Fakes.NativeText { Text = "Gain {aura.0} Max Health" }
                        }
                    }
                },
                Tiers = new Dictionary<NativeSkillTier, BazaarGameShared.Fakes.NativeTier>
                {
                    { NativeSkillTier.Gold, new BazaarGameShared.Fakes.NativeTier
                        { AuraIds = new string[] { "0" }, AbilityIds = new string[0] } }
                },
                Auras = new Dictionary<string, object>
                {
                    { "0", new BazaarGameShared.Fakes.TCardAuraFake
                        { Action = new BazaarGameShared.Fakes.TAuraActionPlayerModifyAttributeFake
                            { AttributeType = "HealthMax", Value = new BazaarGameShared.Fakes.TFixedValueFake
                                { Value = 75 } } } }
                }
            };
            return skill;
        }

        private static void ReadsNativeStateAndPolymorphicTemplate()
        {
            NativeSkillRun run = SetRun();
            run.Player.Skills.Add(Skill());
            BoardReadResult result = new BoardReadResult();
            result.Ok = true;
            result.Cards.Add(new LiveCardEntry { TemplateId = "item", Socket = 3 });
            LiveSkillReader.Populate(result);

            Assert.True(result.Ok);
            Assert.Equal<string>(null, result.SkillReadError);
            Assert.Near(900, result.PlayerHealth.Value);
            Assert.Near(1075, result.PlayerMaxHealth.Value);
            Assert.Equal("Pygmalien", result.EnemyHero);
            Assert.Equal(5, result.PlayerAttributes["CritChance"]);
            Assert.Equal(1, result.Skills.Count);
            LiveSkillEntry skill = result.Skills[0];
            Assert.Equal("skill-instance-1", skill.InstanceId);
            Assert.Equal("abcdef12-3456-7890-abcd-abcdef123456", skill.TemplateId);
            Assert.Equal("Gold", skill.Tier);
            Assert.Equal("教学时刻", skill.Name);
            Assert.Equal(25, skill.Attributes["Custom_0"]);
            Assert.Equal(0, skill.Order);
            Assert.Equal("TCardSkillFake", skill.Template["$type"].AsString());
            Assert.Equal("0", skill.Template["Tiers"]["Gold"]["AuraIds"][0].AsString());
            Assert.Equal("TAuraActionPlayerModifyAttributeFake",
                skill.Template["Auras"]["0"]["Action"]["$type"].AsString());
            Assert.Near(75, skill.Template["Auras"]["0"]["Action"]["Value"]["Value"].AsDouble());

            JsonValue dump = Json.Parse(LiveSkillReader.BuildDebugJson(result));
            Assert.Equal(1, dump["skills"].Count);
            Assert.Equal(1, dump["board"].Count);
            Assert.Equal("item", dump["board"][0]["TemplateId"].AsString());
            Assert.Equal("Pygmalien", dump["enemyHero"].AsString());
            Assert.Near(1075, dump["playerMaxHealth"].AsDouble());
            Assert.Equal(((NativeSkillCard)run.Player.Skills[0]).LocalizedDescription,
                dump["skills"][0]["rawTooltips"][0].AsString());
            Assert.Equal(JsonKind.Null, dump["skillReadError"].Kind);
        }

        private static void PreservesMalformedSkillsAndContinuesReading()
        {
            NativeSkillRun run = SetRun();
            NativeSkillCard cyclic = Skill();
            BazaarGameShared.Fakes.TCardSkillFake template =
                (BazaarGameShared.Fakes.TCardSkillFake)cyclic.Template;
            template.Extra = template;
            run.Player.Skills.Add(cyclic);
            run.Player.Skills.Add(new NativeSkillBrokenAttributes());
            run.Player.Skills.Add(Skill());
            BoardReadResult result = new BoardReadResult();
            result.Ok = true;
            LiveSkillReader.Populate(result);

            Assert.True(result.Ok);
            Assert.Equal(2, result.Skills.Count);
            Assert.Equal("教学时刻", result.Skills[0].Name);
            Assert.Equal(25, result.Skills[0].Attributes["Custom_0"]);
            Assert.Equal<JsonValue>(null, result.Skills[0].Template);
            Assert.True(result.Skills[1].Template != null);
            Assert.Equal(2, result.Skills[1].Order);
            Assert.True(result.SkillReadError.Contains("循环引用"));
            Assert.True(result.SkillReadError.Contains("attribute read failure"));
            JsonValue dump = Json.Parse(LiveSkillReader.BuildDebugJson(result));
            Assert.Equal(2, dump["skills"].Count);
            Assert.Equal(JsonKind.Null, dump["skills"][0]["template"].Kind);
        }

        private static void FallsBackToTemplateText()
        {
            NativeSkillRun run = SetRun();
            NativeSkillCard skill = Skill();
            skill.TooltipFailure = true;
            run.Player.Skills.Add(skill);
            BoardReadResult result = new BoardReadResult();
            LiveSkillReader.Populate(result);
            Assert.Equal("Teachable Moment", result.Skills[0].Name);
            Assert.Equal("Gain {aura.0} Max Health", result.Skills[0].RawTooltips[0]);
            Assert.Equal<string>(null, result.SkillReadError);
        }

        private static void ClearsStaleStateWhenRunUnavailable()
        {
            NativeSkillRun run = SetRun();
            run.Player.Skills.Add(Skill());
            BoardReadResult result = new BoardReadResult();
            LiveSkillReader.Populate(result);
            TheBazaar.Data.Run = null;
            LiveSkillReader.Populate(result);
            Assert.Equal(0, result.Skills.Count);
            Assert.Equal(0, result.PlayerAttributes.Count);
            Assert.Equal<string>(null, result.EnemyHero);
            Assert.False(result.PlayerHealth.HasValue);
            Assert.False(result.PlayerMaxHealth.HasValue);
            Assert.True(result.SkillReadError.Contains("当前没有玩家状态"));
        }
    }
}

namespace TheBazaar
{
    public static class Data
    {
        public static object Run { get; set; }
        public static object Entities { get; set; }
        public static object CurrentState { get; set; }
        public static bool IsInCombat { get; set; }
        public static object StaticManager { get; set; }
        public static object GetStatic() { return StaticManager; }
        public static List<BazaarGameClient.Domain.Models.Cards.Card> VerificationPlayerCards = new List<BazaarGameClient.Domain.Models.Cards.Card>();
        public static List<BazaarGameClient.Domain.Models.Cards.Card> VerificationOpponentCards = new List<BazaarGameClient.Domain.Models.Cards.Card>();
        public static List<T> GetCards<T>(BazaarActionAxis.Tests.VerificationSide side, object section) where T : class
        {
            List<T> result = new List<T>();
            foreach (object card in side == BazaarActionAxis.Tests.VerificationSide.Player ? VerificationPlayerCards : VerificationOpponentCards)
                if (card is T) result.Add((T)card);
            return result;
        }
    }
}

namespace TheBazaar.Tooltips
{
    public sealed class CardTooltipData
    {
        private BazaarActionAxis.Tests.NativeSkillCard _card;

        public static CardTooltipData CreateCardTooltipData(object nativeCard)
        {
            BazaarActionAxis.Tests.NativeSkillCard card =
                nativeCard as BazaarActionAxis.Tests.NativeSkillCard;
            if (card.TooltipFailure) throw new InvalidOperationException("tooltip unavailable");
            return new CardTooltipData { _card = card };
        }

        public string GetTitle() { return _card.LocalizedTitle; }
        public string BuildCombinedEffectDescription() { return _card.LocalizedDescription; }
    }
}

namespace BazaarGameShared.Fakes
{
    public sealed class TCardSkillFake
    {
        public string InternalName { get; set; }
        public NativeLocalization Localization { get; set; }
        public Dictionary<BazaarActionAxis.Tests.NativeSkillTier, NativeTier> Tiers { get; set; }
        public Dictionary<string, object> Auras { get; set; }
        public object Extra { get; set; }
    }

    public sealed class NativeLocalization
    {
        public NativeText Title { get; set; }
        public List<NativeTooltip> Tooltips { get; set; }
    }

    public sealed class NativeText { public string Text { get; set; } }
    public sealed class NativeTooltip { public NativeText Content { get; set; } }
    public sealed class NativeTier
    {
        public string[] AuraIds { get; set; }
        public string[] AbilityIds { get; set; }
    }
    public sealed class TCardAuraFake { public object Action { get; set; } }
    public sealed class TAuraActionPlayerModifyAttributeFake
    {
        public string AttributeType { get; set; }
        public object Value { get; set; }
    }
    public sealed class TFixedValueFake { public double Value { get; set; } }
}
