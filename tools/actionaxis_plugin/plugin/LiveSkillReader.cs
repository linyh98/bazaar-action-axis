using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    // Skills are not included in BPP's item-only LiveCardSnapshotReader.
    // Read the native player's skill list on the Unity thread alongside the board.
    public static class LiveSkillReader
    {
        private const BindingFlags InstanceFlags = BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.Instance;

        public static void Populate(BoardReadResult result)
        {
            if (result == null) throw new ArgumentNullException("result");
            result.Skills.Clear();
            result.PlayerAttributes.Clear();
            result.PlayerHealth = null;
            result.PlayerMaxHealth = null;
            result.SkillReadError = null;
            try
            {
                Type dataType = FindType("TheBazaar.Data");
                if (dataType == null) throw new InvalidOperationException("未找到游戏 Data 类型");
                object run = GetStatic(dataType, "Run");
                object player = GetMember(run, "Player");
                if (player == null) throw new InvalidOperationException("当前没有玩家状态");

                result.PlayerAttributes = ReadAttributes(GetMember(player, "Attributes"));
                int health;
                if (result.PlayerAttributes.TryGetValue("Health", out health))
                    result.PlayerHealth = health;
                if (result.PlayerAttributes.TryGetValue("HealthMax", out health))
                    result.PlayerMaxHealth = health;

                object skills = GetMember(player, "Skills");
                if (skills == null) throw new InvalidOperationException("当前玩家的 Skills 不可用");
                int order = 0;
                foreach (object nativeSkill in ReflectionUtil.GetEnumerable(skills))
                {
                    if (nativeSkill == null) continue;
                    try
                    {
                        result.Skills.Add(ReadSkill(nativeSkill, order++, result));
                    }
                    catch (Exception error)
                    {
                        AddError(result, "技能 " + order + "：" + RootMessage(error));
                    }
                }
            }
            catch (Exception error)
            {
                AddError(result, "读取技能失败：" + RootMessage(error));
            }
        }

        private static LiveSkillEntry ReadSkill(object nativeSkill, int order, BoardReadResult result)
        {
            LiveSkillEntry skill = new LiveSkillEntry();
            object instance = GetMember(nativeSkill, "InstanceId");
            skill.InstanceId = GetMember(instance, "Value") as string ?? Text(instance);
            skill.TemplateId = Text(GetMember(nativeSkill, "TemplateId"));
            if (skill.TemplateId != null) skill.TemplateId = skill.TemplateId.ToLowerInvariant();
            skill.Tier = Text(GetMember(nativeSkill, "Tier"));
            skill.Order = order;
            skill.Attributes = ReadAttributes(GetMember(nativeSkill, "Attributes"));
            object template = null;
            try
            {
                template = GetMember(nativeSkill, "Template");
                if (template == null) throw new InvalidOperationException("技能模板为空");
                skill.Template = SerializeNative(template, 0, new List<object>());
            }
            catch (Exception error)
            {
                AddError(result, "技能模板 " + skill.TemplateId + "：" + RootMessage(error));
            }
            object title = GetMember(GetMember(template, "Localization"), "Title");
            skill.Name = GetMember(title, "Text") as string ??
                GetMember(template, "InternalName") as string ?? skill.TemplateId;

            // The game's tooltip builder resolves both localization and live attribute tokens.
            // Its failure does not discard the native template or the skill itself.
            try
            {
                Type tooltipType = FindType("TheBazaar.Tooltips.CardTooltipData");
                MethodInfo create = tooltipType == null ? null : tooltipType.GetMethod(
                    "CreateCardTooltipData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object tooltip = create == null ? null : create.Invoke(null, new object[] { nativeSkill });
                if (tooltip != null)
                {
                    MethodInfo getTitle = tooltipType.GetMethod("GetTitle", InstanceFlags);
                    string localized = getTitle == null ? null : getTitle.Invoke(tooltip, null) as string;
                    if (!string.IsNullOrEmpty(localized)) skill.Name = localized;
                    MethodInfo getDescription = tooltipType.GetMethod("BuildCombinedEffectDescription", InstanceFlags);
                    string description = getDescription == null ? null : getDescription.Invoke(tooltip, null) as string;
                    if (!string.IsNullOrEmpty(description)) skill.RawTooltips.Add(description);
                }
            }
            catch (Exception) { }
            if (skill.RawTooltips.Count == 0)
            {
                object localization = GetMember(template, "Localization");
                foreach (object line in ReflectionUtil.GetEnumerable(GetMember(localization, "Tooltips")))
                {
                    string text = GetMember(GetMember(line, "Content"), "Text") as string;
                    if (!string.IsNullOrEmpty(text)) skill.RawTooltips.Add(text);
                }
            }
            return skill;
        }

        public static string BuildDebugJson(BoardReadResult result)
        {
            Dictionary<string, JsonValue> root = new Dictionary<string, JsonValue>();
            root["capturedAt"] = JsonValue.FromString(DateTime.UtcNow.ToString("o"));
            root["playerHealth"] = OptionalNumber(result.PlayerHealth);
            root["playerMaxHealth"] = OptionalNumber(result.PlayerMaxHealth);
            root["playerAttributes"] = SerializeNative(result.PlayerAttributes, 0, new List<object>());
            root["skillReadError"] = StringValue(result.SkillReadError);
            root["cardReadError"] = StringValue(result.CardReadError);
            List<JsonValue> skills = new List<JsonValue>();
            foreach (LiveSkillEntry skill in result.Skills)
            {
                Dictionary<string, JsonValue> record = new Dictionary<string, JsonValue>();
                record["instanceId"] = StringValue(skill.InstanceId);
                record["templateId"] = StringValue(skill.TemplateId);
                record["tier"] = StringValue(skill.Tier);
                record["name"] = StringValue(skill.Name);
                record["order"] = JsonValue.FromNumber(skill.Order);
                record["attributes"] = SerializeNative(skill.Attributes, 0, new List<object>());
                record["rawTooltips"] = SerializeNative(skill.RawTooltips, 0, new List<object>());
                record["template"] = skill.Template ?? JsonValue.Null;
                skills.Add(JsonValue.FromObject(record));
            }
            root["skills"] = JsonValue.FromArray(skills);
            root["board"] = SerializeNative(result.Cards, 0, new List<object>());
            StringBuilder json = new StringBuilder();
            WriteJson(json, JsonValue.FromObject(root));
            return json.ToString();
        }

        public static JsonValue SerializeTemplate(object value)
        {
            return SerializeNative(value, 0, new List<object>());
        }

        public static string BuildDebugJson(object value)
        {
            StringBuilder json = new StringBuilder();
            WriteJson(json, SerializeTemplate(value));
            return json.ToString();
        }

        private static JsonValue SerializeNative(object value, int depth, List<object> ancestors)
        {
            if (value == null) return JsonValue.Null;
            JsonValue existing = value as JsonValue;
            if (existing != null) return existing;
            Type type = value.GetType();
            if (value is string || value is char || value is Guid || type.IsEnum)
                return JsonValue.FromString(value.ToString());
            if (value is bool) return JsonValue.FromBool((bool)value);
            if (type.IsPrimitive || value is decimal)
                return JsonValue.FromNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            if (depth > 40) throw new InvalidOperationException("技能模板嵌套层数超过限制");
            foreach (object ancestor in ancestors)
                if (object.ReferenceEquals(ancestor, value))
                    throw new InvalidOperationException("技能模板包含循环引用");
            ancestors.Add(value);
            try
            {
                IDictionary dictionary = value as IDictionary;
                if (dictionary != null)
                {
                    Dictionary<string, JsonValue> entries = new Dictionary<string, JsonValue>();
                    foreach (DictionaryEntry entry in dictionary)
                        entries[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] =
                            SerializeNative(entry.Value, depth + 1, ancestors);
                    return JsonValue.FromObject(entries);
                }
                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null)
                {
                    List<JsonValue> entries = new List<JsonValue>();
                    foreach (object entry in enumerable)
                        entries.Add(SerializeNative(entry, depth + 1, ancestors));
                    return JsonValue.FromArray(entries);
                }

                Dictionary<string, JsonValue> fields = new Dictionary<string, JsonValue>();
                // Polymorphic actions, targets and triggers need their concrete game type.
                if (type.Namespace != null && type.Namespace.StartsWith("BazaarGameShared.", StringComparison.Ordinal))
                    fields["$type"] = JsonValue.FromString(type.Name);
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    MethodInfo getter = property.GetGetMethod(false);
                    if (getter == null) continue;
                    fields[property.Name] = SerializeNative(property.GetValue(value, null), depth + 1, ancestors);
                }
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!fields.ContainsKey(field.Name))
                        fields[field.Name] = SerializeNative(field.GetValue(value), depth + 1, ancestors);
                return JsonValue.FromObject(fields);
            }
            finally { ancestors.RemoveAt(ancestors.Count - 1); }
        }

        private static Dictionary<string, int> ReadAttributes(object attributes)
        {
            Dictionary<string, int> result = new Dictionary<string, int>();
            foreach (object pair in ReflectionUtil.GetEnumerable(attributes))
            {
                object key = GetMember(pair, "Key");
                if (key != null) result[key.ToString()] = Convert.ToInt32(GetMember(pair, "Value"));
            }
            return result;
        }

        internal static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        internal static object GetStatic(Type type, string name)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null) return property.GetValue(null, null);
            FieldInfo field = type.GetField(name, flags);
            return field == null ? null : field.GetValue(null);
        }

        internal static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, InstanceFlags);
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = type.GetField(name, InstanceFlags);
            return field == null ? null : field.GetValue(target);
        }

        private static string Text(object value) { return value == null ? null : value.ToString(); }
        private static JsonValue StringValue(string value)
        {
            return value == null ? JsonValue.Null : JsonValue.FromString(value);
        }
        private static JsonValue OptionalNumber(double? value)
        {
            return value.HasValue ? JsonValue.FromNumber(value.Value) : JsonValue.Null;
        }
        private static void AddError(BoardReadResult result, string message)
        {
            result.SkillReadError = string.IsNullOrEmpty(result.SkillReadError) ? message :
                result.SkillReadError + "；" + message;
        }
        private static string RootMessage(Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            return error.Message;
        }

        private static void WriteJson(StringBuilder builder, JsonValue value)
        {
            switch (value.Kind)
            {
                case JsonKind.Null: builder.Append("null"); return;
                case JsonKind.Bool: builder.Append(value.AsBool() ? "true" : "false"); return;
                case JsonKind.Number: builder.Append(value.AsDouble().ToString("R", CultureInfo.InvariantCulture)); return;
                case JsonKind.String: WriteString(builder, value.AsString()); return;
                case JsonKind.Array:
                    builder.Append('[');
                    for (int i = 0; i < value.Count; i++)
                    {
                        if (i > 0) builder.Append(',');
                        WriteJson(builder, value[i]);
                    }
                    builder.Append(']'); return;
                case JsonKind.Object:
                    builder.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, JsonValue> pair in value.AsObject())
                    {
                        if (!first) builder.Append(',');
                        first = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        WriteJson(builder, pair.Value);
                    }
                    builder.Append('}'); return;
            }
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            if (value == null) { builder.Append("null"); return; }
            builder.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') { builder.Append('\\'); builder.Append(c); }
                else if (c < ' ') builder.Append("\\u" + ((int)c).ToString("x4"));
                else builder.Append(c);
            }
            builder.Append('"');
        }
    }
}
