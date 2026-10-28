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
        // 原生 DamageCrit：加在基础暴击倍率上的百分点（回放：2x + DC/100）。
        public double DamageCrit;
        public double Lifesteal;
    }

    public sealed class DamageOptions
    {
        public bool IsDot;
        public string DotKind;
        // Native periodic packets retain health below zero until the matching
        // regeneration packet is added; otherwise overkill becomes free heal.
        public bool PreservePeriodicHealth;
        // 沙暴是环境伤害：不计入玩家输出、不触发 onDamageDealt/Taken、不吃减伤。
        public bool IsSandstorm;
        public int Depth;
        public int? ChainId;
        // DoT ticks are applied once, then split for attribution.
        public List<BattleEventShare> Shares;
    }

    public static class Effects
    {
        public static double EvaluateNum(
            NumExpr expr,
            BattleContext ctx,
            InternalItem source,
            InternalHero targetHero,
            string triggerSourceItemId = null,
            string triggerTargetItemId = null,
            InternalItem valueTarget = null)
        {
            if (expr == null) return 0;
            double value = EvaluateBase(expr, ctx, source, targetHero, triggerSourceItemId, triggerTargetItemId, valueTarget);
            if (expr.Modifier != null)
            {
                double other = EvaluateNum(expr.Modifier, ctx, source, targetHero, triggerSourceItemId, triggerTargetItemId, valueTarget);
                value = expr.ModifyMode == "Add" ? value + other :
                    expr.ModifyMode == "Subtract" ? value - other : value * other;
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
            string triggerTargetItemId,
            InternalItem valueTarget)
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
                    InternalItem item = ResolveCardAttributeItem(ctx, source, expr, triggerSourceItemId, triggerTargetItemId, valueTarget);
                    return item == null ? expr.DefaultValue : ReadCardAttribute(item, expr);
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
                        // 没有显式目标时，聚合作用于当前动作/光环目标（单卡）。
                        if (expr.CardTarget == null && expr.TargetKind == "actionTargetItem")
                            return valueTarget == null ? expr.DefaultValue : ReadCardAttribute(valueTarget, expr);
                        double sum = 0;
                        foreach (InternalItem item in items) sum += ReadCardAttribute(item, expr);
                        return sum;
                    }
                    if (!expr.Distinct)
                    {
                        int total = 0;
                        foreach (InternalItem item in items) total += item.Card.Tags.Count + item.ExtraTags.Count + item.AuraTags.Count;
                        return total;
                    }
                    HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (InternalItem item in items)
                    {
                        foreach (string tag in item.Card.Tags) unique.Add(tag);
                        foreach (string tag in item.ExtraTags) unique.Add(tag);
                        foreach (string tag in item.AuraTags) unique.Add(tag);
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

        private static double ReadCardAttribute(InternalItem item, NumExpr expr)
        {
            return expr.Unscaled
                ? ItemAttributes.Unscaled(item, expr.Attribute, expr.DefaultValue)
                : ItemAttributes.GetOrDefault(item, expr.Attribute, expr.DefaultValue);
        }

        private static InternalItem ResolveCardAttributeItem(
            BattleContext ctx,
            InternalItem source,
            NumExpr expr,
            string triggerSourceItemId,
            string triggerTargetItemId,
            InternalItem valueTarget)
        {
            // 原生 Target:null 表示当前动作的作用对象；光环在逐目标求值时传入。
            if (expr.TargetKind == "actionTargetItem") return valueTarget;
            if (expr.CardTarget != null)
            {
                List<InternalItem> items = NativeItemsForTarget(ctx, source, expr.CardTarget, int.MaxValue, triggerSourceItemId, triggerTargetItemId);
                // 非聚合引用读取原生 targets[0]；不叠加整组属性。
                return items.Count > 0 ? items[0] : null;
            }
            return ResolveNumSource(ctx, source, expr.TargetKind, triggerSourceItemId, triggerTargetItemId);
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
            Target target = expr.CardTarget ?? new Target { Kind = expr.TargetKind ?? "allSelfItems",
                NativeCondition = expr.Condition, ExcludeSelf = expr.Exclude };
            // 数值引用没有独立的 TargetCount：全体、左右侧和最近邻都保留
            // 完整目标集；只有最左/最右这种本身就只选一张的目标取 1。
            bool single = target.Kind == "leftMostSelf" || target.Kind == "rightMostSelf" ||
                target.Kind == "leftMostEnemy" || target.Kind == "rightMostEnemy" ||
                target.Kind == "leftMostPlayer" || target.Kind == "rightMostPlayer" ||
                target.Kind == "leftMostOpponent" || target.Kind == "rightMostOpponent";
            return NativeItemsForTarget(ctx, source, target, single ? 1 : int.MaxValue, triggerSourceItemId, triggerTargetItemId);
        }

        public static double ApplyHeal(
            BattleContext ctx,
            Side side,
            double amount,
            double time,
            string sourceItemId,
            string sourceCardId,
            int? chainId,
            int depth,
            List<BattleEventShare> shares = null,
            bool cleanseStatuses = false,
            string recoveryKind = null)
        {
            InternalHero hero = ctx.Hero(side);
            double before = hero.Hp;
            double requested = Math.Max(0, JsMath.Round(amount));
            hero.Hp = Math.Min(hero.MaxHp, hero.Hp + requested);
            double healed = hero.Hp - before;
            double overheal = Math.Max(0, requested - healed);
            ctx.Totals.For(side).Heal += healed;
            ctx.Totals.For(side).NominalHeal += requested;

            if (ctx.Config.NativeMechanics && cleanseStatuses && requested > 0)
            {
                // Native healing cleanses 5% of the nominal heal (at least 1),
                // even at full HP. Round each resulting integer attribute,
                // rather than the removed amount: heal 50 takes Poison 5->2
                // and Burn 10->8 in replay d7d560e5 frame 81.
                double cleanse = Math.Max(1, requested * 0.05);
                double burnBefore = hero.Burn;
                double poisonBefore = hero.Poison;
                hero.Burn = Math.Max(0, Math.Round(hero.Burn - cleanse));
                hero.Poison = Math.Max(0, Math.Round(hero.Poison - cleanse));
                StatusPools.Consume(hero, "burn", burnBefore - hero.Burn);
                StatusPools.Consume(hero, "poison", poisonBefore - hero.Poison);
                if (burnBefore > hero.Burn) EmitPlayerAttribute(ctx, side, "Burn", "Loss", burnBefore - hero.Burn, depth, chainId);
                if (poisonBefore > hero.Poison) EmitPlayerAttribute(ctx, side, "Poison", "Loss", poisonBefore - hero.Poison, depth, chainId);
            }

            DamageSource source = new DamageSource();
            source.Id = sourceItemId ?? "heal";
            source.CardId = sourceCardId ?? "heal";
            source.Side = side;
            if (!ctx.Config.NativeMechanics || recoveryKind == null)
            {
                EmitTrigger(ctx, "onHeal", source, side, depth, null, chainId);
                if (overheal > 1e-9) EmitTrigger(ctx, "onOverHeal", source, side, depth, null, chainId);
            }
            if (healed > 0) EmitPlayerAttribute(ctx, side, "Health", "Gain", healed, depth, chainId);

            if (shares != null && shares.Count > 0)
            {
                // Healing is capped by the target's Max-HP as one pool; split
                // the accepted result so a multi-source Regen tick stays
                // attributable without changing the actual heal.
                double weight = 0;
                foreach (BattleEventShare share in shares) weight += Math.Max(0, share.Amount);
                int residual = shares.Count - 1;
                while (residual > 0 && shares[residual].Amount <= 0) residual--;
                double allocated = 0;
                for (int index = 0; index < shares.Count; index++)
                {
                    BattleEventShare share = shares[index];
                    double accepted = index == residual
                        ? Math.Max(0, healed - allocated)
                        : weight > 0 ? healed * Math.Max(0, share.Amount) / weight : 0;
                    if (index != residual) accepted = Math.Min(accepted, Math.Max(0, share.Amount));
                    allocated += accepted;
                    share.ActualAmount = accepted;
                    share.Overheal = Math.Max(0, share.Amount - accepted);
                }
            }

            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = side;
            ev.TargetSide = side;
            ev.SourceItemId = sourceItemId;
            ev.SourceCardId = sourceCardId;
            ev.Kind = "heal";
            ev.Value = healed;
            ev.NominalAmount = requested;
            ev.Overheal = overheal;
            ev.Note = ctx.Config.NativeMechanics ? recoveryKind : null;
            ev.HpAfter = hero.Hp;
            ev.Shares = shares;
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

        // 原生帧日志（0276c46b 帧 58-60）：Light Drone“每 3 次使用，护盾 40”的前置
        // 条件未满足时护盾不结算，但观察者仍收到 CardPerformedShield——Sharp
        // Corners 在它的每次使用后都 +6 伤害，而实际护盾只在第 3 次生效。
        // 前置条件只拦截数值，不拦截“已执行护盾”的观察事件。
        internal static void EmitPerformedShield(BattleContext ctx, InternalItem item, int depth, int? chainId)
        {
            if (item == null || ctx.IsTrainingDummySource(item.Side)) return;
            EmitTrigger(ctx, "onShield", SourceOf(item), item.Side, depth, null, chainId);
        }

        private static void EmitTrigger(
            BattleContext ctx,
            string eventName,
            DamageSource source,
            Side? targetSide,
            int depth,
            string targetItemId,
            int? chainId,
            string actorItemId = null,
            Side? actorSide = null)
        {
            // 终局属性提交不再向观察者链式派发事件。
            if (ctx.TerminalAttributesOnly) return;
            TriggerPayload payload = new TriggerPayload();
            payload.Event = eventName;
            payload.SourceSide = source.Side;
            payload.SourceItemId = source.Id;
            payload.TargetSide = targetSide;
            payload.TargetItemId = targetItemId;
            payload.ActorItemId = actorItemId;
            payload.ActorSide = actorSide;
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
        internal static void EmitCardAttribute(BattleContext ctx, InternalItem item, string attribute, string changeType, double amount, int depth, int? chainId)
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

        // 原生同一（充能者,目标）的 CardCharge 至少间隔 250ms（5 帧）：
        // 12,930 次相邻充能间隔恰为 5 帧，1-4 帧间隔为 0。
        internal const double ChargeSpacing = 0.25;

        // 结算窗口检查：允许立即结算时记录时刻并返回 true。
        private static bool TryReserveCharge(BattleContext ctx, InternalItem source, InternalItem target, double time)
        {
            string key = source.Id + "|" + target.Id;
            double last;
            if (ctx.LastChargeAt.TryGetValue(key, out last) && time - last < ChargeSpacing - 1e-9) return false;
            ctx.LastChargeAt[key] = time;
            return true;
        }

        private static void ApplyCharge(BattleContext ctx, InternalItem source, InternalItem target, double seconds, double time, int depth, int? chainId)
        {
            // 只有“已就绪且本帧确实会出手”的卡才把充能挂起到重置后的新冷却上：
            // 冻结/延迟就绪期间到达的充能原生会被丢弃（07ae8be8 帧203 Harmadillo
            // 在冻结中就绪，充能 1000ms 未结转到帧226 的新冷却；170666bf 帧161
            // 是就绪同帧出手，充能结转到新冷却 5000→4000）。
            bool actsThisFrame = target.FrozenUntil < time - 1e-9 && time >= target.NativeReadyAfter - 1e-9 &&
                (!target.AmmoMax.HasValue || target.Ammo > 0);
            if (ctx.Config.NativeMechanics && target.Cooldown <= 1e-9 && !target.Disabled && !target.Destroyed &&
                !target.Card.Passive && target.CooldownMax > 0 && actsThisFrame &&
                Math.Abs(target.LastReloadFromEmptyAt - time) > 1e-9)
            {
                // 已就绪、本帧将重置：充能挂起到重置后的新冷却上，不浪费。
                target.PendingChargeOnReset += seconds;
            }
            else target.Cooldown = Math.Max(0, target.Cooldown - seconds);
            // 充能的对象是被充能的卡，被动绑定它的“被充能”事件。
            EmitTrigger(ctx, "onCharge", SourceOf(target), target.Side, depth, null, chainId);
        }

        // 延迟充能到期结算：串行窗口再次校验后应用并补发 charge 事件。
        internal static void SettleDeferredCharge(BattleContext ctx, DeferredCharge charge, double time)
        {
            if (!TryReserveCharge(ctx, charge.Charger, charge.Target, time)) return;
            ApplyCharge(ctx, charge.Charger, charge.Target, charge.Seconds, time, charge.Depth, charge.ChainId);
            BattleEngine.MarkNativeReady(ctx, charge.Target, time);
            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = charge.Charger.Side;
            ev.TargetSide = charge.Target.Side;
            ev.SourceItemId = charge.Charger.Id;
            ev.TargetItemId = charge.Target.Id;
            ev.Kind = "charge";
            ev.Value = charge.Seconds;
            ev.ChainId = charge.ChainId.HasValue ? charge.ChainId.Value : 0;
            ev.Depth = charge.Depth;
            ctx.Events.Add(FinishEvent(ctx, ev));
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

            double heroCrit = ctx.Config.NativeMechanics && source.Side != targetSide
                ? ctx.Hero(source.Side).AuraCritChance / 100.0 : 0;
            // 先抽签再取先知：磁带命中与否都不跳过 RNG 抽取，保证共享随机数
            // 流的位置只由“发生了哪些判定”决定，不被磁带消费移位。
            bool rollCritical = !opts.IsDot && ctx.Rng.Chance(source.CritBonus + heroCrit);
            bool? replayCritical = !opts.IsDot && ctx.ReplayRandom != null
                ? ctx.ReplayRandom.ChooseCritical(source.Id, "damage", time, targetSide, source.CritBonus + heroCrit) : null;
            if (!opts.IsDot && (replayCritical.HasValue ? replayCritical.Value : rollCritical))
            {
                critical = true;
                // 回放证据：DC=0 为 2×，DC=100 为 3×；DamageCrit 是加在
                // 基础暴击倍率上的百分点，不是再乘一倍。治疗/护盾不加 DC。
                double critMultiplier = ctx.Rules.CritMultiplier + Math.Max(0, source.DamageCrit) / 100.0;
                amount = JsMath.Round(amount * critMultiplier);
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
            // 动态人物光环的百分比减伤：与 MaxHp 一样是派生层，只影响
            // 来袭伤害，不改变输出方的理论伤害记录。
            double damageReduction = hero.AuraPercentDamageReduction;
            if (time <= hero.ActionPercentDamageReductionUntil) damageReduction += hero.ActionPercentDamageReduction;
            if (!opts.IsSandstorm && ctx.Config.NativeMechanics && source.Side != targetSide && damageReduction > 0)
                amount = JsMath.Round(amount * (1 - Math.Min(100, damageReduction) / 100.0));
            double absorbed;
            double healthDamage;
            if (opts.IsSandstorm)
            {
                // 沙暴先消耗护盾，剩余进生命；不受 DoT 穿盾规则影响。
                absorbed = Math.Min(hero.Shield, amount);
                healthDamage = amount - absorbed;
            }
            else if (ctx.Config.NativeMechanics && opts.IsDot && opts.DotKind == "poison")
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
            bool infiniteDummy = ctx.IsTrainingDummySource(targetSide);
            double hpLoss = infiniteDummy ? healthDamage : Math.Min(Math.Max(0, hero.Hp), healthDamage);
            // An infinite practice target accepts the whole packet. Its display
            // HP can reach the floor without turning later output into overkill.
            hero.Hp = infiniteDummy ? Math.Max(1, hero.Hp - hpLoss) :
                hero.Hp - (opts.PreservePeriodicHealth ? healthDamage : hpLoss);
            double overkill = Math.Max(0, healthDamage - hpLoss);
            // Burn/Shield prevention is neither HP loss nor Shield consumption;
            // keeping it explicit makes Damage = Output + Overkill + Prevented.
            double prevented = Math.Max(0, amount - hpLoss - absorbed - overkill);

            List<BattleEventShare> shares = opts.Shares;
            if (shares != null && shares.Count > 0)
            {
                SplitDamageShares(shares, hpLoss, absorbed, overkill, prevented);
                foreach (BattleEventShare share in shares)
                {
                    if (share.Side == targetSide) continue;
                    TotalsSide totals = ctx.Totals.For(share.Side);
                    totals.Damage += share.Amount;
                    totals.EffectiveDamage += Math.Max(0, share.ActualAmount - share.Absorbed);
                    totals.ShieldAbsorbed += share.Absorbed;
                    totals.Overkill += share.Overkill;
                    totals.Prevented += share.Prevented;
                }
            }
            else if (source.Side != targetSide)
            {
                TotalsSide totals = ctx.Totals.For(source.Side);
                totals.Damage += amount;
                totals.EffectiveDamage += hpLoss;
                totals.ShieldAbsorbed += absorbed;
                totals.Overkill += overkill;
                totals.Prevented += prevented;
            }

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
            ev.Overkill = overkill;
            ev.Prevented = prevented;
            ev.HpAfter = hero.Hp;
            ev.ShieldAfter = hero.Shield;
            ev.Shares = shares;
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ev.Note = opts.IsDot ? opts.DotKind : (critical ? "critical" : null);
            ctx.Events.Add(FinishEvent(ctx, ev));

            if (amount > 0 && !opts.IsSandstorm)
            {
                if (absorbed > 0) EmitPlayerAttribute(ctx, targetSide, "Shield", "Loss", absorbed, depth, chainId);
                if (hpLoss > 0) EmitPlayerAttribute(ctx, targetSide, "Health", "Loss", hpLoss, depth, chainId);
                if (shares != null && shares.Count > 0)
                {
                    foreach (BattleEventShare share in shares)
                    {
                        if (share.Amount <= 0) continue;
                        DamageSource shareSource = new DamageSource();
                        shareSource.Id = share.SourceItemId;
                        shareSource.CardId = share.SourceCardId;
                        shareSource.Side = share.Side;
                        EmitTrigger(ctx, "onDamageDealt", shareSource, targetSide, depth, null, chainId);
                    }
                }
                else EmitTrigger(ctx, "onDamageDealt", source, targetSide, depth, null, chainId);

                TriggerPayload taken = new TriggerPayload();
                taken.Event = "onDamageTaken";
                taken.SourceSide = targetSide;
                taken.SourceItemId = source.Id;
                taken.TargetSide = targetSide;
                taken.ChainId = chainId;
                taken.Depth = depth;
                ctx.TriggerQueue.Enqueue(taken);

                if (critical) PublishCrit(ctx, source, targetSide, time, depth, chainId);
            }

            // Self-damage must never feed Lifesteal or cross-side output totals.
            double healBasis = ctx.Rules.LifestealOnShieldDamage ? hpLoss + absorbed : hpLoss;
            if (source.Lifesteal > 0 && healBasis > 0 && !opts.IsDot && source.Side != targetSide)
            {
                ApplyHeal(
                    ctx,
                    source.Side,
                    healBasis * source.Lifesteal,
                    time,
                    source.Id,
                    source.CardId,
                    chainId,
                    depth,
                    null,
                    false,
                    "lifesteal");
            }
        }

        private static void SplitDamageShares(
            List<BattleEventShare> shares,
            double hpLoss,
            double absorbed,
            double overkill,
            double prevented)
        {
            double weight = 0;
            foreach (BattleEventShare share in shares) weight += Math.Max(0, share.Amount);
            int residual = shares.Count - 1;
            while (residual > 0 && shares[residual].Amount <= 0) residual--;
            double allocatedHp = 0, allocatedAbsorbed = 0, allocatedOverkill = 0, allocatedPrevented = 0;
            for (int index = 0; index < shares.Count; index++)
            {
                BattleEventShare share = shares[index];
                double part = weight > 0 ? Math.Max(0, share.Amount) / weight : index == 0 ? 1 : 0;
                bool last = index == residual;
                // Keep the aggregate exactly conserved; a zero-weight trailing
                // share must never absorb the rounding residual.
                double hpPart = last ? hpLoss - allocatedHp : hpLoss * part;
                double absorbedPart = last ? absorbed - allocatedAbsorbed : absorbed * part;
                share.Absorbed = absorbedPart;
                share.ActualAmount = hpPart + absorbedPart;
                share.Overkill = last ? overkill - allocatedOverkill : overkill * part;
                share.Prevented = last ? prevented - allocatedPrevented : prevented * part;
                allocatedHp += hpPart;
                allocatedAbsorbed += absorbedPart;
                allocatedOverkill += share.Overkill;
                allocatedPrevented += share.Prevented;
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

        private static bool IsHeroEffect(string kind)
        {
            return kind == "damage" || kind == "heal" || kind == "shield" || kind == "burn" ||
                kind == "poison" || kind == "regen" || kind == "rage" || kind == "playerAttribute";
        }

        private static Effect CloneWithTarget(Effect source, string targetKind)
        {
            return new Effect
            {
                Kind = source.Kind, Attribute = source.Attribute, Operation = source.Operation,
                NativeAbilityId = source.NativeAbilityId,
                Amount = source.Amount, AmountValue = source.AmountValue, Seconds = source.Seconds,
                Count = source.Count, CountValue = source.CountValue, Chance = source.Chance,
                Percent = source.Percent, Crit = source.Crit, Tags = source.Tags,
                Target = new Target { Kind = targetKind }, Effects = source.Effects,
                FromEnchantment = source.FromEnchantment, PreventOverride = source.PreventOverride,
                EnchantmentNames = source.EnchantmentNames, EnchantmentWeights = source.EnchantmentWeights,
                DurationValue = source.DurationValue, Prerequisites = source.Prerequisites
            };
        }

        private static List<InternalItem> ItemsForTarget(
            BattleContext ctx,
            InternalItem source,
            Target target,
            int count,
            string triggerSourceItemId,
            string triggerTargetItemId = null,
            bool includeDisabled = false)
        {
            List<InternalItem> picked = ItemsForTargetCore(ctx, source, target, count, triggerSourceItemId,
                triggerTargetItemId, includeDisabled);
            if (!ctx.Config.NativeMechanics || includeDisabled || target == null) return picked;
            // 原生禁用的卡等同离场，不再作为常规动作目标（光环层 CanReceiveAura 同规则）：
            // 07f044ee 帧 141 Oblivion Vortex 的充能跳过刚被禁用的 The Big One。
            // 显式指定实例的目标与“修复/强制使用”链路仍然保留（Drone Crusher）。
            if (ExplicitInstanceTarget(target)) return picked;
            picked.RemoveAll(delegate(InternalItem item) { return item.Disabled; });
            return picked;
        }

        private static bool ExplicitInstanceTarget(Target target)
        {
            string kind = target.Kind;
            return kind == "triggerSourceItem" || kind == "triggerTargetItem" || kind == "occupyingItem" ||
                kind == "selfItem" || (kind == "enemyItem" && target.ItemId != null);
        }

        private static List<InternalItem> ItemsForTargetCore(
            BattleContext ctx,
            InternalItem source,
            Target target,
            int count,
            string triggerSourceItemId,
            string triggerTargetItemId = null,
            bool includeDisabled = false)
        {
            if (count <= 0) return new List<InternalItem>();
            if (target != null && target.PositionMode != null)
                return PositionalTargets(ctx, source, target, count, triggerSourceItemId);
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
            List<InternalItem> allItems = new List<InternalItem>(selfItems);
            allItems.AddRange(enemyItems);
            List<InternalItem> playerItems = allItems.FindAll(delegate(InternalItem item) { return item.Side == Side.Player; });
            List<InternalItem> opponentItems = allItems.FindAll(delegate(InternalItem item) { return item.Side == Side.Enemy; });
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
                case "allHandsItems":
                    list = allItems;
                    break;
                case "allPlayerItems":
                    list = playerItems;
                    break;
                case "allOpponentItems":
                    list = opponentItems;
                    break;
                case "occupyingItem":
                    if (NativeSkillSupport.IsSocketEffect(source) && source.Slot >= 0)
                        foreach (InternalItem item in selfItems)
                            if (item.Slot == source.Slot && item.Id != source.Id) list.Add(item);
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
                case "randomHandsItem":
                    list = Sample(ctx, allItems, count);
                    break;
                case "randomPlayerItem":
                    list = Sample(ctx, playerItems, count);
                    break;
                case "randomOpponentItem":
                    list = Sample(ctx, opponentItems, count);
                    break;
                case "triggerSourceLeft":
                case "triggerSourceRight":
                case "triggerSourceAdjacent":
                    AddTriggerSourceTargets(ctx, triggerSourceItemId, kind, list);
                    break;
                case "randomTriggerSourceAdjacent":
                    AddTriggerSourceTargets(ctx, triggerSourceItemId, "triggerSourceAdjacent", list);
                    list = Sample(ctx, list, count);
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
                case "leftMostPlayer":
                    list = TakeMost(ctx, playerItems, false, count, target, source.Id);
                    break;
                case "rightMostPlayer":
                    list = TakeMost(ctx, playerItems, true, count, target, source.Id);
                    break;
                case "leftMostOpponent":
                    list = TakeMost(ctx, opponentItems, false, count, target, source.Id);
                    break;
                case "rightMostOpponent":
                    list = TakeMost(ctx, opponentItems, true, count, target, source.Id);
                    break;
                case "lowestHpEnemyItem":
                    if (enemyItems.Count > 0) list.Add(enemyItems[0]);
                    break;
                default:
                    break;
            }

            if (kind == "allSelfItems" || kind == "allEnemyItems" || kind == "allHandsItems" ||
                kind == "allPlayerItems" || kind == "allOpponentItems" || kind == "adjacentSelf")
                return list;

            int take = Math.Max(1, count);
            return list.Count > take ? list.GetRange(0, take) : list;
        }

        internal static List<InternalItem> NativeItemsForTarget(BattleContext ctx, InternalItem source, Target target, int count, string triggerSourceItemId, string triggerTargetItemId = null)
        {
            if (count <= 0) return new List<InternalItem>();
            if (target != null && target.PositionMode != null)
                return PositionalTargets(ctx, source, target, count, triggerSourceItemId);
            List<InternalItem> own = new List<InternalItem>();
            List<InternalItem> opponents = new List<InternalItem>();
            // 回放：TransformDestroyed 产生的新实例在出生帧即可被同一事件的
            // 观察者（FlyingStart/Charge 等）选中；它们在帧边界前仍在 PendingItems。
            List<InternalItem> candidates = new List<InternalItem>(ctx.Items);
            candidates.AddRange(ctx.PendingItems);
            foreach (InternalItem item in candidates)
                if (item.Destroyed || NativeSkillSupport.IsSocketEffect(item)) continue;
                else if (item.Side == source.Side) own.Add(item); else opponents.Add(item);
            own.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            opponents.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            List<InternalItem> all = new List<InternalItem>(own);
            all.AddRange(opponents);
            List<InternalItem> players = all.FindAll(delegate(InternalItem item) { return item.Side == Side.Player; });
            List<InternalItem> absoluteOpponents = all.FindAll(delegate(InternalItem item) { return item.Side == Side.Enemy; });
            players.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            absoluteOpponents.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
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
                case "allHandsItems": case "randomHandsItem": selected = all; break;
                case "allPlayerItems": case "randomPlayerItem": selected = players; break;
                case "allOpponentItems": case "randomOpponentItem": selected = absoluteOpponents; break;
                case "occupyingItem":
                    if (NativeSkillSupport.IsSocketEffect(source) && source.Slot >= 0)
                        foreach (InternalItem item in own)
                            if (item.Slot == source.Slot && item.Id != source.Id) selected.Add(item);
                    break;
                case "adjacentSelf": case "randomAdjacentSelf": selected = adjacent; break;
                case "triggerSourceLeft":
                case "triggerSourceRight":
                case "triggerSourceAdjacent":
                case "randomTriggerSourceAdjacent":
                    AddTriggerSourceTargets(ctx, triggerSourceItemId,
                        kind == "randomTriggerSourceAdjacent" ? "triggerSourceAdjacent" : kind, selected);
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
                case "leftMostPlayer":
                    selected = TakeMost(ctx, players, false, count, target, source.Id);
                    break;
                case "rightMostPlayer":
                    selected = TakeMost(ctx, players, true, count, target, source.Id);
                    break;
                case "leftMostOpponent":
                    selected = TakeMost(ctx, absoluteOpponents, false, count, target, source.Id);
                    break;
                case "rightMostOpponent":
                    selected = TakeMost(ctx, absoluteOpponents, true, count, target, source.Id);
                    break;
                case "leftSelf": if (index > 0) selected.Add(own[index - 1]); break;
                case "rightSelf": if (index >= 0 && index + 1 < own.Count) selected.Add(own[index + 1]); break;
            }
            if (target != null)
                selected.RemoveAll(delegate(InternalItem item)
                {
                    return item.Destroyed || (target.ExcludeSelf && item.Id == source.Id) ||
                        (target.FlyingOnly && !item.Flying) ||
                        (target.NotFlyingOnly && item.Flying) ||
                        (target.HasCooldownOnly && item.CooldownMax <= 0) ||
                        (target.AdjacentOnly && !adjacent.Contains(item)) ||
                        !NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx, triggerSourceItemId);
                });
            if (kind == "randomSelfItem" || kind == "randomEnemyItem" || kind == "randomHandsItem" ||
                kind == "randomPlayerItem" || kind == "randomOpponentItem" ||
                kind == "randomAdjacentSelf" || kind == "randomTriggerSourceAdjacent")
                return Sample(ctx, selected, count);
            int take = Math.Max(0, count);
            return selected.Count > take ? selected.GetRange(0, take) : selected;
        }

        private static List<InternalItem> PositionalTargets(BattleContext ctx, InternalItem source, Target target, int count, string triggerSourceItemId)
        {
            InternalItem origin = target.PositionOrigin == "TriggerSource" ? FindItem(ctx, triggerSourceItemId) : source;
            List<InternalItem> result = new List<InternalItem>();
            List<InternalItem> candidates = new List<InternalItem>(ctx.Items);
            candidates.AddRange(ctx.PendingItems);
            foreach (InternalItem item in candidates)
            {
                if (!NativeSkillSupport.PositionalMatch(ctx, origin, item, target.PositionMode, target.IncludeOrigin)) continue;
                if (target.ExcludeSelf && item.Id == source.Id || target.FlyingOnly && !item.Flying ||
                    target.NotFlyingOnly && item.Flying || target.HasCooldownOnly && item.CooldownMax <= 0) continue;
                if (!NativeSkillSupport.CardMatches(item, target.NativeCondition, ctx, triggerSourceItemId)) continue;
                result.Add(item);
            }
            result.Sort(delegate(InternalItem a, InternalItem b) { return a.Slot.CompareTo(b.Slot); });
            if (target.PositionMode == "Neighbor" && count < result.Count) return Sample(ctx, result, count);
            return result.Count > count ? result.GetRange(0, count) : result;
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
            if (pool.Count == 0) return new List<InternalItem>();
            // 先按普通抽取消耗随机数流，再让磁带覆盖结果：磁带命中与否都不
            // 改变流位置，磁带化的决策与未磁带化的判定（暴击等）互不干扰。
            List<InternalItem> copy = new List<InternalItem>(pool);
            List<InternalItem> drawn = new List<InternalItem>();
            int n = Math.Min(Math.Max(1, count), copy.Count);
            for (int i = 0; i < n; i++)
            {
                int index = ctx.Rng.Int(copy.Count);
                drawn.Add(copy[index]);
                copy.RemoveAt(index);
            }
            if (ctx.ReplayRandom != null)
            {
                List<InternalItem> recorded = ctx.ReplayRandom.ChooseTargets(pool, count);
                if (recorded != null) return recorded;
            }
            return drawn;
        }

        // 原生 TTriggerOnCardCritted 在同一张卡的同一次结算里只派发一次。
        // b61c28ab 帧 86/91：Scalpel 的直伤与燃烧/中毒同时暴击，原生该帧
        // 只有一条 +15 CritChance 更新；逐效果派发会重复两次并留下延迟队列。
        private static void PublishCrit(
            BattleContext ctx,
            DamageSource source,
            Side targetSide,
            double time,
            int depth,
            int? chainId)
        {
            if (source == null) return;
            int frame = (int)Math.Round(time / 0.05);
            if (!ctx.CritTriggerFrames.Add(source.Id + "|" + frame)) return;
            EmitTrigger(ctx, "onCrit", source, targetSide, depth, null, chainId);
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
            double heroCrit = ctx.Hero(source.Side) == null ? 0 : ctx.Hero(source.Side).AuraCritChance / 100.0;
            string replayKind = ctx.ReplayRandom != null && ctx.ReplayRandom.Scope != null
                ? ctx.ReplayRandom.Scope.Kind : "unknown";
            bool rollCritical = ctx.Rng.Chance(source.CritBonus + heroCrit);
            bool? recordedCritical = ctx.ReplayRandom == null ? null :
                ctx.ReplayRandom.ChooseCritical(source.Id, replayKind, time, targetSide, source.CritBonus + heroCrit);
            if (!(recordedCritical.HasValue ? recordedCritical.Value : rollCritical)) return amount;
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
            // Damage publishes this in ApplyDamage. Non-damage critical
            // actions must notify the same native card observers as well
            // (Ice 9000 poison crit -> Critical Core charge, b162 frame 121).
            if (ctx.Config.NativeMechanics)
                PublishCrit(ctx, SourceOf(source), targetSide, time, depth, chainId);
            return next;
        }

        // 沙暴开始是全场一次性事件。卡牌动作和 30s 默认沙暴共用此入口；
        // cardAction=true 时额外产出原生 CardBeginSandstorm 对应的 beginSandstorm 事件。
        public static bool StartSandstorm(
            BattleContext ctx,
            InternalItem source,
            double time,
            int depth,
            int? chainId,
            bool cardAction)
        {
            if (ctx.SandstormStarted) return false;
            int ch = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ctx.SandstormStarted = true;
            ctx.SandstormStartedAt = time;
            ctx.SandstormTickIndex = 0;
            ctx.NextSandstormTickAt = time;
            ctx.LastSandstormTickAt = -1;
            if (cardAction && source != null)
            {
                BattleEvent ev = new BattleEvent();
                ev.Time = time;
                ev.Side = source.Side;
                ev.SourceItemId = source.Id;
                ev.SourceCardId = source.CardId;
                ev.Kind = "beginSandstorm";
                ev.Value = 0;
                ev.ChainId = ch;
                ev.Depth = depth;
                ctx.Events.Add(FinishEvent(ctx, ev));
            }
            TriggerPayload payload = new TriggerPayload
            {
                Event = "onSandstorm",
                SourceSide = source != null ? source.Side : Side.Player,
                SourceItemId = source != null ? source.Id : null,
                ChainId = ch,
                Depth = depth,
                Global = true
            };
            ctx.TriggerQueue.Enqueue(payload);
            return true;
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
            // Blank targets still publish use events for player reactions, but
            // cannot acquire damage or other source abilities through transforms.
            if (source != null && ctx.IsTrainingDummySource(source.Side)) return;
            // 终局快照模式只允许确定性属性反应落地，其余效果不产生副作用。
            if (ctx.TerminalAttributesOnly && !NativeTerminalAttributes.IsTerminalAttributeEffect(effect)) return;
            if (time > ctx.Now) ctx.Now = time;
            NativeDynamicAuras.Refresh(ctx);
            ReplayRandomScope previousScope = ctx.ReplayRandom == null ? null : ctx.ReplayRandom.Scope;
            if (ctx.ReplayRandom != null && source != null && effect != null)
                ctx.ReplayRandom.Scope = new ReplayRandomScope {
                    Frame = (int)Math.Round(time / 0.05), Source = source.Id, Kind = effect.Kind,
                    Ability = effect.NativeAbilityId ?? (previousScope == null ? null : previousScope.Ability)
                };
            if (!TrySpendActionCost(ctx, effect, source, time, depth, chainId)) return;
            try { ResolveEffectCore(ctx, effect, source, time, depth, chainId, triggerSourceItemId, triggerTargetItemId); }
            finally
            {
                if (ctx.ReplayRandom != null) ctx.ReplayRandom.Scope = previousScope;
                NativeDynamicAuras.Refresh(ctx);
                // Immediate observers of performed actions run on this frame;
                // their non-Immediate matches retain next-frame execution.
                if (ctx.Config.NativeMechanics && !ctx.Ended && !ctx.TerminalAttributesOnly)
                {
                    if (ctx.Player.Hp > 0 && ctx.Enemy.Hp > 0)
                        Triggers.DrainImmediateActions(ctx, time);
                    else
                        Triggers.DrainImmediateCriticals(ctx, time);
                }
            }
        }

        // 行动代价在能力结算前一次性扣除；代价不足时该能力整段不执行
        // （原生 Tuba：Tempo 只够第一段时后续分段不产生 Slow，也不发
        // CardActionCostSpent）。事件本身按原生字段导出为 actionCost。
        private static bool TrySpendActionCost(BattleContext ctx, Effect effect, InternalItem source,
            double time, int depth, int? chainId)
        {
            if (effect == null || string.IsNullOrEmpty(effect.CostAttribute) || source == null) return true;
            InternalHero hero = ctx.Hero(source.Side);
            double cost = Math.Max(0, JsMath.Round(EvaluateNum(effect.CostAmount, ctx, source, hero, null, null)));
            if (hero.Tempo + 1e-9 < cost) return false;
            hero.Tempo = Math.Max(0, hero.Tempo - cost);
            BattleEvent ev = new BattleEvent();
            ev.Time = time;
            ev.Side = source.Side;
            ev.TargetSide = source.Side;
            ev.SourceItemId = source.Id;
            ev.SourceCardId = source.CardId;
            ev.TargetItemId = source.Id;
            ev.Kind = "actionCost";
            ev.Value = cost;
            ev.Note = effect.CostAttribute;
            ev.ChainId = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            ev.Depth = depth;
            ctx.Events.Add(FinishEvent(ctx, ev));
            return true;
        }

        private static void ResolveEffectCore(
            BattleContext ctx, Effect effect, InternalItem source, double time,
            int depth, int? chainId, string triggerSourceItemId, string triggerTargetItemId)
        {
            if (effect == null || string.IsNullOrEmpty(effect.Kind)) return;
            int ch = chainId.HasValue ? chainId.Value : ctx.NextChainId++;
            bool allTargets = effect.Target != null &&
                (effect.Target.Kind == "allSelfItems" || effect.Target.Kind == "allEnemyItems" ||
                 effect.Target.Kind == "allHandsItems" || effect.Target.Kind == "adjacentSelf");
            int targetCount = effect.CountValue == null ? effect.Count ?? (allTargets ? int.MaxValue : 1) :
                (int)Math.Min(int.MaxValue, Math.Max(0, EvaluateNum(effect.CountValue, ctx, source,
                    ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId)));

            if (effect.Target != null && effect.Target.Kind == "bothHeroes" && IsHeroEffect(effect.Kind))
            {
                ResolveEffectCore(ctx, CloneWithTarget(effect, "selfHero"), source, time, depth, chainId, triggerSourceItemId, triggerTargetItemId);
                ResolveEffectCore(ctx, CloneWithTarget(effect, "enemyHero"), source, time, depth, chainId, triggerSourceItemId, triggerTargetItemId);
                return;
            }

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
                    ApplyHeal(ctx, hero.Side, crit, time, source.Id, source.CardId, ch, depth, null, true);
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
                    // 暴击作用于本次叠层而非后续 tick。Regen 同样可暴击：
                    // 0624335c 帧 131 Stove 的 RegenApplyAmount=4，原生
                    // HealthRegen 从 36 增至 44，只有这一条再生施加动作。
                    amount = CriticalAmount(ctx, source, amount, time, depth, ch, hero.Side);
                    RecordStat(ctx, effect, source, time, amount, 1);

                    if (effect.Kind == "burn")
                    {
                        hero.Burn += amount;
                        hero.BurnSource = source.Side;
                        hero.BurnSourceItemId = source.Id;
                        hero.BurnSourceCardId = source.CardId;
                        StatusPools.Add(hero, "burn", source.Side, source.Id, source.CardId, amount);
                    }
                    else if (effect.Kind == "poison")
                    {
                        hero.Poison += amount;
                        hero.PoisonSource = source.Side;
                        hero.PoisonSourceItemId = source.Id;
                        hero.PoisonSourceCardId = source.CardId;
                        StatusPools.Add(hero, "poison", source.Side, source.Id, source.CardId, amount);
                    }
                    else
                    {
                        hero.Regen += amount;
                        hero.RegenSourceItemId = source.Id;
                        hero.RegenSourceCardId = source.CardId;
                        StatusPools.Add(hero, "regen", source.Side, source.Id, source.CardId, amount);
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

                    if (effect.Kind == "burn")
                    {
                        EmitTrigger(ctx, "onBurnApplied", SourceOf(source), hero.Side, depth, null, ch);
                        if (amount > 0) EmitPlayerAttribute(ctx, hero.Side, "Burn", "Gain", amount, depth, ch);
                    }
                    else if (effect.Kind == "poison")
                    {
                        EmitTrigger(ctx, "onPoisonApplied", SourceOf(source), hero.Side, depth, null, ch);
                        if (amount > 0) EmitPlayerAttribute(ctx, hero.Side, "Poison", "Gain", amount, depth, ch);
                    }
                    else
                    {
                        EmitTrigger(ctx, "onRegen", SourceOf(source), hero.Side, depth, null, ch);
                        if (amount > 0) EmitPlayerAttribute(ctx, hero.Side, "HealthRegen", "Gain", amount, depth, ch);
                    }
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

                    int count = targetCount;
                    List<InternalItem> targets = ItemsForTarget(ctx, source, target, count, triggerSourceItemId, triggerTargetItemId);
                    double duration = effect.DurationValue == null ? effect.Seconds :
                        EvaluateNum(effect.DurationValue, ctx, source, ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId) / 1000.0;
                    duration = Math.Max(0, duration);
                    RecordStat(ctx, effect, source, time, duration, targets.Count);

                    foreach (InternalItem item in targets)
                    {
                        double seconds = duration;
                        if (ctx.Config.NativeMechanics)
                        {
                            double reduction = effect.Kind == "slow" ? NativeDynamicAuras.SlowResistance(item) :
                                effect.Kind == "freeze" ? NativeDynamicAuras.FreezeResistance(item) :
                                effect.Kind == "haste" ? NativeDynamicAuras.HasteResistance(item) :
                                effect.Kind == "charge" ? item.Card.PercentChargeReduction : 0;
                            if (item.Flying && (effect.Kind == "slow" || effect.Kind == "freeze")) reduction = Math.Max(50, reduction);
                            seconds *= Math.Max(0, 1 - Math.Min(100, reduction) / 100);
                        }
                        double statusTime = ctx.NativeOpeningEffects ? time - ctx.Rules.Dt : time;
                        if (effect.Kind == "haste")
                            item.HasteUntil = ctx.Config.NativeMechanics ? Math.Max(statusTime, item.HasteUntil) + seconds : Math.Max(item.HasteUntil, time + seconds);
                        else if (effect.Kind == "slow")
                            item.SlowUntil = ctx.Config.NativeMechanics ? Math.Max(statusTime, item.SlowUntil) + seconds : Math.Max(item.SlowUntil, time + seconds);
                        else if (effect.Kind == "freeze")
                            item.FrozenUntil = ctx.Config.NativeMechanics ? Math.Max(statusTime, item.FrozenUntil) + seconds : Math.Max(item.FrozenUntil, time + seconds);
                        else
                        {
                            if (ctx.Config.NativeMechanics && !TryReserveCharge(ctx, source, item, time))
                            {
                                // 串行窗口内的充能延后结算；时长按本次计算的减免结果冻结。
                                double last;
                                ctx.LastChargeAt.TryGetValue(source.Id + "|" + item.Id, out last);
                                ctx.DeferredCharges.Add(new DeferredCharge
                                {
                                    Charger = source, Target = item, Seconds = seconds,
                                    DueAt = JsMath.RoundTo6(last + ChargeSpacing), Depth = depth, ChainId = ch
                                });
                                continue;
                            }
                            ApplyCharge(ctx, source, item, seconds, time, depth, ch);
                            // Keep High charge in the regular readiness scan
                            // (b60fa630: Alpha Ray precedes the charged cast).
                            // Medium charge can reserve before the parent's
                            // onUse observers (034c3648: Copper Ed precedes Gamma).
                            if (NativeSkillSupport.EffectPriorityRank(effect.Priority) >= 3)
                                BattleEngine.MarkNativeReady(ctx, item, time);
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        if (item.Flying) continue;
                        item.Flying = true;
                        item.FlyingSince = time;
                        changed++;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "fly", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        // 飞行状态的主体是被影响的卡，而不是造成飞行的来源。
                        EmitTrigger(ctx, "onFly", SourceOf(item), item.Side, depth, null, ch);
                        EmitCardAttribute(ctx, item, "Flying", "Gain", 1, depth, ch);
                        // 回放证据（0466d70e 帧 60/120/180、170666bf 帧 160）：
                        // 起飞瞬间 +50% 冰冻/缓慢减免，落地撤销——游戏级飞行
                        // 机制而非物品光环。
                        SetFlyingResistance(item, true);
                    }
                    RecordStat(ctx, effect, source, time, 1, changed);
                    return;
                }
                case "flyStop":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        if (!item.Flying) continue;
                        item.Flying = false;
                        item.FlyingSince = -1;
                        changed++;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "flyStop", Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        EmitTrigger(ctx, "onFlyStop", SourceOf(item), item.Side, depth, null, ch);
                        EmitCardAttribute(ctx, item, "Flying", "Loss", 1, depth, ch);
                        SetFlyingResistance(item, false);
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
                    if (effect.Kind == "burnRemove")
                    {
                        hero.Burn = Math.Max(0, hero.Burn - removed);
                        StatusPools.Consume(hero, "burn", removed);
                    }
                    else
                    {
                        hero.Poison = Math.Max(0, hero.Poison - removed);
                        StatusPools.Consume(hero, "poison", removed);
                    }
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
                    int generation = source.Generation;
                    foreach (Effect child in effect.Effects)
                    {
                        ResolveEffect(ctx, child, source, time, depth + 1, ch, triggerSourceItemId, triggerTargetItemId);
                        if (ctx.Ended || source.Destroyed || source.Generation != generation) return;
                    }
                    return;
                }
                case "beginSandstorm":
                {
                    // 原生“开始沙暴”是整场战斗的一次性事件。重复开始不再派发
                    // onSandstorm，否则 Windmill 的 Max Health 会被重复翻倍。
                    StartSandstorm(ctx, source, time, depth, ch, true);
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
                        if (rule == "fixedSlushee")
                        {
                            CardDef fixedDonor = NativeTransform.FindFixedDonor(ctx, source, NativeTransform.SlusheeTemplate);
                            if (fixedDonor == null) continue;
                            if (NativeTransform.ApplyFixed(ctx, source, target, fixedDonor, copies, time, depth, ch)) changed++;
                        }
                        else
                        {
                            InternalItem donor = NativeTransform.FindDonor(ctx, source, target, rule);
                            if (donor == null) continue;
                            if (NativeTransform.Apply(ctx, source, target, donor, copies, time, depth, ch,
                                rule == "leftMediumEnchanted")) changed++;
                        }
                        if (ctx.Ended) break;
                    }
                    RecordStat(ctx, effect, source, time, copies, changed);
                    return;
                }
                case "transformDestroyed":
                {
                    // 该动作在 onBeforeDestroy 中把“本应被摧毁的卡”原位变成供体：
                    // Abducted Cow 的 Target=Self，Drone Crusher 的 Target=TriggerTarget。
                    // 变形成功后 DestroyItem 会看到 Generation 变化并取消摧毁。
                    string rule = NativeTransform.DestroyDonorRule(source.Card);
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, 1, triggerSourceItemId, triggerTargetItemId);
                    int copies = effect.Count.HasValue ? effect.Count.Value : 1;
                    int changed = 0;
                    foreach (InternalItem target in targets)
                    {
                        if (target == null || target.Destroyed) continue;
                        CardDef donor = NativeTransform.FindDestroyDonor(ctx, source, target, rule);
                        if (donor == null) continue;
                        // 回放顺序：先产生新实例，再让同一事件的观察者选择新卡。
                        // 旧实例保留原 Card 供已匹配的后续被动读取，但立即离场。
                        if (NativeTransform.ApplyDestroyed(ctx, source, target, donor, copies, time, depth, ch))
                            changed++;
                        if (ctx.Ended) break;
                    }
                    RecordStat(ctx, effect, source, time, copies, changed);
                    return;
                }
                case "enchant":
                case "enchantRandom":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        string enchantment = effect.Kind == "enchantRandom"
                            ? NativeEnchant.Pick(ctx, item, effect.EnchantmentNames, effect.EnchantmentWeights)
                            : effect.EnchantmentNames != null && effect.EnchantmentNames.Count > 0 ? effect.EnchantmentNames[0] : null;
                        if (string.IsNullOrEmpty(enchantment)) continue;
                        if (NativeEnchant.Apply(ctx, source, item, enchantment, effect.PreventOverride, time, depth, ch)) changed++;
                        if (ctx.Ended) break;
                    }
                    RecordStat(ctx, effect, source, time, 1, changed);
                    return;
                }
                case "enchantRemove":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    int changed = 0;
                    foreach (InternalItem item in targets)
                    {
                        if (NativeEnchant.Remove(ctx, source, item, time, depth, ch)) changed++;
                        if (ctx.Ended) break;
                    }
                    RecordStat(ctx, effect, source, time, 1, changed);
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

                    int count = targetCount;
                    List<InternalItem> targets = ItemsForTarget(ctx, source, target, count, triggerSourceItemId, triggerTargetItemId);
                    double raw = effect.Amount != null ? EvaluateNum(effect.Amount, ctx, source,
                        ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId) : effect.AmountValue;
                    int amount = (int)JsMath.Round(raw);
                    RecordStat(ctx, effect, source, time, amount < 0 ? 0 : amount, targets.Count);

                    foreach (InternalItem item in targets)
                    {
                        if (!item.AmmoMax.HasValue) continue;
                        int before = item.Ammo;
                        item.Ammo = amount < 0 ? item.AmmoMax.Value :
                            Math.Min(item.AmmoMax.Value, item.Ammo + amount);
                        // 0624335c frame 201: Panic reloads an empty, ready
                        // item and charges it before its new cooldown starts.
                        // That zero-cooldown charge has no reset carry.
                        if (before <= 0 && item.Ammo > 0) item.LastReloadFromEmptyAt = time;
                        // Native reload listeners react to accepted ammunition,
                        // not an attempt to reload an already full item.
                        if (ctx.Config.NativeMechanics && item.Ammo == before) continue;
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
                        if (item.Ammo != before)
                            EmitCardAttribute(ctx, item, "Ammo", item.Ammo > before ? "Gain" : "Loss", Math.Abs(item.Ammo - before), depth, ch);
                        // 装填的 Subject 是行动者，Target 是被装填的卡。
                        EmitTrigger(ctx, "onReload", SourceOf(source), item.Side, depth, item.Id, ch, source.Id, source.Side);
                    }
                    return;
                }
                case "modifyAttribute":
                {
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    // 数值引用 Target:null 时按当前被修改卡逐个求值；固定值结果不变。
                    double firstValue = 0;
                    bool first = true;
                    foreach (InternalItem item in targets)
                    {
                        double value = EvaluateNum(effect.Amount, ctx, source, ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId, item);
                        if (first) { firstValue = value; first = false; }
                        // A queued observation can retain an explicit trigger
                        // target after that card becomes disabled. Native still
                        // reports the action but does not apply its attribute
                        // gain (Ramrod -> Grapeshot, fourth replay frame 171).
                        // Self-target lifecycle effects keep their existing path.
                        bool disabledObservation = ctx.Config.NativeMechanics && item.Disabled &&
                            effect.Target != null && effect.Target.Kind == "triggerTargetItem";
                        double reported = disabledObservation ? 0 :
                            ApplyAttributeChange(ctx, item, effect.Attribute, effect.Operation, value, time, effect.Seconds);
                        if ((effect.Attribute == "CritChance" || effect.Attribute == "RegenApplyAmount" || effect.Attribute == "SellPrice") && Math.Abs(reported) > 1e-9)
                            EmitCardAttribute(ctx, item, effect.Attribute, reported > 0 ? "Gain" : "Loss", Math.Abs(reported), depth, ch);
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
                    RecordStat(ctx, effect, source, time, firstValue, targets.Count);
                    return;
                }
                case "playerAttribute":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double value = EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId);
                    double before = effect.Attribute == "HealthMax" ? hero.MaxHp : effect.Attribute == "HealthRegen" ? hero.Regen :
                        effect.Attribute == "RageMax" ? hero.RageMax :
                        effect.Attribute == "PercentDamageReduction" ? hero.ActionPercentDamageReduction :
                        effect.Attribute == "Rage" ? hero.Rage : hero.EnragedDurationMs;
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
                    else if (effect.Attribute == "HealthRegen")
                    {
                        hero.Regen = Math.Max(0, after);
                        double delta = hero.Regen - before;
                        if (delta > 0) StatusPools.Add(hero, "regen", source.Side, source.Id, source.CardId, delta);
                        else if (delta < 0) StatusPools.Consume(hero, "regen", -delta);
                    }
                    else if (effect.Attribute == "RageMax") hero.RageMax = Math.Max(0, after);
                    else if (effect.Attribute == "PercentDamageReduction")
                    {
                        hero.ActionPercentDamageReduction = Math.Min(100, Math.Max(0, after));
                        hero.ActionPercentDamageReductionUntil = effect.Seconds > 0 ? time + effect.Seconds : double.MaxValue;
                    }
                    else if (effect.Attribute == "Rage")
                    {
                        after = Math.Max(0, after);
                        double rageDelta = after - hero.Rage;
                        hero.Rage = after;
                        if (rageDelta > 0)
                        {
                            EmitPlayerAttribute(ctx, hero.Side, "Rage", "Gain", rageDelta, depth, ch);
                            TryEnrage(ctx, hero, time, depth, ch);
                        }
                        else if (rageDelta < 0)
                            EmitPlayerAttribute(ctx, hero.Side, "Rage", "Loss", -rageDelta, depth, ch);
                    }
                    else if (effect.Attribute == "Tempo")
                    {
                        // 音符插槽的“用 Tempo 物品时获得 N Tempo”。原生只把
                        // Tempo 写进人物状态，不产生效果账本行（0276c46b 帧
                        // 0-241 没有 PlayerModifyAttribute），观察者事件照常派发。
                        after = Math.Max(0, after);
                        double tempoDelta = after - hero.Tempo;
                        hero.Tempo = after;
                        if (tempoDelta > 0) EmitPlayerAttribute(ctx, hero.Side, "Tempo", "Gain", tempoDelta, depth, ch);
                        else if (tempoDelta < 0) EmitPlayerAttribute(ctx, hero.Side, "Tempo", "Loss", -tempoDelta, depth, ch);
                        return;
                    }
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
                    // Max-HP changes can move current HP; that movement is not
                    // damage or healing and must stay in its own ledger row.
                    ev.HpAfter = hero.Hp;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));
                    if (effect.Attribute == "HealthMax" && Math.Abs(after - before) > 1e-9)
                        EmitPlayerAttribute(ctx, hero.Side, "HealthMax", after > before ? "Gain" : "Loss", Math.Abs(after - before), depth, ch);
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
                case "tempo":
                {
                    InternalHero hero = HeroFor(ctx, source, effect.Target, source.Side);
                    double amount = Math.Max(0, JsMath.Round(EvaluateNum(effect.Amount, ctx, source, hero, triggerSourceItemId, triggerTargetItemId)));
                    hero.Tempo += amount;
                    BattleEvent ev = new BattleEvent();
                    ev.Time = time;
                    ev.Side = source.Side;
                    ev.TargetSide = hero.Side;
                    ev.SourceItemId = source.Id;
                    ev.SourceCardId = source.CardId;
                    ev.Kind = "tempo";
                    ev.Value = amount;
                    ev.ChainId = ch;
                    ev.Depth = depth;
                    ctx.Events.Add(FinishEvent(ctx, ev));
                    // Tempo 变化属于人物属性变化（Ticket Terra 的累积依赖它）。
                    if (amount > 0) EmitPlayerAttribute(ctx, hero.Side, "Tempo", "Gain", amount, depth, ch);
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    foreach (InternalItem item in targets) DestroyItem(ctx, item, time, depth, ch, source);
                    RecordStat(ctx, effect, source, time, 1, targets.Count);
                    return;
                }
                case "disable":
                case "repair":
                {
                    // 修复/禁用链路要能选中已禁用的卡（原生 TActionCardRepair 用途）。
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId, true);
                    bool disable = effect.Kind == "disable";
                    if (disable && ctx.Config.NativeMechanics)
                    {
                        // 原生“摧毁/禁用”不选已经飞行中的卡：07f044ee 帧 157 Wrecking
                        // Ball 的随机敌方手牌目标在敌方 6 张全部飞行后为空，没有
                        // CardDisable 事件；同帧刚起飞的卡仍可被选中（帧 60 气象机）。
                        targets.RemoveAll(delegate(InternalItem item)
                        {
                            return item.Flying && item.FlyingSince >= -1e-9 && time > item.FlyingSince + 1e-9;
                        });
                    }
                    foreach (InternalItem item in targets)
                    {
                        if (item.Destroyed) continue;
                        if (disable)
                        {
                            DisableItem(ctx, item, source, time, depth, ch);
                            continue;
                        }
                        if (ctx.Config.NativeMechanics && !item.Disabled && !item.CombatDisabled) continue;
                        item.Disabled = false;
                        item.CombatDisabled = false;
                        BattleEvent ev = new BattleEvent { Time = time, Side = source.Side, SourceItemId = source.Id, SourceCardId = source.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = effect.Kind, Value = 1, ChainId = ch, Depth = depth };
                        ctx.Events.Add(FinishEvent(ctx, ev));
                        TriggerPayload payload = new TriggerPayload { Event = "onCardRepaired",
                            SourceSide = item.Side, SourceItemId = item.Id, TargetSide = item.Side, TargetItemId = item.Id,
                            ActorSide = source.Side, ActorItemId = source.Id, ChainId = ch, Depth = depth };
                        ctx.TriggerQueue.Enqueue(payload);
                    }
                    RecordStat(ctx, effect, source, time, 1, targets.Count);
                    return;
                }
                case "forceUse":
                {
                    // 强制使用是纯规则例外：Drone Crusher 先禁用再 ForceUse，被禁用卡仍执行。
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId, true);
                    foreach (InternalItem item in targets)
                    {
                        // 回放证据：Drone Crusher 先禁用再 ForceUse，被禁用卡仍执行。
                        // 强制使用是显式例外；已离场（Destroyed）仍然不能使用。
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
                    foreach (InternalItem item in targets)
                    {
                        item.Flying = !item.Flying;
                        item.FlyingSince = item.Flying ? time : -1;
                        if (ctx.Config.NativeMechanics) SetFlyingResistance(item, item.Flying);
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
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
                    List<InternalItem> targets = ItemsForTarget(ctx, source, effect.Target, targetCount, triggerSourceItemId, triggerTargetItemId);
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
                    double raw = effect.Amount != null ? EvaluateNum(effect.Amount, ctx, source,
                        ctx.Hero(source.Side), triggerSourceItemId, triggerTargetItemId) : effect.AmountValue;
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
                    if (amount != 0) EmitCardAttribute(ctx, source, "Ammo", amount > 0 ? "Gain" : "Loss", Math.Abs(amount), depth, ch);
                    return;
                }
                default:
                    return;
            }
        }

        internal static void SetFlyingResistance(InternalItem item, bool flying)
        {
            if (item.FlyingResistanceApplied == flying) return;
            double delta = flying ? 50 : -50;
            ItemAttributes.AddDelta(item, "PercentFreezeReduction", delta);
            ItemAttributes.AddDelta(item, "PercentSlowReduction", delta);
            item.FlyingResistanceApplied = flying;
        }

        // 返回实际变化量；冷却类属性直接作用于当前冷却，其余计入运行时属性。
        private static double ApplyAttributeChange(BattleContext ctx, InternalItem item, string attribute, string operation, double value, double time, double seconds)
        {
            // Ammo 是实例字段（不属于运行时属性表）：使用本卡时消耗全部 Ammo
            // 这类动作要直接改 item.Ammo，并重建派生层让 Multicast=Ammo 立即生效
            // （07ae8be8 帧159 Shuriken：Ammo 3→0、Multicast 3→0）。
            if (attribute == "Ammo")
            {
                double ammoBefore = item.Ammo;
                double ammoNext = Operate(item.Ammo, value, operation);
                if (item.AmmoMax.HasValue) ammoNext = Math.Min(item.AmmoMax.Value, ammoNext);
                item.Ammo = (int)JsMath.Round(Math.Max(0, ammoNext));
                NativeDynamicAuras.Refresh(ctx);
                return item.Ammo - ammoBefore;
            }
            // Native actions still execute on targets lacking this attribute,
            // but do not invent a Damage/Shield/etc. field on utility items.
            // A declared attribute with value zero remains a valid target.
            if (ctx.Config.NativeMechanics && item.Card.NativeAttributesAreLive &&
                item.Card.NativeTemplate != null && item.Card.NativeTemplate.Kind == JsonKind.Object &&
                attribute != null &&
                !ItemAttributes.Has(item, attribute)) return 0;
            double current = ItemAttributes.Get(item, attribute);
            if (ctx.Config.NativeMechanics && item.AuraBaseAttributes == null &&
                (attribute == "FlatCooldownReduction" || attribute == "PercentCooldownReduction"))
            {
                double flatBefore = ItemAttributes.Get(item, "FlatCooldownReduction");
                double percentBefore = ItemAttributes.Get(item, "PercentCooldownReduction");
                double change = Operate(current, value, operation) - current;
                if (seconds > 0) ItemAttributes.AddTimed(item, attribute, change, time + seconds);
                else ItemAttributes.AddDelta(item, attribute, change);
                RebaseNativeCooldown(item, flatBefore, percentBefore);
                return change;
            }
            if (item.AuraBaseAttributes != null && (ctx.Config.NativeMechanics ||
                attribute != "FlatCooldownReduction" && attribute != "PercentCooldownReduction"))
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

        internal static void RebaseNativeCooldown(InternalItem item, double flatBefore, double percentBefore)
        {
            if (percentBefore >= 100) return;
            double unscaled = NativeDynamicAuras.UnscaledCooldownMs(item.CooldownMax * 1000, flatBefore, percentBefore);
            double percent = Math.Min(100, Math.Max(0, ItemAttributes.Get(item, "PercentCooldownReduction")));
            double max = NativeDynamicAuras.EffectiveCooldownMs(unscaled, ItemAttributes.Get(item, "FlatCooldownReduction"), percent) / 1000;
            item.Cooldown = Math.Max(0, item.Cooldown + max - item.CooldownMax);
            item.CooldownMax = max;
        }

        internal static double Operate(double before, double value, string operation)
        {
            return operation == "Add" ? before + value : operation == "Subtract" ? before - value :
                operation == "Multiply" ? before * value : value;
        }

        // Base Rage Effect（原生 4472da8a）：暴怒期间本方手牌 +10% 冷却缩减。
        // 该值必须作为光环百分比参与 EffectiveCooldownMs，否则单纯乘 0.9 会在
        // 下一次 NativeDynamicAuras.Refresh 时被光环层改回原值（034c3648 帧
        // 301：原生 CooldownMax 3960 -> 3559 并保持到暴怒结束）。
        internal const double BaseRageCooldownPercent = 10;

        // 怒火达到上限时进入暴怒；持续时间为 EnragedDurationMs。
        internal static void TryEnrage(BattleContext ctx, InternalHero hero, double time, int depth, int? chainId)
        {
            if (hero.EnragedUntil > 0 || hero.RageMax <= 0 || hero.Rage < hero.RageMax) return;
            hero.Rage = 0;
            hero.EnragedUntil = time + Math.Max(0, hero.EnragedDurationMs) / 1000.0;
            // Base Rage Effect（原生 4472da8a）：暴怒时清空本方 Slow/Freeze。
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Side != hero.Side) continue;
                item.SlowUntil = -1;
                item.FrozenUntil = -1;
                if (item.IsSkill || item.BaseRageCooldownMax >= 0) continue;
                item.BaseRageCooldownMax = item.CooldownMax;
                // 立即值只是本帧近似；Refresh 会按光环百分比用 float32+截断
                // 重算到原生毫秒值（例如 4000 -> 3559）。
                double reduced = item.CooldownMax * (1 - BaseRageCooldownPercent / 100.0);
                double previousCooldown = item.Cooldown;
                item.Cooldown = Math.Max(0, item.Cooldown - (item.CooldownMax - reduced));
                item.CooldownMax = reduced;
                // CDR resolves after this frame's readiness check. A newly
                // ready card stays at zero until the next frame reserves it.
                if (ctx.Config.NativeMechanics && previousCooldown > 1e-9 && item.Cooldown <= 1e-9)
                    item.NativeReadyAfter = JsMath.RoundTo6(time + ctx.Rules.Dt);
            }
            TriggerPayload payload = new TriggerPayload { Event = "onPlayerEnraged", SourceSide = hero.Side, ChainId = chainId, Depth = depth };
            ctx.TriggerQueue.Enqueue(payload);
            BattleEvent ev = new BattleEvent { Time = time, Side = hero.Side, TargetSide = hero.Side, Kind = "enrage", Value = hero.EnragedDurationMs, ChainId = chainId ?? ctx.NextChainId++, Depth = depth };
            ctx.Events.Add(FinishEvent(ctx, ev));
        }

        // 原生回放：CardDisable 也会触发摧毁前/完成摧毁观察者
        // （Drone Crusher 禁用 Parts Picker、Ancient Locket 自禁用后触发减伤），
        // 但卡只进入 Disabled，不产生 destroy。若摧毁前能力把它变成新实例，
        // 则按 TransformDestroyed 语义离场。
        private static void DisableItem(BattleContext ctx, InternalItem item, InternalItem actor,
            double time, int depth, int chainId)
        {
            if (item.Destroyed || item.Disabled || item.Disabling) return;
            item.Disabling = true;
            int generation = item.Generation;
            try
            {
                // 回放顺序：CardDisable 先落状态/事件，再跑摧毁前观察者；
                // Drone Crusher 的禁用会在同一事件链里把目标变换成新实例。
                item.Disabled = true;
                item.CombatDisabled = true;
                // 禁用等同离场：减速/冻结状态当帧清零（07f044ee 帧 140 The Big One
                // Slow/Freeze 200→0，帧 142 变形副本 100→0；之后不再逐帧衰减）。
                item.SlowUntil = Math.Min(item.SlowUntil, time);
                item.FrozenUntil = Math.Min(item.FrozenUntil, time);
                BattleEvent ev = new BattleEvent { Time = time, Side = actor == null ? item.Side : actor.Side,
                    SourceItemId = actor == null ? item.Id : actor.Id, SourceCardId = actor == null ? item.CardId : actor.CardId,
                    TargetSide = item.Side, TargetItemId = item.Id, Kind = "disable", Value = 1, ChainId = chainId, Depth = depth };
                ctx.Events.Add(FinishEvent(ctx, ev));
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onBeforeDestroy",
                    SourceSide = item.Side, SourceItemId = item.Id, TargetSide = item.Side, TargetItemId = item.Id,
                    ActorSide = actor == null ? (Side?)null : actor.Side, ActorItemId = actor == null ? null : actor.Id,
                    ChainId = chainId, Depth = depth });
                if (ctx.Config.NativeMechanics && ctx.Player.Hp > 0 && ctx.Enemy.Hp > 0)
                {
                    // 原生（ebde2082 帧 79/80、Drone Crusher 同象）：禁用触发的
                    // 摧毁前观察者下一帧才结算——Boosted Saucer 的充能在禁用后
                    // 1 帧落地。onCardDisabled 排在其后，由下一帧按队列顺序结算。
                    // 死亡结算中的“自禁用->摧毁前->复活”链仍走同步排空。
                    // 例外：Immediate 优先级的摧毁前被动与禁用同帧（07f044ee 帧 140
                    // The Big One 的 PlayerDamage/Poison 先于属性复位落地）。
                    Triggers.DrainImmediateBeforeDestroy(ctx, time);
                    ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onCardDisabled",
                        SourceSide = item.Side, SourceItemId = item.Id, TargetSide = item.Side, TargetItemId = item.Id,
                        ActorSide = actor == null ? (Side?)null : actor.Side, ActorItemId = actor == null ? null : actor.Id,
                        ChainId = chainId, Depth = depth });
                    // CardDisable counts as a performed destruction without
                    // removing its target (Trollosaur: disable 163, force 164).
                    // Keep the existing before-destroy/disabled queue order.
                    EnqueueDestroyedObserver(ctx, item, actor, chainId, depth);
                    return;
                }
                Triggers.DrainEventWhileDead(ctx, time, "onBeforeDestroy");
                if (item.Destroyed || item.Generation != generation) return;
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onCardDisabled",
                    SourceSide = item.Side, SourceItemId = item.Id, TargetSide = item.Side, TargetItemId = item.Id,
                    ActorSide = actor == null ? (Side?)null : actor.Side, ActorItemId = actor == null ? null : actor.Id,
                    ChainId = chainId, Depth = depth });
                if (ctx.Config.NativeMechanics)
                    EnqueueDestroyedObserver(ctx, item, actor, chainId, depth);
            }
            finally
            {
                item.Disabling = false;
            }
        }

        private static void DestroyItem(BattleContext ctx, InternalItem item, double time, int depth, int chainId, InternalItem actor)
        {
            // A 摧毁 B、B 的摧毁前被动又摧毁 A 时，避免同一实例递归进入两次。
            if (item.Destroyed || item.Destroying) return;
            if (ctx.Config.NativeMechanics && NativeDynamicAuras.DestroyImmunity(item) > 0)
            {
                BattleEvent blocked = new BattleEvent { Time = time, Side = item.Side, SourceItemId = item.Id, SourceCardId = item.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "destroyBlocked", Value = 1, ChainId = chainId, Depth = depth };
                ctx.Events.Add(FinishEvent(ctx, blocked));
                return;
            }
            int generation = item.Generation;
            item.Destroying = true;
            try
            {
                // 摧毁前事件同步结算：Subject 是待摧毁的卡，Source 是行动者。
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onBeforeDestroy",
                    SourceSide = item.Side, SourceItemId = item.Id, TargetSide = item.Side, TargetItemId = item.Id,
                    ActorSide = actor == null ? (Side?)null : actor.Side, ActorItemId = actor == null ? null : actor.Id,
                    ChainId = chainId, Depth = depth });
                Triggers.DrainEventWhileDead(ctx, time, "onBeforeDestroy");
                if (item.Destroyed) return;
                if (item.Generation != generation) return;
                item.Destroyed = true;
                item.Generation++;
                if (item.History != null) item.History.RemovedAt = time;
                item.Disabled = true;
                item.CombatDisabled = true;
                item.Cooldown = 0;
                BattleEvent ev = new BattleEvent { Time = time, Side = item.Side, SourceItemId = item.Id, SourceCardId = item.CardId, TargetSide = item.Side, TargetItemId = item.Id, Kind = "destroy", Value = 1, ChainId = chainId, Depth = depth };
                ctx.Events.Add(FinishEvent(ctx, ev));
                EnqueueDestroyedObserver(ctx, item, actor, chainId, depth);
            }
            finally
            {
                item.Destroying = false;
            }
        }

        // 摧毁后是观察事件，按帧延后到下一次排空；Subject 是行动者。
        private static void EnqueueDestroyedObserver(BattleContext ctx, InternalItem item, InternalItem actor, int chainId, int depth)
        {
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onCardDestroyed",
                SourceSide = actor == null ? item.Side : actor.Side, SourceItemId = actor == null ? item.Id : actor.Id,
                TargetSide = item.Side, TargetItemId = item.Id, ChainId = chainId, Depth = depth });
        }

        private static DamageSource SourceOf(InternalItem item)
        {
            DamageSource source = new DamageSource();
            source.Id = item.Id;
            source.CardId = item.CardId;
            source.Side = item.Side;
            source.CritBonus = item.CritBonus;
            source.DamageCrit = Math.Max(0, ItemAttributes.Get(item, "DamageCrit"));
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
