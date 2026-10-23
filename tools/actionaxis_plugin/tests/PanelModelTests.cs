using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class PanelModelTests
    {
        public static void Run()
        {
            var zero = new CardStats();
            Assert.Equal(0, PanelModel.BadgeLines(zero).Count);
            Assert.Equal("x0", PanelModel.TriggerText(zero));
            Assert.Equal("x0", PanelModel.TriggerText(null));

            var stats = new CardStats();
            stats.ActivateCount = 3;
            stats.Damage = 42;
            stats.FreezeCount = 2;
            stats.FreezeSeconds = 4;
            stats.Heal = 9;

            List<BadgeLine> lines = PanelModel.BadgeLines(stats);
            Assert.Equal(3, lines.Count);
            Assert.Equal("x3", PanelModel.TriggerText(stats));
            Assert.Equal("伤害 42", lines[0].Text);
            Assert.Equal("damage", lines[0].ColorKey);
            Assert.Equal("冻结 ×2 · 4s", lines[1].Text);
            Assert.Equal("freeze", lines[1].ColorKey);
            Assert.Equal("治疗 9", lines[2].Text);
            Assert.Equal("heal", lines[2].ColorKey);

            BoardSlotsPreserveSizeAndGaps();

            Assert.Near(0, PanelModel.PlayheadFromMouse(-10, 100, 800, 60));
            Assert.Near(60, PanelModel.PlayheadFromMouse(2000, 100, 800, 60));
            Assert.Near(22.5, PanelModel.PlayheadFromMouse(400, 100, 800, 60));
            Assert.Near(0, PanelModel.PlayheadFromMouse(400, 100, 0, 60));

            Assert.Equal("42", PanelModel.FormatNumber(42));
            Assert.Equal("3.5", PanelModel.FormatNumber(3.5));
        }

        private static void BoardSlotsPreserveSizeAndGaps()
        {
            CardDef small = new CardDef();
            CardDef medium = new CardDef();
            medium.Size = "Medium";
            CardDef large = new CardDef();
            large.Size = "Large";
            Assert.Equal(1, PanelModel.CardSpan(small));
            Assert.Equal(2, PanelModel.CardSpan(medium));
            Assert.Equal(3, PanelModel.CardSpan(large));

            // Ten 80 px sockets, nine 8 px gaps. Socket 0 is intentionally empty.
            const float boardX = 20;
            const float boardWidth = 872;
            const float gap = 8;
            Assert.Near(80, PanelModel.BoardUnitWidth(boardWidth, gap));
            Assert.Near(108, PanelModel.BoardCardX(1, boardX, boardWidth, gap));
            Assert.Near(196, PanelModel.BoardCardX(2, boardX, boardWidth, gap));
            Assert.Near(168, PanelModel.BoardCardWidth(medium, boardWidth, gap));
            Assert.Near(256, PanelModel.BoardCardWidth(large, boardWidth, gap));
            float mediumRight = PanelModel.BoardCardX(2, boardX, boardWidth, gap) +
                PanelModel.BoardCardWidth(medium, boardWidth, gap);
            Assert.Near(mediumRight + gap, PanelModel.BoardCardX(4, boardX, boardWidth, gap));
            Assert.Near(boardX + boardWidth, PanelModel.BoardCardX(7, boardX, boardWidth, gap) +
                PanelModel.BoardCardWidth(large, boardWidth, gap));
        }
    }
}
