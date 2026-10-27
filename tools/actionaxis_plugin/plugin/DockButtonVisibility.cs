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
        private readonly Dictionary<string, MemberInfo> _members = new Dictionary<string, MemberInfo>();

        public DockButtonVisibility(Func<string, Type> findType)
        {
            _findType = findType;
        }

        public bool CanOpen()
        {
            try
            {
                if (ReadFlag("TheBazaar.AppState", "BlockInput", true)) return false;
                if (!HasActiveRun()) return false;
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

        private bool HasActiveRun()
        {
            // Data.Run survives menu/loading transitions. Require the live native
            // app state as well, and reject the handover frame where it disagrees
            // with the server's current run state. BPP's GameStateProbe uses the
            // same RunAppState boundary; StartRunAppState is still a loading state.
            object run = ReadStatic("TheBazaar.Data", "Run", false);
            if (run == null || ReadInstance(run, "Player") == null) return false;
            object appState = ReadStatic("TheBazaar.AppState", "CurrentState", false);
            Type runStateType = _findType("TheBazaar.RunAppState");
            if (runStateType == null || appState == null || !runStateType.IsInstanceOfType(appState)) return false;
            object state = ReadStatic("TheBazaar.Data", "CurrentState", false);
            string stateName = Text(ReadInstance(state, "StateName"));
            if (stateName != Text(ReadInstance(appState, "StateName"))) return false;
            switch (stateName)
            {
                case "Choice": case "Encounter": case "Combat": case "PVPCombat":
                case "LevelUp": case "Loot": case "Pedestal": return true;
                default: return false;
            }
        }

        private bool ReadFlag(string typeName, string propertyName, bool missingValue)
        {
            object value = ReadStatic(typeName, propertyName, true);
            if (value == null) return missingValue;
            if (!(value is bool)) throw new InvalidOperationException(typeName + "." + propertyName);
            return (bool)value;
        }

        private object ReadStatic(string typeName, string name, bool optionalType)
        {
            string key = typeName + "." + name;
            MemberInfo member;
            if (!_members.TryGetValue(key, out member))
            {
                Type type = _findType(typeName);
                // 不缓存缺失类型，兼容 BPP 比行动轴更晚加载。
                if (type == null)
                {
                    if (optionalType) return null;
                    throw new InvalidOperationException(typeName);
                }
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                member = (MemberInfo)type.GetProperty(name, flags) ?? type.GetField(name, flags);
                if (member == null) throw new InvalidOperationException(key);
                _members.Add(key, member);
            }
            PropertyInfo property = member as PropertyInfo;
            return property == null ? ((FieldInfo)member).GetValue(null) : property.GetValue(null, null);
        }

        private static object ReadInstance(object owner, string name)
        {
            if (owner == null) return null;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = owner.GetType().GetProperty(name, flags);
            if (property != null) return property.GetValue(owner, null);
            FieldInfo field = owner.GetType().GetField(name, flags);
            return field == null ? null : field.GetValue(owner);
        }

        private static string Text(object value) { return value == null ? null : value.ToString(); }
    }
}
