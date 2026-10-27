using System;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayCr2VirusTransformTests
    {
        public static void Run()
        {
            string output = Path.GetTempFileName();
            try
            {
                Assert.Equal(0, NativeReplayLedgerExport.Run(
                    "tools/actionaxis_plugin/tests/fixtures/native_cr2_virus_replay.json",
                    "tools/actionaxis_plugin/build/native_mapping_templates.jsonl",
                    "src/data/cards.json", output));
                JsonValue ledger = Json.Parse(File.ReadAllText(output));
                Assert.Near(21.25, ledger["combat"]["outcome"]["endTime"].AsDouble());
                Assert.Equal("enemy", ledger["combat"]["outcome"]["winnerSide"].AsString());
                foreach (string channel in new[] { "target", "critical", "order", "identity" })
                {
                    Assert.Equal(ledger["replayRandom"][channel + "Records"].AsInt(),
                        ledger["replayRandom"][channel + "Applied"].AsInt());
                    Assert.Equal(0, ledger["replayRandom"][channel + "Fallbacks"].AsInt());
                }
                Assert.Equal(3, ledger["replayRandom"]["identityApplied"].AsInt());
                Assert.Equal(0, ledger["replayRandom"]["invalidChoices"].AsInt());
                int transforms = 0, lethalPoison = 0, toxicBirth = 0;
                foreach (JsonValue ev in ledger["combat"]["events"].AsArray())
                {
                    if (ev["kind"].AsString() == "transform")
                    {
                        transforms++;
                        Assert.True(ev["source"].AsString() != ev["target"].AsString());
                        Assert.Equal("020a0ec0-21e6-41af-899f-063573ba9ca5", ev["value"].AsString());
                    }
                    if (ev["frame"].AsInt() == 400 && ev["kind"].AsString() == "poisonApply")
                    {
                        lethalPoison++;
                        Assert.Near(16, ev["value"].AsDouble());
                    }
                }
                foreach (JsonValue row in ledger["combat"]["cardUpdates"].AsArray())
                    if (row["frame"].AsInt() == 320 && row["attribute"].AsString() == "PoisonApplyAmount" &&
                        row["current"].AsDouble() == 16)
                    {
                        toxicBirth++;
                        Assert.Near(0, row["previous"].AsDouble());
                    }
                Assert.Equal(3, transforms);
                Assert.Equal(1, lethalPoison);
                Assert.Equal(1, toxicBirth);
            }
            finally { File.Delete(output); }
        }
    }
}
