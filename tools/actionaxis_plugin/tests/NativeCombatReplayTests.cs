using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin;

namespace BazaarActionAxis.Tests
{
    public static class NativeCombatReplayTests
    {
        public static void Run()
        {
            ReadsActualNativeHealthFixture();
            CapsNativeNominalHealingUsingActualReplay();
            SeparatesHealingShieldAndOtherDamage();
            PreservesUnknownAndMissingOpeningHealth();
            ReaderCachesOnlyTheCurrentReplay();
        }

        private static void CapsNativeNominalHealingUsingActualReplay()
        {
            NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(Json.Parse(File.ReadAllText(
                "tools/actionaxis_plugin/tests/fixtures/native_replay_overheal.json")));
            NativeCombatReplaySnapshot firstHeal = data.At(5);
            Assert.Near(550, firstHeal.OpponentHp);
            Assert.Near(54, firstHeal.NominalHealing);
            Assert.Near(20, firstHeal.Healing);
            Assert.Near(4, firstHeal.Regen);
            Assert.Near(0, firstHeal.OtherHpChange);
            NativeCombatReplaySnapshot secondHeal = data.At(10);
            Assert.Near(550, secondHeal.OpponentHp);
            Assert.Near(110, secondHeal.NominalHealing);
            Assert.Near(25, secondHeal.Healing);
            Assert.Near(9, secondHeal.Regen);
            Assert.Near(0, secondHeal.OtherHpChange);
            Assert.True(secondHeal.HealingKnown);
        }

        private static void ReadsActualNativeHealthFixture()
        {
            NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(Json.Parse(File.ReadAllText(
                "tools/actionaxis_plugin/tests/fixtures/native_replay_health.json")));
            Assert.Equal(581, data.FrameCount);
            Assert.Near(29, data.Duration);
            Assert.Near(1685, data.EnemyStartHp);
            NativeCombatReplaySnapshot six = data.At(6.0);
            Assert.Near(1681, six.OpponentHp);
            Assert.Near(5, six.OpponentShield);
            Assert.Near(13, six.TotalDamage);
            Assert.Near(7, six.BurnDamage);
            Assert.Near(6, six.PoisonDamage);
            Assert.Near(7, six.ShieldAbsorbed);
            Assert.Near(1681, data.At(6.999).OpponentHp);
            Assert.Near(1658, data.At(7).OpponentHp); // CurrentValue, not Value/PreviousValue.
            NativeCombatReplaySnapshot middle = data.At(15.3);
            Assert.Near(1398, middle.OpponentHp);
            Assert.Near(341, middle.TotalDamage);
            Assert.Near(117, middle.BurnDamage);
            Assert.Near(224, middle.PoisonDamage);
            Assert.Near(48, middle.ShieldAbsorbed);
            Assert.Near(6, middle.OtherHpChange); // Native max-HP gains, not Heal events.
            Assert.Near(287, middle.NetHpLoss);
            Assert.True(middle.TotalDamageKnown);
            NativeCombatReplaySnapshot end = data.At(double.PositiveInfinity);
            Assert.Near(0, end.OpponentHp);
            Assert.Near(-5, end.OpponentRawHp);
            Assert.Near(1822, end.TotalDamage);
            Assert.Near(5, end.OverkillDamage);
            Assert.True(end.TotalDamageKnown);
            // Returned snapshots cannot mutate cached results when the UI scrubs back.
            middle.TotalDamage = -1;
            Assert.Near(341, data.At(15.3).TotalDamage);
            string path = Path.GetTempFileName();
            try
            {
                data.SaveJson(path);
                NativeCombatReplayData roundTrip = NativeCombatReplayData.FromNativeJson(Json.Parse(File.ReadAllText(path)));
                Assert.Near(341, roundTrip.At(15.3).TotalDamage);
            }
            finally { File.Delete(path); }
        }

