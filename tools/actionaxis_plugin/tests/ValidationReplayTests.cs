using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    public static class ValidationReplayTests
    {
        public static void Run()
        {
            CapturesBothSidesBeforePlayback();
            List<JsonValue> saved = new List<JsonValue>();
            ValidationReplayCapture capture = new ValidationReplayCapture(delegate(string name, JsonValue data) {
                saved.Add(data); }, JsonValue.Null, "catalog-test.json.gz");
            JsonValue message = Json.Parse("{\"Native\":{\"MessageId\":\"first\"}}");
            JsonValue before = Json.Parse("{\"Player\":{\"Attributes\":{\"Health\":123}}}");
            capture.Accept("NetMessageGameSim", "first", true, message, before, JsonValue.Null);
            Assert.Equal("opening", saved[0]["Stage"].AsString());
            capture.Accept("NetMessageGameSim", "first", true, message, before, JsonValue.Null);
            Assert.Equal(1, saved.Count);
            capture.Accept("NetMessageCombatSim", "combat", false, message, null, null);
            Assert.Equal("combat", saved[1]["Stage"].AsString());
            Assert.Equal("opening", saved[0]["Stage"].AsString());
            Assert.Equal(JsonKind.Null, saved[0]["CombatMessage"].Kind);
            capture.Accept("NetMessageGameSim", "end", false, message, null, null);
            Assert.Equal("complete", saved[2]["Stage"].AsString());
            Assert.Equal(123, saved[2]["OpeningBeforeDispatch"]["Player"]["Attributes"]["Health"].AsInt());
            capture.Accept("NetMessageCombatSim", "orphan", false, message, null, null);
            Assert.Equal("combat-without-opening", saved[3]["CaptureGap"].AsString());
            Assert.Equal(JsonKind.Null, saved[3]["SpawnMessage"].Kind);
            capture.Accept("NetMessageGameSim", "next", true, message, before, null);
            Assert.Equal("interrupted", saved[4]["Stage"].AsString());
            Assert.False(saved[5]["CaptureId"].AsString() == saved[0]["CaptureId"].AsString());
            capture.Interrupt("test-stop");
            Assert.Equal("test-stop", saved[6]["CaptureGap"].AsString());

            // A frozen message is independent of later playback mutations, including crit flags.
            MutableMessage native = new MutableMessage();
            JsonValue frozen = ValidationSnapshot.Message(native);
            native.Data["IsCrit"] = false;
            Assert.True(frozen["Native"]["Data"]["IsCrit"].AsBool());
            Assert.True(frozen["Errors"].Count > 0); // Wire serializer absent in this isolated test.
            JsonValue catalog = ValidationSnapshot.Catalog(new CatalogManager());
            Assert.Equal(1, catalog["Templates"].Count);
            Assert.Equal(0, catalog["Errors"].Count);

            string folder = Path.Combine(Path.GetTempPath(), "axis-recorder-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (ValidationReplayWriter writer = new ValidationReplayWriter(folder))
                {
                    writer.Save("test.json.gz", saved[0]);
                    writer.Save("test.json.gz", saved[2]);
                }
                using (FileStream file = File.OpenRead(Path.Combine(folder, "test.json.gz")))
                using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip))
                    Assert.Equal("complete", Json.Parse(reader.ReadToEnd())["Stage"].AsString());
                Assert.False(File.Exists(Path.Combine(folder, "test.json.gz.tmp")));
            }
            finally { File.Delete(Path.Combine(folder, "test.json.gz")); Directory.Delete(folder); }
        }
        public sealed class MutableMessage { public Dictionary<string, object> Data = new Dictionary<string, object> { { "IsCrit", true } }; }
        private static void CapturesBothSidesBeforePlayback()
        {
            object previousRun = TheBazaar.Data.Run;
            try
            {
                var item = new VerificationItem { InstanceId = "p", TemplateId = "p-template", Enchantment = "Heavy" };
                item.Attributes["Cooldown"] = 2350;
                TheBazaar.Data.VerificationPlayerCards.Add(item);
                TheBazaar.Data.VerificationOpponentCards.Add(new VerificationItem { InstanceId = "e", TemplateId = "e-template" });
                var player = new { Hero = "Vanessa", Attributes = new Dictionary<string, int> { { "Health", 777 } },
                    Skills = new object[] { new BazaarGameClient.Domain.Models.Cards.Card { InstanceId = "skill", TemplateId = "skill-template" } } };
                TheBazaar.Data.Run = new { Player = player, Opponent = player };
                JsonValue snapshot = ValidationSnapshot.Capture();
                item.Attributes["Cooldown"] = 0;
                Assert.Equal(0, snapshot["Errors"].Count);
                Assert.Equal(1, snapshot["Opponent"]["Cards"].Count);
                Assert.Equal("Heavy", snapshot["Player"]["Cards"][0]["Enchantment"].AsString());
                Assert.Equal(2350, snapshot["Player"]["Cards"][0]["Attributes"]["Cooldown"].AsInt());
                Assert.Equal(777, snapshot["Player"]["Attributes"]["Health"].AsInt());
                Assert.Equal("skill", snapshot["Player"]["Skills"][0]["InstanceId"].AsString());
            }
            finally
            {
                TheBazaar.Data.Run = previousRun;
                TheBazaar.Data.VerificationPlayerCards.Clear();
                TheBazaar.Data.VerificationOpponentCards.Clear();
            }
        }
        public sealed class CatalogManager
        {
            public Dictionary<Guid, object> GetCardMap()
            { return new Dictionary<Guid, object> { { Guid.Empty, new { InternalName = "Test", Tiers = new { Gold = 1 } } } }; }
        }
    }
    public enum VerificationSide { Player, Opponent }
    public sealed class VerificationItem : BazaarGameClient.Domain.Models.Cards.Card
    { public string Enchantment { get; set; } }
}

namespace BazaarGameClient.Domain.Models.Cards
{
    public class Card
    {
        public string InstanceId, TemplateId, Tier = "Gold", Section = "Hand";
        public Dictionary<string, int> Attributes = new Dictionary<string, int>();
        public object Template = new { Tiers = new { Gold = new object() } };
    }
}
