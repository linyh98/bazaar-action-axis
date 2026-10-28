using System.Collections.Generic;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayNativeWarningResolutionTests
    {
        public static void Run()
        {
            CompleteNativeMappingResolvesOnlyItsOwnLegacyWarning();
            FailedOrAbsentNativeMappingKeepsLegacyWarning();
            NativeDefinitionWarningsRemainVisible();
        }

        private static CardDef Legacy(string id)
        {
            CardDef card = new CardDef { Id = id + "@Bronze", BaseItemId = id,
                NameCn = "shared-name", Tier = "Bronze", Cooldown = 1 };
            card.Effects.Add(new Effect { Kind = "burn", Amount = new NumExpr { Value = 2 },
                Target = new Target { Kind = "enemyHero" } });
            card.Effects.Add(new Effect { Kind = "burn", Amount = new NumExpr { Value = 4 },
                Target = new Target { Kind = "enemyHero" } });
            return card;
        }

        private static JsonValue Template(string id, bool unsupported, bool missing)
        {
            string action = unsupported ? "TUnsupportedReplayAction" : "TActionPlayerBurnApply";
            return Json.Parse(@"{""$type"":""TCardItem"",""Id"":""" + id + @""",""Type"":""Item"",
                ""InternalName"":""native burn"",""Tags"":[],""HiddenTags"":[],
                ""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":1000,""BurnApplyAmount"":10},
                    ""AbilityIds"":[""0""" + (missing ? ",\"missing\"" : "") + @"],""AuraIds"":[]}},
                ""Abilities"":{""0"":{""Id"":""0"",""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere"",
                    ""Trigger"":{""$type"":""TTriggerOnCardFired""},
                    ""Action"":{""$type"":""" + action + @""",""ReferenceValue"":null,
                        ""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Opponent""}},
                    ""Priority"":""Medium""}},""Auras"":{}}");
        }

        private static LiveCardEntry Entry(string id, JsonValue template, int slot)
        {
            return new LiveCardEntry { TemplateId = id, InstanceId = "instance-" + id, UseInstanceIdAsId = true,
                Tier = "Bronze", Socket = slot, Template = template,
                Attributes = new Dictionary<string, int> { { "CooldownMax", 1000 }, { "BurnApplyAmount", 21 } } };
        }

        private static void CompleteNativeMappingResolvesOnlyItsOwnLegacyWarning()
        {
            MappedBoard board = CardMapper.Build(new[] {
                Entry("native-ok", Template("native-ok", false, false), 0),
                Entry("legacy-only", JsonValue.Null, 1)
            }, CardCatalog.FromCards(new List<CardDef> { Legacy("native-ok"), Legacy("legacy-only") }), 1000, 1000, 1.1);
            Assert.Equal(2, board.LiveAttributeWarnings.Count);
            Assert.Equal(2, board.UnresolvedLiveAttributeWarnings(board.Config).Count);
            board.Config.NativeMechanics = true;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(board.Config).Count);
            Assert.Equal("native-template", board.Config.Cards["instance-native-ok"].DataSource);
            Assert.Equal("structured", board.Config.Cards["instance-native-ok"].ParseStatus);
            // Both imported cards have the same warning text. Resolution must
            // follow card identity instead of filtering by message or name.
            Assert.Equal(1, board.UnresolvedLiveAttributeWarnings(board.Config).Count);
            BattleResult result = BattleEngine.Simulate(board.Config);
            Assert.True(result.Events.Any(delegate(BattleEvent ev) {
                return ev.SourceItemId == "instance-native-ok" && ev.Kind == "burn" && ev.Value == 21;
            }));
        }

        private static void FailedOrAbsentNativeMappingKeepsLegacyWarning()
        {
            foreach (bool missingTemplate in new[] { false, true })
            {
                MappedBoard board = CardMapper.Build(new[] { Entry("fallback",
                    missingTemplate ? JsonValue.Null : Template("fallback", true, false), 0) },
                    CardCatalog.FromCards(new List<CardDef> { Legacy("fallback") }), 1000, 1000, 1);
                board.Config.NativeMechanics = true;
                List<string> nativeWarnings = NativeCardAbilityMapper.Apply(board.Config);
                Assert.Equal(missingTemplate ? 0 : 1, nativeWarnings.Count);
                Assert.Equal(1, board.UnresolvedLiveAttributeWarnings(board.Config).Count);
                if (!missingTemplate) Assert.Equal("partial", board.Config.Cards["instance-fallback"].ParseStatus);
            }
        }

        private static void NativeDefinitionWarningsRemainVisible()
        {
            MappedBoard board = CardMapper.Build(new[] { Entry("native-missing",
                Template("native-missing", false, true), 0) },
                CardCatalog.FromCards(new List<CardDef> { Legacy("native-missing") }), 1000, 1000, 1);
            board.Config.NativeMechanics = true;
            NativeCardAbilityMapper.Apply(board.Config);
            Assert.Equal(0, board.UnresolvedLiveAttributeWarnings(board.Config).Count);
            Assert.True(board.Config.Cards["instance-native-missing"].MappingWarnings.Any(delegate(string warning) {
                return warning.Contains("能力定义缺失 missing");
            }));
        }
    }
}
