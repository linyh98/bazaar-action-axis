using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace BazaarSim
{
    [BepInPlugin("com.bazaarsim.livecardsdump", "Bazaar Live Cards Dump", "1.0.0")]
    public class LiveCardsDumpPlugin : BaseUnityPlugin
    {
        private const string OutPath = @"D:\bazaar-action-axis\captures\live_cards_bpp.json";
        private const string ErrorPath = @"D:\bazaar-action-axis\captures\live_cards_bpp.error.txt";

        private float _nextDump;
        private string _lastJson = "";

        private void Update()
        {
            if (Time.time < _nextDump)
            {
                return;
            }

            _nextDump = Time.time + 2f;

            try
            {
                string json = BuildJson();
                if (json == null || json == _lastJson)
                {
                    return;
                }

                _lastJson = json;
                EnsureDirectory(OutPath);
                File.WriteAllText(OutPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                try
                {
                    EnsureDirectory(ErrorPath);
                    File.WriteAllText(ErrorPath, ex.ToString(), new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }

        private static void EnsureDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string BuildJson()
        {
            Type readerType = FindReaderType();
            if (readerType == null)
            {
                return null;
            }

            object reader = Activator.CreateInstance(readerType, true);
            MethodInfo read = readerType.GetMethod(
                "Read",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (read == null)
            {
                return null;
            }

            object outcome = read.Invoke(reader, null);
            object snapshot = GetProp(outcome, "Snapshot");
            if (snapshot == null)
            {
                return null;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("{\"capturedAt\":\"");
            builder.Append(DateTime.UtcNow.ToString("o"));
            builder.Append("\",\"hero\":");
            AppendNullableString(builder, EnumText(GetProp(snapshot, "Hero")));
            builder.Append(",\"shop\":");
            AppendCards(builder, GetProp(snapshot, "ShopItems"));
            builder.Append(",\"board\":");
            AppendCards(builder, GetProp(snapshot, "BoardItems"));
            builder.Append(",\"stash\":");
            AppendCards(builder, GetProp(snapshot, "StashItems"));
            builder.Append("}");
            return builder.ToString();
        }

        private static Type FindReaderType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(
                    "BazaarPlusPlus.GameInterop.LiveCards.LiveCardSnapshotReader",
                    false
                );
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void AppendCards(StringBuilder builder, object items)
        {
            builder.Append("[");
            IEnumerable enumerable = items as IEnumerable;
            bool first = true;
            if (enumerable != null)
            {
                foreach (object item in enumerable)
                {
                    if (!first)
                    {
                        builder.Append(",");
                    }

                    first = false;
                    AppendCard(builder, item);
                }
            }

            builder.Append("]");
        }

        private static void AppendCard(StringBuilder builder, object card)
        {
            builder.Append("{");
            builder.Append("\"instanceId\":");
            AppendNullableString(builder, GetProp(card, "InstanceId") as string);
            builder.Append(",\"templateId\":");
            AppendNullableString(builder, EnumText(GetProp(card, "TemplateId")));
            builder.Append(",\"order\":");
            builder.Append(IntText(GetProp(card, "Order")));
            builder.Append(",\"tier\":");
            AppendNullableString(builder, EnumText(GetProp(card, "Tier")));
            builder.Append(",\"size\":");
            AppendNullableString(builder, EnumText(GetProp(card, "Size")));
            builder.Append(",\"enchantment\":");
            AppendNullableString(builder, EnumText(GetProp(card, "EnchantmentType")));
            builder.Append(",\"socket\":");
            AppendNullableString(builder, EnumText(GetProp(card, "SocketId")));
            builder.Append(",\"attributes\":{");

            IEnumerable attributes = GetProp(card, "Attributes") as IEnumerable;
            bool first = true;
            if (attributes != null)
            {
                foreach (object entry in attributes)
                {
                    object key = GetProp(entry, "Key");
                    if (key == null)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        builder.Append(",");
                    }

                    first = false;
                    builder.Append(Quote(key.ToString()));
                    builder.Append(":");
                    builder.Append(IntText(GetProp(entry, "Value")));
                }
            }

            builder.Append("}}");
        }

        private static object GetProp(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            return property == null ? null : property.GetValue(target, null);
        }

        private static string EnumText(object value)
        {
            return value == null ? null : value.ToString();
        }

        private static string IntText(object value)
        {
            if (value == null)
            {
                return "0";
            }

            return Convert.ToInt32(value).ToString();
        }

        private static void AppendNullableString(StringBuilder builder, string value)
        {
            builder.Append(value == null ? "null" : Quote(value));
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("\"");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\')
                {
                    builder.Append('\\');
                    builder.Append(c);
                }
                else if (c == '\n')
                {
                    builder.Append("\\n");
                }
                else if (c == '\r')
                {
                    builder.Append("\\r");
                }
                else if (c == '\t')
                {
                    builder.Append("\\t");
                }
                else
                {
                    builder.Append(c);
                }
            }

            builder.Append("\"");
            return builder.ToString();
        }
    }
}
