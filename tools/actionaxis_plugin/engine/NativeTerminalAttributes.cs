using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Native final snapshots include accepted deterministic attribute reactions
    // whose next-frame action notification never appears (b60fa630, ddc6683).
    // Commit only already matched item-attribute effects, not further combat.
    internal static class NativeTerminalAttributes
    {
        public static void Commit(BattleContext ctx, double time)
        {
            CommitMatched(ctx, time);
            // 击杀同帧的暴击观察者还留在队列里，下一帧永远不会到来，但原生
            // 仍把它们的确定性属性反应写进最终快照（b61c28ab 帧 185 Scalpel
            // 的 +15 CritChance/DamageAmount）。只结算 onCrit 的已匹配属性
            // 效果，不补发事件、不继续触发其它被动。
            ResolveTerminalCriticals(ctx, time);
        }

        private static void ResolveTerminalCriticals(BattleContext ctx, double time)
        {
            List<TriggerPayload> crits = new List<TriggerPayload>();
            List<TriggerPayload> others = new List<TriggerPayload>();
            while (ctx.TriggerQueue.Count > 0)
            {
                TriggerPayload payload = ctx.TriggerQueue.Dequeue();
                if (payload.Event == "onCrit" && payload.DeferredMatches == null) crits.Add(payload);
                else others.Add(payload);
            }
            foreach (TriggerPayload payload in others) ctx.TriggerQueue.Enqueue(payload);
            if (crits.Count == 0) return;
            int events = ctx.Events.Count;
            int stats = ctx.EffectStats.Count;
            ctx.TerminalAttributesOnly = true;
            try
            {
                foreach (TriggerPayload payload in crits)
                    Triggers.ResolveTerminal(ctx, payload, time);
            }
            finally
            {
                ctx.TerminalAttributesOnly = false;
                if (ctx.Events.Count > events) ctx.Events.RemoveRange(events, ctx.Events.Count - events);
                if (ctx.EffectStats.Count > stats) ctx.EffectStats.RemoveRange(stats, ctx.EffectStats.Count - stats);
            }
        }

        private static void CommitMatched(BattleContext ctx, double time)
        {
            List<KeyValuePair<TriggerPayload, DeferredTriggerMatch>> pending = new List<KeyValuePair<TriggerPayload, DeferredTriggerMatch>>();
            HashSet<Tuple<TriggerPayload, InternalItem, TriggerDef>> accepted = new HashSet<Tuple<TriggerPayload, InternalItem, TriggerDef>>();
            foreach (TriggerPayload payload in ctx.TriggerQueue)
            {
                if (payload.DeferredMatches == null) continue;
                foreach (DeferredTriggerMatch match in payload.DeferredMatches.ToArray())
                {
                    if (!CanCommit(match)) continue;
                    Dictionary<TriggerDef, double> next;
                    double available;
                    if (ctx.NativeTriggerNextAt.TryGetValue(match.Owner, out next) &&
                        next.TryGetValue(match.Trigger, out available) && available > time + ctx.Rules.Dt + 1e-9) continue;
                    if (accepted.Add(Tuple.Create(payload, match.Owner, match.Trigger)))
                        pending.Add(new KeyValuePair<TriggerPayload, DeferredTriggerMatch>(payload, match));
                    // Keep an empty match list rather than rematching an onCrit
                    // payload during ResolveTerminalCriticals or a repeated commit.
                    payload.DeferredMatches.Remove(match);
                }
            }
            // 0466d70e: Brick Buddy accepted Copper Ed's onUse, but its 250 ms
            // queue reserves frame 321. Native commits that +40 to the death
            // snapshot at frame 320 without publishing another combat action.
            // The reservation's own due time, not the ability's next free slot,
            // decides whether it belongs to this final one-frame window.
            foreach (DeferredNativeTrigger queued in ctx.DeferredNativeTriggers.ToArray())
            {
                DeferredTriggerMatch match = queued.Match;
                if (!CanCommit(match) || queued.Payload == null ||
                    match.Owner.Generation != queued.Generation ||
                    !(queued.DueAt <= time + ctx.Rules.Dt + 1e-9)) continue;
                ctx.DeferredNativeTriggers.Remove(queued);
                if (accepted.Add(Tuple.Create(queued.Payload, match.Owner, match.Trigger)))
                    pending.Add(new KeyValuePair<TriggerPayload, DeferredTriggerMatch>(queued.Payload, match));
            }
            foreach (KeyValuePair<TriggerPayload, DeferredTriggerMatch> entry in pending)
                CommitAttributeEffects(ctx, entry.Key, entry.Value, time);
        }

        private static bool CanCommit(DeferredTriggerMatch match)
        {
            return match != null && match.Trigger != null && match.Owner != null &&
                !match.Owner.Destroyed && !match.Owner.Disabled;
        }

        private static void CommitAttributeEffects(BattleContext ctx, TriggerPayload payload,
            DeferredTriggerMatch match, double time)
        {
            int events = ctx.Events.Count;
            int stats = ctx.EffectStats.Count;
            bool previousTerminalMode = ctx.TerminalAttributesOnly;
            ctx.TerminalAttributesOnly = true;
            ctx.PushTriggerAmount(payload.Amount);
            try
            {
                foreach (Effect effect in match.Trigger.Effects)
                {
                    if (!IsTerminalAttributeEffect(effect)) continue;
                    Effects.ResolveEffect(ctx, effect, match.Owner, time, payload.Depth + 1,
                        payload.ChainId, payload.SourceItemId, payload.TargetItemId);
                }
            }
            finally
            {
                ctx.PopTriggerAmount();
                ctx.TerminalAttributesOnly = previousTerminalMode;
                // These are final attribute commits, not native combat actions.
                // Their real values remain in item snapshots, with no observers.
                if (ctx.Events.Count > events) ctx.Events.RemoveRange(events, ctx.Events.Count - events);
                if (ctx.EffectStats.Count > stats) ctx.EffectStats.RemoveRange(stats, ctx.EffectStats.Count - stats);
            }
        }

        internal static bool IsTerminalAttributeEffect(Effect effect)
        {
            if (effect == null || effect.Kind != "modifyAttribute") return false;
            if (!Deterministic(effect.Target) || !Deterministic(effect.Amount)) return false;
            return effect.CountValue == null || Deterministic(effect.CountValue);
        }

        internal static bool Deterministic(Target target)
        {
            // A limited Neighbor selection samples among candidates even though
            // its target kind does not contain the word "random".
            return target != null && target.PositionMode != "Neighbor" &&
                (target.Kind ?? "").IndexOf("random", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static bool Deterministic(NumExpr amount)
        {
            if (amount == null) return false;
            if (amount.Type != "const" && amount.Type != "cardAttribute" && amount.Type != "playerAttribute") return false;
            if ((amount.TargetKind ?? "").IndexOf("random", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (amount.CardTarget != null && !Deterministic(amount.CardTarget)) return false;
            return amount.Modifier == null || Deterministic(amount.Modifier);
        }
    }
}
