using System;
using System.Collections;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    public static class NativeCombatReplayReader
    {
        private static object _cachedCombat;
        private static int _cachedFrameCount;
        private static NativeCombatReplayData _cachedData;

        // Read only, Unity thread. The complete simulation is normally available
        // before playback starts. Never return an earlier fight outside ReplayState.
        public static bool TryRead(out NativeCombatReplayData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                Type appState = LiveSkillReader.FindType("TheBazaar.AppState");
                object current = appState == null ? null : LiveSkillReader.GetStatic(appState, "CurrentState");
                if (current == null || current.GetType().FullName != "TheBazaar.ReplayState")
                {
                    error = "当前没有实战回放";
                    return false;
                }
                object sequence = LiveSkillReader.GetMember(current, "_sequence");
                object combatMessage = LiveSkillReader.GetMember(sequence, "CombatMessage");
                object combat = LiveSkillReader.GetMember(combatMessage, "Data");
                object frames = LiveSkillReader.GetMember(combat, "Frames");
                ICollection collection = frames as ICollection;
                int count = collection == null ? 0 : collection.Count;
                if (combat == null || count == 0)
                {
                    error = "实战回放帧尚未准备好";
                    return false;
                }
                if (object.ReferenceEquals(_cachedCombat, combat) && _cachedFrameCount == count && _cachedData != null)
                {
                    data = _cachedData;
                    return true;
                }
                Dictionary<string, JsonValue> root = new Dictionary<string, JsonValue>();
                root["CapturedAt"] = JsonValue.FromString(DateTime.UtcNow.ToString("o"));
                root["NativeSequence"] = LiveSkillReader.SerializeTemplate(sequence);
                root["OpeningCollection"] = LiveSkillReader.SerializeTemplate(LiveSkillReader.GetMember(current, "_combatCollection"));
                // ReplayCardCollection caches hand ITEMS only. Skill attributes
                // below are explicitly capture-time metadata, not opening values.
                root["SkillMetadataAtCapture"] = SkillMetadata();
                string combatId = CombatId(sequence);
                if (string.IsNullOrEmpty(combatId)) combatId = "native-" + DateTime.UtcNow.Ticks.ToString();
                root["CombatId"] = JsonValue.FromString(combatId);
                data = NativeCombatReplayData.FromNativeJson(JsonValue.FromObject(root));
                _cachedCombat = combat;
                _cachedFrameCount = count;
                _cachedData = data;
                return true;
            }
            catch (Exception failure)
            {
                while (failure.InnerException != null) failure = failure.InnerException;
                error = "读取实战回放失败：" + failure.Message;
                return false;
            }
        }

        private static string CombatId(object sequence)
        {
            object spawn = LiveSkillReader.GetMember(LiveSkillReader.GetMember(sequence, "SpawnMessage"), "Data");
            foreach (object ev in ReflectionUtil.GetEnumerable(LiveSkillReader.GetMember(spawn, "Events")))
            {
                if (ev == null) continue;
                // Native combat entry is a GameSimEventStateTransitioned to the
                // combat encounter; do not mistake a spawned item InstanceId for it.
                if (ev.GetType().Name != "GameSimEventStateTransitioned") continue;
                object instance = LiveSkillReader.GetMember(ev, "CurrentEncounterId");
                string value = instance == null ? null : instance.ToString();
                if (!string.IsNullOrEmpty(value)) return value;
            }
            object message = LiveSkillReader.GetMember(sequence, "CombatMessage");
            object messageId = LiveSkillReader.GetMember(message, "MessageId");
            return messageId == null ? null : messageId.ToString();
        }

        private static JsonValue SkillMetadata()
        {
            Dictionary<string, JsonValue> sides = new Dictionary<string, JsonValue>();
            try
            {
                Type dataType = LiveSkillReader.FindType("TheBazaar.Data");
                object run = dataType == null ? null : LiveSkillReader.GetStatic(dataType, "Run");
                foreach (string side in new string[] { "Player", "Opponent" })
                {
                    List<JsonValue> entries = new List<JsonValue>();
                    object player = LiveSkillReader.GetMember(run, side);
                    foreach (object skill in ReflectionUtil.GetEnumerable(LiveSkillReader.GetMember(player, "Skills")))
                    {
                        Dictionary<string, JsonValue> item = new Dictionary<string, JsonValue>();
                        foreach (string field in new string[] { "InstanceId", "TemplateId", "Tier", "Attributes", "Template" })
                            item[field] = LiveSkillReader.SerializeTemplate(LiveSkillReader.GetMember(skill, field));
                        entries.Add(JsonValue.FromObject(item));
                    }
                    sides[side] = JsonValue.FromArray(entries);
                }
            }
            catch (Exception failure)
            {
                while (failure.InnerException != null) failure = failure.InnerException;
                sides["ReadError"] = JsonValue.FromString(failure.Message);
            }
            return JsonValue.FromObject(sides);
        }
    }
}
