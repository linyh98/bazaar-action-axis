using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;
using UnityEngine;

namespace BazaarActionAxis.Plugin
{
    // Unity thread, read only. Read the visible encounter portraits rather than
    // Run.Opponent, which can still point at the monster from an earlier fight.
    public static class LiveMonsterReader
    {
        private static object _catalog;
        private static readonly Dictionary<string, JsonValue> _templates = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JsonValue> _monsters = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
        private sealed class VisibleChoice
        {
            public LiveMonsterEntry Monster;
            public float X;
        }

        public static MonsterReadResult ReadChoices()
        {
            MonsterReadResult result = new MonsterReadResult();
            try
            {
                Type dataType = LiveSkillReader.FindType("TheBazaar.Data");
                if (dataType == null) throw new InvalidOperationException("未找到游戏 Data 类型");
                object run = LiveSkillReader.GetStatic(dataType, "Run");
                if (run == null) return result;
                object inCombat = LiveSkillReader.GetStatic(dataType, "IsInCombat");
                if (inCombat is bool && (bool)inCombat) return result;
                Type appStateType = LiveSkillReader.FindType("TheBazaar.AppState");
                object appState = appStateType == null ? null : LiveSkillReader.GetStatic(appStateType, "CurrentState");
                if (appState != null && appState.GetType().Name == "ReplayState") return result;

                object manager = ReadyManager(dataType);
                if (manager == null) throw new InvalidOperationException("游戏静态数据库尚未就绪");
                if (!object.ReferenceEquals(_catalog, manager))
                {
                    _catalog = manager;
                    _templates.Clear();
                    _monsters.Clear();
                }
                Func<string, JsonValue> getTemplate = delegate(string id)
                {
                    if (string.IsNullOrEmpty(id)) return null;
                    JsonValue found;
                    if (_templates.TryGetValue(id, out found)) return found;
                    object template = Lookup(manager, "GetCardById", id);
                    found = template == null ? null : LiveSkillReader.SerializeTemplate(template);
                    _templates[id] = found;
                    return found;
                };

                List<VisibleChoice> choices = new List<VisibleChoice>();
                Type controllerType = LiveSkillReader.FindType("EncounterController");
                if (controllerType != null)
                    foreach (UnityEngine.Object native in UnityEngine.Object.FindObjectsOfType(controllerType))
                    {
                        Component component = native as Component;
                        if (component == null || !component.gameObject.activeInHierarchy) continue;
                        object selectable = LiveSkillReader.GetMember(native, "IsSelectableEncounter");
                        if (!(selectable is bool) || !(bool)selectable) continue;
                        object card = LiveSkillReader.GetMember(native, "CardData");
                        LiveMonsterEntry monster = ReadEncounter(manager, card, getTemplate);
                        if (monster == null) continue;
                        VisibleChoice choice = new VisibleChoice();
                        choice.Monster = monster;
                        Camera camera = Camera.main;
                        choice.X = camera == null ? component.transform.position.x : camera.WorldToScreenPoint(component.transform.position).x;
                        choices.Add(choice);
                    }
                choices.Sort(delegate(VisibleChoice a, VisibleChoice b) { return a.X.CompareTo(b.X); });
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (VisibleChoice choice in choices)
                {
                    string identity = choice.Monster.EncounterId ?? choice.Monster.Id;
                    if (seen.Add(identity)) result.Choices.Add(choice.Monster);
                }

                // A locked native monster tooltip is the explicit detail fallback.
                // Hidden and old unlocked tooltip data must never select an enemy.
                if (result.Choices.Count == 0)
                {
                    Type tooltipType = LiveSkillReader.FindType("TheBazaar.UI.Tooltips.CardTooltipController");
                    if (tooltipType != null)
                        foreach (UnityEngine.Object native in UnityEngine.Object.FindObjectsOfType(tooltipType))
                        {
                            Component component = native as Component;
                            if (component == null || !component.gameObject.activeInHierarchy) continue;
                            object locked = LiveSkillReader.GetMember(native, "IsLocked");
                            if (!(locked is bool) || !(bool)locked) continue;
                            object tooltip = LiveSkillReader.GetMember(native, "CurrentTooltipData");
                            object monster = LiveSkillReader.GetMember(tooltip, "Monster");
                            if (monster == null) continue;
                            LiveMonsterEntry entry = MonsterTemplateExtractor.FromJson(LiveSkillReader.SerializeTemplate(monster), getTemplate);
                            entry.Name = TooltipTitle(tooltip) ?? entry.Name;
                            entry.EncounterId = Text(LiveSkillReader.GetMember(LiveSkillReader.GetMember(tooltip, "CardInstance"), "InstanceId"));
                            result.Choices.Add(entry);
                            break;
                        }
                }
                if (result.Choices.Count > 0)
                {
                    List<string> identities = new List<string>();
                    foreach (LiveMonsterEntry entry in result.Choices)
                        identities.Add((entry.EncounterId ?? entry.Id) + ":" + entry.Id);
                    object state = LiveSkillReader.GetStatic(dataType, "CurrentState");
                    result.ChoiceKey = Text(LiveSkillReader.GetMember(run, "Day")) + ":" +
                        Text(LiveSkillReader.GetMember(run, "Hour")) + ":" +
                        Text(LiveSkillReader.GetMember(state, "SelectionSet")) + ":" + string.Join("|", identities.ToArray());
                    result.DefaultIndex = result.Choices.Count / 2;
                }
            }
            catch (Exception error)
            {
                while (error.InnerException != null) error = error.InnerException;
                result.Error = "读取怪物失败：" + error.Message;
            }
            return result;
        }

