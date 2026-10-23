using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class DamageSource
    {
        public string Id;
        public string CardId;
        public Side Side;
        public double CritBonus;
        public double Lifesteal;
    }

    public sealed class DamageOptions
    {
        public bool IsDot;
        public string DotKind;
        public int Depth;
        public int? ChainId;
    }

    public static class Effects
    {
        public static double EvaluateNum(
            NumExpr expr,
            BattleContext ctx,
            InternalItem source,
            InternalHero targetHero)
        {
            if (expr == null) return 0;
            switch (expr.Type)
            {
                case "const":
                    return expr.Value;
                case "percentMaxHp":
                {
                    double basis = expr.Basis == "source"
                        ? ctx.Hero(source.Side).MaxHp
                        : targetHero.MaxHp;
                    return basis * expr.Value / 100.0;
                }
                case "regen":
                    return targetHero.Regen;
                case "perStack":
                    return (expr.Stack == "burn" ? targetHero.Burn : targetHero.Poison) * expr.Per;
                default:
                    return 0;
            }
        }

        public static double ApplyHeal(
            BattleContext ctx,
            Side side,
            double amount,
            double time,
            string sourceItemId,
            string sourceCardId,
            int? chainId,
            int depth)
        {
            InternalHero hero = ctx.Hero(side);
            double before = hero.Hp;
            hero.Hp = Math.Min(hero.MaxHp, hero.Hp + Math.Max(0, JsMath.Round(amount)));
            double healed = hero.Hp - before;
            ctx.Totals.For(side).Heal += healed;

            DamageSource source = new DamageSource();
            source.Id = sourceItemId ?? "heal";
            source.CardId = sourceCardId ?? "heal";
            source.Side = side;
            EmitTrigger(ctx, "onHeal", source, side, depth, null, chainId);

            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = side;
            ev.TargetSide = side;
            ev.SourceItemId = sourceItemId;
            ev.SourceCardId = sourceCardId;
            ev.Kind = "heal";
            ev.Value = healed;
            ev.HpAfter = hero.Hp;
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ctx.Events.Add(FinishEvent(ctx, ev));
            return healed;
        }

        public static void ApplyShield(
            BattleContext ctx,
            Side side,
            double amount,
            double time,
            string sourceItemId,
            string sourceCardId,
            int? chainId,
            int depth)
        {
            InternalHero hero = ctx.Hero(side);
            double gained = Math.Max(0, JsMath.Round(amount));
            hero.Shield += gained;
            ctx.Totals.For(side).Shield += gained;

            DamageSource source = new DamageSource();
            source.Id = sourceItemId ?? "shield";
            source.CardId = sourceCardId ?? "shield";
            source.Side = side;
            EmitTrigger(ctx, "onShield", source, side, depth, null, chainId);

            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = side;
            ev.TargetSide = side;
            ev.SourceItemId = sourceItemId;
            ev.SourceCardId = sourceCardId;
            ev.Kind = "shield";
            ev.Value = gained;
            ev.ShieldAfter = hero.Shield;
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ctx.Events.Add(FinishEvent(ctx, ev));
        }

        private static void EmitTrigger(
            BattleContext ctx,
            string eventName,
            DamageSource source,
            Side? targetSide,
            int depth,
            string targetItemId,
            int? chainId)
        {
            TriggerPayload payload = new TriggerPayload();
            payload.Event = eventName;
            payload.SourceSide = source.Side;
            payload.SourceItemId = source.Id;
            payload.TargetSide = targetSide;
            payload.TargetItemId = targetItemId;
            payload.ChainId = chainId;
            payload.Depth = depth;
            ctx.TriggerQueue.Enqueue(payload);
        }

        public static void ApplyDamage(
            BattleContext ctx,
            DamageSource source,
            Side targetSide,
            double rawAmount,
            double time,
            DamageOptions options)
        {
            DamageOptions opts = options ?? new DamageOptions();
            double amount = Math.Max(0, JsMath.Round(rawAmount));
            int depth = opts.Depth;
            int? chainId = opts.ChainId;
            bool critical = false;

            if (!opts.IsDot && source.CritBonus > 0 && ctx.Rng.Chance(source.CritBonus))
            {
                critical = true;
                amount = JsMath.Round(amount * ctx.Rules.CritMultiplier);
                BattleEvent critEvent = new BattleEvent();
                critEvent.Time = time;
                critEvent.Side = source.Side;
                critEvent.SourceItemId = source.Id;
                critEvent.SourceCardId = source.CardId;
                critEvent.Kind = "crit";
                critEvent.TargetSide = targetSide;
                critEvent.Value = amount;
                critEvent.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
                critEvent.Depth = depth;
                critEvent.Note = null;
                ctx.Events.Add(FinishEvent(ctx, critEvent));
            }

            InternalHero hero = ctx.Hero(targetSide);
            double absorbed;
            double healthDamage;
            if (ctx.Config.NativeMechanics && opts.IsDot && opts.DotKind == "poison")
            {
                absorbed = 0;
                healthDamage = amount;
            }
            else if (ctx.Config.NativeMechanics && opts.IsDot && opts.DotKind == "burn")
            {
                // Native frames round the full shield cost down: 9 Burn can be
                // fully absorbed by 4 Shield. With too little Shield, each point
                // still prevents 2 damage (8 Burn into 1 Shield loses 6 HP).
                double shieldCost = Math.Floor(amount / 2);
                absorbed = Math.Min(hero.Shield, shieldCost);
                healthDamage = hero.Shield > 0 && hero.Shield >= shieldCost ? 0 : Math.Max(0, amount - 2 * hero.Shield);
            }
            else
            {
                absorbed = opts.IsDot && ctx.Rules.DotBypassesShield ? 0 : Math.Min(hero.Shield, amount);
                healthDamage = amount - absorbed;
            }
            hero.Shield -= absorbed;
            double hpLoss = Math.Min(hero.Hp, healthDamage);
            hero.Hp -= hpLoss;

            ctx.Totals.For(source.Side).Damage += amount;
            ctx.Totals.For(source.Side).EffectiveDamage += hpLoss;

            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = source.Side;
            ev.SourceItemId = source.Id;
            ev.SourceCardId = source.CardId;
            ev.Kind = opts.IsDot ? "dotTick" : "damage";
            ev.TargetSide = targetSide;
            ev.Value = amount;
            ev.Absorbed = absorbed;
            // Damage packets are retained for legacy parity; live output uses
            // the health and shield actually removed, excluding overkill.
            ev.ActualDamage = ctx.Config.NativeMechanics ? (double?)(hpLoss + absorbed) : null;
            ev.HpAfter = hero.Hp;
            ev.ShieldAfter = hero.Shield;
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ev.Note = opts.IsDot ? opts.DotKind : (critical ? "critical" : null);
            ctx.Events.Add(FinishEvent(ctx, ev));

            if (amount > 0)
            {
                EmitTrigger(ctx, "onDamageDealt", source, targetSide, depth, null, chainId);

                TriggerPayload taken = new TriggerPayload();
                taken.Event = "onDamageTaken";
                taken.SourceSide = targetSide;
                taken.SourceItemId = source.Id;
                taken.TargetSide = targetSide;
                taken.ChainId = chainId;
                taken.Depth = depth;
                ctx.TriggerQueue.Enqueue(taken);

                if (critical) EmitTrigger(ctx, "onCrit", source, targetSide, depth, null, chainId);
            }

            if (source.Lifesteal > 0 && hpLoss > 0 && !opts.IsDot)
            {
                ApplyHeal(
                    ctx,
                    source.Side,
                    hpLoss * source.Lifesteal,
                    time,
                    source.Id,
                    source.CardId,
                    chainId,
                    depth);
            }
        }

        private static InternalHero HeroFor(
            BattleContext ctx,
            InternalItem source,
            Target target,
            Side defaultSide)
        {
            if (target == null) return ctx.Hero(defaultSide);
            if (target.Kind == "selfHero") return ctx.Hero(source.Side);
            if (target.Kind == "enemyHero") return ctx.Hero(SideUtil.Opposing(source.Side));
            return ctx.Hero(defaultSide);
        }

        private static List<InternalItem> ItemsForTarget(
            BattleContext ctx,
            InternalItem source,
            Target target,
            int count,
            string triggerSourceItemId)
        {
            if (ctx.Config.NativeMechanics)
                return NativeItemsForTarget(ctx, source, target, count, triggerSourceItemId);
            List<InternalItem> selfItems = new List<InternalItem>();
            List<InternalItem> enemyItems = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Side == source.Side) selfItems.Add(item);
                else enemyItems.Add(item);
            }
            selfItems.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            enemyItems.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            int sourceIndex = selfItems.FindIndex(delegate(InternalItem item) { return item.Id == source.Id; });

            List<InternalItem> list = new List<InternalItem>();
            string kind = target != null ? target.Kind : "selfHero";
            if (target != null && target.NativeCondition != null)
            {
                selfItems.RemoveAll(delegate(InternalItem item) { return !NativeSkillSupport.CardMatches(item, target.NativeCondition); });
                enemyItems.RemoveAll(delegate(InternalItem item) { return !NativeSkillSupport.CardMatches(item, target.NativeCondition); });
            }
            switch (kind)
            {
                case "triggerSourceItem":
                    foreach (InternalItem item in ctx.Items)
                        if (item.Id == triggerSourceItemId && NativeSkillSupport.CardMatches(item, target.NativeCondition)) list.Add(item);
                    break;
                case "selfItem":
                    if (!string.IsNullOrEmpty(target.ItemId))
                    {
                        foreach (InternalItem item in ctx.Items)
                        {
                            bool matches = item.CardId == target.ItemId || item.Id == target.ItemId;
                            if (matches && item.Side == source.Side) list.Add(item);
                        }
                    }
                    else
                    {
                        list.Add(source);
                    }
                    break;
                case "enemyItem":
                    if (target.ItemId != null)
                    {
                        foreach (InternalItem item in ctx.Items)
                        {
                            bool matches = item.CardId == target.ItemId || item.Id == target.ItemId;
                            if (matches && item.Side != source.Side) list.Add(item);
                        }
                    }
                    break;
                case "allSelfItems":
                    list = selfItems;
                    break;
                case "allEnemyItems":
                    list = enemyItems;
                    break;
                case "adjacentSelf":
                    if (sourceIndex - 1 >= 0) list.Add(selfItems[sourceIndex - 1]);
                    if (sourceIndex + 1 < selfItems.Count) list.Add(selfItems[sourceIndex + 1]);
                    break;
                case "leftSelf":
                    if (sourceIndex > 0) list.Add(selfItems[sourceIndex - 1]);
                    break;
                case "rightSelf":
                    if (sourceIndex >= 0 && sourceIndex < selfItems.Count - 1)
                        list.Add(selfItems[sourceIndex + 1]);
                    break;
                case "randomSelfItem":
                    list = Sample(ctx, selfItems, count);
                    break;
                case "randomEnemyItem":
                    list = Sample(ctx, enemyItems, count);
                    break;
                case "lowestHpEnemyItem":
                    if (enemyItems.Count > 0) list.Add(enemyItems[0]);
                    break;
                default:
                    break;
            }

            if (kind == "allSelfItems" || kind == "allEnemyItems" || kind == "adjacentSelf")
                return list;

            int take = Math.Max(1, count);
            return list.Count > take ? list.GetRange(0, take) : list;
        }

        private static List<InternalItem> NativeItemsForTarget(BattleContext ctx, InternalItem source, Target target, int count, string triggerSourceItemId)
        {
            List<InternalItem> own = new List<InternalItem>();
            List<InternalItem> opponents = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
                if (item.Side == source.Side) own.Add(item); else opponents.Add(item);
            own.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            opponents.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            int index = own.FindIndex(delegate(InternalItem item) { return item.Id == source.Id; });
            List<InternalItem> adjacent = new List<InternalItem>();
            if (index > 0) adjacent.Add(own[index - 1]);
            if (index >= 0 && index + 1 < own.Count) adjacent.Add(own[index + 1]);
            List<InternalItem> selected = new List<InternalItem>();
            string kind = target != null ? target.Kind : "selfHero";
            switch (kind)
            {
                case "selfItem":
                    if (target.ItemId == null) selected.Add(source);
                    else foreach (InternalItem item in own) if (item.Id == target.ItemId || item.CardId == target.ItemId) selected.Add(item);
                    break;
                case "enemyItem":
                    foreach (InternalItem item in opponents) if (item.Id == target.ItemId || item.CardId == target.ItemId) selected.Add(item);
                    break;
                case "triggerSourceItem":
                    foreach (InternalItem item in ctx.Items) if (item.Id == triggerSourceItemId) selected.Add(item);
                    break;
                case "allSelfItems": case "randomSelfItem": selected = own; break;
                case "allEnemyItems": case "randomEnemyItem": case "lowestHpEnemyItem": selected = opponents; break;
                case "adjacentSelf": case "randomAdjacentSelf": selected = adjacent; break;
                case "leftSelf": if (index > 0) selected.Add(own[index - 1]); break;
                case "rightSelf": if (index >= 0 && index + 1 < own.Count) selected.Add(own[index + 1]); break;
            }
            if (target != null)
                selected.RemoveAll(delegate(InternalItem item)
                {
                    return (target.ExcludeSelf && item.Id == source.Id) ||
                        (target.FlyingOnly && !item.Flying) ||
                        (target.NotFlyingOnly && item.Flying) ||
                        (target.HasCooldownOnly && (item.Card.Passive || item.Card.Cooldown <= 0)) ||
                        (target.AdjacentOnly && !adjacent.Contains(item)) ||
                        !NativeSkillSupport.CardMatches(item, target.NativeCondition);
                });
            if (kind == "randomSelfItem" || kind == "randomEnemyItem" || kind == "randomAdjacentSelf")
                return Sample(ctx, selected, count);
            if (kind == "allSelfItems" || kind == "allEnemyItems" || kind == "adjacentSelf") return selected;
            int take = Math.Max(1, count);
            return selected.Count > take ? selected.GetRange(0, take) : selected;
        }

        private static List<InternalItem> Sample(
            BattleContext ctx,
            List<InternalItem> pool,
            int count)
        {
            List<InternalItem> copy = new List<InternalItem>(pool);
            List<InternalItem> result = new List<InternalItem>();
            int n = Math.Min(Math.Max(1, count), copy.Count);
            for (int i = 0; i < n; i++)
            {
                int index = ctx.Rng.Int(copy.Count);
                result.Add(copy[index]);
                copy.RemoveAt(index);
            }
            return result;
        }

        private static double CriticalAmount(
            BattleContext ctx,
            InternalItem source,
            double amount,
            double time,
            int depth,
            int chainId,
            Side targetSide)
        {
            if (source.CritBonus <= 0 || !ctx.Rng.Chance(source.CritBonus)) return amount;
            double next = JsMath.Round(amount * ctx.Rules.CritMultiplier);
            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = source.Side;
            ev.SourceItemId = source.Id;
            ev.SourceCardId = source.CardId;
            ev.Kind = "crit";
            ev.TargetSide = targetSide;
            ev.Value = next;
            ev.ChainId = chainId;
            ev.Depth = depth;
            ctx.Events.Add(FinishEvent(ctx, ev));
            return next;
        }

        public static void ResolveEffect(
            BattleContext ctx,
            Effect effect,
            InternalItem source,
            double time,
            int depth,
            int? chainId,
            string triggerSourceItemId = null)
        {
            if (effect == null || string.IsNullOrEmpty(effect.Kind)) return;
            int ch = chainId.HasValue ? chainId.Value : ctx.NextChainId++;

            switch (effect.Kind)
            {
                case "damage":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, SideUtil.Opposing(source.Side));
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero) + source.DamageBonus;
                    RecordStat(ctx, effect, source, time, amount, 1);
                    DamageOptions opts = new DamageOptions();
                    opts.Depth = depth;
                    opts.ChainId = ch;
                    ApplyDamage(ctx, SourceOf(source), hero.Side, amount, time, opts);
                    return;
                }
                case "heal":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero);
                    RecordStat(ctx, effect, source, time, amount, 1);
                    double crit = CriticalAmount(ctx, source, amount, time, depth, ch, hero.Side);
                    ApplyHeal(ctx, hero.Side, crit, time, source.Id, source.CardId, ch, depth);
                    return;
                }
                case "shield":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero);
                    RecordStat(ctx, effect, source, time, amount, 1);
                    double crit = CriticalAmount(ctx, source, amount, time, depth, ch, hero.Side);
                    ApplyShield(ctx, hero.Side, crit, time, source.Id, source.CardId, ch, depth);
                    return;
                }
                case "burn":
                case "poison":
                case "regen":
                {
                    InternalHero hero = HeroFor(
                        ctx,
                        source,
                        effect.Target,
                        effect.Kind == "regen" ? source.Side : SideUtil.Opposing(source.Side));
                    double amount = Math.Max(0, JsMath.Round(EvaluateNum(effect.Amount, ctx, source, hero)));
                    RecordStat(ctx, effect, source, time, amount, 1);

                    if (effect.Kind == "burn")
                    {
                        hero.Burn += amount;
                        hero.BurnSource = source.Side;
                        hero.BurnSourceItemId = source.Id;
                        hero.BurnSourceCardId = source.CardId;
                    }
                    else if (effect.Kind == "poison")
                    {
                        hero.Poison += amount;
                        hero.PoisonSource = source.Side;
                        hero.PoisonSourceItemId = source.Id;
                        hero.PoisonSourceCardId = source.CardId;
                    }
                    else
                    {
                        hero.Regen += amount;
                        hero.RegenSourceItemId = source.Id;
                        hero.RegenSourceCardId = source.CardId;
                    }

                    BattleEvent ev = new BattleEvent();
                    ev.Time = time;
                    ev.Side = source.Side;
                    ev.TargetSide = hero.Side;
                    ev.SourceItemId = source.Id;
                    ev.SourceCardId = source.CardId;
                    ev.Kind = effect.Kind;
                    ev.Value = amount;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));

                    if (effect.Kind == "burn") EmitTrigger(ctx, "onBurnApplied", SourceOf(source), hero.Side, depth, null, ch);
                    else if (effect.Kind == "poison") EmitTrigger(ctx, "onPoisonApplied", SourceOf(source), hero.Side, depth, null, ch);
                    else EmitTrigger(ctx, "onRegen", SourceOf(source), hero.Side, depth, null, ch);
                    return;
                }
                case "haste":
                case "slow":
                case "freeze":
                case "charge":
                {
                    Target target = effect.Target;
                    if (target == null)
                    {
                        target = new Target();
                        target.Kind = "selfItem";
                        target.ItemId = source.Id;
                    }

                    int count = effect.Count.HasValue ? effect.Count.Value : 1;
                    List<InternalItem> targets = ItemsForTarget(ctx, source, target, count, triggerSourceItemId);
                    RecordStat(ctx, effect, source, time, effect.Seconds, targets.Count);

                    foreach (InternalItem item in targets)
                    {
                        double seconds = effect.Seconds;
                        if (ctx.Config.NativeMechanics)
                        {
                            double reduction = effect.Kind == "slow" ? item.Card.PercentSlowReduction :
                                effect.Kind == "freeze" ? item.Card.PercentFreezeReduction :
                                effect.Kind == "charge" ? item.Card.PercentChargeReduction : 0;
                            if (item.Flying && (effect.Kind == "slow" || effect.Kind == "freeze")) reduction = Math.Max(50, reduction);
                            seconds *= Math.Max(0, 1 - reduction / 100);
                        }
                        if (effect.Kind == "haste")
                            item.HasteUntil = ctx.Config.NativeMechanics ? Math.Max(time, item.HasteUntil) + seconds : Math.Max(item.HasteUntil, time + seconds);
                        else if (effect.Kind == "slow")
                            item.SlowUntil = ctx.Config.NativeMechanics ? Math.Max(time, item.SlowUntil) + seconds : Math.Max(item.SlowUntil, time + seconds);
                        else if (effect.Kind == "freeze")
                            item.FrozenUntil = ctx.Config.NativeMechanics ? Math.Max(time, item.FrozenUntil) + seconds : Math.Max(item.FrozenUntil, time + seconds);
                        else
                            item.Cooldown = Math.Max(0, item.Cooldown - seconds);

                        BattleEvent ev = new BattleEvent();
                        ev.Time = time;
                        ev.Side = source.Side;
                        ev.TargetSide = item.Side;
                        ev.SourceItemId = source.Id;
                        ev.TargetItemId = item.Id;
                        ev.Kind = effect.Kind;
                        ev.Value = seconds;
                        ev.ChainId = ch;
                        ev.Depth = depth;
                        ctx.Events.Add(FinishEvent(ctx, ev));

                        if (effect.Kind == "haste") EmitTrigger(ctx, "onHaste", SourceOf(source), item.Side, depth, item.Id, ch);
                        else if (effect.Kind == "slow") EmitTrigger(ctx, "onSlow", SourceOf(source), item.Side, depth, item.Id, ch);
                        else if (effect.Kind == "freeze") EmitTrigger(ctx, "onFreeze", SourceOf(source), item.Side, depth, item.Id, ch);
                    }
                    return;
                }
                case "fly":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        if (item.Flying) continue;
                        item.Flying = true;
                        changed++;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "fly", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        EmitTrigger(ctx, "onFly", SourceOf(source), item.Side, depth, item.Id, ch);
                    }
                    RecordStat(ctx, effect, source, time, 1, changed);
                    return;
                }
                case "buffDamage":
                {
                    double amount = EvaluateNum(effect.Amount, ctx, source, ctx.Hero(source.Side));
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, 1, triggerSourceItemId);
                    foreach (InternalItem item in targets)
                    {
                        item.DamageBonus += amount;
                        BattleEvent ev = new BattleEvent();
                        ev.Time = time;
                        ev.Side = source.Side;
                        ev.SourceItemId = source.Id;
                        ev.SourceCardId = source.CardId;
                        ev.TargetItemId = item.Id;
                        ev.TargetSide = item.Side;
                        ev.Kind = "buffDamage";
                        ev.Value = amount;
                        ev.ChainId = ch;
                        ev.Depth = depth;
                        ctx.Events.Add(FinishEvent(ctx, ev));
                    }
                    RecordStat(ctx, effect, source, time, amount, targets.Count);
                    return;
                }
                case "reload":
                {
                    Target target = effect.Target;
                    if (target == null)
                    {
                        target = new Target();
                        target.Kind = "selfItem";
                        target.ItemId = source.Id;
                    }

                    int count = effect.Count.HasValue ? effect.Count.Value : 1;
                    List<InternalItem> targets = ItemsForTarget(ctx, source, target, count, triggerSourceItemId);
                    double raw = effect.Amount != null ? effect.Amount.Value : effect.AmountValue;
                    int amount = (int)JsMath.Round(raw);
                    RecordStat(ctx, effect, source, time, amount, targets.Count);

                    foreach (InternalItem item in targets)
                    {
                        if (!item.AmmoMax.HasValue) continue;
                        int before = item.Ammo;
                        item.Ammo = Math.Min(item.AmmoMax.Value, item.Ammo + amount);
                        BattleEvent ev = new BattleEvent();
                        ev.Time = time;
                        ev.Side = source.Side;
                        ev.TargetSide = item.Side;
                        ev.SourceItemId = source.Id;
                        ev.TargetItemId = item.Id;
                        ev.Kind = "reload";
                        ev.Value = item.Ammo - before;
                        ev.ChainId = ch;
                        ev.Depth = depth;
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        EmitTrigger(ctx, "onReload", SourceOf(source), item.Side, depth, null, ch);
                    }
                    return;
                }
                case "ammo":
                {
                    double raw = effect.Amount != null ? effect.Amount.Value : effect.AmountValue;
                    int amount = (int)JsMath.Round(raw);
                    RecordStat(ctx, effect, source, time, amount, 1);
                    source.AmmoMax = (source.AmmoMax.HasValue ? source.AmmoMax.Value : 0) + amount;
                    source.Ammo += amount;
                    BattleEvent ev = new BattleEvent();
                    ev.Time = time;
                    ev.Side = source.Side;
                    ev.SourceItemId = source.Id;
                    ev.Kind = "ammo";
                    ev.Value = amount;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));
                    return;
                }
                default:
                    return;
            }
        }

        private static DamageSource SourceOf(InternalItem item)
        {
            DamageSource source = new DamageSource();
            source.Id = item.Id;
            source.CardId = item.CardId;
            source.Side = item.Side;
            source.CritBonus = item.CritBonus;
            source.Lifesteal = item.Lifesteal;
            return source;
        }

        private static void RecordStat(
            BattleContext ctx,
            Effect effect,
            InternalItem source,
            double time,
            double value,
            int targetCount)
        {
            EffectStat stat = new EffectStat();
            stat.Time = time;
            stat.SourceItemId = source.Id;
            stat.SourceCardId = source.CardId;
            stat.Kind = effect.Kind;
            stat.Value = value;
            stat.TargetCount = targetCount;
            ctx.EffectStats.Add(stat);
        }

        internal static BattleEvent FinishEvent(BattleContext ctx, BattleEvent ev)
        {
            ev.Id = ctx.NextEventId++;
            ctx.EventsThisTick++;
            return ev;
        }
    }
}
