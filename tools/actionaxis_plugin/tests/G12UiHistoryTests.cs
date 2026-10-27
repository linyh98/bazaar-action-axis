using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G12 UI gate: the playhead must switch between the old form and the new
    // instance at the transform frame, and must hide an item after removal
    // without discarding the states needed for earlier playhead positions.
    public static class G12UiHistoryTests
    {
        public static void Run()
        {
            SwitchesFormAtTransformFrame();
            HidesRemovedItemWithoutLosingHistory();
        }

        private static CardDef Card(string id, string name)
        {
            return new CardDef { Id = id, BaseItemId = id, Name = name, NameCn = name };
        }

        private static void SwitchesFormAtTransformFrame()
        {
            BattleItemView view = new BattleItemView { Id = "player:slot0", BornAt = 0 };
            view.States.Add(new BattleItemState { Time = 0, Card = Card("old", "Old form"), Slot = 0 });
            view.States.Add(new BattleItemState { Time = 8, Card = Card("new", "New form"), Slot = 0 });
            Assert.Equal("old", view.At(7.999).Card.BaseItemId);
            Assert.Equal("new", view.At(8).Card.BaseItemId);
            Assert.Equal("new", view.At(9).Card.BaseItemId);
            // The old state is still available for a playhead moved backwards.
            Assert.Equal("old", view.At(4).Card.BaseItemId);
        }

        private static void HidesRemovedItemWithoutLosingHistory()
        {
            BattleItemView view = new BattleItemView { Id = "player:slot1", BornAt = 1, RemovedAt = 5 };
            view.States.Add(new BattleItemState { Time = 1, Card = Card("before", "Before"), Slot = 1 });
            view.States.Add(new BattleItemState { Time = 3, Card = Card("after", "After"), Slot = 1 });
            Assert.True(view.At(0.999) == null);
            Assert.Equal("before", view.At(1).Card.BaseItemId);
            Assert.Equal("after", view.At(4.999).Card.BaseItemId);
            Assert.True(view.At(5) == null);
            Assert.Equal("after", view.At(4).Card.BaseItemId);
        }
    }
}
