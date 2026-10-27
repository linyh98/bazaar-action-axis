using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Optional test input: observed random choices only, never damage, HP or time corrections.
    // Normal live forecasts leave this null and continue using the seeded RNG.
    public sealed class ReplayRandomChoice
    {
        public int Frame;
        public string Source;
        public string Ability;
        public string Kind;
        public string TargetSide;
        public List<string> Targets = new List<string>();
        public bool Critical;
    }

    public sealed class ReplayRandomOrder
    {
        public int Frame;
        public List<string> Sources = new List<string>();
    }

    public sealed class ReplayRandomEnchant
    {
        public int Frame;
        public string Source;
        public string Target;
        public string Name;
    }

    public sealed class ReplayTransformIdentity
    {
        public int Frame, Slot, Index;
        public string Original, InstanceId, TemplateId, Side;
    }

    public sealed class ReplayRandomTape
    {
        public List<ReplayRandomChoice> Targets = new List<ReplayRandomChoice>();
        public List<ReplayRandomChoice> Criticals = new List<ReplayRandomChoice>();
        public List<ReplayRandomEnchant> Enchantments = new List<ReplayRandomEnchant>();
        public List<ReplayRandomChoice> Transforms = new List<ReplayRandomChoice>();
        public List<ReplayRandomOrder> Orders = new List<ReplayRandomOrder>();
        public List<ReplayTransformIdentity> Identities = new List<ReplayTransformIdentity>();
        public string ReplaySha256;

        public static ReplayRandomTape FromJson(JsonValue raw)
        {
            if (raw == null || raw.Kind != JsonKind.Object) return null;
            ReplayRandomTape tape = new ReplayRandomTape();
            tape.ReplaySha256 = raw["replaySha256"].AsString();
            Read(raw.Has("transforms") ? raw["transforms"] : JsonValue.Null, tape.Transforms);
            Read(raw["targets"], tape.Targets);
            Read(raw["criticals"], tape.Criticals);
            foreach (JsonValue row in raw["identities"].AsArray())
                tape.Identities.Add(new ReplayTransformIdentity {
                    Frame = row["frame"].AsInt(), Original = row["original"].AsString(),
                    InstanceId = row["instanceId"].AsString(), TemplateId = row["templateId"].AsString(),
                    Side = row["side"].AsString(), Slot = row["slot"].AsInt(), Index = row["index"].AsInt()
                });
            foreach (JsonValue row in raw["enchantments"].AsArray())
            {
                tape.Enchantments.Add(new ReplayRandomEnchant {
                    Frame = row["frame"].AsInt(), Source = row["source"].AsString(),
                    Target = row["target"].AsString(), Name = row["name"].AsString()
                });
            }
            foreach (JsonValue row in raw["order"].AsArray())
            {
                ReplayRandomOrder entry = new ReplayRandomOrder { Frame = row["frame"].AsInt() };
                foreach (JsonValue id in row["sources"].AsArray()) entry.Sources.Add(id.AsString());
                tape.Orders.Add(entry);
            }
            return tape;
        }

        private static void Read(JsonValue rows, List<ReplayRandomChoice> target)
        {
            if (rows == null || rows.Kind != JsonKind.Array) return;
            foreach (JsonValue row in rows.AsArray())
            {
                ReplayRandomChoice choice = new ReplayRandomChoice {
                    Frame = row["frame"].AsInt(), Source = row["source"].AsString(),
                    Ability = row["ability"].AsString(), Kind = row["kind"].AsString(),
                    TargetSide = row["targetSide"].AsString(), Critical = row["critical"].AsBool()
                };
                foreach (JsonValue id in row["targets"].AsArray()) choice.Targets.Add(id.AsString());
                target.Add(choice);
            }
        }
    }

    public sealed class ReplayRandomScope
    {
        public int Frame;
        public string Source;
        public string Ability;
        public string Kind;
    }

    public sealed class ReplayRandomAudit
    {
        public int TargetRecords;
        public int TargetApplied;
        public int CriticalRecords;
        public int CriticalApplied;
        public int TargetFallbacks;
        public int CriticalFallbacks;
        public int EnchantRecords;
        public int EnchantApplied;
        public int EnchantFallbacks;
        public int TransformRecords;
        public int TransformApplied;
        public int TransformFallbacks;
        public int OrderRecords;
        public int OrderApplied;
        public int OrderFallbacks;
        public int InvalidChoices;
        public int IdentityRecords, IdentityApplied, IdentityFallbacks;
        public List<string> Issues = new List<string>();
    }

    public sealed class ReplayRandomSession
    {
        private readonly ReplayRandomTape tape;
        private readonly HashSet<int> usedTargets = new HashSet<int>();
        private readonly HashSet<int> usedCriticals = new HashSet<int>();
        private readonly HashSet<int> usedEnchants = new HashSet<int>();
        private readonly HashSet<int> usedTransforms = new HashSet<int>();
        private readonly HashSet<int> usedOrders = new HashSet<int>();
        private readonly HashSet<int> usedIdentities = new HashSet<int>();
        private readonly Dictionary<string, string> identities = new Dictionary<string, string>();
        public ReplayRandomScope Scope;
        public readonly ReplayRandomAudit Audit = new ReplayRandomAudit();

        public ReplayRandomSession(ReplayRandomTape input)
        {
            tape = input;
            Audit.TargetRecords = tape.Targets.Count;
            Audit.CriticalRecords = tape.Criticals.Count;
            Audit.EnchantRecords = tape.Enchantments.Count;
            Audit.TransformRecords = tape.Transforms.Count;
            Audit.OrderRecords = tape.Orders.Count;
            Audit.IdentityRecords = tape.Identities.Count;
        }

        private string NativeId(string id)
        {
            string native;
            return id != null && identities.TryGetValue(id, out native) ? native : id;
        }

        public bool BindTransformIdentity(string originalId, string createdId, string templateId,
            Side side, int slot, int childIndex, int frame)
        {
            // Identity evidence can label an independently created object; it
            // cannot select its template, location, time or combat attributes.
            if (tape.Identities.Count == 0) return false;
            string original = NativeId(originalId);
            int match = -1;
            for (int i = 0; i < tape.Identities.Count; i++)
            {
                ReplayTransformIdentity row = tape.Identities[i];
                if (usedIdentities.Contains(i) || row.Frame != frame || row.Original != original ||
                    row.TemplateId != templateId || row.Side != SideUtil.Key(side) ||
                    row.Slot != slot || row.Index != childIndex) continue;
                if (match >= 0) { Audit.IdentityFallbacks++; return false; }
                match = i;
            }
            if (match < 0 || identities.ContainsKey(createdId) ||
                identities.ContainsValue(tape.Identities[match].InstanceId))
            { Audit.IdentityFallbacks++; return false; }
            identities[createdId] = tape.Identities[match].InstanceId;
            usedIdentities.Add(match);
            Audit.IdentityApplied++;
            return true;
        }

        // The native ledger names the enchant execution "CardEnchant" whether
        // the card definition used the named or the random-weighted action, so
        // the two sim kinds must match either tape kind.
        private static bool KindMatches(string left, string right)
        {
            if (left == right) return true;
            if (left == null || right == null) return false;
            return (left == "enchant" || left == "enchantRandom") &&
                   (right == "enchant" || right == "enchantRandom");
        }

        // 引擎技能实例 Id 形如 "enemy:skill:<instanceId>"，磁带源是裸实例 ID；
        // 统一取最后一个 ':' 之后的段比较，否则技能源的随机记录永不命中。
        internal static string Bare(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int colon = id.LastIndexOf(':');
            return colon >= 0 ? id.Substring(colon + 1) : id;
        }

        private int Find(List<ReplayRandomChoice> rows, HashSet<int> used, string source,
            string kind, int frame, string targetSide)
        {
            source = NativeId(source);
            string ability = Scope == null ? null : Scope.Ability;
            string bareSource = Bare(source);
            // 帧号必须精确匹配：磁带行带着它被观测到的帧，跨帧消费等于给效果
            // 改时间（WrongFrameDoesNotRetimingEffect 锁定该不变量）。
            for (int i = 0; i < rows.Count; i++)
            {
                ReplayRandomChoice choice = rows[i];
                if (used.Contains(i) || choice.Frame != frame || !KindMatches(choice.Kind, kind)) continue;
                if (choice.Source != source && choice.Source != bareSource &&
                    Bare(choice.Source) != bareSource) continue;
                if (targetSide != null && choice.TargetSide != targetSide) continue;
                if (ability != null && choice.Ability != null && ability != choice.Ability) continue;
                return i;
            }
            return -1;
        }

        // 变形供体先知：原生 CardTransformed 事件记录了新模板，按被变形实例
        // 与帧号取用；候选池无对应模板时回退 RNG（不消费，保留给诊断）。
        public string ConsumeTransform(string source, int frame)
        {
            source = NativeId(source);
            string bare = Bare(source);
            for (int i = 0; i < tape.Transforms.Count; i++)
            {
                if (usedTransforms.Contains(i) || tape.Transforms[i].Frame != frame) continue;
                if (tape.Transforms[i].Source != source && Bare(tape.Transforms[i].Source) != bare) continue;
                usedTransforms.Add(i);
                Audit.TransformApplied++;
                return tape.Transforms[i].Targets.Count > 0 ? tape.Transforms[i].Targets[0] : null;
            }
            Audit.TransformFallbacks++;
            return null;
        }

        /// <summary>Recorded enchantment applied to this target by the same
        /// random enchant effect; null means roll the weighted pool.</summary>
        public string PickEnchantment(string source, int frame, string itemId)
        {
            source = NativeId(source);
            itemId = NativeId(itemId);
            string bareSource = Bare(source);
            for (int i = 0; i < tape.Enchantments.Count; i++)
            {
                ReplayRandomEnchant row = tape.Enchantments[i];
                if (usedEnchants.Contains(i) || row.Frame != frame ||
                    (row.Source != source && Bare(row.Source) != bareSource) ||
                    row.Target != itemId) continue;
                usedEnchants.Add(i);
                Audit.EnchantApplied++;
                return row.Name;
            }
            Audit.EnchantFallbacks++;
            return null;
        }

        public List<InternalItem> ChooseTargets(List<InternalItem> pool, int count)
        {
            if (Scope == null) { Audit.TargetFallbacks++; return null; }
            int index = Find(tape.Targets, usedTargets, Scope.Source, Scope.Kind, Scope.Frame, null);
            if (index < 0) { Audit.TargetFallbacks++; return null; }
            ReplayRandomChoice choice = tape.Targets[index];
            List<InternalItem> selected = new List<InternalItem>();
            foreach (string id in choice.Targets)
            {
                InternalItem item = pool.Find(delegate(InternalItem candidate) { return NativeId(candidate.Id) == id; });
                if (item == null || selected.Contains(item))
                {
                    Invalid("target not eligible: " + id + " at frame " + Scope.Frame);
                    return null;
                }
                selected.Add(item);
            }
            int expected = Math.Min(Math.Max(1, count), pool.Count);
            if (selected.Count != expected)
            {
                Invalid("target count mismatch at frame " + Scope.Frame);
                return null;
            }
            usedTargets.Add(index);
            Audit.TargetApplied++;
            return selected;
        }

        /// <summary>Reorder same-frame ready items into the replay's observed
        /// firing order.  The native tie-break between simultaneously ready
        /// items follows no discoverable rule (216 cross-side frames: slot
        /// 81:107, side 108:108), so it is scripted like crits and targets.</summary>
        public List<InternalItem> ApplyOrder(int frame, List<InternalItem> ready, bool consume = true)
        {
            if (ready.Count < 2) return ready;
            int index = -1;
            for (int i = 0; i < tape.Orders.Count; i++)
            {
                if (consume && usedOrders.Contains(i) || tape.Orders[i].Frame != frame) continue;
                index = i;
                break;
            }
            if (index < 0) { if (consume) Audit.OrderFallbacks++; return ready; }
            if (consume) usedOrders.Add(index);
            List<string> sources = tape.Orders[index].Sources;
            List<InternalItem> ordered = new List<InternalItem>(ready);
            ordered.Sort(delegate(InternalItem a, InternalItem b)
            {
                int left = sources.IndexOf(NativeId(a.Id));
                if (left < 0) left = int.MaxValue;
                int right = sources.IndexOf(NativeId(b.Id));
                if (right < 0) right = int.MaxValue;
                return left.CompareTo(right);
            });
            if (consume) Audit.OrderApplied++;
            return ordered;
        }

        public bool? ChooseCritical(string source, string kind, double time, Side targetSide, double probability)
        {
            int frame = (int)Math.Round(time / 0.05);
            int index = Find(tape.Criticals, usedCriticals, source, kind, frame, SideUtil.Key(targetSide));
            if (index < 0)
            {
                if (probability > 0 && probability < 1) Audit.CriticalFallbacks++;
                return null;
            }
            ReplayRandomChoice choice = tape.Criticals[index];
            if (choice.Critical && probability <= 0 || !choice.Critical && probability >= 1)
            {
                Invalid("critical result incompatible with chance at frame " + frame + ": " + source);
                return null;
            }
            usedCriticals.Add(index);
            Audit.CriticalApplied++;
            return choice.Critical;
        }

        private void Invalid(string reason)
        {
            Audit.InvalidChoices++;
            if (Audit.Issues.Count < 30) Audit.Issues.Add(reason);
        }
    }
}
