using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarActionAxis.Engine
{
    public static class BattleEngine
    {
        private sealed class ScheduledCast
        {
            public InternalItem Item;
            public double Time;
            public int ChainId;
            public int Generation;
            public double ImmediateAt;
            public bool ImmediateSettled;
            public bool Forced;
            // 施放开始时该卡是否仍在场（未禁用）。用于判定“本帧自禁用后不再派发 ItemUsed”。
            public bool EnabledAtStart;
            public int Depth;
            public long QueueOrder;
            public List<Effect> EligibleEffects;
            // 前置条件未满足而未结算的能力；其中护盾类仍要向观察者派发
            // “已执行护盾”（0276c46b 的 Light Drone / Sharp Corners）。
            public List<Effect> SkippedEffects;
        }
        public static BattleResult Simulate(BattleConfig config)
        {
            try
            {
                return SimulateUnsafe(config);
            }
            catch (Exception error)
            {
                BattleResult failed = new BattleResult();
                failed.Winner = "draw";
                failed.EndTime = 0;
                failed.Warnings.Add("simulation failed: " + error.Message);
                return failed;
            }
        }

        public static BattleContext InitBattle(BattleConfig config)
        {
            ResolvedRules rules = Rules.Resolve(config.Rules, config.NativeMechanics);
            BattleContext ctx = new BattleContext();
            ctx.Config = config;
            if (config.ReplayRandom != null) ctx.ReplayRandom = new ReplayRandomSession(config.ReplayRandom);
            ctx.Rules = rules;
            ctx.Rng = new Rng(config.Seed);
            ctx.Player = new InternalHero(Side.Player);
            ctx.Player.Hp = config.PlayerStartHp.HasValue ? Math.Max(0, Math.Min(config.PlayerMaxHp, config.PlayerStartHp.Value)) : config.PlayerMaxHp;
            ctx.Player.MaxHp = config.PlayerMaxHp;
            ctx.Player.Shield = Math.Max(0, config.PlayerStartShield);
            ctx.Player.Regen = Math.Max(0, config.PlayerStartRegen);
            if (ctx.Player.Regen > 0)
                StatusPools.Add(ctx.Player, "regen", Side.Player, "dot:regen", "regen", ctx.Player.Regen);
            ctx.Enemy = new InternalHero(Side.Enemy);
            ctx.Enemy.Hp = config.EnemyStartHp.HasValue ? Math.Max(0, Math.Min(config.EnemyMaxHp, config.EnemyStartHp.Value)) : config.EnemyMaxHp;
            ctx.Enemy.MaxHp = config.EnemyMaxHp;
            ctx.Enemy.Shield = Math.Max(0, config.EnemyStartShield);
            ctx.Enemy.Regen = Math.Max(0, config.EnemyStartRegen);
            if (ctx.Enemy.Regen > 0)
                StatusPools.Add(ctx.Enemy, "regen", Side.Enemy, "dot:regen", "regen", ctx.Enemy.Regen);
            ctx.Player.RageMax = config.PlayerRageMax;
            ctx.Player.EnragedDurationMs = config.PlayerEnragedDurationMs;
            ctx.Enemy.RageMax = config.EnemyRageMax;
            ctx.Enemy.EnragedDurationMs = config.EnemyEnragedDurationMs;
            CopyAttributes(config.PlayerAttributes, ctx.Player);
            CopyAttributes(config.EnemyAttributes, ctx.Enemy);
            if (config.NativeMechanics)
            {
                InitTempoGain(ctx.Player, true);
                InitTempoGain(ctx.Enemy, false);
            }

            Dictionary<string, int> cardCounts = new Dictionary<string, int>();
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                foreach (LoadoutItem entry in LoadoutFor(config, side).Items)
                {
                    CardDef card;
                    if (!config.Cards.TryGetValue(entry.CardId, out card)) continue;
                    int count;
                    cardCounts.TryGetValue(card.Id, out count);
                    cardCounts[card.Id] = count + 1;
                }
            }

            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                foreach (LoadoutItem entry in LoadoutFor(config, side).Items)
                {
                    CardDef card;
                    if (!config.Cards.TryGetValue(entry.CardId, out card))
                    {
                        ctx.Warnings.Add("unknown card: " + entry.CardId);
                        continue;
                    }

                    bool startReady = card.StartReady || rules.StartReady;
                    int count;
                    cardCounts.TryGetValue(card.Id, out count);
                    string instanceId = count > 1
                        ? SideUtil.Key(side) + ":" + entry.Slot + ":" + card.Id
                        : card.Id;

                    InternalItem item = new InternalItem();
                    item.Id = instanceId;
                    item.CardId = card.Id;
                    item.Card = card;
                    item.Side = side;
                    item.Slot = entry.Slot;
                    double openingCooldown = card.Cooldown;
                    if (entry.StartCooldown.HasValue)
                    {
                        openingCooldown = Math.Max(0, entry.StartCooldown.Value);
                        if (card.Cooldown > 0) openingCooldown = Math.Min(card.Cooldown, openingCooldown);
                    }
                    item.Cooldown = startReady ? 0 : openingCooldown;
                    item.CooldownMax = card.Cooldown;
                    item.Ammo = card.AmmoMax.HasValue ? card.AmmoMax.Value : 0;
                    item.AmmoMax = card.AmmoMax;
                    ApplyStaticEffects(item);
                    ctx.RecordItemState(item, 0);
                    ctx.Items.Add(item);
                }
            }

            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                foreach (CardDef card in side == Side.Player ? config.PlayerSkills : config.EnemySkills)
                {
                    InternalItem skill = new InternalItem();
                    skill.Id = card.Id;
                    skill.CardId = card.Id;
                    skill.Card = card;
                    skill.Side = side;
                    skill.Slot = -1;
                    skill.IsSkill = true;
                    ctx.Skills.Add(skill);
                }
            }
            NativeDynamicAuras.Initialize(ctx);
            return ctx;
        }

        private static Loadout LoadoutFor(BattleConfig config, Side side)
        {
            return side == Side.Player ? config.Player : config.Enemy;
        }

        private static void CopyAttributes(Dictionary<string, int> source, InternalHero hero)
        {
            if (source == null || hero == null) return;
            foreach (KeyValuePair<string, int> pair in source) hero.Attributes[pair.Key] = pair.Value;
        }

        private static void ApplyStaticEffects(InternalItem item)
        {
            int staticMulticast = 0;
            foreach (Effect effect in item.Card.Effects)
            {
                if (effect.Kind == "multicast")
                {
                    int count = effect.Count.HasValue ? effect.Count.Value : 1;
                    staticMulticast += Math.Max(0, count - 1);
                }
                else if (effect.Kind == "crit")
                {
                    item.CritBonus += effect.Chance;
                }
                else if (effect.Kind == "lifesteal")
                {
                    item.Lifesteal += effect.Percent;
                }
            }

            item.MulticastBonus = item.Card.HasMulticastOverride
                ? item.Card.MulticastBonus
                : staticMulticast;
        }

        public static void FlushPendingItems(BattleContext ctx)
        {
            if (ctx.PendingItems.Count == 0) return;
            ctx.Items.AddRange(ctx.PendingItems);
            ctx.PendingItems.Clear();
            // Recompute auras before the new instance's first snapshot/action.
            NativeDynamicAuras.Refresh(ctx);
            NativeDynamicAuras.Refresh(ctx);
        }

        private static void AdvanceCooldowns(BattleContext ctx, double t)
        {
            foreach (InternalItem item in ctx.Items)
            {
                double rate = CooldownRate(ctx, item, t);
                item.Cooldown = Math.Max(0, item.Cooldown - ctx.Rules.Dt * rate);
                MarkNativeReady(ctx, item, t);
            }
        }

        internal static void MarkNativeReady(BattleContext ctx, InternalItem item, double t)
        {
            if (item.Cooldown > 1e-9 || item.Disabled || item.Destroyed)
            {
                item.NativeReadyOrder = 0;
                return;
            }
            if (!ctx.Config.NativeMechanics || item.NativeReadyOrder != 0 || item.Card.Passive ||
                item.CooldownMax <= 0 || item.Cooldown > 1e-9 || item.Disabled || item.Destroyed ||
                FrozenActive(item, t) || t < item.NativeReadyAfter - 1e-9) return;
            item.NativeReadyOrder = ++ctx.NativeQueueSequence;
        }

        private static double CooldownRate(BattleContext ctx, InternalItem item, double t)
        {
            if (item.Disabled || item.Destroyed) return 0;
            // The interval ending on Freeze 50 -> 0 is still frozen. Native
            // Haste/Slow use their post-expiry state, but Freeze resumes the
            // cooldown only on the following frame (b1621d7b, frame 140).
            bool frozen = FrozenActive(item, t);
            bool hasted = StatusActive(ctx, item.HasteUntil, t);
            bool slowed = StatusActive(ctx, item.SlowUntil, t);
            return frozen ? 0 :
                (hasted ? ctx.Rules.HasteMultiplier : 1) *
                (slowed ? ctx.Rules.SlowMultiplier : 1);
        }

        private static bool StatusActive(BattleContext ctx, double until, double t)
        {
            // Native Haste/Slow use the expired state. Freeze keeps its own
            // rule below; the two differ on the expiry frame.
            return ctx.Config.NativeMechanics ? t < until - 1e-9 : t <= until + 1e-9;
        }

        private static bool FrozenActive(InternalItem item, double t)
        {
            // 归零帧（快照 Freeze 50 -> 0）仍然冻结，原生下一帧才恢复出手：
            // 034c3648 帧 152 归零，帧 153 才重置冷却；引擎若在同帧重置，
            // 后续充能串行队列整体前移（帧 261 多出一次自充能）。
            return t <= item.FrozenUntil + 1e-9;
        }

        private static readonly string[] SnapshotAttributes = {
            "DamageAmount", "HealAmount", "ShieldApplyAmount", "BurnApplyAmount",
            "PoisonApplyAmount", "RegenApplyAmount", "RageApplyAmount", "CritChance",
            "DamageCrit", "HealCrit", "ShieldCrit", "BurnCrit", "PoisonCrit",
            "Lifesteal", "LifestealPercentage", "FlatCooldownReduction",
            "PercentCooldownReduction", "PercentChargeReduction", "PercentHasteReduction",
            "PercentSlowReduction", "PercentFreezeReduction", "ChargeAmount",
            "ChargeTargets", "HasteAmount", "HasteTargets", "SlowAmount", "SlowTargets",
            "FreezeAmount", "FreezeTargets", "FlyingTargets", "EnchantTargets",
            "ReloadAmount", "ReloadTargets", "BurnRemoveAmount", "PoisonRemoveAmount",
            "DestroyImmunity", "Heated", "Chilled", "SellPrice", "BuyPrice"
        };

        private static double RemainingMs(double until, double t)
        {
            return until > t + 1e-9 ? Math.Round((until - t) * 1000) : 0;
        }

        private static void CaptureItemAttributes(InternalItem item, double t,
            Dictionary<string, double> target)
        {
            target.Clear();
            target["Cooldown"] = Math.Round(item.Cooldown * 1000);
            target["CooldownMax"] = Math.Round(item.CooldownMax * 1000);
            target["Ammo"] = item.Ammo;
            target["AmmoMax"] = item.AmmoMax.HasValue ? item.AmmoMax.Value : 0;
            target["Haste"] = RemainingMs(item.HasteUntil, t);
            target["Slow"] = RemainingMs(item.SlowUntil, t);
            target["Freeze"] = RemainingMs(item.FrozenUntil, t);
            target["Flying"] = item.Flying ? 1 : 0;
            target["Multicast"] = 1 + item.MulticastBonus;
            HashSet<string> names = new HashSet<string>();
            foreach (string name in SnapshotAttributes) names.Add(name);
            if (item.Card != null)
            {
                if (item.Card.LiveAttributes != null) names.UnionWith(item.Card.LiveAttributes.Keys);
                if (item.Card.NativeAuraSeedAttributes != null) names.UnionWith(item.Card.NativeAuraSeedAttributes.Keys);
            }
            if (item.AuraAttributes != null) names.UnionWith(item.AuraAttributes.Keys);
            if (item.AttributeDeltas != null) names.UnionWith(item.AttributeDeltas.Keys);
            if (item.AuraBaseAttributes != null) names.UnionWith(item.AuraBaseAttributes.Keys);
            foreach (string name in names)
            {
                if (name == "Cooldown" || name == "Ammo" || name == "Flying" ||
                    name == "Haste" || name == "Slow" || name == "Freeze" ||
                    name == "Multicast" || name == "CooldownMax") continue;
                // 抗性面板按原生显示：卡面 + 光环 + 运行时增量（含飞行 +50），
                // 不再用有效值截断；实际减免在 NativeDynamicAuras.*Resistance 截断。
                double value = name == "PercentSlowReduction" || name == "PercentFreezeReduction"
                    ? NativeDynamicAuras.ResistanceDisplay(item, name)
                    : ItemAttributes.Get(item, name);
                target[name] = Math.Round(value);
            }
        }

        // 原生在禁用发生的那一帧末尾起不再执行该卡光环：禁用链当帧仍读光环值
        // （Elixir 满血复活），帧末关闭并重建派生层，快照记录回落到卡面基底的值
        // （02dae1ed 帧390 Pepper Spray SlowAmount 4000→2000）。
        private static void DeactivateDisabledAuras(BattleContext ctx)
        {
            bool changed = false;
            foreach (InternalItem item in ctx.Items)
            {
                if (!item.Disabled || item.AuraInactive) continue;
                item.AuraInactive = true;
                changed = true;
            }
            if (changed) NativeDynamicAuras.Refresh(ctx);
        }

        private static void CaptureItemSnapshots(BattleContext ctx, double t)
        {
            HashSet<string> activated = new HashSet<string>();
            for (int index = ctx.Events.Count - 1; index >= 0; index--)
            {
                BattleEvent ev = ctx.Events[index];
                if (ev.Time < t - 1e-9) break;
                if (ev.Kind == "activate" && ev.SourceItemId != null)
                    activated.Add(ev.SourceItemId);
            }
            foreach (InternalItem item in ctx.Items)
            {
                ItemSnapshot snapshot = new ItemSnapshot();
                snapshot.Time = t;
                snapshot.ItemId = item.Id;
                snapshot.CooldownRemaining = item.Cooldown;
                snapshot.CooldownMax = item.CooldownMax;
                snapshot.HasCooldown = !item.Destroyed && !item.Card.Passive && item.CooldownMax > 0;
                snapshot.Activated = activated.Contains(item.Id);
                // The next interval uses the same status-expiry boundary as AdvanceCooldowns.
                snapshot.CooldownRate = CooldownRate(ctx, item, t + ctx.Rules.Dt);
                snapshot.Ammo = item.Ammo;
                snapshot.AmmoMax = item.AmmoMax;
                CaptureItemAttributes(item, t, snapshot.Attributes);
                ctx.ItemSnapshots.Add(snapshot);
            }
            // 技能实例的属性变化（Warm Hugs 的 Custom_0 计数等）在原生回放里
            // 与卡牌更新同一台账导出；不入快照会整段缺行。
            foreach (InternalItem skill in ctx.Skills)
            {
                ItemSnapshot snapshot = new ItemSnapshot();
                snapshot.Time = t;
                snapshot.ItemId = skill.Id;
                snapshot.CooldownRemaining = skill.Cooldown;
                snapshot.CooldownMax = skill.CooldownMax;
                snapshot.HasCooldown = false;
                snapshot.Activated = false;
                snapshot.Ammo = skill.Ammo;
                snapshot.AmmoMax = skill.AmmoMax;
                CaptureItemAttributes(skill, t, snapshot.Attributes);
                ctx.ItemSnapshots.Add(snapshot);
            }
        }

        private static List<InternalItem> CollectReady(BattleContext ctx)
        {
            List<InternalItem> ready = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Disabled || item.Destroyed || ctx.Now < item.NativeReadyAfter - 1e-9) continue;
                if (!item.Card.Passive && item.CooldownMax > 0 && item.Cooldown <= 1e-9)
                    ready.Add(item);
            }

            return ready
                .OrderBy(delegate(InternalItem item) { return item.Side == Side.Player ? 0 : 1; })
                .ThenBy(delegate(InternalItem item) { return item.Slot; })
                .ToList();
        }

        private static void Activate(BattleContext ctx, InternalItem item, double t)
        {
            if (FrozenActive(item, t) || item.Disabled || item.Destroyed) return;
            if (item.AmmoMax.HasValue && item.Ammo <= 0) return;

            int chainId = ctx.NextChainId++;
            int casts = Math.Max(0, 1 + item.MulticastBonus);
            int generation = item.Generation;
            item.Cooldown = item.CooldownMax;
            // 弹药在激活开始扣减，整次激活（含多播）只消耗一发（同原生语义）。
            if (item.AmmoMax.HasValue)
            {
                int beforeAmmo = item.Ammo;
                item.Ammo = Math.Max(0, item.Ammo - 1);
                if (item.Ammo < beforeAmmo)
                    Effects.EmitCardAttribute(ctx, item, "Ammo", "Loss", beforeAmmo - item.Ammo, 0, chainId);
            }

            for (int cast = 0; cast < casts; cast++)
            {
                if (ctx.Ended || item.Destroyed || item.Disabled || item.Generation != generation) break;

                BattleEvent activate = new BattleEvent();
                activate.Time = t;
                activate.Side = item.Side;
                activate.SourceItemId = item.Id;
                activate.SourceCardId = item.CardId;
                activate.Kind = "activate";
                activate.ChainId = chainId;
                activate.Depth = 0;
                ctx.Events.Add(Effects.FinishEvent(ctx, activate));

                // 使用前的被动必须先结算，否则本回合的加成本回合不生效。
                TriggerPayload beforeUse = new TriggerPayload { Event = "onBeforeUse", SourceSide = item.Side, SourceItemId = item.Id, ChainId = chainId, Depth = 0 };
                Triggers.Enqueue(ctx, beforeUse);
                Triggers.DrainEvent(ctx, t, "onBeforeUse");

                foreach (Effect effect in NativeSkillSupport.OrderedEffects(item.Card.Effects))
                {
                    if (item.Destroyed || item.Disabled || item.Generation != generation) break;
                    if (effect.Kind == "multicast" ||
                        effect.Kind == "crit" ||
                        effect.Kind == "lifesteal")
                        continue;
                    if (!EffectReady(ctx, item, effect)) continue;

                    Effects.ResolveEffect(ctx, effect, item, t, 0, chainId);
                    if (ctx.Ended) break;
                }

                if (item.Destroyed || item.Generation != generation) break;
                // 自身施放中把自己禁用的卡不再派发 ItemUsed（07f044ee 帧 142）。
                if (item.Disabled) break;
                TriggerPayload onUse = new TriggerPayload();
                onUse.Event = "onUse";
                onUse.SourceSide = item.Side;
                onUse.SourceItemId = item.Id;
                onUse.ChainId = chainId;
                onUse.Depth = 0;
                Triggers.Enqueue(ctx, onUse);
                Triggers.Drain(ctx, t);

                if (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0) CheckDeath(ctx, t);
                // 弹药已在激活开始扣减，此处不再逐施放扣减。
            }
        }

        private static bool EffectReady(BattleContext ctx, InternalItem item, Effect effect)
        {
            if (effect.Prerequisites == null) return true;
            NativeSkillRule rule = new NativeSkillRule { Prerequisites = effect.Prerequisites };
            return NativeSkillSupport.PrerequisitesMatch(ctx, item, rule);
        }

        private static void ScheduleNativeCasts(BattleContext ctx, InternalItem item, double t, List<ScheduledCast> pending)
        {
            if (FrozenActive(item, t) || item.Disabled || item.Destroyed) return;
            if (item.AmmoMax.HasValue && item.Ammo <= 0) return;
            long queueOrder = item.NativeReadyOrder != 0 ? item.NativeReadyOrder : ++ctx.NativeQueueSequence;
            item.NativeReadyOrder = 0;
            int chainId = ctx.NextChainId++;
            // The active cast count is latched before spending Ammo. Auras
            // such as Dive Weights then publish their new Multicast this frame
            // without removing casts already committed for this activation.
            int casts = Math.Max(0, 1 + item.MulticastBonus);
            item.Cooldown = item.CooldownMax;
            // 原生在就绪帧（冷却重置同帧）扣减一发弹药，整次激活（含多播）
            // 只消耗一发（06a610ca M9FNzRf：multicast 2 / 3 次激活各扣 1）。
            if (item.AmmoMax.HasValue)
            {
                int beforeAmmo = item.Ammo;
                item.Ammo = Math.Max(0, item.Ammo - 1);
                if (item.Ammo < beforeAmmo)
                    Effects.EmitCardAttribute(ctx, item, "Ammo", "Loss", beforeAmmo - item.Ammo, 0, chainId);
                NativeDynamicAuras.Refresh(ctx);
            }
            // 就绪期间到达的充能在重置后的新冷却上扣减（原生 170666bf 帧 161）。
            if (item.PendingChargeOnReset > 0)
            {
                item.Cooldown = Math.Max(0, item.Cooldown - item.PendingChargeOnReset);
                item.PendingChargeOnReset = 0;
            }
            for (int cast = 0; cast < casts; cast++)
            {
                List<Effect> eligible;
                List<Effect> skipped;
                SplitNativeEffects(ctx, item, out eligible, out skipped);
                ScheduledCast scheduled = new ScheduledCast { Item = item,
                    Time = ReserveNativeCastTime(ctx, item, t),
                    ImmediateAt = t,
                    QueueOrder = queueOrder,
                    ChainId = chainId, Generation = item.Generation,
                    EnabledAtStart = !item.Disabled,
                    EligibleEffects = eligible, SkippedEffects = skipped };
                pending.Add(scheduled);
                // Immediate own abilities run on the cooldown-reset frame;
                // the remaining abilities keep the following-frame timing.
                ExecuteNativeImmediateEffects(ctx, scheduled, t);
            }
        }

        // 前置条件决定能力是否结算，但原生仍把“已执行护盾”派发给观察者
        // （0276c46b 帧 58-60：Light Drone 第 1/2 次使用没有护盾，Sharp
        // Corners 依旧每帧 +6 伤害）。这里保留被前置拦下的能力以便补发。
        private static void SplitNativeEffects(BattleContext ctx, InternalItem item,
            out List<Effect> eligible, out List<Effect> skipped)
        {
            eligible = new List<Effect>();
            skipped = new List<Effect>();
            foreach (Effect effect in NativeSkillSupport.OrderedEffects(item.Card.Effects))
            {
                if (EffectReady(ctx, item, effect)) eligible.Add(effect);
                else skipped.Add(effect);
            }
        }

        private static double ReserveNativeCastTime(BattleContext ctx, InternalItem item, double t)
        {
            if (item.NativeCastGeneration != item.Generation)
            {
                item.NativeCastGeneration = item.Generation;
                item.NativeCastNextAt = 0;
            }
            double time = JsMath.RoundTo6(Math.Max(t + ctx.Rules.Dt, item.NativeCastNextAt));
            item.NativeCastNextAt = JsMath.RoundTo6(time + 0.25);
            return time;
        }

        private static void ExecuteNativeImmediateEffects(BattleContext ctx, ScheduledCast cast, double t)
        {
            if (cast.ImmediateSettled) return;
            cast.ImmediateSettled = true;
            InternalItem item = cast.Item;
            if (ctx.Ended || item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) return;
            foreach (Effect effect in cast.EligibleEffects)
            {
                if (effect.Priority != "Immediate" || effect.Kind == "multicast" || effect.Kind == "crit" || effect.Kind == "lifesteal") continue;
                Effects.ResolveEffect(ctx, effect, item, t, cast.Depth, cast.ChainId);
                if (ctx.Ended || item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) break;
            }
        }

        private static void ScheduleNativeForcedCast(BattleContext ctx, InternalItem item, double t, int depth, List<ScheduledCast> pending)
        {
            int chainId = ctx.NextChainId++;
            if (item.AmmoMax.HasValue)
            {
                item.Ammo = Math.Max(0, item.Ammo - 1);
                Effects.EmitCardAttribute(ctx, item, "Ammo", "Loss", 1, depth, chainId);
                NativeDynamicAuras.Refresh(ctx);
            }
            List<Effect> eligible;
            List<Effect> skipped;
            SplitNativeEffects(ctx, item, out eligible, out skipped);
            ScheduledCast cast = new ScheduledCast { Item = item, Time = ReserveNativeCastTime(ctx, item, t),
                QueueOrder = ++ctx.NativeQueueSequence,
                ImmediateAt = t, ChainId = chainId, Generation = item.Generation, Depth = depth, Forced = true,
                EnabledAtStart = !item.Disabled,
                EligibleEffects = eligible, SkippedEffects = skipped };
            pending.Add(cast);
            ExecuteNativeImmediateEffects(ctx, cast, t);
        }

        // 同一（充能者,目标）250ms 串行窗口内被延后的充能在此到期结算；
        // 若窗口又被更早的充能占用，顺延到新的 上次结算+0.25s。
        private static void ProcessDeferredCharges(BattleContext ctx, double t)
        {
            for (int index = ctx.DeferredCharges.Count - 1; index >= 0; index--)
            {
                DeferredCharge charge = ctx.DeferredCharges[index];
                if (ctx.Ended || charge.Charger == null || charge.Charger.Destroyed ||
                    charge.Target == null || charge.Target.Destroyed)
                {
                    ctx.DeferredCharges.RemoveAt(index);
                    continue;
                }
                if (charge.DueAt > t + 1e-9) continue;
                double last;
                if (ctx.LastChargeAt.TryGetValue(charge.Charger.Id + "|" + charge.Target.Id, out last) &&
                    t - last < Effects.ChargeSpacing - 1e-9)
                {
                    charge.DueAt = JsMath.RoundTo6(last + Effects.ChargeSpacing);
                    continue;
                }
                ctx.DeferredCharges.RemoveAt(index);
                Effects.SettleDeferredCharge(ctx, charge, t);
            }
        }

        private static void ExecuteNativeCasts(BattleContext ctx, double t, List<ScheduledCast> pending)
        {
            // A periodic packet may leave death pending until this frame's
            // already accepted casts settle. Keep that boundary through every
            // due cast (00a37bf3 frame 360), while ordinary card damage still
            // ends combat immediately after its own cast.
            bool deferredDeath = ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0;
            foreach (ScheduledCast cast in pending.ToList())
                if (cast.ImmediateAt <= t + 1e-9) ExecuteNativeImmediateEffects(ctx, cast, t);
            List<ScheduledCast> due = pending.Where(delegate(ScheduledCast cast) { return cast.Time <= t + 1e-9; }).ToList();
            if (ctx.ReplayRandom != null && due.Count > 1)
            {
                List<InternalItem> order = ctx.ReplayRandom.ApplyOrder((int)Math.Round(t / 0.05),
                    due.Select(delegate(ScheduledCast cast) { return cast.Item; }).ToList());
                due = due.OrderBy(delegate(ScheduledCast cast) { return order.IndexOf(cast.Item); }).ToList();
            }
            List<ScheduledCast> active = new List<ScheduledCast>();
            List<TriggerPayload> beforeUses = new List<TriggerPayload>();
            foreach (ScheduledCast cast in due)
            {
                pending.Remove(cast);
                if (ctx.Ended) break;
                InternalItem item = cast.Item;
                if (item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) continue;
                // 弹药资格已在 ScheduleNativeCasts 就绪帧判定并扣减，此处不再检查。
                BattleEvent activate = new BattleEvent { Time = t, Side = item.Side, SourceItemId = item.Id, SourceCardId = item.CardId, Kind = "activate", ChainId = cast.ChainId, Depth = cast.Depth };
                ctx.Events.Add(Effects.FinishEvent(ctx, activate));
                TriggerPayload beforeUse = new TriggerPayload { Event = "onBeforeUse", SourceSide = item.Side, SourceItemId = item.Id, ChainId = cast.ChainId, Depth = cast.Depth };
                Triggers.BeginNativeUse(ctx, beforeUse, t);
                active.Add(cast);
                beforeUses.Add(beforeUse);
            }
            // 原生同一帧就绪的多张卡：Highest/High 的属性修改先于其它卡的
            // 其它效果（00a37bf3 帧 120 的 DamageAmount 加伤必须让同帧武器
            // 伤害读到新值）。Medium 仍按施放预订顺序，Low/Lowest 在后续阶段结算。
            for (int rank = 1; rank <= 2 && !ctx.Ended; rank++)
            {
                for (int index = 0; index < active.Count && !ctx.Ended; index++)
                {
                    ScheduledCast cast = active[index];
                    InternalItem item = cast.Item;
                    foreach (Effect effect in cast.EligibleEffects)
                    {
                        if (item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) break;
                        if (effect.Kind != "modifyAttribute") continue;
                        if (NativeSkillSupport.EffectPriorityRank(effect.Priority) != rank) continue;
                        Effects.ResolveEffect(ctx, effect, item, t, cast.Depth, cast.ChainId);
                        if (ctx.Ended) break;
                    }
                }
            }
            List<int> late = new List<int>();
            List<int> lateCompletions = new List<int>();
            bool hasLow = active.Exists(delegate(ScheduledCast cast) {
                return cast.EligibleEffects.Exists(delegate(Effect effect) {
                    return NativeSkillSupport.EffectPriorityRank(effect.Priority) >= 4;
                });
            });
            for (int index = 0; index < active.Count && !ctx.Ended; index++)
            {
                ScheduledCast cast = active[index];
                Triggers.DrainNativeAfterEarlierCast(ctx, t, cast.QueueOrder, cast.Item);
                InternalItem item = cast.Item;
                if (item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) continue;
                foreach (Effect effect in cast.EligibleEffects)
                {
                    if (item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) break;
                    if (effect.Kind == "multicast" || effect.Kind == "crit" || effect.Kind == "lifesteal") continue;
                    if (effect.Priority == "Immediate" ||
                        NativeSkillSupport.EffectPriorityRank(effect.Priority) >= 4) continue;
                    if (effect.Kind == "modifyAttribute" &&
                        NativeSkillSupport.EffectPriorityRank(effect.Priority) <= 2) continue;
                    Effects.ResolveEffect(ctx, effect, item, t, cast.Depth, cast.ChainId);
                    if (ctx.Ended) break;
                }
                bool low = cast.EligibleEffects.Exists(delegate(Effect effect) {
                    return NativeSkillSupport.EffectPriorityRank(effect.Priority) >= 4;
                });
                if (low) late.Add(index);
                bool lifecycle = cast.EligibleEffects.Exists(delegate(Effect effect) {
                    return NativeSkillSupport.EffectPriorityRank(effect.Priority) >= 4 &&
                        (effect.Kind == "disable" || effect.Kind == "destroy" ||
                         effect.Kind == "transform" || effect.Kind == "transformDestroyed");
                });
                // ItemUsed observers retain their cast's reservation order even
                // when a Low action resolves in the later frame phase. A Low
                // lifecycle action must first decide whether its owner survives
                // to publish ItemUsed (07f044ee).
                if (lifecycle) lateCompletions.Add(index);
                else CompleteNativeCast(ctx, cast, beforeUses[index], t, !hasLow && !deferredDeath);
                // A Low phase postpones death for the whole accepted batch,
                // not only the cast preceding it (101f635 frame 240).
                if (!hasLow && !deferredDeath && !ctx.Config.EnemyIsTrainingDummy &&
                    (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0)) break;
            }
            // Low/Lowest actions share the frame's priority phase. In
            // particular, a Low heal cannot overheal before a different
            // already-due card's Medium damage (0466d70e frame 160).
            for (int rank = 4; rank <= 5 && !ctx.Ended; rank++)
                foreach (int index in late)
                {
                    ScheduledCast cast = active[index];
                    InternalItem item = cast.Item;
                    foreach (Effect effect in cast.EligibleEffects)
                    {
                        if (item.Destroyed || item.Disabled && !cast.Forced || item.Generation != cast.Generation) break;
                        if (NativeSkillSupport.EffectPriorityRank(effect.Priority) != rank) continue;
                        Effects.ResolveEffect(ctx, effect, item, t, cast.Depth, cast.ChainId);
                        if (ctx.Ended) break;
                    }
                }
            foreach (int index in lateCompletions)
                CompleteNativeCast(ctx, active[index], beforeUses[index], t, false);
            Triggers.DrainNativeAfterEarlierCast(ctx, t, long.MaxValue);
            if (!ctx.Ended && (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0))
            {
                Triggers.DrainNativeLateFrame(ctx, t);
                CheckDeath(ctx, t);
            }
        }

        private static void CompleteNativeCast(BattleContext ctx, ScheduledCast cast,
            TriggerPayload beforeUse, double t, bool checkDeath)
        {
            InternalItem item = cast.Item;
            if (cast.SkippedEffects != null)
                foreach (Effect effect in cast.SkippedEffects)
                    if (effect.Kind == "shield")
                        Effects.EmitPerformedShield(ctx, item, cast.Depth, cast.ChainId);
            Triggers.EndNativeUse(ctx, beforeUse, t);
            if (item.Destroyed || item.Generation != cast.Generation) return;
            // 原生在自身施放中把自己禁用的卡不再派发 ItemUsed：07f044ee 帧 142
            // 的变形副本先自禁用（Low），下一帧的 Inspired Rage 不再加怒气；
            // 施放前就已禁用（ForceUse 例外）或被后续事件禁用的卡仍会派发
            // （b60fa630 帧 161 的 Grapeshot 在帧 163 才被禁用，帧 166 的观察者照常执行）。
            if (cast.EnabledAtStart && item.Disabled) return;
            TriggerPayload onUse = new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id, ChainId = cast.ChainId, Depth = cast.Depth };
            Triggers.Enqueue(ctx, onUse);
            // Immediate 优先级的 onUse 被动与被用物品自身效果同帧结算。
            Triggers.DrainImmediateOnUse(ctx, t);
            if (checkDeath && (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0))
            {
                // Already due Low actions still belong to this frame,
                // including its final snapshot (b1621d7b frame 203).
                Triggers.DrainNativeLateFrame(ctx, t);
                CheckDeath(ctx, t);
            }
        }


        private static void CheckDeath(BattleContext ctx, double t)
        {
            // Training measures a complete window even if the player's own
            // effects exhaust their HP. Real monster combat still ends normally.
            if (ctx.Config.EnemyIsTrainingDummy) return;
            if (ctx.Ended) return;

            bool playerDead = ctx.Player.Hp <= 0;
            bool enemyDead = ctx.Enemy.Hp <= 0;
            if (!playerDead && !enemyDead) return;
            // Periodic packets may carry health debt through regeneration.
            // Revival begins at zero rather than inheriting that old debt.
            if (playerDead) ctx.Player.Hp = 0;
            if (enemyDead) ctx.Enemy.Hp = 0;

            // 死亡触发允许复活/回血。复活次数由每张卡自己的前提/计数器决定
            // （例如 Fiery Rebirth 的 Custom_0），不能用一个全局“每方一次”去
            // 代替所有技能；同一次死亡只派发一次 onPlayerDied。
            if (playerDead)
            {
                FirePlayerDied(ctx, Side.Player, t);
                playerDead = ctx.Player.Hp <= 0;
            }
            if (enemyDead)
            {
                FirePlayerDied(ctx, Side.Enemy, t);
                enemyDead = ctx.Enemy.Hp <= 0;
            }
            if (!playerDead && !enemyDead) return;

            ctx.Ended = true;
            ctx.EndTime = t;
            ctx.Winner = playerDead && enemyDead ? "draw" : (playerDead ? "enemy" : "player");

            BattleEvent death = new BattleEvent();
            death.Time = t;
            death.Side = playerDead ? Side.Player : Side.Enemy;
            death.Kind = "death";
            death.Value = 0;
            death.ChainId = ctx.NextChainId++;
            death.Depth = 0;
            ctx.Events.Add(Effects.FinishEvent(ctx, death));

            if (!(playerDead && enemyDead))
            {
                Side killerSide = playerDead ? Side.Enemy : Side.Player;
                int killChain = ctx.NextChainId++;
                foreach (InternalItem item in ctx.Items)
                {
                    if (item.Side != killerSide || item.Destroyed) continue;
                    if (ctx.IsTrainingDummySource(item.Side)) continue;
                    foreach (TriggerDef trigger in item.Card.Triggers)
                    {
                        if (trigger.Event != "onKill") continue;
                        foreach (Effect effect in trigger.Effects)
                            Effects.ResolveEffect(ctx, effect, item, t, 1, killChain);
                    }
                }
            }
            if (ctx.Config.NativeMechanics)
            {
                NativeTerminalAttributes.Commit(ctx, t);
                // Native terminal updates remove Flying from both boards,
                // without firing combat actions/observers after the result.
                foreach (InternalItem item in ctx.Items)
                {
                    item.Flying = false;
                    Effects.SetFlyingResistance(item, false);
                }
                NativeDynamicAuras.Refresh(ctx);
            }
        }

        private static void FirePlayerDied(BattleContext ctx, Side side, double t)
        {
            if (!ctx.Config.NativeMechanics)
            {
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onPlayerDied", SourceSide = side, ChainId = ctx.NextChainId++, Depth = 0 });
                Triggers.DrainWhileDead(ctx, t);
                return;
            }
            // Immediate health counters observe the lethal transition now;
            // ordinary reactions retain their next-frame timing after revival.
            Triggers.DrainImmediatePlayerAttributes(ctx, t);
            Queue<TriggerPayload> pending = ctx.TriggerQueue;
            ctx.TriggerQueue = new Queue<TriggerPayload>();
            try
            {
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onPlayerDied", SourceSide = side, ChainId = ctx.NextChainId++, Depth = 0 });
                Triggers.DrainWhileDead(ctx, t);
            }
            finally
            {
                Queue<TriggerPayload> remaining = ctx.TriggerQueue;
                ctx.TriggerQueue = pending;
                while (remaining.Count > 0) ctx.TriggerQueue.Enqueue(remaining.Dequeue());
            }
        }

        // 被强制使用的卡不重置冷却、不消耗弹药，只执行一次完整使用流程。
        public static void ForceUse(BattleContext ctx, InternalItem item, double t, int depth)
        {
            // 回放中的 CardForceUse 可强制使用刚被禁用的卡；Destroyed 仍拒绝。
            // Bear Trap 回放确认：0 弹药时强制使用空转；成功使用消耗 1 弹药。
            if (item == null || item.Destroyed || ctx.Ended) return;
            if (item.AmmoMax.HasValue && item.Ammo <= 0) return;
            if (depth > ctx.Rules.MaxTriggerDepth)
            {
                string warning = "强制使用嵌套超过上限，已停止 t=" + t.ToString();
                if (!ctx.Warnings.Contains(warning)) ctx.Warnings.Add(warning);
                return;
            }
            if (ctx.Config.NativeMechanics && ctx.NativeForceUseScheduler != null)
            {
                ctx.NativeForceUseScheduler(item, t, depth);
                return;
            }
            int generation = item.Generation;
            bool enabledAtStart = !item.Disabled;
            int chainId = ctx.NextChainId++;
            BattleEvent activate = new BattleEvent { Time = t, Side = item.Side, SourceItemId = item.Id, SourceCardId = item.CardId, Kind = "activate", ChainId = chainId, Depth = depth };
            ctx.Events.Add(Effects.FinishEvent(ctx, activate));
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onBeforeUse", SourceSide = item.Side, SourceItemId = item.Id, ChainId = chainId, Depth = depth });
            Triggers.DrainEvent(ctx, t, "onBeforeUse");
            foreach (Effect effect in NativeSkillSupport.OrderedEffects(item.Card.Effects))
            {
                if (item.Destroyed || item.Generation != generation) return;
                if (effect.Kind == "multicast" || effect.Kind == "crit" || effect.Kind == "lifesteal") continue;
                if (!EffectReady(ctx, item, effect)) continue;
                Effects.ResolveEffect(ctx, effect, item, t, depth, chainId);
                if (ctx.Ended) return;
            }
            if (item.Destroyed || item.Generation != generation) return;
            // 自身施放中自禁用不再派发 ItemUsed；施放前已禁用的强制使用仍派发。
            if (enabledAtStart && item.Disabled) return;
            Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id, ChainId = chainId, Depth = depth });
            Triggers.Drain(ctx, t);
            if (item.AmmoMax.HasValue) item.Ammo = Math.Max(0, item.Ammo - 1);
            if (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0) CheckDeath(ctx, t);
        }

        // 原生沙暴：20s 倒计时，30s 开始；卡牌可提前开始，但整场只开始一次。
        // tick/伤害参数来自 CombatReplays 的 23 份实测序列。
        private static void TickSandstorm(BattleContext ctx, double t)
        {
            if (!ctx.Config.NativeMechanics || !ctx.Config.SandstormEnabled) return;
            if (!ctx.SandstormStarted)
            {
                if (!ctx.SandstormCountdownStarted && t + 1e-9 >= ctx.Config.SandstormCountdownStart)
                {
                    ctx.SandstormCountdownStarted = true;
                    ctx.SandstormCountdownAt = t;
                }
                double startAt = ctx.Config.SandstormCountdownStart + ctx.Config.SandstormCountdownDuration;
                if (t + 1e-9 >= startAt)
                    Effects.StartSandstorm(ctx, null, t, 0, null, false);
            }
            if (!ctx.SandstormStarted || t + 1e-9 < ctx.NextSandstormTickAt) return;
            if (ctx.LastSandstormTickAt >= 0 && t <= ctx.LastSandstormTickAt + 1e-9) return;

            int index = ctx.SandstormTickIndex;
            double damage = index < 10
                ? ctx.Config.SandstormBaseDamage
                : ctx.Config.SandstormBaseDamage + ctx.Config.SandstormAdditiveScalar * (index - 9);
            int chain = ctx.NextChainId++;
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                DamageSource source = new DamageSource { Id = "sandstorm", CardId = "sandstorm", Side = side };
                DamageOptions opts = new DamageOptions
                {
                    IsDot = true,
                    DotKind = "sandstorm",
                    IsSandstorm = true,
                    ChainId = chain
                };
                Effects.ApplyDamage(ctx, source, side, damage, t, opts);
            }

            ctx.LastSandstormTickAt = t;
            ctx.SandstormTickIndex = index + 1;
            int stage = (index + 1) / 5;
            int rate = Math.Max(ctx.Config.SandstormTickRateFloor,
                ctx.Config.SandstormBaseTickRate - stage * ctx.Config.SandstormTickRateDecrement);
            ctx.NextSandstormTickAt = JsMath.RoundTo6(t + rate * ctx.Rules.Dt);
            CheckDeath(ctx, t);
        }

        // 原生 TempoGain：英雄按 TempoGainCooldownMax 自动 +1 Tempo。
        // 0276c46b：帧 0 Black Lipstick +4 之后，帧 20/41/62/83/104/125…
        // 每 21 帧（1050 ms）再 +1，正好等于 1000 ms 冷却 + 一帧结算延迟。
        private static void InitTempoGain(InternalHero hero, bool defaultInterval)
        {
            double max = 0, flat = 0, percent = 0;
            bool hasMax = false;
            if (hero.Attributes != null)
            {
                hasMax = hero.Attributes.TryGetValue("TempoGainCooldownMax", out max);
                hero.Attributes.TryGetValue("FlatTempoGainCooldownReduction", out flat);
                hero.Attributes.TryGetValue("PercentTempoGainCooldownReduction", out percent);
            }
            // 玩家存档未显式携带 TempoGainCooldownMax 时按原生默认 1000ms/点；
            // 07ae8be8：玩家开局属性只有 Health/Tempo/Shield/Poison/Burn，但帧 120/205
            // 的 Dragon Lighter 仍能支付 Tempo 代价（CardActionCostSpent）。
            if (!hasMax && defaultInterval) max = 1000;
            double interval = Math.Max(0, max - flat) * Math.Max(0, 1 - percent / 100.0);
            if (interval <= 0) return;
            hero.TempoGainInterval = interval / 1000.0;
            hero.NextTempoGainAt = hero.TempoGainInterval;
        }

        private static void TickTempoGain(BattleContext ctx, double t)
        {
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);
                if (hero.NextTempoGainAt < 0 || t < hero.NextTempoGainAt - 1e-9) continue;
                hero.Tempo += 1;
                hero.NextTempoGainAt = JsMath.RoundTo6(t + hero.TempoGainInterval + ctx.Rules.Dt);
            }
        }

        internal static void TickEnrage(BattleContext ctx, double t)
        {
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);
                if (hero.EnragedUntil <= 0 || t < hero.EnragedUntil - 1e-9) continue;
                hero.EnragedUntil = -1;
                foreach (InternalItem item in ctx.Items)
                {
                    if (item.Side != side || item.BaseRageCooldownMax < 0) continue;
                    item.Cooldown = Math.Max(0, item.Cooldown + item.BaseRageCooldownMax - item.CooldownMax);
                    item.CooldownMax = item.BaseRageCooldownMax;
                    item.BaseRageCooldownMax = -1;
                }
                ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onPlayerEnrageEnded", SourceSide = side, ChainId = ctx.NextChainId++, Depth = 0 });
                BattleEvent ev = new BattleEvent { Time = t, Side = side, TargetSide = side, Kind = "enrageEnd", Value = 0, ChainId = ctx.NextChainId++, Depth = 0 };
                ctx.Events.Add(Effects.FinishEvent(ctx, ev));
                // 若暴怒期间又积满了怒火，结束后立即重新进入。
                Effects.TryEnrage(ctx, hero, t, 0, null);
                // 暴怒结束必须立即撤销人物/卡牌动态光环，不能等下一帧。
                NativeDynamicAuras.Refresh(ctx);
            }
        }

        private static string ComputeWinner(BattleContext ctx)
        {
            double playerRatio = ctx.Player.Hp / ctx.Player.MaxHp;
            double enemyRatio = ctx.Enemy.Hp / ctx.Enemy.MaxHp;
            if (playerRatio > enemyRatio) return "player";
            if (enemyRatio > playerRatio) return "enemy";

            bool playerHasItems = ctx.Items.Any(delegate(InternalItem item) { return item.Side == Side.Player; });
            bool enemyHasItems = ctx.Items.Any(delegate(InternalItem item) { return item.Side == Side.Enemy; });
            if (playerHasItems && !enemyHasItems) return "player";
            if (enemyHasItems && !playerHasItems) return "enemy";
            return "draw";
        }

        private static void TickDots(BattleContext ctx, double t)
        {
            if (ctx.Ended) return;

            int dotChain = ctx.NextChainId++;
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);

                if (hero.Burn > 0)
                {
                    Side sourceSide = hero.BurnSource.HasValue
                        ? hero.BurnSource.Value
                        : SideUtil.Opposing(side);
                    DamageSource source = new DamageSource();
                    source.Id = hero.BurnSourceItemId ?? "dot:burn";
                    source.CardId = hero.BurnSourceCardId ?? "burn";
                    source.Side = sourceSide;
                    DamageOptions opts = new DamageOptions();
                    opts.IsDot = true;
                    opts.DotKind = "burn";
                    opts.ChainId = dotChain;
                    opts.Shares = StatusPools.Allocate(hero, "burn", hero.Burn);
                    Effects.ApplyDamage(ctx, source, side, hero.Burn, t, opts);
                    if (!ctx.Config.EnemyIsTrainingDummy && (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0))
                    {
                        CheckDeath(ctx, t);
                        return;
                    }
                    double decay = Math.Min(hero.Burn, Math.Max(0, ctx.Rules.BurnDecay));
                    hero.Burn = Math.Max(0, hero.Burn - decay);
                    StatusPools.Consume(hero, "burn", decay);
                }

                if (hero.Poison > 0)
                {
                    Side sourceSide = hero.PoisonSource.HasValue
                        ? hero.PoisonSource.Value
                        : SideUtil.Opposing(side);
                    DamageSource source = new DamageSource();
                    source.Id = hero.PoisonSourceItemId ?? "dot:poison";
                    source.CardId = hero.PoisonSourceCardId ?? "poison";
                    source.Side = sourceSide;
                    DamageOptions opts = new DamageOptions();
                    opts.IsDot = true;
                    opts.DotKind = "poison";
                    opts.ChainId = dotChain;
                    opts.Shares = StatusPools.Allocate(hero, "poison", hero.Poison);
                    Effects.ApplyDamage(ctx, source, side, hero.Poison, t, opts);
                    if (!ctx.Config.EnemyIsTrainingDummy && (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0))
                    {
                        CheckDeath(ctx, t);
                        return;
                    }
                    double decay = Math.Min(hero.Poison, Math.Max(0, ctx.Rules.PoisonDecay));
                    hero.Poison = Math.Max(0, hero.Poison - decay);
                    StatusPools.Consume(hero, "poison", decay);
                }

                if (hero.Regen > 0)
                {
                    Effects.ApplyHeal(
                        ctx,
                        side,
                        hero.Regen,
                        t,
                        hero.RegenSourceItemId,
                        hero.RegenSourceCardId,
                        dotChain,
                        0,
                        StatusPools.Allocate(hero, "regen", hero.Regen), false, "regen");

                    BattleEvent tick = new BattleEvent();
                    tick.Time = t;
                    tick.Side = side;
                    tick.TargetSide = side;
                    tick.SourceItemId = hero.RegenSourceItemId;
                    tick.SourceCardId = hero.RegenSourceCardId;
                    tick.Kind = "dotTick";
                    tick.Value = hero.Regen;
                    tick.HpAfter = hero.Hp;
                    tick.ChainId = dotChain;
                    tick.Depth = 0;
                    tick.Note = "regen";
                    ctx.Events.Add(Effects.FinishEvent(ctx, tick));
                }
            }
        }

        private static void TickNativeDots(BattleContext ctx, double t, bool second)
        {
            if (ctx.Ended) return;
            int chainId = ctx.NextChainId++;
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                InternalHero hero = ctx.Hero(side);
                // The native whole-second ledger is Poison, then Burn, then Regeneration.
                if (second && hero.Poison > 0)
                {
                    NativeDotDamage(ctx, hero, "poison", hero.Poison, t, chainId, false);
                    if (ctx.Ended) return;
                }
                if (hero.Burn > 0)
                {
                    double opening = hero.Burn;
                    double hpBefore = hero.Hp;
                    double shieldBefore = hero.Shield;
                    // Complete the Burn tick, including decay, before death
                    // reactions. A lethal packet still decays (b57a frame 360),
                    // even when death recovery raises HP above its prior value.
                    NativeDotDamage(ctx, hero, "burn", opening, t, chainId, false);
                    // A low Burn stack can be fully prevented by the native
                    // half-Shield rule (1 Burn into 50 Shield costs 0 Shield
                    // and 0 HP). Native keeps that stack; only a tick that
                    // actually moved HP or Shield is followed by decay.
                    bool dealt = hero.Hp < hpBefore - 1e-9 || hero.Shield < shieldBefore - 1e-9;
                    if (dealt)
                    {
                        double decay = Math.Min(opening, Math.Max(1, Math.Floor(opening * 0.03)));
                        hero.Burn = Math.Max(0, opening - decay);
                        StatusPools.Consume(hero, "burn", decay);
                    }
                }
                if (second && hero.Regen > 0)
                {
                    Effects.ApplyHeal(ctx, side, hero.Regen, t, hero.RegenSourceItemId, hero.RegenSourceCardId, chainId, 0,
                        StatusPools.Allocate(hero, "regen", hero.Regen), false, "regen");
                    BattleEvent tick = new BattleEvent { Time = t, Side = side, TargetSide = side, SourceItemId = hero.RegenSourceItemId, SourceCardId = hero.RegenSourceCardId, Kind = "dotTick", Value = hero.Regen, HpAfter = hero.Hp, ChainId = chainId, Note = "regen" };
                    ctx.Events.Add(Effects.FinishEvent(ctx, tick));
                }
                // Periodic damage and regeneration settle in one frame; a poison
                // packet reaching zero can be healed by the same tick (034c3648
                // frame 340: 430 - 430 + 103). When a card is due to act on this
                // frame, native settles the death after those actions instead
                // (07ae8be8 frame 300: lBehLui poison/hp bookkeeping precede the
                // CombatantDied event), so the settlement is deferred to the
                // end-of-frame CheckDeath in that case.
            }
            if (!HasNativeFrameWork(ctx, t)) CheckDeath(ctx, t);
        }

        private static bool HasNativeFrameWork(BattleContext ctx, double t)
        {
            if (!ctx.Config.NativeMechanics) return false;
            return CollectReady(ctx).Count > 0 || ctx.NativeEarliestDueCastOrder > 0 ||
                ctx.DeferredNativeTriggers.Exists(delegate(DeferredNativeTrigger queued) {
                    return queued.AfterEarlierCast && queued.DueAt <= t + 1e-9 &&
                        queued.Match != null && queued.Match.Owner != null &&
                        !queued.Match.Owner.Destroyed && !queued.Match.Owner.Disabled &&
                        queued.Match.Owner.Generation == queued.Generation;
                });
        }

        private static void NativeDotDamage(BattleContext ctx, InternalHero hero, string kind, double amount, double t, int chainId, bool settleDeath = true)
        {
            bool burn = kind == "burn";
            DamageSource source = new DamageSource
            {
                Id = (burn ? hero.BurnSourceItemId : hero.PoisonSourceItemId) ?? "dot:" + kind,
                CardId = (burn ? hero.BurnSourceCardId : hero.PoisonSourceCardId) ?? kind,
                Side = (burn ? hero.BurnSource : hero.PoisonSource) ?? SideUtil.Opposing(hero.Side)
            };
            Effects.ApplyDamage(ctx, source, hero.Side, amount, t, new DamageOptions
            {
                IsDot = true,
                DotKind = kind,
                PreservePeriodicHealth = true,
                ChainId = chainId,
                Shares = StatusPools.Allocate(hero, kind, amount)
            });
            // DoT packets bypass ResolveEffect's performed-action drain.
            // Immediate Health observers still run on this tick, not next frame.
            Triggers.DrainImmediateActions(ctx, t);
            if (settleDeath) CheckDeath(ctx, t);
        }

        private static HeroVitals CaptureVitals(InternalHero hero)
        {
            return new HeroVitals { Hp = Math.Max(0, hero.Hp), MaxHp = hero.MaxHp,
                Shield = hero.Shield, Burn = hero.Burn, Poison = hero.Poison, Regen = hero.Regen };
        }

        private static BattleHealthSnapshot CaptureHealth(BattleContext ctx, double time)
        {
            return new BattleHealthSnapshot { Time = time,
                Player = CaptureVitals(ctx.Player), Enemy = CaptureVitals(ctx.Enemy) };
        }

        private static BattleResult SimulateUnsafe(BattleConfig config)
        {
            BattleContext ctx = InitBattle(config);
            double playerStartHp = ctx.Player.Hp;
            double playerStartShield = ctx.Player.Shield;
            double enemyStartHp = ctx.Enemy.Hp;
            double enemyStartShield = ctx.Enemy.Shield;
            double dt = ctx.Rules.Dt;
            double maxTime = Math.Min(config.MaxTime, ctx.Rules.MaxTime);
            List<ScheduledCast> pendingCasts = new List<ScheduledCast>();
            if (config.NativeMechanics)
                ctx.NativeForceUseScheduler = delegate(InternalItem item, double time, int depth) {
                    ScheduleNativeForcedCast(ctx, item, time, depth, pendingCasts);
                };
            // 开局前与每帧结算后各保存双边状态，包含治疗、复活和生命上限变动。
            List<BattleHealthSnapshot> health = new List<BattleHealthSnapshot>();
            health.Add(CaptureHealth(ctx, -1));
            Dictionary<string, Dictionary<string, double>> openingAttributes = new Dictionary<string, Dictionary<string, double>>();
            if (config.NativeMechanics)
            {
                List<InternalItem> openingItems = new List<InternalItem>(ctx.Items);
                openingItems.AddRange(ctx.Skills);
                foreach (InternalItem item in openingItems)
                {
                    Dictionary<string, double> attributes = new Dictionary<string, double>();
                    CaptureItemAttributes(item, 0, attributes);
                    openingAttributes[item.Id] = attributes;
                }
            }

            // 原生战斗帧 0 最先执行 CombatOnly 开场光环（AuraExecuted 序列 0），
            // 之后才是 atCombatStart 技能；光环写入的 SellPrice 等属性当帧即可读。
            NativeDynamicAuras.ExecuteOpeningAuras(ctx);
            ctx.NativeOpeningEffects = config.NativeMechanics;
            foreach (InternalItem item in ctx.Items)
            {
                if (ctx.IsTrainingDummySource(item.Side)) continue;
                int chainId = ctx.NextChainId++;
                foreach (TriggerDef trigger in item.Card.Triggers)
                {
                    if (trigger.Event != "atCombatStart") continue;
                    if (!NativeSkillSupport.PrerequisitesMatch(ctx, item, trigger.SkillRule)) continue;
                    foreach (Effect effect in trigger.Effects)
                        Effects.ResolveEffect(ctx, effect, item, 0, 1, chainId);
                }
            }
            Triggers.FireSkillStart(ctx);
            ctx.NativeOpeningEffects = false;
            if (!config.NativeMechanics) Triggers.Drain(ctx, 0);
            CheckDeath(ctx, 0);

            for (int step = 0; step * dt <= maxTime + 1e-9; step++)
            {
                double t = JsMath.RoundTo6(step * dt);
                ctx.Now = t;
                ctx.EventsThisTick = 0;
                // Native periodic statuses resolve before the environmental
                // tick on the same frame; legacy keeps its existing order.
                if (!config.NativeMechanics) TickSandstorm(ctx, t);
                if (ctx.Ended)
                {
                    DeactivateDisabledAuras(ctx);
                    CaptureItemSnapshots(ctx, t);
                    health.Add(CaptureHealth(ctx, t));
                    break;
                }
                FlushPendingItems(ctx);
                // Native replay frame 0 already advances cooldowns by 50 ms.
                // The first 6 s card is readied on frame 119 and acts on frame
                // 120 (6.0 s); subsequent multicast casts are 5 frames apart.
                if (step > 0 || config.NativeMechanics) AdvanceCooldowns(ctx, t);
                ItemAttributes.Expire(ctx, t);
                foreach (Side side in new[] { Side.Player, Side.Enemy })
                {
                    InternalHero hero = ctx.Hero(side);
                    if (hero.ActionPercentDamageReductionUntil >= 0 && t > hero.ActionPercentDamageReductionUntil)
                    {
                        hero.ActionPercentDamageReduction = 0;
                        hero.ActionPercentDamageReductionUntil = -1;
                    }
                }
                NativeDynamicAuras.Refresh(ctx);
                TickEnrage(ctx, t);
                if (config.NativeMechanics) TickTempoGain(ctx, t);
                if (config.NativeMechanics && step > 0)
                {
                    ctx.NativePeriodicBeforeObservers = Math.Abs(t * 2 - Math.Round(t * 2)) < 1e-9;
                    ctx.NativeEarliestDueCastOrder = pendingCasts
                        .Where(delegate(ScheduledCast cast) { return cast.Time <= t + 1e-9; })
                        .Select(delegate(ScheduledCast cast) { return cast.QueueOrder; })
                        .DefaultIfEmpty(0).Min();
                    Triggers.DrainNativeFrame(ctx, t, true);
                }
                // 延迟充能到期结算（同对 250ms 串行），紧随本帧触发器之后。
                if (config.NativeMechanics) ProcessDeferredCharges(ctx, t);
                FlushPendingItems(ctx);
                if (config.NativeMechanics && step == 0)
                {
                    // Native frame 0 already contains the first DoT tick for
                    // statuses applied by opening skills / at-combat-start.
                    TickNativeDots(ctx, t, true);
                    if (ctx.Ended)
                    {
                        DeactivateDisabledAuras(ctx);
                        CaptureItemSnapshots(ctx, t);
                        health.Add(CaptureHealth(ctx, t));
                        break;
                    }
                }

                double previous = (step - 1) * dt;
                bool crossedSecond = step > 0 &&
                    Math.Floor(t + 1e-9) > Math.Floor(previous + 1e-9);
                bool crossedHalfSecond = step > 0 && Math.Floor(t * 2 + 1e-9) > Math.Floor(previous * 2 + 1e-9);

                if (config.NativeMechanics && crossedHalfSecond)
                    TickNativeDots(ctx, t, crossedSecond);

                if (crossedSecond)
                {
                    if (!config.NativeMechanics) TickDots(ctx, t);

                    foreach (InternalItem item in ctx.Items)
                    {
                        if (ctx.IsTrainingDummySource(item.Side) || item.Destroyed || item.Disabled) continue;
                        int chainId = ctx.NextChainId++;
                        foreach (TriggerDef trigger in item.Card.Triggers)
                        {
                            if (trigger.Event != "everySecond") continue;
                            double every = trigger.EverySeconds.HasValue
                                ? trigger.EverySeconds.Value
                                : 1;
                            double quotient = t / every;
                            if (Math.Abs(quotient - JsMath.Round(quotient)) > 1e-6) continue;
                            foreach (Effect effect in trigger.Effects)
                                Effects.ResolveEffect(ctx, effect, item, t, 1, chainId);
                        }
                    }

                    if (!config.NativeMechanics) Triggers.Drain(ctx, t);
                    if (!HasNativeFrameWork(ctx, t)) CheckDeath(ctx, t);
                }

                // Native frame order is periodic statuses -> Sandstorm ->
                // card actions. Shield gained by a card this frame cannot
                // retroactively absorb the environmental packet (baca661).
                if (config.NativeMechanics)
                {
                    TickSandstorm(ctx, t);
                    if (ctx.Ended)
                    {
                        DeactivateDisabledAuras(ctx);
                        CaptureItemSnapshots(ctx, t);
                        health.Add(CaptureHealth(ctx, t));
                        break;
                    }
                }

                // Ready-frame Immediate abilities precede previously queued
                // Medium actions on the same frame. Those actions may then
                // charge additional items to ready, handled below.
                List<InternalItem> readyBeforeActions = null;
                if (config.NativeMechanics)
                {
                    readyBeforeActions = CollectReady(ctx);
                    if (ctx.ReplayRandom != null)
                        readyBeforeActions = ctx.ReplayRandom.ApplyOrder((int)Math.Round(t / 0.05) + 1, readyBeforeActions, false);
                    foreach (InternalItem item in readyBeforeActions)
                    {
                        if (ctx.Ended) break;
                        ScheduleNativeCasts(ctx, item, t, pendingCasts);
                    }
                    ExecuteNativeCasts(ctx, t, pendingCasts);
                    Triggers.DrainNativeLateFrame(ctx, t);
                }
                FlushPendingItems(ctx);

                List<InternalItem> ready = CollectReady(ctx);
                if (readyBeforeActions != null)
                    ready.RemoveAll(delegate(InternalItem item) { return readyBeforeActions.Contains(item); });
                if (ctx.ReplayRandom != null)
                {
                    // Ready checks land one loop frame before the native event
                    // frame (native frame 0 already advances cooldowns by 50 ms),
                    // so the order tape is keyed by the native frame.
                    ready = ctx.ReplayRandom.ApplyOrder((int)Math.Round(t / 0.05) + 1, ready, false);
                }
                foreach (InternalItem item in ready)
                {
                    if (ctx.Ended) break;
                    if (config.NativeMechanics) ScheduleNativeCasts(ctx, item, t, pendingCasts);
                    else Activate(ctx, item, t);
                }

                if (!config.NativeMechanics) Triggers.Drain(ctx, t);
                FlushPendingItems(ctx);
                TickSandstorm(ctx, t);
                CheckDeath(ctx, t);

                DeactivateDisabledAuras(ctx);

                CaptureItemSnapshots(ctx, t);
                health.Add(CaptureHealth(ctx, t));

                if (ctx.Ended) break;
            }

            if (!ctx.Ended)
            {
                ctx.EndTime = maxTime;
                ctx.Winner = ComputeWinner(ctx);
            }

            BattleResult result = new BattleResult();
            result.Events = ctx.Events;
            result.EffectStats = ctx.EffectStats;
            result.ItemSnapshots = ctx.ItemSnapshots;
            result.OpeningItemAttributes = openingAttributes;
            if (ctx.ReplayRandom != null) result.ReplayRandom = ctx.ReplayRandom.Audit;
            result.HealthSnapshots = health;
            result.EndedByDeath = ctx.Ended;
            result.EnemyStartHp = enemyStartHp;
            result.EnemyStartShield = enemyStartShield;
            result.PlayerStartHp = playerStartHp;
            result.PlayerStartShield = playerStartShield;
            foreach (InternalItem item in ctx.Items)
            {
                // 包括已经离场的实例，拖回过去仍能查看其原形态和出手历史。
                BattleItemView view = item.History;
                if (item.Side == Side.Player) result.PlayerItems.Add(view);
                else result.EnemyItems.Add(view);
            }
            result.PlayerItems.Sort(delegate(BattleItemView a, BattleItemView b)
            {
                return a.Slot.CompareTo(b.Slot);
            });
            result.EnemyItems.Sort(delegate(BattleItemView a, BattleItemView b)
            {
                return a.Slot.CompareTo(b.Slot);
            });
            result.Totals = ctx.Totals;
            result.Winner = ctx.Winner ?? "draw";
            result.EndTime = ctx.EndTime;
            result.SandstormStarted = ctx.SandstormStarted;
            result.SandstormStartedAt = ctx.SandstormStartedAt;
            result.Warnings = ctx.Warnings;
            return result;
        }
    }
}
