using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    // Unity thread, read only. Scene portraits, locked tooltips and Run.Opponent
    // can survive a transition; only the current native state can select an enemy.
    public static class LiveMonsterReader
    {
        private static object _catalog;
        private static readonly Dictionary<string, JsonValue> _templates = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JsonValue> _monsters = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
        // 固定模板变形（如 Blender → Slushee）需要棋盘外的原生卡模板。
        // 只读单个 GetCardById，不改变选择/战斗状态。
        public static JsonValue LoadCardTemplate(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            Type dataType = LiveSkillReader.FindType("TheBazaar.Data");
            object manager = ReadyManager(dataType);
            object template = manager == null ? null : Lookup(manager, "GetCardById", cardId);
            return template == null ? null : LiveSkillReader.SerializeTemplate(template, "card[" + cardId + "].Template");
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
                object state = LiveSkillReader.GetStatic(dataType, "CurrentState");
                string stateName = Text(LiveSkillReader.GetMember(state, "StateName"));
                bool isChoice = IsState(appState, "ChoiceState") && stateName == "Choice";
                bool isEncounter = IsState(appState, "EncounterState") && stateName == "Encounter";
                if (!isChoice && !isEncounter) return result;

                List<string> selectedIds = new List<string>();
                string currentId = Text(LiveSkillReader.GetMember(state, "CurrentEncounterId"));
                if (isChoice)
                    foreach (object selected in ReflectionUtil.GetEnumerable(LiveSkillReader.GetMember(state, "SelectionSet")))
                    {
                        string id = Identity(selected);
                        if (!string.IsNullOrEmpty(id)) selectedIds.Add(id);
                    }
                else if (!string.IsNullOrEmpty(currentId)) selectedIds.Add(currentId);
                if (selectedIds.Count == 0) return result;

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
                    found = template == null ? null : LiveSkillReader.SerializeTemplate(template, "card[" + id + "].Template");
                    _templates[id] = found;
                    return found;
                };

                Dictionary<string, object> entities = ReadEntities(dataType);
                if (isEncounter)
                {
                    object currentCard;
                    object currentTemplate = EncounterTemplate(manager, entities, currentId, out currentCard);
                    // Events such as Mountain Pass keep the parent event as
                    // CurrentEncounterId; their generated combat option lives
                    // in SelectionSet. Direct combats (including PvP) must not
                    // reuse a preceding encounter's selection.
                    if (Text(LiveSkillReader.GetMember(currentTemplate, "Type")) == "EventEncounter")
                        foreach (object selected in ReflectionUtil.GetEnumerable(LiveSkillReader.GetMember(state, "SelectionSet")))
                        {
                            string id = Identity(selected);
                            if (!string.IsNullOrEmpty(id)) selectedIds.Add(id);
                        }
                }
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Preserve the order of the current, generated native choices.
                foreach (string id in selectedIds)
                {
                    if (!seen.Add(id)) continue;
                    object card;
                    object template = EncounterTemplate(manager, entities, id, out card);
                    LiveMonsterEntry monster = ReadEncounter(manager, card, template, id, getTemplate);
                    if (monster != null) result.Choices.Add(monster);
                }
                if (result.Choices.Count > 0)
                {
                    List<string> identities = new List<string>();
                    foreach (LiveMonsterEntry entry in result.Choices)
                        identities.Add((entry.EncounterId ?? entry.Id) + ":" + entry.Id);
                    result.ChoiceKey = stateName + ":" + currentId + ":" + Text(LiveSkillReader.GetMember(run, "Day")) + ":" +
                        Text(LiveSkillReader.GetMember(run, "Hour")) + ":" +
                        string.Join("|", identities.ToArray());
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

        private static object EncounterTemplate(object manager, Dictionary<string, object> entities, string id, out object card)
        {
            card = null;
            if (string.IsNullOrEmpty(id)) return null;
            entities.TryGetValue(id, out card);
            object template = LiveSkillReader.GetMember(card, "Template");
            return template ?? Lookup(manager, "GetCardById", card == null ? id : Text(LiveSkillReader.GetMember(card, "TemplateId")));
        }

        private static LiveMonsterEntry ReadEncounter(object manager, object card, object template, string encounterId, Func<string, JsonValue> getTemplate)
        {
            object combatant = LiveSkillReader.GetMember(template, "CombatantType");
            string monsterId = Text(LiveSkillReader.GetMember(combatant, "MonsterTemplateId"));
            if (string.IsNullOrEmpty(monsterId)) return null;
            JsonValue monsterJson;
            if (!_monsters.TryGetValue(monsterId, out monsterJson))
            {
                object monster = Lookup(manager, "GetMonsterById", monsterId);
                if (monster == null) throw new InvalidOperationException("怪物模板不存在：" + monsterId +
                    "（路径：GetMonsterById(" + monsterId + ")）");
                monsterJson = LiveSkillReader.SerializeTemplate(monster, "monster[" + monsterId + "]");
                _monsters[monsterId] = monsterJson;
            }
            LiveMonsterEntry result = MonsterTemplateExtractor.FromJson(monsterJson, getTemplate);
            result.EncounterId = encounterId;
            result.Name = CardTitle(card) ?? Text(LiveSkillReader.GetMember(LiveSkillReader.GetMember(LiveSkillReader.GetMember(template, "Localization"), "Title"), "Text")) ?? result.Name;
            return result;
        }

        private static Dictionary<string, object> ReadEntities(Type dataType)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (object pair in ReflectionUtil.GetEnumerable(LiveSkillReader.GetStatic(dataType, "Entities")))
            {
                string id = Identity(LiveSkillReader.GetMember(pair, "Key"));
                object card = LiveSkillReader.GetMember(pair, "Value");
                if (!string.IsNullOrEmpty(id) && card != null) result[id] = card;
            }
            return result;
        }

        private static bool IsState(object state, string name)
        {
            for (Type type = state == null ? null : state.GetType(); type != null; type = type.BaseType)
                if (type.Name == name) return true;
            return false;
        }

        private static string Identity(object value)
        {
            return LiveSkillReader.GetMember(value, "Value") as string ?? Text(value);
        }

        private static string CardTitle(object card)
        {
            if (card == null) return null;
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
