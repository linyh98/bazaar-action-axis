using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    public static class ValidationSnapshot
    {
        public static JsonValue Capture()
        {
            List<string> errors = new List<string>();
            Dictionary<string, JsonValue> root = new Dictionary<string, JsonValue>();
            Type data = LiveSkillReader.FindType("TheBazaar.Data");
            object run = data == null ? null : LiveSkillReader.GetStatic(data, "Run");
            root["RunId"] = Read(run, "Id", errors, "RunId");
            object current = data == null ? null : LiveSkillReader.GetStatic(data, "CurrentState");
            root["CurrentState"] = Read(current, "StateName", errors, "CurrentState");
            foreach (string side in new string[] { "Player", "Opponent" })
            {
                Dictionary<string, JsonValue> player = new Dictionary<string, JsonValue>();
                object native = LiveSkillReader.GetMember(run, side);
                if (native == null) errors.Add(side + ": player unavailable at this dispatch phase");
                foreach (string name in new string[] { "Hero", "Attributes" })
                    player[name] = Read(native, name, errors, side + "." + name);
                List<JsonValue> skills = new List<JsonValue>();
                object nativeSkills = LiveSkillReader.GetMember(native, "Skills");
                if (nativeSkills == null) errors.Add(side + ".Skills unavailable");
                foreach (object skill in ReflectionUtil.GetEnumerable(nativeSkills)) skills.Add(Card(skill, errors, side));
                player["Skills"] = JsonValue.FromArray(skills);
                try
                {
                    MethodInfo getter = null;
                    if (data != null) foreach (MethodInfo method in data.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        if (method.Name == "GetCards" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2)
                        { getter = method; break; }
                    Type cardType = LiveSkillReader.FindType("BazaarGameClient.Domain.Models.Cards.Card");
                    if (getter == null || cardType == null) throw new InvalidOperationException("Data.GetCards<Card> unavailable");
                    object sideValue = Enum.Parse(getter.GetParameters()[0].ParameterType, side);
                    object cards = getter.MakeGenericMethod(cardType).Invoke(null, new object[] { sideValue, null });
                    List<JsonValue> entries = new List<JsonValue>();
                    foreach (object card in ReflectionUtil.GetEnumerable(cards)) entries.Add(Card(card, errors, side));
                    player["Cards"] = JsonValue.FromArray(entries); // All inventory sections, including stash.
                }
                catch (Exception error) { errors.Add(side + ".Cards: " + Error(error)); }
                root[side] = JsonValue.FromObject(player);
            }
            root["Errors"] = LiveSkillReader.SerializeTemplate(errors);
            return JsonValue.FromObject(root);
        }

        private static JsonValue Card(object native, List<string> errors, string side)
        {
            Dictionary<string, JsonValue> card = new Dictionary<string, JsonValue>();
            foreach (string name in new string[] { "InstanceId", "TemplateId", "Type", "Size", "Section",
                "LeftSocketId", "Tier", "Tags", "HiddenTags", "Heroes", "State", "Name", "Attributes", "Template" })
                card[name] = Read(native, name, errors, side + ".Card." + name);
            try
            {
                // GetEnchantment() is an extension in the game; ItemCard owns the actual property.
                // Skills have no enchantment and must not produce a false capture error.
                PropertyInfo enchant = native.GetType().GetProperty("Enchantment");
                card["Enchantment"] = LiveSkillReader.SerializeTemplate(enchant == null ? null : enchant.GetValue(native, null));
            }
            catch (Exception error) { errors.Add(side + ".Card.Enchantment: " + Error(error)); }
            return JsonValue.FromObject(card);
        }

        // Match BPP's concrete-message serializer/options, keeping the exact wire data as well as named JSON.
        public static JsonValue Message(object message)
        {
            List<string> errors = new List<string>();
            Dictionary<string, JsonValue> result = new Dictionary<string, JsonValue>();
            result["Type"] = JsonValue.FromString(message.GetType().FullName);
            result["Native"] = Safe(message, errors, "NativeMessage");
            try
            {
                Type config = LiveSkillReader.FindType("BazaarGameShared.MessagePackConfig");
                object options = config == null ? null : LiveSkillReader.GetStatic(config, "Options");
                if (options == null) throw new InvalidOperationException("MessagePackConfig.Options unavailable");
                Type serializer = LiveSkillReader.FindType("MessagePack.MessagePackSerializer");
                MethodInfo serialize = null;
                if (serializer != null) foreach (MethodInfo method in serializer.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (method.Name == "Serialize" && method.IsGenericMethodDefinition && method.ReturnType == typeof(byte[]) &&
                        method.GetParameters().Length == 3) { serialize = method; break; }
                if (serialize == null) throw new InvalidOperationException("MessagePack.Serialize<T> unavailable");
                byte[] bytes = (byte[])serialize.MakeGenericMethod(message.GetType()).Invoke(null,
                    new object[] { message, options, CancellationToken.None });
                result["MessagePackBase64"] = JsonValue.FromString(Convert.ToBase64String(bytes));
            }
            catch (Exception error) { errors.Add("MessagePack: " + Error(error)); }
            result["Errors"] = LiveSkillReader.SerializeTemplate(errors);
            return JsonValue.FromObject(result);
        }

        public static object ReadyManager()
        {
            Type data = LiveSkillReader.FindType("TheBazaar.Data");
            MethodInfo get = data == null ? null : data.GetMethod("GetStatic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            object manager = get == null ? null : get.Invoke(null, null);
            System.Threading.Tasks.Task task = manager as System.Threading.Tasks.Task;
            if (task != null)
            {
                if (!task.IsCompleted || task.IsFaulted || task.IsCanceled) return null;
                manager = LiveSkillReader.GetMember(manager, "Result");
            }
            return manager;
        }

        public static JsonValue Catalog(object manager)
        {
            List<string> errors = new List<string>();
            Dictionary<string, JsonValue> templates = new Dictionary<string, JsonValue>();
            MethodInfo get = manager.GetType().GetMethod("GetCardMap", Type.EmptyTypes);
            if (get == null) throw new InvalidOperationException("GetCardMap unavailable");
            object map;
            try { map = get.Invoke(manager, null); }
            catch (Exception error) { errors.Add("GetCardMap: " + Error(error)); map = null; }
            foreach (object pair in ReflectionUtil.GetEnumerable(map))
            {
                string id = Convert.ToString(LiveSkillReader.GetMember(pair, "Key"));
                object template = LiveSkillReader.GetMember(pair, "Value");
                templates[id] = Safe(template, errors, "Template." + id);
            }
            if (templates.Count == 0) errors.Add("Empty card catalog");
            return JsonValue.FromObject(new Dictionary<string, JsonValue> {
                { "Schema", JsonValue.FromString("actionaxis.catalog.v1") },
                { "Templates", JsonValue.FromObject(templates) },
                { "Errors", LiveSkillReader.SerializeTemplate(errors) }
            });
        }

        private static JsonValue Safe(object value, List<string> errors, string path)
        {
            try { return LiveSkillReader.SerializeTemplate(value, path); }
            catch (Exception error) { errors.Add(path + ": " + Error(error)); return JsonValue.Null; }
        }
        private static JsonValue Read(object value, string name, List<string> errors, string path)
        {
            try { return Safe(LiveSkillReader.GetMember(value, name), errors, path); }
            catch (Exception error) { errors.Add(path + ": " + Error(error)); return JsonValue.Null; }
        }
        public static string Error(Exception error)
        { while (error.InnerException != null) error = error.InnerException; return error.Message; }
    }
}
