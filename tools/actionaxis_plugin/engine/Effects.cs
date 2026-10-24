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
            InternalHero targetHero,
            string triggerSourceItemId = null,
            string triggerTargetItemId = null)
        {
            if (expr == null) return 0;
            double value = EvaluateBase(expr, ctx, source, targetHero, triggerSourceItemId, triggerTargetItemId);
            if (expr.Modifier != null)
            {
                double other = EvaluateNum(expr.Modifier, ctx, source, targetHero, triggerSourceItemId, triggerTargetItemId);
                value = expr.ModifyMode == "Add" ? value + other : value * other;
                if (expr.ShouldRound) value = JsMath.Round(value);
            }
            return value;
        }

        private static double EvaluateBase(
            NumExpr expr,
            BattleContext ctx,
            InternalItem source,
            InternalHero targetHero,
            string triggerSourceItemId,
            string triggerTargetItemId)
        {
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
                case "cardAttribute":
                {
                    InternalItem item = ResolveNumSource(ctx, source, expr.TargetKind, triggerSourceItemId, triggerTargetItemId);
                    return item == null ? 0 : ItemAttributes.Get(item, expr.Attribute);
                }
                case "playerAttribute":
                {
                    Side side = expr.Side == "enemy" ? SideUtil.Opposing(source.Side) : source.Side;
                    return NativeSkillSupport.PlayerAttributeValue(ctx, ctx.Hero(side), expr.Attribute);
                }
                case "tagCount":
                case "aggregate":
                case "cardCount":
                {
                    List<InternalItem> items = EvaluateTargetItems(ctx, source, expr, triggerSourceItemId, triggerTargetItemId);
                    if (expr.Type == "cardCount") return items.Count;
                    if (expr.Type == "aggregate")
                    {
                        double sum = 0;
                        foreach (InternalItem item in items) sum += ItemAttributes.Get(item, expr.Attribute);
                        return sum;
                    }
                    if (!expr.Distinct)
                    {
                        int total = 0;
                        foreach (InternalItem item in items) total += item.Card.Tags.Count + item.ExtraTags.Count;
                        return total;
                    }
                    HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (InternalItem item in items)
                    {
                        foreach (string tag in item.Card.Tags) unique.Add(tag);
                        foreach (string tag in item.ExtraTags) unique.Add(tag);
                    }
                    return unique.Count;
                }
                case "range":
                {
                    int min = (int)JsMath.Round(expr.Min), max = (int)JsMath.Round(expr.Max);
                    if (max <= min) return min;
                    return min + ctx.Rng.Int(max - min + 1);
                }
                // TReferenceValueAttributeChange：本次触发携带的变化量；
                // 静态光环或主动能力没有触发来源时回退到原生 DefaultValue。
                case "attributeChange":
                    return ctx.TriggerAmount.HasValue ? ctx.TriggerAmount.Value : expr.Value;
                default:
                    return 0;
            }
        }

        private static InternalItem ResolveNumSource(BattleContext ctx, InternalItem source, string targetKind, string triggerSourceItemId, string triggerTargetItemId)
        {
            if (targetKind == "triggerSourceItem") return FindItem(ctx, triggerSourceItemId);
            if (targetKind == "triggerTargetItem") return FindItem(ctx, triggerTargetItemId);
            return source;
        }

        private static InternalItem FindItem(BattleContext ctx, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (InternalItem item in ctx.Items) if (item.Id == id) return item;
            return null;
        }

        private static List<InternalItem> EvaluateTargetItems(BattleContext ctx, InternalItem source, NumExpr expr, string triggerSourceItemId, string triggerTargetItemId)
        {
            Target target = new Target();
            target.Kind = expr.TargetKind ?? "allSelfItems";
            target.NativeCondition = expr.Condition;
            target.ExcludeSelf = expr.Exclude;
            List<InternalItem> items = NativeItemsForTarget(ctx, source, target, int.MaxValue, triggerSourceItemId);
            if (target.Kind == "triggerTargetItem")
            {
                InternalItem targetItem = FindItem(ctx, triggerTargetItemId);
                items = targetItem == null ? new List<InternalItem>() : new List<InternalItem> { targetItem };
            }
            return items;
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
            double requested = Math.Max(0, JsMath.Round(amount));
            hero.Hp = Math.Min(hero.MaxHp, hero.Hp + requested);
            double healed = hero.Hp - before;
            ctx.Totals.For(side).Heal += healed;

            DamageSource source = new DamageSource();
            source.Id = sourceItemId ?? "heal";
            source.CardId = sourceCardId ?? "heal";
            source.Side = side;
            EmitTrigger(ctx, "onHeal", source, side, depth, null, chainId);
            if (requested - healed > 1e-9) EmitTrigger(ctx, "onOverHeal", source, side, depth, null, chainId);
            if (healed > 0) EmitPlayerAttribute(ctx, side, "Health", "Gain", healed, depth, chainId);

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
            if (gained > 0) EmitPlayerAttribute(ctx, side, "Shield", "Gain", gained, depth, chainId);

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

        // 人物属性变化：主体是发生变化的那一方英雄。
        private static void EmitPlayerAttribute(BattleContext ctx, Side side, string attribute, string changeType, double amount, int depth, int? chainId)
        {
            TriggerPayload payload = new TriggerPayload();
            payload.Event = "onPlayerAttributeChanged";
            payload.SourceSide = side;
            payload.AttributeName = attribute;
            payload.ChangeType = changeType;
            payload.Amount = amount;
            payload.ChainId = chainId;
            payload.Depth = depth;
            ctx.TriggerQueue.Enqueue(payload);
        }

        // 卡牌属性变化：主体是被改变状态的那张卡。
        private static void EmitCardAttribute(BattleContext ctx, InternalItem item, string attribute, string changeType, double amount, int depth, int? chainId)
        {
            TriggerPayload payload = new TriggerPayload();
            payload.Event = "onCardAttributeChanged";
            payload.SourceSide = item.Side;
            payload.SourceItemId = item.Id;
            payload.AttributeName = attribute;
            payload.ChangeType = changeType;
            payload.Amount = amount;
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
                if (absorbed > 0) EmitPlayerAttribute(ctx, targetSide, "Shield", "Loss", absorbed, depth, chainId);
                if (hpLoss > 0) EmitPlayerAttribute(ctx, targetSide, "Health", "Loss", hpLoss, depth, chainId);
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
            string triggerSourceItemId,
            string triggerTargetItemId = null)
        {
            if (ctx.Config.NativeMechanics)
                return NativeItemsForTarget(ctx, source, target, count, triggerSourceItemId, triggerTargetItemId);
            List<InternalItem> selfItems = new List<InternalItem>();
            List<InternalItem> enemyItems = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Destroyed) continue;
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
                selfItems.RemoveAll(delegate(InternalItem item) { return !NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx); });
                enemyItems.RemoveAll(delegate(InternalItem item) { return !NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx); });
            }
            switch (kind)
            {
                case "triggerSourceItem":
                    foreach (InternalItem item in ctx.Items)
                        if (item.Id == triggerSourceItemId && NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx)) list.Add(item);
                    break;
                case "triggerTargetItem":
                    foreach (InternalItem item in ctx.Items)
                        if (item.Id == triggerTargetItemId && NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx)) list.Add(item);
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
                case "triggerSourceLeft":
                case "triggerSourceRight":
                case "triggerSourceAdjacent":
                    AddTriggerSourceTargets(ctx, triggerSourceItemId, kind, list);
                    break;
                case "leftMostSelf":
                    list = TakeMost(ctx, selfItems, false, count, target, source.Id);
                    break;
                case "rightMostSelf":
                    list = TakeMost(ctx, selfItems, true, count, target, source.Id);
                    break;
                case "leftMostEnemy":
                    list = TakeMost(ctx, enemyItems, false, count, target, source.Id);
                    break;
                case "rightMostEnemy":
                    list = TakeMost(ctx, enemyItems, true, count, target, source.Id);
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

        private static List<InternalItem> NativeItemsForTarget(BattleContext ctx, InternalItem source, Target target, int count, string triggerSourceItemId, string triggerTargetItemId = null)
        {
            List<InternalItem> own = new List<InternalItem>();
            List<InternalItem> opponents = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
                if (item.Destroyed) continue; else if (item.Side == source.Side) own.Add(item); else opponents.Add(item);
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
                case "triggerTargetItem":
                    foreach (InternalItem item in ctx.Items) if (item.Id == triggerTargetItemId) selected.Add(item);
                    break;
                case "allSelfItems": case "randomSelfItem": selected = own; break;
                case "allEnemyItems": case "randomEnemyItem": case "lowestHpEnemyItem": selected = opponents; break;
                case "adjacentSelf": case "randomAdjacentSelf": selected = adjacent; break;
                case "triggerSourceLeft":
                case "triggerSourceRight":
                case "triggerSourceAdjacent":
                    AddTriggerSourceTargets(ctx, triggerSourceItemId, kind, selected);
                    break;
                case "leftMostSelf":
                    selected = TakeMost(ctx, own, false, count, target, source.Id);
                    break;
                case "rightMostSelf":
                    selected = TakeMost(ctx, own, true, count, target, source.Id);
                    break;
                case "leftMostEnemy":
                    selected = TakeMost(ctx, opponents, false, count, target, source.Id);
                    break;
                case "rightMostEnemy":
                    selected = TakeMost(ctx, opponents, true, count, target, source.Id);
                    break;
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
                        !NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx, triggerSourceItemId);
                });
            if (kind == "randomSelfItem" || kind == "randomEnemyItem" || kind == "randomAdjacentSelf")
                return Sample(ctx, selected, count);
            if (kind == "allSelfItems" || kind == "allEnemyItems" || kind == "adjacentSelf") return selected;
            int take = Math.Max(1, count);
            return selected.Count > take ? selected.GetRange(0, take) : selected;
        }

        private static void AddTriggerSourceTargets(BattleContext ctx, string triggerSourceItemId, string kind, List<InternalItem> target)
        {
            if (string.IsNullOrEmpty(triggerSourceItemId)) return;
            InternalItem origin = null;
            foreach (InternalItem candidate in ctx.Items)
                if (candidate.Id == triggerSourceItemId) { origin = candidate; break; }
            if (origin == null) return;
            List<InternalItem> pool = new List<InternalItem>();
            foreach (InternalItem candidate in ctx.Items)
                if (candidate.Side == origin.Side) pool.Add(candidate);
            pool.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            int index = pool.FindIndex(delegate(InternalItem item) { return item.Id == origin.Id; });
            if (index < 0) return;
            if (kind == "triggerSourceLeft" || kind == "triggerSourceAdjacent")
                if (index > 0) target.Add(pool[index - 1]);
            if (kind == "triggerSourceRight" || kind == "triggerSourceAdjacent")
                if (index + 1 < pool.Count) target.Add(pool[index + 1]);
        }

        private static List<InternalItem> TakeMost(BattleContext ctx, List<InternalItem> candidates, bool right,
            int count, Target target, string sourceId)
        {
            List<InternalItem> pool = new List<InternalItem>();
            foreach (InternalItem item in candidates)
            {
                if (target != null && target.ExcludeSelf && item.Id == sourceId) continue;
                if (target != null && !NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx)) continue;
                pool.Add(item);
            }
            pool.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            int take = Math.Min(Math.Max(1, count), pool.Count);
            if (take <= 0) return new List<InternalItem>();
            if (!right) return pool.GetRange(0, take);
            pool.Reverse();
            return pool.GetRange(0, take);
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
            string triggerSourceItemId = null,
            string triggerTargetItemId = null)
        {
            NativeDynamicAuras.Refresh(ctx);
            try { ResolveEffectCore(ctx, effect, source, time, depth, chainId, triggerSourceItemId, triggerTargetItemId); }
            finally { NativeDynamicAuras.Refresh(ctx); }
        }

        private static void ResolveEffectCore(
            BattleContext ctx, Effect effect, InternalItem source, double time,
            int depth, int? chainId, string triggerSourceItemId, string triggerTargetItemId)
        {
            if (effect == null || string.IsNullOrEmpty(effect.Kind)) return;
            int ch = chainId.HasValue ? chainId.Value : ctx.NextChainId++;

            switch (effect.Kind)
            {
                case "damage":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, SideUtil.Opposing(source.Side));
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId) + source.DamageBonus;
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
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId);
                    RecordStat(ctx, effect, source, time, amount, 1);
                    double crit = CriticalAmount(ctx, source, amount, time, depth, ch, hero.Side);
                    ApplyHeal(ctx, hero.Side, crit, time, source.Id, source.CardId, ch, depth);
                    return;
                }
                case "shield":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId);
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
                    double amount = Math.Max(0, JsMath.Round(EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId)));
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, target, count, triggerSourceItemId, triggerTargetItemId);
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
                        {
                            item.Cooldown = Math.Max(0, item.Cooldown - seconds);
                            // 充能的对象是被充能的卡，被动绑定它的“被充能”事件。
                            EmitTrigger(ctx, "onCharge", SourceOf(item), item.Side, depth, null, ch);
                        }

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
                        if (effect.Kind != "charge")
                            EmitCardAttribute(ctx, item, effect.Kind == "haste" ? "Haste" : effect.Kind == "slow" ? "Slow" : "Freeze", "Gain", seconds, depth, ch);
                    }
                    return;
                }
                case "fly":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        if (item.Flying) continue;
                        item.Flying = true;
                        changed++;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "fly", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        // 飞行状态的主体是被影响的卡，而不是造成飞行的来源。
                        EmitTrigger(ctx, "onFly", SourceOf(item), item.Side, depth, null, ch);
                        EmitCardAttribute(ctx, item, "Flying", "Gain", 1, depth, ch);
                    }
                    RecordStat(ctx, effect, source, time, 1, changed);
                    return;
                }
                case "flyStop":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        if (!item.Flying) continue;
                        item.Flying = false;
                        changed++;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "flyStop", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        EmitTrigger(ctx, "onFlyStop", SourceOf(item), item.Side, depth, null, ch);
                        EmitCardAttribute(ctx, item, "Flying", "Loss", 1, depth, ch);
                    }
                    RecordStat(ctx, effect, source, time, 1, changed);
                    return;
                }
                case "burnRemove":
                case "poisonRemove":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double stacks = effect.Kind == "burnRemove" ? hero.Burn : hero.Poison;
                    double removed = effect.Amount == null ? stacks :
                        Math.Min(stacks, Math.Max(0, JsMath.Round(EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId))));
                    if (effect.Kind == "burnRemove") hero.Burn = Math.Max(0, hero.Burn - removed);
                    else hero.Poison = Math.Max(0, hero.Poison - removed);
                    RecordStat(ctx, effect, source, time, removed, 1);
                    BattleEvent ev = new BattleEvent();
                    ev.Time = time;
                    ev.Side = source.Side;
                    ev.TargetSide = hero.Side;
                    ev.SourceItemId = source.Id;
                    ev.SourceCardId = source.CardId;
                    ev.Kind = effect.Kind;
                    ev.Value = removed;
                    ev.HpAfter = hero.Hp;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));
                    return;
                }
                case "composite":
                {
                    if (effect.Effects == null) return;
                    foreach (Effect child in effect.Effects)
                    {
                        ResolveEffect(ctx, child, source, time, depth + 1, ch, triggerSourceItemId, triggerTargetItemId);
                        if (ctx.Ended) return;
                    }
                    return;
                }
                case "transform":
                {
                    // 场上复制变形：目标原位替换为供体卡，额外副本排队到本帧末加入。
                    string rule = NativeTransform.DonorRule(source.Card);
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, 1, triggerSourceItemId, triggerTargetItemId);
                    int copies = effect.Count.HasValue ? effect.Count.Value : 1;
                    int changed = 0;
                    foreach (InternalItem target in targets)
                    {
                        InternalItem donor = NativeTransform.FindDonor(ctx, source, target, rule);
                        if (donor == null) continue;
                        if (NativeTransform.Apply(ctx, source, target, donor, copies, time, depth, ch)) changed++;
                        if (ctx.Ended) break;
                    }
                    RecordStat(ctx, effect, source, time, copies, changed);
                    return;
                }
                case "buffDamage":
                {
                    double amount = EvaluateNum(effect.Amount, ctx, source, ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId);
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, 1, triggerSourceItemId, triggerTargetItemId);
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, target, count, triggerSourceItemId, triggerTargetItemId);
                    double raw = effect.Amount != null ? effect.Amount.Value : effect.AmountValue;
                    int amount = (int)JsMath.Round(raw);
                    RecordStat(ctx, effect, source, time, amount < 0 ? 0 : amount, targets.Count);

                    foreach (InternalItem item in targets)
                    {
                        if (!item.AmmoMax.HasValue) continue;
                        int before = item.Ammo;
                        item.Ammo = amount < 0 ? item.AmmoMax.Value :
                            Math.Min(item.AmmoMax.Value, item.Ammo + amount);
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
                        // 装填的主体是被装填的卡。
                        EmitTrigger(ctx, "onReload", SourceOf(item), item.Side, depth, null, ch);
                    }
                    return;
                }
                case "modifyAttribute":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    double value = EvaluateNum(effect.Amount, ctx, source, ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId);
                    double reported = value;
                    foreach (InternalItem item in targets)
                    {
                        reported = ApplyAttributeChange(ctx, item, effect.Attribute, effect.Operation, value, time, effect.Seconds);
                        BattleEvent ev = new BattleEvent();
                        ev.Time = time;
                        ev.Side = source.Side;
                        ev.SourceItemId = source.Id;
                        ev.SourceCardId = source.CardId;
                        ev.TargetItemId = item.Id;
                        ev.TargetSide = item.Side;
                        ev.Kind = "modifyAttribute";
                        ev.Value = reported;
                        ev.Note = effect.Attribute;
                        ev.ChainId = ch;
                        ev.Depth = depth;
                        ctx.Events.Add(FinishEvent(ctx, ev));
                    }
                    RecordStat(ctx, effect, source, time, value, targets.Count);
                    return;
                }
                case "playerAttribute":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double value = EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId);
                    double before = effect.Attribute == "HealthMax" ? hero.MaxHp : effect.Attribute == "HealthRegen" ? hero.Regen :
                        effect.Attribute == "RageMax" ? hero.RageMax : hero.EnragedDurationMs;
                    double after = Operate(before, value, effect.Operation);
                    if (effect.Attribute == "HealthMax")
                    {
                        after = Math.Max(1, after);
                        double gain = after - hero.MaxHp;
                        hero.MaxHp = after;
                        // 原生食物/成长类最大生命同时抬高当前生命（无法超过新上限）。
                        if (gain > 0) hero.Hp = Math.Min(after, hero.Hp + gain);
                        else hero.Hp = Math.Max(0, Math.Min(after, hero.Hp));
                    }
                    else if (effect.Attribute == "HealthRegen") hero.Regen = Math.Max(0, after);
                    else if (effect.Attribute == "RageMax") hero.RageMax = Math.Max(0, after);
                    else hero.EnragedDurationMs = Math.Max(0, after);
                    BattleEvent ev = new BattleEvent();
                    ev.Time = time;
                    ev.Side = source.Side;
                    ev.TargetSide = hero.Side;
                    ev.SourceItemId = source.Id;
                    ev.SourceCardId = source.CardId;
                    ev.Kind = "playerAttribute";
                    ev.Value = after - before;
                    ev.Note = effect.Attribute;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));
                    return;
                }
                case "rage":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double amount = Math.Max(0, JsMath.Round(EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId)));
                    hero.Rage += amount;
                    RecordStat(ctx, effect, source, time, amount, 1);
                    BattleEvent ev = new BattleEvent();
                    ev.Time = time;
                    ev.Side = source.Side;
                    ev.TargetSide = hero.Side;
                    ev.SourceItemId = source.Id;
                    ev.SourceCardId = source.CardId;
                    ev.Kind = "rage";
                    ev.Value = amount;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));
                    // 怒气变化同样属于人物属性变化（Geyser / Pauldrons 依赖它）。
                    if (amount > 0) EmitPlayerAttribute(ctx, hero.Side, "Rage", "Gain", amount, depth, ch);
                    TryEnrage(ctx, hero, time, depth, ch);
                    return;
                }
                case "revive":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double amount = EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId);
                    RecordStat(ctx, effect, source, time, amount, 1);
                    ApplyHeal(ctx, hero.Side, amount, time, source.Id, source.CardId, ch, depth);
                    return;
                }
                case "destroy":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    foreach (InternalItem item in targets) DestroyItem(ctx, item, time, depth, ch);
                    RecordStat(ctx, effect, source, time, 1, targets.Count);
                    return;
                }
                case "disable":
                case "repair":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    bool disable = effect.Kind == "disable";
                    foreach (InternalItem item in targets)
                    {
                        if (item.Destroyed) continue;
                        item.Disabled = disable;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = effect.Kind, Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        TriggerPayload payload = new TriggerPayload { Event = disable ? "onCardDisabled" : "onCardRepaired", SourceSide = item.Side, SourceItemId = item.Id, ChainId = ch, Depth = depth };
                        ctx.TriggerQueue.Enqueue(payload);
                    }
                    RecordStat(ctx, effect, source, time, 1, targets.Count);
                    return;
                }
                case "forceUse":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    foreach (InternalItem item in targets)
                    {
                        if (item.Destroyed) continue;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "forceUse", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        BattleEngine.ForceUse(ctx, item, time, depth + 1);
                    }
                    RecordStat(ctx, effect, source, time, 1, targets.Count);
                    return;
                }
                case "flyToggle":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    foreach (InternalItem item in targets)
                    {
                        item.Flying = !item.Flying;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = item.Flying ? "fly" : "flyStop", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        EmitTrigger(ctx, item.Flying ? "onFly" : "onFlyStop", SourceOf(item), item.Side, depth, null, ch);
                        EmitCardAttribute(ctx, item, "Flying", item.Flying ? "Gain" : "Loss", 1, depth, ch);
                    }
                    RecordStat(ctx, effect, source, time, 1, targets.Count);
                    return;
                }
                case "addTags":
                case "addTagsRandom":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    List<string> tags = effect.Tags == null ? new List<string>() : new List<string>(effect.Tags);
                    if (effect.Kind == "addTagsRandom")
                    {
                        int count = Math.Max(1, (int)JsMath.Round(EvaluateNum(effect.Amount, ctx, source, ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId)));
                        List<string> picked = new List<string>();
                        List<string> pool = new List<string>(tags);
                        for (int i = 0; i < count && pool.Count > 0; i++)
                        {
                            int index = ctx.Rng.Int(pool.Count);
                            picked.Add(pool[index]);
                            pool.RemoveAt(index);
                        }
                        tags = picked;
                    }
                    foreach (InternalItem item in targets)
                    {
                        foreach (string tag in tags) ItemAttributes.AddTag(item, tag);
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "addTags", Value = tags.Count, ChainId = ch, Depth = depth, Note = string.Join(",", tags.ToArray()) };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                    }
                    RecordStat(ctx, effect, source, time, tags.Count, targets.Count);
                    return;
                }
                case "addTagsBySource":
                {
                    List<InternalItem> origins = ItemsForTarget(ctx, source, effect.SourceTarget, 1, triggerSourceItemId, triggerTargetItemId);
                    List<string> tags = new List<string>();
                    foreach (InternalItem origin in origins)
                    {
                        foreach (string tag in origin.Card.Tags) if (!tags.Contains(tag)) tags.Add(tag);
                        foreach (string tag in origin.ExtraTags) if (!tags.Contains(tag)) tags.Add(tag);
                    }
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, effect.Count ?? 1, triggerSourceItemId, triggerTargetItemId);
                    foreach (InternalItem item in targets)
                    {
                        foreach (string tag in tags) ItemAttributes.AddTag(item, tag);
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "addTags", Value = tags.Count, ChainId = ch, Depth = depth, Note = string.Join(",", tags.ToArray()) };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                    }
                    RecordStat(ctx, effect, source, time, tags.Count, targets.Count);
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

        // 返回实际变化量；冷却类属性直接作用于当前冷却，其余计入运行时属性。
        private static double ApplyAttributeChange(BattleContext ctx, InternalItem item, string attribute, string operation, double value, double time, double seconds)
        {
            double current = ItemAttributes.Get(item, attribute);
            if (item.AuraBaseAttributes != null && attribute != "FlatCooldownReduction" && attribute != "PercentCooldownReduction")
            {
                double baseline = NativeDynamicAuras.BeforeAura(item, attribute);
                double delta = Operate(baseline, value, operation) - baseline;
                if (seconds > 0) ItemAttributes.AddTimed(item, attribute, delta, time + seconds);
                else ItemAttributes.AddDelta(item, attribute, delta);
                NativeDynamicAuras.Refresh(ctx);
                return ItemAttributes.Get(item, attribute) - current;
            }
            double next = Operate(current, value, operation);
            // Keep the combat gain independently from the derived aura layer.
            // Otherwise the next refresh would erase a Multicast/CD/Crit gain.
            if (attribute != "FlatCooldownReduction" && attribute != "PercentCooldownReduction")
            {
                if (seconds > 0) ItemAttributes.AddTimed(item, attribute, next - current, time + seconds);
                else ItemAttributes.AddDelta(item, attribute, next - current);
            }
            switch (attribute)
            {
                case "CooldownMax":
                    next = Math.Max(0, next);
                    item.Cooldown = Math.Max(0, item.Cooldown + (next - current) / 1000.0);
                    item.CooldownMax = next / 1000.0;
                    return next - current;
                case "FlatCooldownReduction":
                {
                    double ms = operation == "Subtract" ? -value : value;
                    item.Cooldown = Math.Max(0, item.Cooldown - ms / 1000.0);
                    return ms;
                }
                case "PercentCooldownReduction":
                    item.Cooldown = Math.Max(0, item.Cooldown * (1 - value / 100.0));
                    return value;
                case "CooldownDisabled":
                    item.Disabled = next > 0;
                    return next;
                case "AmmoMax":
                    item.AmmoMax = next > 0 ? (int?)(int)JsMath.Round(next) : null;
                    item.Ammo = Math.Min(item.Ammo, item.AmmoMax ?? 0);
                    return next;
                case "Multicast":
                {
                    int multicast = (int)JsMath.Round(Math.Max(1, next));
                    item.MulticastBonus = Math.Max(0, multicast - 1);
                    return multicast;
                }
                case "CritChance":
                    item.CritBonus = Math.Max(0, item.CritBonus + (next - current) / 100.0);
                    return next - current;
                case "Lifesteal":
                case "LifestealPercentage":
                    item.Lifesteal = Math.Max(0, item.Lifesteal + (next - current) / 100.0);
                    return next - current;
                default:
                    return next - current;
            }
        }

        internal static double Operate(double before, double value, string operation)
        {
            return operation == "Add" ? before + value : operation == "Subtract" ? before - value :
                operation == "Multiply" ? before * value : value;
        }

        // 怒火达到上限时进入暴怒；持续时间为 EnragedDurationMs。
        internal static void TryEnrage(BattleContext ctx, InternalHero hero, double time, int depth, int? chainId)
        {
            if (hero.EnragedUntil > 0 || hero.RageMax <= 0 || hero.Rage < hero.RageMax) return;
            hero.Rage = 0;
            hero.EnragedUntil = time + Math.Max(0, hero.EnragedDurationMs) / 1000.0;
            TriggerPayload payload = new TriggerPayload { Event = "onPlayerEnraged", SourceSide = hero.Side, ChainId = chainId, Depth = depth };
            ctx.TriggerQueue.Enqueue(payload);
            BattleEvent ev = new BattleEvent { Time = time, Side = hero.Side, TargetSide = hero.Side, Kind = "enrage", Value = hero.EnragedDurationMs, ChainId = chainId ?? ctx.NextChainId++, Depth = depth };
            ctx.Events.Add(FinishEvent(ctx, ev));
        }

        private static void DestroyItem(BattleContext ctx, InternalItem item, double time, int depth, int chainId)
        {
            if (item.Destroyed) return;
            // 被摧毁卡自己的“摧毁前”被动先于移除结算。
            Triggers.FireOwn(ctx, item, "onBeforeDestroy", time, depth, chainId);
            item.Destroyed = true;
            item.Disabled = true;
            item.Cooldown = 0;
            BattleEvent ev = new BattleEvent { Time = time, Side = item.Side, SourceItemId = item.Id, SourceCardId = item.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "destroy", Value = 1, ChainId = chainId, Depth = depth };
            ctx.Events.Add(FinishEvent(ctx, ev));
            // “摧毁后”是观察事件，按帧延后到下一次排空。
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onCardDestroyed", SourceSide = item.Side, SourceItemId = item.Id, TargetItemId = item.Id, ChainId = chainId, Depth = depth });
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
