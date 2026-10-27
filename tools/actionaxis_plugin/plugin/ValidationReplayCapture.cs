using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    // All inputs are frozen JSON, never references to the board being animated.
    public sealed class ValidationReplayCapture
    {
        private readonly Action<string, JsonValue> _save;
        private readonly JsonValue _environment;
        private readonly string _catalog;
        private Dictionary<string, JsonValue> _fight;
        private readonly Queue<string> _recent = new Queue<string>();
        private string _openingId;

        public ValidationReplayCapture(Action<string, JsonValue> save, JsonValue environment, string catalog)
        { _save = save; _environment = environment; _catalog = catalog; }
        public bool HasPendingFight { get { return _fight != null; } }

        public static bool IsOpening(object message)
        {
            if (message == null || message.GetType().Name != "NetMessageGameSim") return false;
            object state = LiveSkillReader.GetMember(LiveSkillReader.GetMember(
                LiveSkillReader.GetMember(message, "Data"), "CurrentState"), "StateName");
            return state != null && (state.ToString() == "Combat" || state.ToString() == "PVPCombat");
        }

        public void Accept(string kind, string messageId, bool opening, JsonValue message,
            JsonValue before, JsonValue after)
        {
            string key = kind + ":" + messageId;
            string wire = message["MessagePackBase64"].AsString();
            if (wire != null) using (SHA256 sha = SHA256.Create())
                key += ":" + Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(wire)));
            if (!string.IsNullOrEmpty(messageId) && _recent.Contains(key)) return;
            if (!string.IsNullOrEmpty(messageId))
            {
                _recent.Enqueue(key);
                while (_recent.Count > 128) _recent.Dequeue();
            }
            if (opening)
            {
                Interrupt("next-opening-before-despawn");
                _openingId = Guid.NewGuid().ToString("N");
                _fight = new Dictionary<string, JsonValue>();
                _fight["Schema"] = JsonValue.FromString("actionaxis.validation.v1");
                _fight["CaptureId"] = JsonValue.FromString(_openingId);
                _fight["StartedAtUtc"] = JsonValue.FromString(DateTime.UtcNow.ToString("o"));
                _fight["Environment"] = _environment;
                _fight["CatalogFile"] = JsonValue.FromString(_catalog);
                _fight["FrameSeconds"] = JsonValue.FromNumber(0.05);
                _fight["OpeningBeforeDispatch"] = before ?? JsonValue.Null;
                _fight["OpeningAfterDispatch"] = after ?? JsonValue.Null;
                _fight["SpawnMessage"] = message;
                _fight["Randomness"] = JsonValue.FromString(
                    "Native effect targets, execution contexts and health IsCrit flags are preserved. " +
                    "No server RNG seed or absent source attribution is invented; ambiguous decisions remain unknown.");
                Persist("opening");
                return;
            }
            if (kind == "NetMessageCombatSim")
            {
                if (_fight == null)
                {
                    // A mid-fight install/reconnect must leave evidence, not a fabricated opening.
                    _openingId = Guid.NewGuid().ToString("N");
                    _fight = new Dictionary<string, JsonValue> {
                        { "Schema", JsonValue.FromString("actionaxis.validation.v1") },
                        { "CaptureId", JsonValue.FromString(_openingId) },
                        { "Environment", _environment }, { "CatalogFile", JsonValue.FromString(_catalog) },
                        { "FrameSeconds", JsonValue.FromNumber(0.05) },
                        { "CaptureGap", JsonValue.FromString("combat-without-opening") }
                    };
                }
                if (_fight.ContainsKey("CombatMessage"))
                { Interrupt("second-combat-without-opening"); Accept(kind, null, false, message, null, null); return; }
                _fight["CombatMessage"] = message;
                Persist("combat"); // Durable even if the game quits before the despawn message.
                return;
            }
            if (kind != "NetMessageGameSim" || _fight == null) return;
            if (!_fight.ContainsKey("CombatMessage"))
            { Interrupt("noncombat-message-before-combat"); return; }
            _fight["DespawnMessage"] = message;
            Persist("complete");
            _fight = null;
        }

        public void Interrupt(string reason)
        {
            if (_fight == null) return;
            _fight["CaptureGap"] = JsonValue.FromString(reason);
            Persist("interrupted");
            _fight = null;
        }

        private void Persist(string stage)
        {
            _fight["Stage"] = JsonValue.FromString(stage);
            _fight["UpdatedAtUtc"] = JsonValue.FromString(DateTime.UtcNow.ToString("o"));
            // The background writer can still be writing the previous revision.
            _save(_openingId + ".validation.json.gz", JsonValue.FromObject(
                new Dictionary<string, JsonValue>(_fight)));
        }
    }
}
