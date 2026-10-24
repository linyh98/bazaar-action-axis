using System.Collections.Generic;
using BazaarActionAxis.Engine;

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
        }
    }
}
