using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    // BPP's item snapshot exposes the enchantment name and live attributes, but
    // does not include the additional native ability graph. Resolve the same
    // instance in Data.Entities; never infer an enchantment from an attribute.
    public static class LiveCardDetailReader
    {
        public static void Populate(BoardReadResult result)
        {
            if (result == null) throw new ArgumentNullException("result");
            result.CardReadError = null;
            foreach (LiveCardEntry entry in result.Cards)
            {
                entry.EnchantmentDefinition = null;
                entry.EnchantmentReadError = null;
                entry.Template = null;
            }
            try
            {
                Type data = LiveSkillReader.FindType("TheBazaar.Data");
                if (data == null) throw new InvalidOperationException("未找到游戏 Data 类型");
                Dictionary<string, object> entities = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (object pair in ReflectionUtil.GetEnumerable(LiveSkillReader.GetStatic(data, "Entities")))
                {
                    object native = LiveSkillReader.GetMember(pair, "Value");
                    if (native == null) continue;
                    object instance = LiveSkillReader.GetMember(native, "InstanceId");
                    string key = Text(LiveSkillReader.GetMember(instance, "Value")) ?? Text(instance);
                    if (key != null) entities[key] = native;
                }
                foreach (LiveCardEntry entry in result.Cards)
                {
                    try
                    {
                        object native;
                        if (string.IsNullOrEmpty(entry.InstanceId) || !entities.TryGetValue(entry.InstanceId, out native))
                            throw new InvalidOperationException("未找到对应的卡牌实例");
                        object template = LiveSkillReader.GetMember(native, "Template");
                        if (template == null) throw new InvalidOperationException("原生卡牌模板不可用");
                        entry.Template = LiveSkillReader.SerializeTemplate(template);
                        if (string.IsNullOrEmpty(entry.Enchantment) || entry.Enchantment == "None") continue;
                        object selected = null;
                        foreach (object pair in ReflectionUtil.GetEnumerable(LiveSkillReader.GetMember(template, "Enchantments")))
                        {
                            if (Text(LiveSkillReader.GetMember(pair, "Key")) == entry.Enchantment)
                            {
                                selected = LiveSkillReader.GetMember(pair, "Value");
                                break;
                            }
                        }
                        if (selected == null) throw new InvalidOperationException("原生模板中缺少该附魔定义");
                        entry.EnchantmentDefinition = LiveSkillReader.SerializeTemplate(selected);
                    }
                    catch (Exception error)
                    {
                        while (error.InnerException != null) error = error.InnerException;
                        if (!string.IsNullOrEmpty(entry.Enchantment) && entry.Enchantment != "None")
                            entry.EnchantmentReadError = error.Message;
                        AddError(result, entry.TemplateId + " [" + entry.Enchantment + "]：" + error.Message);
                    }
                }
            }
            catch (Exception error)
            {
                AddError(result, "读取卡牌效果失败：" + error.Message);
            }
        }

        private static string Text(object value) { return value == null ? null : value.ToString(); }
        private static void AddError(BoardReadResult result, string message)
        {
            result.CardReadError = string.IsNullOrEmpty(result.CardReadError) ? message : result.CardReadError + "；" + message;
        }
    }
}
