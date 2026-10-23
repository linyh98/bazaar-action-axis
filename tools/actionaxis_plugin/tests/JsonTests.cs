using System;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class JsonTests
    {
        public static void Run()
        {
            var v = Json.Parse("{\"a\":1,\"b\":\"x\\ny\",\"c\":[true,null,2.5],\"d\":{\"e\":\"\\u4e2d\"}}");
            Assert.Equal(1, v["a"].AsInt());
            Assert.Equal("x\ny", v["b"].AsString());
            Assert.Equal(true, v["c"][0].AsBool());
            Assert.Equal(JsonKind.Null, v["c"][1].Kind);
            Assert.Equal(2.5, v["c"][2].AsDouble());
            Assert.Equal("中", v["d"]["e"].AsString());

            var cards = Json.Parse(File.ReadAllText("src/data/cards.json"));
            Assert.Equal(3620, cards.Count);
            Assert.Equal("0efe7d3f-4f18-4667-89a3-ac915a93a9f5@Gold", cards[0]["id"].AsString());
            Assert.Throws<FormatException>(() => Json.Parse("{"));
        }
    }
}
