using System;
using System.Reflection;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    public sealed class LiveBoardReader
    {
        private const string ReaderTypeName =
            "BazaarPlusPlus.GameInterop.LiveCards.LiveCardSnapshotReader";

        public BoardReadResult Read()
        {
            try
            {
                Type readerType = FindReaderType();
                if (readerType == null)
                    return BoardReadResult.Fail(
                        "未找到 BazaarPlusPlus 的 LiveCardSnapshotReader，请确认 BPP 已安装并进入一局游戏");

                object reader = Activator.CreateInstance(readerType, true);
                MethodInfo read = readerType.GetMethod(
                    "Read",
                    BindingFlags.Public | BindingFlags.Instance);
                if (read == null)
                    return BoardReadResult.Fail("BazaarPlusPlus 的 LiveCardSnapshotReader.Read() 不存在");

                object outcome = read.Invoke(reader, null);
                object snapshot = ReflectionUtil.GetProp(outcome, "Snapshot");
                if (snapshot == null)
                    return BoardReadResult.Fail("BPP 没有返回游戏状态，请先进入一局游戏");

                BoardReadResult result = SnapshotExtractor.FromSnapshot(snapshot);
                LiveCardDetailReader.Populate(result);
                LiveSkillReader.Populate(result);
                if (!result.Ok && result.Error == "当前棋盘没有卡牌" && result.Skills.Count > 0)
                {
                    result.Ok = true;
                    result.Error = null;
                }
                return result;
            }
            catch (Exception error)
            {
                return BoardReadResult.Fail("读取棋盘失败：" + error.Message);
            }
        }

        private static Type FindReaderType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(ReaderTypeName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
