using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReflectionTests
    {
        private enum FakeTier { Gold }
        private enum FakeSocket { Socket_3 }
        private enum FakeAttr { CooldownMax }

        private sealed class FakeCard
        {
            public string InstanceId { get; set; }
            public string TemplateId { get; set; }
            public object Tier { get; set; }
            public string Size { get; set; }
            public int Order { get; set; }
            public object SocketId { get; set; }
            public object EnchantmentType { get; set; }
            public Dictionary<object, int> Attributes { get; set; }

            public FakeCard()
            {
                InstanceId = "itm_1";
                TemplateId = "ABC-123";
                Tier = FakeTier.Gold;
                Size = "Large";
                Order = 2;
                SocketId = FakeSocket.Socket_3;
                Attributes = new Dictionary<object, int>();
                Attributes[FakeAttr.CooldownMax] = 7000;
            }
        }

        private sealed class FakeSnapshot
        {
            public List<FakeCard> BoardItems { get; set; }

            public FakeSnapshot(bool empty)
            {
                BoardItems = new List<FakeCard>();
                if (!empty) BoardItems.Add(new FakeCard());
            }
        }

        public static void Run()
        {
            var result = SnapshotExtractor.FromSnapshot(new FakeSnapshot(false));
            Assert.True(result.Ok);
            Assert.Equal(1, result.Cards.Count);
            Assert.Equal("abc-123", result.Cards[0].TemplateId);
            Assert.Equal("Gold", result.Cards[0].Tier);
            Assert.Equal("Large", result.Cards[0].Size);
            Assert.Equal(3, result.Cards[0].Socket);
            Assert.Equal(7000, result.Cards[0].Attributes["CooldownMax"]);

            Assert.False(SnapshotExtractor.FromSnapshot(null).Ok);
            Assert.False(SnapshotExtractor.FromSnapshot(new FakeSnapshot(true)).Ok);

            var snapshot = new FakeSnapshot(true);
            var card = new FakeCard();
            card.SocketId = null;
            card.Order = 4;
            snapshot.BoardItems.Add(card);
            var fallback = SnapshotExtractor.FromSnapshot(snapshot);
            Assert.True(fallback.Ok);
            Assert.Equal(4, fallback.Cards[0].Socket);
        }
    }
}