        private static LiveMonsterEntry ReadEncounter(object manager, object card, Func<string, JsonValue> getTemplate)
        {
            if (card == null) return null;
            object template = LiveSkillReader.GetMember(card, "Template");
            if (template == null) template = Lookup(manager, "GetCardById", Text(LiveSkillReader.GetMember(card, "TemplateId")));
            object combatant = LiveSkillReader.GetMember(template, "CombatantType");
            string monsterId = Text(LiveSkillReader.GetMember(combatant, "MonsterTemplateId"));
            if (string.IsNullOrEmpty(monsterId)) return null;
            JsonValue monsterJson;
            if (!_monsters.TryGetValue(monsterId, out monsterJson))
            {
                object monster = Lookup(manager, "GetMonsterById", monsterId);
                if (monster == null) throw new InvalidOperationException("怪物模板不存在：" + monsterId);
                monsterJson = LiveSkillReader.SerializeTemplate(monster);
                _monsters[monsterId] = monsterJson;
            }
            LiveMonsterEntry result = MonsterTemplateExtractor.FromJson(monsterJson, getTemplate);
            result.EncounterId = Text(LiveSkillReader.GetMember(card, "InstanceId"));
            result.Name = CardTitle(card) ?? Text(LiveSkillReader.GetMember(LiveSkillReader.GetMember(LiveSkillReader.GetMember(template, "Localization"), "Title"), "Text")) ?? result.Name;
            return result;
        }

        private static string CardTitle(object card)
        {
            try
            {
                Type type = LiveSkillReader.FindType("TheBazaar.Tooltips.CardTooltipData");
                MethodInfo create = type == null ? null : type.GetMethod("CreateCardTooltipData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return create == null ? null : TooltipTitle(create.Invoke(null, new object[] { card }));
            }
            catch (Exception) { return null; }
        }

        private static string TooltipTitle(object tooltip)
        {
            if (tooltip == null) return null;
            MethodInfo title = tooltip.GetType().GetMethod("GetTitle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return title == null ? null : title.Invoke(tooltip, null) as string;
        }

        private static object ReadyManager(Type data)
        {
            MethodInfo get = data.GetMethod("GetStatic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object manager = get == null ? null : get.Invoke(null, null);
            if (manager == null) return null;
            if (manager is System.Threading.Tasks.Task)
            {
                System.Threading.Tasks.Task task = (System.Threading.Tasks.Task)manager;
                if (!task.IsCompleted || task.IsFaulted || task.IsCanceled) return null;
                manager = LiveSkillReader.GetMember(manager, "Result");
            }
            return manager;
        }

        private static object Lookup(object manager, string method, string id)
        {
            Guid parsed;
            if (manager == null || !Guid.TryParse(id, out parsed)) return null;
            MethodInfo lookup = manager.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(Guid) }, null);
            return lookup == null ? null : lookup.Invoke(manager, new object[] { parsed });
        }

        private static string Text(object value) { return value == null ? null : value.ToString(); }
    }
}
