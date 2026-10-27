using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayFourthStateExportTests
    {
        public static void Run()
        {
            FourthBattleDisableMatchesNativeStateFields();
            AcceptedRepairRestoresAliveAndRepeatedStatesAreOmitted();
            SameFrameStateRoundTripHasNoFinalChange();
            SnapshotsAndUnrelatedEventsDoNotInventStateChanges();
        }

        private static BattleEvent StateEvent(string kind, string target, double time)
        {
            return new BattleEvent { Kind = kind, TargetItemId = target, Time = time,
                SourceItemId = "state-source", Side = Side.Player, TargetSide = Side.Enemy, Value = 1 };
        }

        private static List<Dictionary<string, object>> States(BattleResult result)
        {
            MethodInfo export = typeof(NativeReplayLedgerExport).GetMethod("CardUpdates",
                BindingFlags.Static | BindingFlags.NonPublic);
            List<object> exported = (List<object>)export.Invoke(null, new object[] { result });
            List<Dictionary<string, object>> states = new List<Dictionary<string, object>>();
            foreach (object value in exported)
            {
                Dictionary<string, object> row = (Dictionary<string, object>)value;
                object kind;
                if (row.TryGetValue("kind", out kind) && (string)kind == "state") states.Add(row);
            }
            return states;
        }

        private static void FourthBattleDisableMatchesNativeStateFields()
        {
            // b60fa630 frame 163: native Kiu281b is Alive -> Disabled.
            // The accepted sim event exists even when numeric snapshots do
            // not change, so the ledger must expose this categorical field.
            BattleResult result = new BattleResult();
            result.Events.Add(StateEvent("disable", "Kiu281b", 8.15));
            List<Dictionary<string, object>> rows = States(result);
            Assert.Equal(1, rows.Count);
            Assert.Equal(163, Convert.ToInt32(rows[0]["frame"]));
            Assert.Near(8.15, Convert.ToDouble(rows[0]["time"]));
            Assert.Equal("Kiu281b", (string)rows[0]["instanceId"]);
            Assert.Equal("enemy", (string)rows[0]["side"]);
            Assert.Equal("Alive", (string)rows[0]["previous"]);
            Assert.Equal("Disabled", (string)rows[0]["current"]);
            Assert.True(!rows[0].ContainsKey("attribute"));
        }

        private static void AcceptedRepairRestoresAliveAndRepeatedStatesAreOmitted()
        {
            // fb7f06c3 frames 80/82: Cku1bCr disabled, then repaired Alive.
            BattleResult result = new BattleResult();
            result.Events.Add(StateEvent("repair", "Cku1bCr", 3));
            result.Events.Add(StateEvent("disable", "Cku1bCr", 4));
            result.Events.Add(StateEvent("disable", "Cku1bCr", 4.05));
            result.Events.Add(StateEvent("repair", "Cku1bCr", 4.1));
            result.Events.Add(StateEvent("repair", "Cku1bCr", 4.15));
            List<Dictionary<string, object>> rows = States(result);
            Assert.Equal(2, rows.Count);
            Assert.Equal(80, Convert.ToInt32(rows[0]["frame"]));
            Assert.Equal("Alive", (string)rows[0]["previous"]);
            Assert.Equal("Disabled", (string)rows[0]["current"]);
            Assert.Equal(82, Convert.ToInt32(rows[1]["frame"]));
            Assert.Equal("Disabled", (string)rows[1]["previous"]);
            Assert.Equal("Alive", (string)rows[1]["current"]);
        }

        private static void SameFrameStateRoundTripHasNoFinalChange()
        {
            BattleResult result = new BattleResult();
            result.Events.Add(StateEvent("disable", "round-trip", 4));
            result.Events.Add(StateEvent("repair", "round-trip", 4));
            result.Events.Add(StateEvent("disable", "round-trip", 4.05));
            List<Dictionary<string, object>> rows = States(result);
            Assert.Equal(1, rows.Count);
            Assert.Equal(81, Convert.ToInt32(rows[0]["frame"]));
            Assert.Equal("Alive", (string)rows[0]["previous"]);
            Assert.Equal("Disabled", (string)rows[0]["current"]);
        }

        private static void SnapshotsAndUnrelatedEventsDoNotInventStateChanges()
        {
            BattleResult result = new BattleResult();
            result.Events.Add(StateEvent("destroy", "removed", 2));
            result.Events.Add(StateEvent("transform", "transformed", 3));
            result.Events.Add(StateEvent("activate", "ordinary", 4));
            result.Events.Add(StateEvent("disable", null, 5));
            ItemSnapshot first = new ItemSnapshot { ItemId = "snapshot-only", Time = 0 };
            first.Attributes["CooldownDisabled"] = 0;
            result.ItemSnapshots.Add(first);
            ItemSnapshot later = new ItemSnapshot { ItemId = "snapshot-only", Time = 1 };
            later.Attributes["CooldownDisabled"] = 1;
            result.ItemSnapshots.Add(later);
            Assert.Equal(0, States(result).Count);
        }
    }
}
