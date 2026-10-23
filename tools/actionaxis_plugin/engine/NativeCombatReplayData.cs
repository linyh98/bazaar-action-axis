using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BazaarActionAxis.Engine
{
    // These are recorded native health adjustments, not estimates from the local simulator.
    // The native format has no damage-source field: totals describe damage RECEIVED by
    // the opponent, and include native overkill. They must not be attributed to a card.
    public sealed class NativeCombatReplaySnapshot
    {
        public double Time;
        public double OpponentHp;
        public double OpponentRawHp;
        public double MaxHp;
        public double OpponentShield;
        public double TotalDamage;
        public double DirectDamage;
        public double BurnDamage;
        public double PoisonDamage;
        public double OtherDamage;
        public double Healing; // Accepted Health gains of Heal AND Regen (overheal excluded).
        public double NominalHealing;
        public double Regen;
        public double NetHpLoss;
        public double ShieldAbsorbed;
        public double OverkillDamage;
        public double OtherHpChange; // HP/max-HP changes without a HealthAdjustment.
        public int UnknownAdjustmentCount;
        public bool TotalDamageKnown;
        public bool HealingKnown;

        internal NativeCombatReplaySnapshot Copy()
        {
            return (NativeCombatReplaySnapshot)MemberwiseClone();
        }
    }

    public sealed class NativeCombatReplayData
    {
        public const double FrameSeconds = 0.05;
        private readonly List<NativeCombatReplaySnapshot> _points = new List<NativeCombatReplaySnapshot>();
        private readonly NativeCombatReplaySnapshot _initial = new NativeCombatReplaySnapshot();
        private JsonValue _source;

        public string CombatId { get; private set; }
        public double EnemyStartHp { get; private set; }
        public double Duration { get { return Math.Max(0, _points.Count - 1) * FrameSeconds; } }
        public int FrameCount { get { return _points.Count; } }
        public bool TotalDamageKnown { get { return _points.Count > 0 && _points[_points.Count - 1].TotalDamageKnown; } }
        public JsonValue OpeningCollection { get; private set; }
        public JsonValue NativeSequence { get; private set; }

        // NativeSequence is the field-named native DTO (SpawnMessage, CombatMessage,
        // DespawnMessage). OpeningCollection preserves ReplayState's saved opening
        // player hand items, placements and attributes, not the mutated live board.
        public static NativeCombatReplayData FromNativeJson(JsonValue source)
        {
            if (source == null || source.Kind != JsonKind.Object)
                throw new FormatException("回放快照不是对象");
            NativeCombatReplayData result = new NativeCombatReplayData();
            result._source = source;
            result.NativeSequence = Member(source, "NativeSequence");
            if (result.NativeSequence.Kind == JsonKind.Null) result.NativeSequence = source;
            result.OpeningCollection = Member(source, "OpeningCollection");
            JsonValue combatMessage = Member(result.NativeSequence, "CombatMessage");
            JsonValue combat = Member(combatMessage, "Data");
            JsonValue frames = Member(combat, "Frames");
            if (frames.Kind != JsonKind.Array || frames.Count == 0)
                throw new FormatException("回放没有 CombatMessage.Data.Frames");
            result.CombatId = Text(Member(source, "CombatId"));
            if (string.IsNullOrEmpty(result.CombatId)) result.CombatId = Text(Member(combatMessage, "MessageId"));
            if (string.IsNullOrEmpty(result.CombatId)) result.CombatId = "native-" + frames.Count.ToString(CultureInfo.InvariantCulture);

            JsonValue spawn = Member(Member(result.NativeSequence, "SpawnMessage"), "Data");
            JsonValue attributes = Member(Member(spawn, "Opponent"), "Attributes");
            double? startHp = Attribute(attributes, "Health", "10", false);
            double? maxHp = Attribute(attributes, "HealthMax", "11", false);
            double? startShield = Attribute(attributes, "Shield", "19", false);
            // Spawns may be delta-only. The first native PreviousValue is an exact
            // opening value, even when that attribute first changes several seconds in.
            if (!startHp.HasValue) startHp = FirstPrevious(frames, "Health", "10");
            if (!maxHp.HasValue) maxHp = FirstPrevious(frames, "HealthMax", "11");
            if (!startShield.HasValue) startShield = FirstPrevious(frames, "Shield", "19");
            if (!startHp.HasValue) throw new FormatException("回放缺少敌方开场生命，不能精确对照");
            result.EnemyStartHp = Math.Max(0, startHp.Value);
            result._initial.OpponentRawHp = startHp.Value;
            result._initial.OpponentHp = Math.Max(0, startHp.Value);
            result._initial.MaxHp = maxHp ?? startHp.Value;
            result._initial.OpponentShield = Math.Max(0, startShield ?? 0);
            result._initial.TotalDamageKnown = true;
            result._initial.HealingKnown = maxHp.HasValue;
            NativeCombatReplaySnapshot current = result._initial.Copy();
            for (int i = 0; i < frames.Count; i++)
            {
                current.Time = i * FrameSeconds;
                ApplyUpdate(current, Member(frames[i], "OpponentUpdates"));
                current.OpponentHp = Math.Max(0, current.OpponentRawHp);
                current.NetHpLoss = result.EnemyStartHp - current.OpponentHp;
                result._points.Add(current.Copy());
            }
            return result;
        }

        public NativeCombatReplaySnapshot At(double time)
        {
            if (double.IsNaN(time) || time < 0 || _points.Count == 0) return _initial.Copy();
            int index = double.IsPositiveInfinity(time) ? _points.Count - 1 :
                (int)Math.Min(_points.Count - 1, Math.Floor(time / FrameSeconds + 1e-8));
            return _points[Math.Max(0, index)].Copy();
        }

        private static void ApplyUpdate(NativeCombatReplaySnapshot current, JsonValue update)
        {
            if (update.Kind == JsonKind.Null) return;
            if (update.Kind != JsonKind.Object)
            {
                current.UnknownAdjustmentCount++;
                current.TotalDamageKnown = false;
                return;
            }
            JsonValue attributes = Member(update, "Attributes");
            double? previousHp = Attribute(attributes, "Health", "10", true);
            double? closingHp = Attribute(attributes, "Health", "10", false);
            double? closingMax = Attribute(attributes, "HealthMax", "11", false);
            double? closingShield = Attribute(attributes, "Shield", "19", false);
            double hpAtFrameStart = previousHp ?? current.OpponentRawHp;
            double hpDelta = 0;
            JsonValue adjustments = Member(update, "HealthAdjustments");
            if (adjustments.Kind != JsonKind.Null && adjustments.Kind != JsonKind.Array)
            {
                current.UnknownAdjustmentCount++;
                current.TotalDamageKnown = false;
            }
            foreach (JsonValue adjustment in adjustments.AsArray())
            {
                string type = EnumName(Member(adjustment, "DamageType"));
                string changed = EnumName(Member(adjustment, "AttributeChanged"));
                JsonValue amountValue = Member(adjustment, "Amount");
                if (amountValue.Kind != JsonKind.Number || type == null || changed == null)
                {
                    current.UnknownAdjustmentCount++;
                    current.TotalDamageKnown = false;
                    continue;
                }
                double amount = amountValue.AsDouble();
                bool health = IsEnum(changed, "Health", "0");
                bool shield = IsEnum(changed, "Shield", "2");
                bool direct = IsEnum(type, "Damage", "0");
                bool burn = IsEnum(type, "Burn", "1");
                bool poison = IsEnum(type, "Poison", "2");
                bool sandstorm = IsEnum(type, "Sandstorm", "3");
                bool heal = IsEnum(type, "Heal", "4");
                bool shieldChange = IsEnum(type, "Shield", "5");
                bool regen = IsEnum(type, "Regen", "7");
                double acceptedAmount = amount;
                if (health && amount > 0 && (heal || regen))
                {
                    // Native Heal/Regen adjustments contain the nominal amount,
                    // even at full health. Preserve list order when capping it.
                    double available = Math.Max(0, (closingMax ?? current.MaxHp) - (hpAtFrameStart + hpDelta));
                    acceptedAmount = Math.Min(amount, available);
                    if (closingMax.HasValue && Math.Abs(closingMax.Value - current.MaxHp) > 1e-9)
                        current.HealingKnown = false; // Max-HP change order is not encoded here.
                }
                if (health) hpDelta += acceptedAmount;
                if ((health || shield) && amount < 0 && (direct || burn || poison || sandstorm))
                {
                    double damage = -amount;
                    current.TotalDamage += damage;
                    if (direct) current.DirectDamage += damage;
                    else if (burn) current.BurnDamage += damage;
                    else if (poison) current.PoisonDamage += damage;
                    else current.OtherDamage += damage;
                    if (shield) current.ShieldAbsorbed += damage;
                    if (health)
                    {
                        double rawBefore = hpAtFrameStart + hpDelta - amount;
                        current.OverkillDamage += Math.Max(0, damage - Math.Max(0, rawBefore));
                    }
                }
                else if (health && amount > 0 && (heal || regen))
                {
                    current.NominalHealing += amount;
                    current.Healing += acceptedAmount;
                    if (regen) current.Regen += acceptedAmount;
                }
                else if ((shield && shieldChange) || IsEnum(changed, "Joy", "1") || amount == 0)
                {
                    // Shield grants/removal and Joy are not damage; keep the raw
                    // adjustment in the saved record, but do not double-count them.
                }
                else
                {
                    current.UnknownAdjustmentCount++;
                    current.TotalDamageKnown = false;
                }
            }
            if (closingHp.HasValue)
            {
                current.OtherHpChange += closingHp.Value - hpAtFrameStart - hpDelta;
                current.OpponentRawHp = closingHp.Value;
            }
            else current.OpponentRawHp = hpAtFrameStart + hpDelta;
            if (closingMax.HasValue) current.MaxHp = closingMax.Value;
            if (closingShield.HasValue) current.OpponentShield = Math.Max(0, closingShield.Value);
        }

        private static double? FirstPrevious(JsonValue frames, string attribute, string numericKey)
        {
            foreach (JsonValue frame in frames.AsArray())
            {
                double? value = Attribute(Member(Member(frame, "OpponentUpdates"), "Attributes"), attribute, numericKey, true);
                if (value.HasValue) return value;
            }
            return null;
        }

        private static double? Attribute(JsonValue attributes, string name, string number, bool previous)
        {
            JsonValue value = Member(attributes, name);
            if (value.Kind == JsonKind.Null) value = Member(attributes, number);
            if (value.Kind == JsonKind.Number) return previous ? (double?)null : value.AsDouble();
            value = Member(value, previous ? "PreviousValue" : "CurrentValue");
            return value.Kind == JsonKind.Number ? (double?)value.AsDouble() : null;
        }

        private static JsonValue Member(JsonValue value, string key)
        {
            return value != null && value.Kind == JsonKind.Object ? value[key] : JsonValue.Null;
        }
        private static string Text(JsonValue value) { return value.Kind == JsonKind.String ? value.AsString() : null; }
        private static string EnumName(JsonValue value)
        {
            if (value.Kind == JsonKind.String) return value.AsString();
            if (value.Kind == JsonKind.Number) return value.AsDouble().ToString(CultureInfo.InvariantCulture);
            return null;
        }
        private static bool IsEnum(string value, string name, string number)
        {
            return string.Equals(value, name, StringComparison.Ordinal) || value == number;
        }

        public void SaveJson(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            Dictionary<string, JsonValue> document = new Dictionary<string, JsonValue>(_source.AsObject());
            document["SchemaVersion"] = JsonValue.FromNumber(1);
            document["DamageBasis"] = JsonValue.FromString("Native opponent HealthAdjustments. Includes Health and Shield damage after mitigation and native overkill; source is not recorded. Healing includes accepted Heal and Regen health gains, excludes overheal. OtherDamage includes Sandstorm.");
            StringBuilder builder = new StringBuilder();
            WriteJson(builder, JsonValue.FromObject(document));
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static void WriteJson(StringBuilder builder, JsonValue value)
        {
            if (value.Kind == JsonKind.Null) { builder.Append("null"); return; }
            if (value.Kind == JsonKind.Bool) { builder.Append(value.AsBool() ? "true" : "false"); return; }
            if (value.Kind == JsonKind.Number) { builder.Append(value.AsDouble().ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value.Kind == JsonKind.String) { WriteString(builder, value.AsString()); return; }
            if (value.Kind == JsonKind.Array)
            {
                builder.Append('[');
                for (int i = 0; i < value.Count; i++) { if (i > 0) builder.Append(','); WriteJson(builder, value[i]); }
                builder.Append(']'); return;
            }
            builder.Append('{'); bool first = true;
            foreach (KeyValuePair<string, JsonValue> entry in value.AsObject())
            {
                if (!first) builder.Append(','); first = false;
                WriteString(builder, entry.Key); builder.Append(':'); WriteJson(builder, entry.Value);
            }
            builder.Append('}');
        }
        private static void WriteString(StringBuilder builder, string value)
        {
            if (value == null) { builder.Append("null"); return; }
            builder.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') { builder.Append('\\'); builder.Append(c); }
                else if (c < ' ') builder.Append("\\u" + ((int)c).ToString("x4"));
                else builder.Append(c);
            }
            builder.Append('"');
        }
    }
}
