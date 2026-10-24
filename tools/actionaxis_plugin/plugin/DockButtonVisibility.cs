using System;
using System.Collections.Generic;
using System.Reflection;

namespace BazaarActionAxis.Plugin
{
    // 只读游戏与 BPP 的面板状态，不接管其他插件的输入或生命周期。
    internal sealed class DockButtonVisibility
    {
        private static readonly string[] PanelTypes = {
            "BazaarPlusPlus.Game.CollectionPanel.CollectionPanel",
            "BazaarPlusPlus.Game.HistoryPanel.HistoryPanel",
            "BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanel"
        };
        private readonly Func<string, Type> _findType;
        private readonly Dictionary<string, PropertyInfo> _properties = new Dictionary<string, PropertyInfo>();

        public DockButtonVisibility(Func<string, Type> findType)
        {
            _findType = findType;
        }

        public bool CanOpen()
        {
            try
            {
                if (ReadFlag("TheBazaar.AppState", "BlockInput", true)) return false;
                foreach (string panel in PanelTypes)
                    if (ReadFlag(panel, "IsVisible", false)) return false;
                return true;
            }
            catch
            {
                // 已安装组件的接口失效时隐藏入口，避免穿透到其他面板上。
                return false;
            }
        }

        private bool ReadFlag(string typeName, string propertyName, bool missingValue)
        {
            PropertyInfo property;
            if (!_properties.TryGetValue(typeName, out property))
            {
                Type type = _findType(typeName);
                // 不缓存缺失类型，兼容 BPP 比行动轴更晚加载。
                if (type == null) return missingValue;
                property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property == null || property.PropertyType != typeof(bool) || !property.CanRead)
                    throw new InvalidOperationException(typeName + "." + propertyName);
                _properties.Add(typeName, property);
            }
            return (bool)property.GetValue(null, null);
        }
    }
}