        private static void SeparatesHealingShieldAndOtherDamage()
        {
            NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(Json.Parse(@"{
                'SpawnMessage': {'Data': {'Opponent': {'Attributes': {'Health': 900, 'HealthMax': 900, 'Shield': 20}}}},
                'CombatMessage': {'Data': {'Frames': [
                    {'OpponentUpdates': null},
                    {'OpponentUpdates': {'HealthAdjustments': [
                        {'DamageType':'Damage','AttributeChanged':'Shield','Amount':-20},
                        {'DamageType':'Damage','AttributeChanged':'Health','Amount':-100},
                        {'DamageType':'Burn','AttributeChanged':'Health','Amount':-50},
                        {'DamageType':'Poison','AttributeChanged':'Health','Amount':-60},
                        {'DamageType':'Regen','AttributeChanged':'Health','Amount':9},
                        {'DamageType':'Heal','AttributeChanged':'Health','Amount':21},
                        {'DamageType':'Sandstorm','AttributeChanged':'Health','Amount':-10},
                        {'DamageType':'Shield','AttributeChanged':'Shield','Amount':30}
                    ], 'Attributes': {'Health': {'PreviousValue':900,'CurrentValue':710},
                        'Shield': {'PreviousValue':20,'CurrentValue':30}}}}
                ]}}
            }".Replace('\'', '"')));
            Assert.Near(0, data.At(0.049).TotalDamage);
            NativeCombatReplaySnapshot point = data.At(.05);
            Assert.Near(710, point.OpponentHp);
            Assert.Near(240, point.TotalDamage);
            Assert.Near(120, point.DirectDamage);
            Assert.Near(50, point.BurnDamage);
            Assert.Near(60, point.PoisonDamage);
            Assert.Near(10, point.OtherDamage);
            Assert.Near(30, point.Healing);
            Assert.Near(9, point.Regen);
            Assert.Near(20, point.ShieldAbsorbed);
            Assert.Near(190, point.NetHpLoss);
            Assert.Near(0, point.OtherHpChange);
            Assert.True(point.TotalDamageKnown);
        }

        private static void PreservesUnknownAndMissingOpeningHealth()
        {
            NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(Json.Parse(@"{
                'CombatMessage': {'Data': {'Frames': [
                    {'OpponentUpdates': {'HealthAdjustments': [], 'Attributes': {
                        '10': {'PreviousValue':900, 'CurrentValue':900}}}},
                    {'OpponentUpdates': {'HealthAdjustments': [
                        {'DamageType':99,'AttributeChanged':0,'Amount':-20}
                    ], 'Attributes': {'10': {'PreviousValue':900,'CurrentValue':880}}}}
                ]}}
            }".Replace('\'', '"')));
            Assert.Near(900, data.EnemyStartHp);
            Assert.True(data.At(0).TotalDamageKnown);
            Assert.False(data.At(.05).TotalDamageKnown);
            Assert.Equal(1, data.At(.05).UnknownAdjustmentCount);
            Assert.Near(20, data.At(.05).NetHpLoss);
            Assert.Near(0, data.At(.05).TotalDamage); // Never invent damage from HP loss.
            Assert.Throws<FormatException>(delegate
            {
                NativeCombatReplayData.FromNativeJson(Json.Parse("{\"CombatMessage\":{\"Data\":{\"Frames\":[{}]}}}"));
            });
        }

        private static void ReaderCachesOnlyTheCurrentReplay()
        {
            try
            {
                NativeReplayFakeCombat combat = new NativeReplayFakeCombat();
                NativeReplayFakeSequence sequence = new NativeReplayFakeSequence();
                sequence.SpawnMessage = new NativeReplayFakeMessage { Data = Json.Parse(
                    "{\"Opponent\":{\"Attributes\":{\"Health\":900,\"HealthMax\":900}}}") };
                sequence.CombatMessage = new NativeReplayFakeMessage { MessageId = "replay-test", Data = combat };
                TheBazaar.AppState.CurrentState = new TheBazaar.ReplayState(sequence);
                NativeCombatReplayData first;
                string error;
                Assert.False(NativeCombatReplayReader.TryRead(out first, out error)); // Zero frames must not cache.
                combat.Frames.Add(Json.Parse("{\"OpponentUpdates\":null}"));
                Assert.True(NativeCombatReplayReader.TryRead(out first, out error));
                NativeCombatReplayData second;
                Assert.True(NativeCombatReplayReader.TryRead(out second, out error));
                Assert.True(object.ReferenceEquals(first, second));
                combat.Frames.Add(Json.Parse("{\"OpponentUpdates\":null}"));
                Assert.True(NativeCombatReplayReader.TryRead(out second, out error));
                Assert.False(object.ReferenceEquals(first, second));
                Assert.Equal(2, second.FrameCount);
                TheBazaar.AppState.CurrentState = new object();
                Assert.False(NativeCombatReplayReader.TryRead(out second, out error));
                Assert.True(second == null); // Never show the previous fight as current.
            }
            finally { TheBazaar.AppState.CurrentState = null; }
        }
    }

    public sealed class NativeReplayFakeMessage { public object Data; public string MessageId; }
    public sealed class NativeReplayFakeSequence { public object SpawnMessage; public object CombatMessage; }
    public sealed class NativeReplayFakeCombat { public List<JsonValue> Frames = new List<JsonValue>(); }
}

namespace TheBazaar
{
    public static class AppState { public static object CurrentState; }
    public sealed class ReplayState
    {
        private object _sequence;
        private object _combatCollection = JsonValue.Null;
        public ReplayState(object sequence) { _sequence = sequence; }
    }
}
