using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    public static class DockButtonLayoutTests
    {
        public static void Run()
        {
            var anchors = new List<DockButtonBounds>();
            var blockers = new List<DockButtonBounds>();
            anchors.Add(new DockButtonBounds(1400, 65, 48, 48, true));
            anchors.Add(new DockButtonBounds(1400, 120, 48, 48, false));
            DockButtonBounds placed;
            Assert.True(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            Assert.Near(1400, placed.X);
            Assert.Near(119.24, placed.Y);

            // The recorder is visible during combat. Occupy the next actual slot.
            anchors[1] = new DockButtonBounds(1400, 120, 48, 48, true);
            Assert.True(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            Assert.Near(174.24, placed.Y);
            Assert.False(placed.Overlaps(anchors[0]));
            Assert.False(placed.Overlaps(anchors[1]));

            // 离开战斗后录制按钮隐藏，行动轴应立即回到数据库按钮下面。
            anchors[1] = new DockButtonBounds(1400, 120, 48, 48, false);
            Assert.True(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            Assert.Near(119.24, placed.Y);
            anchors[1] = new DockButtonBounds(1400, 120, 48, 48, true);

            blockers.Add(new DockButtonBounds(1400, 173, 48, 50, true));
            Assert.True(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            Assert.Near(228.48, placed.Y);
            Assert.False(placed.Overlaps(blockers[0]));

            // Scene clones off the viewport do not move the visible button.
            anchors.Add(new DockButtonBounds(1400, 1200, 48, 48, true));
            Assert.True(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            Assert.Near(228.48, placed.Y);

            anchors.Clear();
            blockers.Clear();
            anchors.Add(new DockButtonBounds(1000, 65, 60, 60, true));
            Assert.True(DockButtonLayout.TryPlace(1100, 700, anchors, blockers, 48, out placed));
            Assert.Near(1000, placed.X);
            Assert.Near(60, placed.Width);
            Assert.Near(132.8, placed.Y);

            anchors[0] = new DockButtonBounds(1000, 635, 60, 60, true);
            Assert.False(DockButtonLayout.TryPlace(1100, 700, anchors, blockers, 48, out placed));
            Assert.False(DockButtonLayout.TryPlace(0, 700, anchors, blockers, 48, out placed));

            // 主界面锚点消失、隐藏或离屏后，不能退回屏幕边缘生成悬浮入口。
            anchors.Clear();
            Assert.False(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            Assert.False(placed.Visible);
            Assert.False(DockButtonLayout.TryPlace(1465, 903, null, blockers, 48, out placed));
            anchors.Add(new DockButtonBounds(1400, 65, 48, 48, false));
            Assert.False(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            anchors[0] = new DockButtonBounds(1400, 1200, 48, 48, true);
            Assert.False(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));
            anchors[0] = new DockButtonBounds(1400, 65, 48, 48, true);
            Assert.True(DockButtonLayout.TryPlace(1465, 903, anchors, blockers, 48, out placed));

            MatchBppSize();
            VisibilityRules();
        }

        private static void MatchBppSize()
        {
            foreach (double scale in new[] { 0.25, 0.5, 1.0, 2.0, 4.0 })
            {
                var anchors = new List<DockButtonBounds>();
                var blockers = new List<DockButtonBounds>();
                var frame = new DockButtonBounds(1480 * scale, 50 * scale, 56 * scale, 48 * scale, true);
                // 包含阴影和录制提示的避让范围明显大于真正的按钮外框。
                anchors.Add(new DockButtonBounds(1460 * scale, 30 * scale, 100 * scale, 90 * scale, true));
                anchors.Add(new DockButtonBounds(1440 * scale, 140 * scale, 140 * scale, 110 * scale, true));
                DockButtonBounds placed;
                Assert.True(DockButtonLayout.TryPlace(1600 * scale, 900 * scale, anchors, blockers, frame, out placed));
                Assert.Near(frame.Width, placed.Width);
                Assert.Near(frame.Height, placed.Height);
                Assert.Near(frame.CenterX, placed.CenterX);
                Assert.True(placed.Y > anchors[1].Bottom);
                Assert.False(placed.Overlaps(anchors[1]));

                // 录制按钮隐藏只移动入口，不改变宽高或横向对齐。
                anchors.RemoveAt(1);
                double previousY = placed.Y;
                Assert.True(DockButtonLayout.TryPlace(1600 * scale, 900 * scale, anchors, blockers, frame, out placed));
                Assert.Near(frame.Width, placed.Width);
                Assert.Near(frame.Height, placed.Height);
                Assert.Near(frame.CenterX, placed.CenterX);
                Assert.True(placed.Y < previousY);

                // 额外障碍仍需避让，视觉尺寸和点击区域不能因此缩放。
                blockers.Add(placed);
                Assert.True(DockButtonLayout.TryPlace(1600 * scale, 900 * scale, anchors, blockers, frame, out placed));
                Assert.False(placed.Overlaps(blockers[0]));
                Assert.Near(frame.Width, placed.Width);
                Assert.Near(frame.Height, placed.Height);
                anchors.Clear();
                Assert.False(DockButtonLayout.TryPlace(1600 * scale, 900 * scale, anchors, blockers, frame, out placed));
            }

            // 小窗口也不能用屏幕百分比上限把 BPP 的原生尺寸缩小。
            var largeFrame = new DockButtonBounds(150, 4, 80, 60, true);
            var largeAnchors = new List<DockButtonBounds> { largeFrame };
            DockButtonBounds largePlaced;
            Assert.True(DockButtonLayout.TryPlace(240, 180, largeAnchors, null, largeFrame, out largePlaced));
            Assert.Near(80, largePlaced.Width);
            Assert.Near(60, largePlaced.Height);
            Assert.False(DockButtonLayout.TryPlace(240, 110, largeAnchors, null, largeFrame, out largePlaced));
        }

        private static void VisibilityRules()
        {
            var types = new Dictionary<string, Type>();
            types.Add("TheBazaar.AppState", typeof(NativeState));
            var visibility = new DockButtonVisibility(delegate(string name) {
                Type type;
                return types.TryGetValue(name, out type) ? type : null;
            });
            NativeState.BlockInput = false;
            Assert.True(visibility.CanOpen()); // 未安装 BPP 仍允许原生主界面入口。
            NativeState.BlockInput = true;
            Assert.False(visibility.CanOpen());
            NativeState.BlockInput = false;
            Assert.True(visibility.CanOpen());

            // 同一个适配器能发现后加载的 BPP，并实时响应三种面板的开关。
            types.Add("BazaarPlusPlus.Game.CollectionPanel.CollectionPanel", typeof(CollectionState));
            types.Add("BazaarPlusPlus.Game.HistoryPanel.HistoryPanel", typeof(HistoryState));
            types.Add("BazaarPlusPlus.Game.LiveBuildPanel.LiveBuildPanel", typeof(BuildState));
            for (int index = 0; index < 3; index++)
            {
                _openPanel = index;
                Assert.False(visibility.CanOpen());
                Assert.False(NativeState.BlockInput); // 检查状态不能更改其他插件的输入状态。
                _openPanel = -1;
                Assert.True(visibility.CanOpen());
            }
            _throwOnRead = true;
            Assert.False(visibility.CanOpen());
            _throwOnRead = false;
            Assert.True(visibility.CanOpen());

            Assert.False(new DockButtonVisibility(delegate { return null; }).CanOpen());
            Assert.False(new DockButtonVisibility(delegate { return typeof(string); }).CanOpen());
            types["BazaarPlusPlus.Game.CollectionPanel.CollectionPanel"] = typeof(string);
            Assert.False(new DockButtonVisibility(delegate(string name) { return types[name]; }).CanOpen());
        }

        private static int _openPanel = -1;
        private static bool _throwOnRead;
        private static bool PanelOpen(int index)
        {
            if (_throwOnRead) throw new InvalidOperationException("模拟 BPP 状态读取失败");
            return _openPanel == index;
        }
        private static class NativeState { public static bool BlockInput { get; set; } }
        private static class CollectionState { public static bool IsVisible { get { return PanelOpen(0); } } }
        private static class HistoryState { public static bool IsVisible { get { return PanelOpen(1); } } }
        private static class BuildState { public static bool IsVisible { get { return PanelOpen(2); } } }
    }
}
