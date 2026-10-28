using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    public static class LiveMonsterReaderTests
    {
        private static readonly Guid ViperId = new Guid("00000000-0000-0000-0000-000000000001");
        private static readonly Guid BoarId = new Guid("00000000-0000-0000-0000-000000000002");
        private static readonly Guid MonsterEncounterId = new Guid("00000000-0000-0000-0000-000000000003");
        private static readonly Guid PvpEncounterId = new Guid("00000000-0000-0000-0000-000000000004");
        private static readonly Guid EventEncounterId = new Guid("00000000-0000-0000-0000-000000000005");
        private static readonly Guid RewardEncounterId = new Guid("00000000-0000-0000-0000-000000000006");
        private static readonly Guid StaticPvpEncounterId = new Guid("00000000-0000-0000-0000-000000000007");

        public static void Run()
        {
            try
            {
                CurrentChoicesExcludeLoadedHistoricalMonsters();
                PvpEncounterIgnoresPreviousSelectionAndClearsSelection();
                PvpPreparationChoiceIsNotAMonsterChoice();
                CurrentMonsterEncounterUsesItsTemplate();
                CurrentInstanceEncounterResolvesEntity();
                NonEncounterStatesAndStateTransitionsRejectOldChoices();
                MissingChoiceDoesNotSearchStaticMonsters();
                RawGuidChoicesResolveOnlyTheirExactTemplates();
                EventEncounterReadsCurrentMonsterChoices();
                EventEncounterFiltersNonCombatAndDuplicateChoices();
                EventEncounterAndChoicesResolveStaticAndInstanceIds();
                EventSelectionChangesAndLeavingClearSelectedMonster();
                EventEncounterDoesNotSearchHistoricalEntitiesOrStaticPools();
                NonEventAndMissingEncountersIgnorePreviousChoices();
            }
            finally
            {
                TheBazaar.Data.Run = null;
                TheBazaar.Data.Entities = null;
                TheBazaar.Data.CurrentState = null;
                TheBazaar.Data.StaticManager = null;
                TheBazaar.Data.IsInCombat = false;
                TheBazaar.AppState.CurrentState = null;
            }
        }

        private static NativeMonsterRunState Setup()
        {
            NativeMonsterManager manager = new NativeMonsterManager();
            manager.Monsters[ViperId] = Monster(ViperId, "毒蛇", 100);
            manager.Monsters[BoarId] = Monster(BoarId, "野猪", 500);
            manager.Cards[MonsterEncounterId] = new NativeMonsterEncounterTemplate {
                CombatantType = new NativeMonsterCombatant { MonsterTemplateId = BoarId }
            };
            manager.Cards[PvpEncounterId] = new NativeMonsterEncounterTemplate {
                CombatantType = new object() // Native TCombatantPVPGhost has no MonsterTemplateId.
            };
            manager.Cards[EventEncounterId] = new NativeMonsterEncounterTemplate { Type = "EventEncounter" };
            manager.Cards[RewardEncounterId] = new NativeMonsterEncounterTemplate { Type = "RewardEncounter" };
            NativeMonsterRunState state = new NativeMonsterRunState { StateName = "Choice" };
            TheBazaar.Data.Run = new NativeMonsterRun { Day = 1, Hour = 3 };
            TheBazaar.Data.CurrentState = state;
            TheBazaar.Data.Entities = new Dictionary<NativeMonsterInstanceId, object> {
                { new NativeMonsterInstanceId("old_viper"), Card("old_viper", ViperId) },
                { new NativeMonsterInstanceId("current_boar"), Card("current_boar", BoarId) },
                { new NativeMonsterInstanceId("current_pvp"), new NativeMonsterCard {
                    TemplateId = PvpEncounterId, Template = manager.Cards[PvpEncounterId]
                } }
            };
            TheBazaar.Data.StaticManager = manager;
            TheBazaar.Data.IsInCombat = false;
            TheBazaar.AppState.CurrentState = new TheBazaar.ChoiceState();
            return state;
        }

        private static NativeMonsterCard Card(string id, Guid monsterId)
        {
            return new NativeMonsterCard {
                InstanceId = new NativeMonsterInstanceId(id),
                Template = new NativeMonsterEncounterTemplate {
                    CombatantType = new NativeMonsterCombatant { MonsterTemplateId = monsterId }
                }
            };
        }

        private static JsonValue Monster(Guid id, string name, int hp)
        {
            return Json.Parse("{\"Id\":\"" + id + "\",\"InternalName\":\"" + name +
                "\",\"Player\":{\"Attributes\":{\"HealthMax\":" + hp + "},\"Hand\":{\"Items\":[]},\"Skills\":[]}}");
        }

        private static void CurrentChoicesExcludeLoadedHistoricalMonsters()
        {
            NativeMonsterRunState state = Setup();
            state.SelectionSet.Add("current_pvp");
            state.SelectionSet.Add("current_boar");
            state.SelectionSet.Add("current_boar");
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(1, read.Choices.Count);
            Assert.Equal(BoarId.ToString(), read.Choices[0].Id);
            Assert.Equal("current_boar", read.Choices[0].EncounterId);
            Assert.Near(500, read.Choices[0].MaxHp);
            Assert.False(read.ChoiceKey.Contains("old_viper"));
        }

        private static void PvpEncounterIgnoresPreviousSelectionAndClearsSelection()        {
            NativeMonsterRunState state = Setup();
            state.SelectionSet.Add("old_viper");
            MonsterSelection selection = new MonsterSelection();
            Assert.Equal(ViperId.ToString(), selection.Resolve(LiveMonsterReader.ReadChoices()).Id);
            // The previous monster entity and SelectionSet both survive the transition.
            state.StateName = "Encounter";
            state.CurrentEncounterId = PvpEncounterId.ToString();
            TheBazaar.AppState.CurrentState = new TheBazaar.EncounterState();
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(0, read.Choices.Count);
            Assert.True(selection.Resolve(read) == null);
            Assert.Equal(0, selection.Index);
        }

        // 2026-10-28 live evidence (Player.log 12:10): before a PvP battle the game
        // keeps a ChoiceState whose only SelectionSet entry is "pvp_qopwiSR",
        // hydrated from a monster encounter template (Viper). It is the server
        // assigned PvP opponent slot, not a monster choice; the panel must fall
        // back to the training dummy instead of showing the placeholder monster.
        private static void PvpPreparationChoiceIsNotAMonsterChoice()
        {
            NativeMonsterRunState state = Setup();
            Dictionary<NativeMonsterInstanceId, object> entities =
                (Dictionary<NativeMonsterInstanceId, object>)TheBazaar.Data.Entities;
            entities[new NativeMonsterInstanceId("pvp_qopwiSR")] = new NativeMonsterCard {
                Template = new NativeMonsterEncounterTemplate {
                    CombatantType = new NativeMonsterCombatant { MonsterTemplateId = ViperId } } };
            state.SelectionSet.Add("pvp_qopwiSR");
            MonsterSelection selection = new MonsterSelection();
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(0, read.Choices.Count);
            Assert.True(selection.Resolve(read) == null);
            Assert.Equal(0, selection.Index);
        }

        private static void PvpEncounterInstanceWithMonsterTemplateIsIgnored()
        {
            NativeMonsterRunState state = Setup();
            Dictionary<NativeMonsterInstanceId, object> entities =
                (Dictionary<NativeMonsterInstanceId, object>)TheBazaar.Data.Entities;
            entities[new NativeMonsterInstanceId("pvp_qopwiSR")] = new NativeMonsterCard {
                Template = new NativeMonsterEncounterTemplate {
                    CombatantType = new NativeMonsterCombatant { MonsterTemplateId = ViperId } } };
            // A previous monster option can survive in SelectionSet; it must not
            // be projected while the current encounter is the PvP instance.
            state.SelectionSet.Add("old_viper");
            state.StateName = "Encounter";
            state.CurrentEncounterId = "pvp_qopwiSR";
            TheBazaar.AppState.CurrentState = new TheBazaar.EncounterState();
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(0, read.Choices.Count);
        }

        private static void StaticPvpEncounterTemplateIsIgnored()
        {
            NativeMonsterRunState state = Setup();
            NativeMonsterManager manager = (NativeMonsterManager)TheBazaar.Data.StaticManager;
            NativeMonsterEncounterTemplate pvp = new NativeMonsterEncounterTemplate {
                CombatantType = new NativeMonsterCombatant { MonsterTemplateId = ViperId },
                InternalName = "Hour 6 PvP" };
            manager.Cards[StaticPvpEncounterId] = pvp;
            state.SelectionSet.Add(StaticPvpEncounterId.ToString());
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
            // The same placeholder monster in an ordinary encounter still reads
            // as a monster, so real monster battles keep their preview.
            pvp.InternalName = "Viper";
            MonsterReadResult normal = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, normal.Error);
            Assert.Equal(1, normal.Choices.Count);
            Assert.Equal(ViperId.ToString(), normal.Choices[0].Id);
        }

        private static void CurrentMonsterEncounterUsesItsTemplate()
        {
            NativeMonsterRunState state = Setup();
            state.StateName = "Encounter";
            state.CurrentEncounterId = MonsterEncounterId.ToString();
            state.SelectionSet.Add("old_viper");
            TheBazaar.AppState.CurrentState = new TheBazaar.EncounterState();
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(1, read.Choices.Count);
            Assert.Equal(BoarId.ToString(), read.Choices[0].Id);
            Assert.Equal(MonsterEncounterId.ToString(), read.Choices[0].EncounterId);
        }

        private static void CurrentInstanceEncounterResolvesEntity()
        {
            NativeMonsterRunState state = Setup();
            state.StateName = "Encounter";
            state.CurrentEncounterId = "current_boar";
            TheBazaar.AppState.CurrentState = new TheBazaar.EncounterState();
            Assert.Equal(BoarId.ToString(), LiveMonsterReader.ReadChoices().Choices[0].Id);
        }

        private static void NonEncounterStatesAndStateTransitionsRejectOldChoices()
        {
            NativeMonsterRunState state = Setup();
            state.SelectionSet.Add("old_viper");
            foreach (string name in new string[] { "PVPCombat", "Combat", "Loot", "LevelUp", "Pedestal", "NewRun" })
            {
                state.StateName = name;
                Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
            }
            state.StateName = "Choice";
            TheBazaar.AppState.CurrentState = new object();
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
            TheBazaar.AppState.CurrentState = new TheBazaar.ChoiceState();
            TheBazaar.Data.IsInCombat = true;
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
        }

        private static void MissingChoiceDoesNotSearchStaticMonsters()
        {
            NativeMonsterRunState state = Setup();
            state.SelectionSet.Add("missing_current_card");
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
            state.SelectionSet.Clear();
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
            state.StateName = "Encounter";
            TheBazaar.AppState.CurrentState = new TheBazaar.EncounterState();
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
        }

        private static void RawGuidChoicesResolveOnlyTheirExactTemplates()
        {
            NativeMonsterRunState state = Setup();
            state.SelectionSet.Add(MonsterEncounterId.ToString());
            Assert.Equal(BoarId.ToString(), LiveMonsterReader.ReadChoices().Choices[0].Id);
            state.SelectionSet[0] = PvpEncounterId.ToString();
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
        }

        private static NativeMonsterRunState SetupEvent()
        {
            NativeMonsterRunState state = Setup();
            state.StateName = "Encounter";
            state.CurrentEncounterId = EventEncounterId.ToString();
            TheBazaar.AppState.CurrentState = new TheBazaar.EncounterState();
            return state;
        }

        private static void EventEncounterReadsCurrentMonsterChoices()
        {
            NativeMonsterRunState state = SetupEvent();
            state.SelectionSet.Add("current_boar");
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(1, read.Choices.Count);
            Assert.Equal(BoarId.ToString(), read.Choices[0].Id);
            Assert.Equal("current_boar", read.Choices[0].EncounterId);
            Assert.Near(500, read.Choices[0].MaxHp);
            Assert.False(read.ChoiceKey.Contains("old_viper"));
        }

        private static void EventEncounterFiltersNonCombatAndDuplicateChoices()
        {
            NativeMonsterRunState state = SetupEvent();
            state.SelectionSet.Add("current_pvp");
            state.SelectionSet.Add(RewardEncounterId.ToString());
            state.SelectionSet.Add("missing_current_option");
            state.SelectionSet.Add("current_boar");
            state.SelectionSet.Add("current_boar");
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(1, read.Choices.Count);
            Assert.Equal(BoarId.ToString(), read.Choices[0].Id);
            Assert.Equal("current_boar", read.Choices[0].EncounterId);
        }

        private static void EventEncounterAndChoicesResolveStaticAndInstanceIds()
        {
            NativeMonsterRunState state = SetupEvent();
            Dictionary<NativeMonsterInstanceId, object> entities = (Dictionary<NativeMonsterInstanceId, object>)TheBazaar.Data.Entities;
            // The parent instance can expose only TemplateId, as native cards do before hydration.
            entities[new NativeMonsterInstanceId("current_event")] = new NativeMonsterCard { TemplateId = EventEncounterId };
            foreach (string parent in new string[] { EventEncounterId.ToString(), "current_event" })
            {
                state.CurrentEncounterId = parent;
                state.SelectionSet.Clear();
                state.SelectionSet.Add("current_boar");
                MonsterReadResult instance = LiveMonsterReader.ReadChoices();
                Assert.Equal<string>(null, instance.Error);
                Assert.Equal(1, instance.Choices.Count);
                Assert.Equal("current_boar", instance.Choices[0].EncounterId);
                state.SelectionSet[0] = MonsterEncounterId.ToString();
                MonsterReadResult template = LiveMonsterReader.ReadChoices();
                Assert.Equal<string>(null, template.Error);
                Assert.Equal(1, template.Choices.Count);
                Assert.Equal(BoarId.ToString(), template.Choices[0].Id);
                Assert.Equal(MonsterEncounterId.ToString(), template.Choices[0].EncounterId);
            }
        }

        private static void EventSelectionChangesAndLeavingClearSelectedMonster()
        {
            NativeMonsterRunState state = SetupEvent();
            MonsterSelection selection = new MonsterSelection();
            state.SelectionSet.Add("current_boar");
            MonsterReadResult first = LiveMonsterReader.ReadChoices();
            Assert.Equal(BoarId.ToString(), selection.Resolve(first).Id);
            state.SelectionSet[0] = "old_viper";
            MonsterReadResult changed = LiveMonsterReader.ReadChoices();
            Assert.False(first.ChoiceKey == changed.ChoiceKey);
            Assert.Equal(ViperId.ToString(), selection.Resolve(changed).Id);
            state.SelectionSet.Clear();
            Assert.True(selection.Resolve(LiveMonsterReader.ReadChoices()) == null);
            Assert.Equal(0, selection.Index);
            state.SelectionSet.Add("current_boar");
            Assert.Equal(BoarId.ToString(), selection.Resolve(LiveMonsterReader.ReadChoices()).Id);
            state.CurrentEncounterId = PvpEncounterId.ToString();
            Assert.True(selection.Resolve(LiveMonsterReader.ReadChoices()) == null);
            state.CurrentEncounterId = EventEncounterId.ToString();
            Assert.Equal(BoarId.ToString(), selection.Resolve(LiveMonsterReader.ReadChoices()).Id);
            state.StateName = "Loot";
            Assert.True(selection.Resolve(LiveMonsterReader.ReadChoices()) == null);
            Assert.Equal(0, selection.Index);
        }

        private static void EventEncounterDoesNotSearchHistoricalEntitiesOrStaticPools()
        {
            NativeMonsterRunState state = SetupEvent();
            // Setup retains both historical monster entities and available static monster templates.
            // Neither represents a generated option until it occurs in the current SelectionSet.
            Assert.Equal(0, LiveMonsterReader.ReadChoices().Choices.Count);
            state.SelectionSet.Add(RewardEncounterId.ToString());
            state.SelectionSet.Add("missing_current_option");
            MonsterReadResult read = LiveMonsterReader.ReadChoices();
            Assert.Equal<string>(null, read.Error);
            Assert.Equal(0, read.Choices.Count);
            Assert.Equal<string>(null, read.ChoiceKey);
        }

        private static void NonEventAndMissingEncountersIgnorePreviousChoices()
        {
            NativeMonsterRunState state = SetupEvent();
            state.SelectionSet.Add("old_viper");
            foreach (string current in new string[] { RewardEncounterId.ToString(), "missing_current_parent" })
            {
                state.CurrentEncounterId = current;
                MonsterReadResult read = LiveMonsterReader.ReadChoices();
                Assert.Equal<string>(null, read.Error);
                Assert.Equal(0, read.Choices.Count);
            }
        }
    }

    public sealed class NativeMonsterRun { public int Day; public int Hour; }
    public sealed class NativeMonsterRunState
    {
        public string StateName;
        public string CurrentEncounterId;
        public List<string> SelectionSet = new List<string>();
    }
    public sealed class NativeMonsterInstanceId
    {
        public string Value;
        public NativeMonsterInstanceId(string value) { Value = value; }
    }
    public sealed class NativeMonsterCard
    {
        public object InstanceId;
        public Guid TemplateId;
        public object Template;
    }
    public sealed class NativeMonsterEncounterTemplate
    {
        public string Type;
        public object CombatantType;
        public string InternalName;
        public object Localization;
    }
    public sealed class NativeMonsterCombatant { public Guid MonsterTemplateId; }
    public sealed class NativeMonsterManager
    {
        public Dictionary<Guid, object> Cards = new Dictionary<Guid, object>();
        public Dictionary<Guid, object> Monsters = new Dictionary<Guid, object>();
        public object GetCardById(Guid id) { object value; return Cards.TryGetValue(id, out value) ? value : null; }
        public object GetMonsterById(Guid id) { object value; return Monsters.TryGetValue(id, out value) ? value : null; }
    }
}

namespace TheBazaar
{
    public sealed class ChoiceState { }
    public sealed class EncounterState { }
}
