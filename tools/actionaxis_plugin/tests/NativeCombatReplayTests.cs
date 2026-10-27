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
            ReadsRealG10ReplayLedger();
            ReadsRealG10PlayerReplayLedger();
            EveryKnownNativeFrameKeepsLedgerIdentity();
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
            Assert.True(firstHeal.TotalDamageKnown);
            AssertNativeLedger(firstHeal);
            NativeCombatReplaySnapshot secondHeal = data.At(10);
            Assert.Near(550, secondHeal.OpponentHp);
            Assert.Near(110, secondHeal.NominalHealing);
            Assert.Near(25, secondHeal.Healing);
            Assert.Near(9, secondHeal.Regen);
            Assert.Near(0, secondHeal.OtherHpChange);
            Assert.True(secondHeal.HealingKnown);
            Assert.True(secondHeal.TotalDamageKnown);
            AssertNativeLedger(secondHeal);
        }

        // Native TotalDamage counts realized Health/Shield damage (including
        // overkill); HP loss additionally removes accepted Heal/Regen and
        // non-adjustment Max-HP movement. Healing must never offset output.
        private static void AssertNativeLedger(NativeCombatReplaySnapshot point)
        {
            Assert.Near(point.NetHpLoss,
                point.TotalDamage - point.ShieldAbsorbed - point.OverkillDamage - point.Healing - point.OtherHpChange);
        }

        // Real BPP replay 084bd840e98c4b81a681cb8d30c7b72f contains the G10
        // seeds Trained Spider (Silver) and Soldering Gun (Bronze, Toxic) on
        // the player board. It is projected enemy-side because that is the
        // side whose exact Health/Shield adjustments the replay records.
        // Poison and Burn are observed; the replay still has no source field,
        // so per-card attribution remains simulator-side only.
        private static void ReadsRealG10ReplayLedger()
        {
            JsonValue source = Json.Parse(File.ReadAllText(
                "tools/actionaxis_plugin/tests/fixtures/native_g10_replay.json"));
            Assert.Equal("a2096bcc1f8eb31cdb274e2948c65271f08542bb989a986ebfe683c23ed06c08",
                source["SourceReplay"]["sha256"].AsString());
            bool spider = false, solderingGun = false;
            foreach (JsonValue item in source["SourceReplay"]["openingItems"].AsArray())
            {
                string template = item["templateId"].AsString();
                if (template == "014d9c98-e823-443c-98a3-6367ab81c956") spider = true;
                if (template == "666f4084-48cc-4558-b678-a46ba18c1bf2") solderingGun = true;
            }
            Assert.True(spider);
            Assert.True(solderingGun);
            NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(source);
            Assert.Equal(426, data.FrameCount);
            Assert.Near(21.25, data.Duration);
            Assert.Near(400, data.EnemyStartHp);
            Assert.True(data.TotalDamageKnown);

            NativeCombatReplaySnapshot firstPoison = data.At(6);
            Assert.Near(4, firstPoison.TotalDamage);
            Assert.Near(4, firstPoison.PoisonDamage);
            Assert.Near(0, firstPoison.ShieldAbsorbed);
            Assert.Near(4, firstPoison.NetHpLoss);
            Assert.Near(0, firstPoison.OverkillDamage);
            Assert.Near(0, firstPoison.Healing);
            Assert.Near(0, firstPoison.OtherHpChange);
            Assert.True(firstPoison.TotalDamageKnown);
            Assert.True(firstPoison.HealingKnown);
            AssertNativeLedger(firstPoison);

            // 2 Burn into 150 Shield consumes exactly one Shield point and
            // produces no health loss; native reports the realized 1 point,
            // not the nominal Burn amount or an inferred source share.
            NativeCombatReplaySnapshot burnShield = data.At(12.5);
            Assert.Near(95, burnShield.TotalDamage);
            Assert.Near(94, burnShield.PoisonDamage);
            Assert.Near(1, burnShield.BurnDamage);
            Assert.Near(1, burnShield.ShieldAbsorbed);
            Assert.Near(306, burnShield.OpponentHp);
            Assert.Near(94, burnShield.NetHpLoss);
            Assert.Near(0, burnShield.OverkillDamage);
            Assert.Near(0, burnShield.Healing);
            Assert.Near(0, burnShield.OtherHpChange);
            AssertNativeLedger(burnShield);

            NativeCombatReplaySnapshot end = data.At(double.PositiveInfinity);
            Assert.Near(323, end.TotalDamage);
            Assert.Near(322, end.PoisonDamage);
            Assert.Near(1, end.BurnDamage);
            Assert.Near(0, end.DirectDamage);
            Assert.Near(1, end.ShieldAbsorbed);
            Assert.Near(322, end.NetHpLoss);
            Assert.Near(78, end.OpponentHp);
            Assert.Near(0, end.OverkillDamage);
            Assert.Near(0, end.Healing);
            Assert.Near(0, end.OtherHpChange);
            Assert.True(end.TotalDamageKnown);
            Assert.True(end.HealingKnown);
            AssertNativeLedger(end);
        }

        // Same replay, player-observed side. SpawnMessage/OpeningMessage do
        // not expose player HealthMax/Shield/Regen, so the fixture omits them
        // and the max-HP fallback stays explicit. The player side has no
        // Heal/Regen adjustment in this sample; damage, overkill and net HP
        // loss are exact.
        private static void ReadsRealG10PlayerReplayLedger()
        {
            JsonValue source = Json.Parse(File.ReadAllText(
                "tools/actionaxis_plugin/tests/fixtures/native_g10_replay_player.json"));
            NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(source);
            Assert.Equal(426, data.FrameCount);
            Assert.Near(400, data.EnemyStartHp);
            Assert.True(data.TotalDamageKnown);
            Assert.False(data.At(0).HealingKnown);

            NativeCombatReplaySnapshot firstHit = data.At(4);
            Assert.Near(30, firstHit.TotalDamage);
            Assert.Near(30, firstHit.DirectDamage);
            Assert.Near(30, firstHit.NetHpLoss);
            Assert.Near(0, firstHit.ShieldAbsorbed);
            Assert.Near(0, firstHit.OverkillDamage);
            Assert.Near(0, firstHit.Healing);
            Assert.Near(0, firstHit.OtherHpChange);

            NativeCombatReplaySnapshot end = data.At(double.PositiveInfinity);
            Assert.Near(402, end.TotalDamage);
            Assert.Near(270, end.DirectDamage);
            Assert.Near(132, end.PoisonDamage);
            Assert.Near(0, end.ShieldAbsorbed);
            Assert.Near(2, end.OverkillDamage);
            Assert.Near(400, end.NetHpLoss);
            Assert.Near(0, end.OpponentHp);
            Assert.Near(-2, end.OpponentRawHp);
            Assert.Near(0, end.Healing);
            Assert.Near(0, end.NominalHealing);
            Assert.Near(0, end.OtherHpChange);
            Assert.True(end.TotalDamageKnown);
            Assert.False(end.HealingKnown);
            Assert.Near(end.NetHpLoss,
                end.TotalDamage - end.ShieldAbsorbed - end.OverkillDamage - end.OtherHpChange);
            for (int frame = 0; frame < data.FrameCount; frame++)
            {
                NativeCombatReplaySnapshot point = data.At(frame * NativeCombatReplayData.FrameSeconds);
                Assert.True(point.TotalDamageKnown);
                Assert.Near(0, point.Healing);
                Assert.Near(point.NetHpLoss,
                    point.TotalDamage - point.ShieldAbsorbed - point.OverkillDamage - point.OtherHpChange);
            }
        }

        private static void EveryKnownNativeFrameKeepsLedgerIdentity()
        {
            string[] fixtures =
            {
                "tools/actionaxis_plugin/tests/fixtures/native_replay_health.json",
                "tools/actionaxis_plugin/tests/fixtures/native_replay_overheal.json",
                "tools/actionaxis_plugin/tests/fixtures/native_g10_replay.json"
            };
            foreach (string path in fixtures)
            {
                NativeCombatReplayData data = NativeCombatReplayData.FromNativeJson(
                    Json.Parse(File.ReadAllText(path)));
                int unknown = 0, healingUnknown = 0, checkedFrames = 0;
                for (int frame = 0; frame < data.FrameCount; frame++)
                {
                    NativeCombatReplaySnapshot point = data.At(frame * NativeCombatReplayData.FrameSeconds);
                    if (!point.TotalDamageKnown)
                    {
                        unknown++;
                        continue;
                    }
                    if (!point.HealingKnown)
                    {
                        healingUnknown++;
                        continue;
                    }
                    AssertNativeLedger(point);
                    checkedFrames++;
                }
                Assert.Equal(0, unknown);
                Assert.Equal(0, healingUnknown);
                Assert.Equal(data.FrameCount, checkedFrames);
            }
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
            AssertNativeLedger(middle);
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
            AssertNativeLedger(point);
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
