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
            ctx.Rules = rules;
            ctx.Rng = new Rng(config.Seed);
            ctx.Player = new InternalHero(Side.Player);
            ctx.Player.Hp = config.PlayerStartHp.HasValue ? Math.Max(0, Math.Min(config.PlayerMaxHp, config.PlayerStartHp.Value)) : config.PlayerMaxHp;
            ctx.Player.MaxHp = config.PlayerMaxHp;
            ctx.Enemy = new InternalHero(Side.Enemy);
            ctx.Enemy.Hp = config.EnemyStartHp.HasValue ? Math.Max(0, Math.Min(config.EnemyMaxHp, config.EnemyStartHp.Value)) : config.EnemyMaxHp;
            ctx.Enemy.MaxHp = config.EnemyMaxHp;
            ctx.Enemy.Shield = Math.Max(0, config.EnemyStartShield);
            ctx.Enemy.Regen = Math.Max(0, config.EnemyStartRegen);

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
                    item.Cooldown = startReady ? 0 : card.Cooldown;
                    item.Ammo = card.AmmoMax.HasValue ? card.AmmoMax.Value : 0;
                    item.AmmoMax = card.AmmoMax;
                    ApplyStaticEffects(item);
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
                    ctx.Skills.Add(skill);
                }
            }
            return ctx;
        }

        private static Loadout LoadoutFor(BattleConfig config, Side side)
        {
            return side == Side.Player ? config.Player : config.Enemy;
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

        private static void AdvanceCooldowns(BattleContext ctx, double t)
        {
            foreach (InternalItem item in ctx.Items)
            {
                double rate = CooldownRate(ctx, item, t);
                item.Cooldown = Math.Max(0, item.Cooldown - ctx.Rules.Dt * rate);
            }
        }

        private static double CooldownRate(BattleContext ctx, InternalItem item, double t)
        {
            if (ctx.IsTrainingDummySource(item.Side)) return 0;
            bool frozen = StatusActive(ctx, item.FrozenUntil, t);
            bool hasted = StatusActive(ctx, item.HasteUntil, t);
            bool slowed = StatusActive(ctx, item.SlowUntil, t);
            return frozen ? 0 :
                (hasted ? ctx.Rules.HasteMultiplier : 1) *
                (slowed ? ctx.Rules.SlowMultiplier : 1);
        }

        private static bool StatusActive(BattleContext ctx, double until, double t)
        {
            // Native attributes expire before this frame's cooldown advance.
            return ctx.Config.NativeMechanics ? t < until - 1e-9 : t <= until + 1e-9;
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
                snapshot.CooldownMax = item.Card.Cooldown;
                snapshot.HasCooldown = !ctx.IsTrainingDummySource(item.Side) && !item.Card.Passive && item.Card.Cooldown > 0;
                snapshot.Activated = activated.Contains(item.Id);
                // The next interval uses the same status-expiry boundary as AdvanceCooldowns.
                snapshot.CooldownRate = CooldownRate(ctx, item, t + ctx.Rules.Dt);
                snapshot.Ammo = item.Ammo;
                snapshot.AmmoMax = item.AmmoMax;
                ctx.ItemSnapshots.Add(snapshot);
            }
        }

        private static List<InternalItem> CollectReady(BattleContext ctx)
        {
            List<InternalItem> ready = new List<InternalItem>();
            foreach (InternalItem item in ctx.Items)
            {
                if (ctx.IsTrainingDummySource(item.Side)) continue;
                if (!item.Card.Passive && item.Card.Cooldown > 0 && item.Cooldown <= 1e-9)
                    ready.Add(item);
            }

            return ready
                .OrderBy(delegate(InternalItem item) { return item.Side == Side.Player ? 0 : 1; })
                .ThenBy(delegate(InternalItem item) { return item.Slot; })
                .ToList();
        }

        private static void Activate(BattleContext ctx, InternalItem item, double t)
        {
            if (StatusActive(ctx, item.FrozenUntil, t)) return;
            if (item.AmmoMax.HasValue && item.Ammo <= 0) return;

            int chainId = ctx.NextChainId++;
            int casts = 1 + item.MulticastBonus;
            item.Cooldown = item.Card.Cooldown;

            for (int cast = 0; cast < casts; cast++)
            {
                if (ctx.Ended) break;
                if (item.AmmoMax.HasValue && item.Ammo <= 0) break;

                BattleEvent activate = new BattleEvent();
                activate.Time = t;
                activate.Side = item.Side;
                activate.SourceItemId = item.Id;
                activate.SourceCardId = item.CardId;
                activate.Kind = "activate";
                activate.ChainId = chainId;
                activate.Depth = 0;
                ctx.Events.Add(Effects.FinishEvent(ctx, activate));

                foreach (Effect effect in item.Card.Effects)
                {
                    if (effect.Kind == "multicast" ||
                        effect.Kind == "crit" ||
                        effect.Kind == "lifesteal")
                        continue;

                    Effects.ResolveEffect(ctx, effect, item, t, 0, chainId);
                    if (ctx.Ended) break;
                }

                TriggerPayload onUse = new TriggerPayload();
                onUse.Event = "onUse";
                onUse.SourceSide = item.Side;
                onUse.SourceItemId = item.Id;
                onUse.ChainId = chainId;
                onUse.Depth = 0;
                Triggers.Enqueue(ctx, onUse);
                Triggers.Drain(ctx, t);

                if (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0) CheckDeath(ctx, t);

                if (item.AmmoMax.HasValue)
                    item.Ammo = Math.Max(0, item.Ammo - 1);
            }
        }

        private static void ScheduleNativeCasts(BattleContext ctx, InternalItem item, double t, List<ScheduledCast> pending)
        {
            if (StatusActive(ctx, item.FrozenUntil, t)) return;
            if (item.AmmoMax.HasValue && item.Ammo <= 0) return;
            int chainId = ctx.NextChainId++;
            item.Cooldown = item.Card.Cooldown;
            int casts = 1 + item.MulticastBonus;
            for (int cast = 0; cast < casts; cast++)
                pending.Add(new ScheduledCast { Item = item, Time = JsMath.RoundTo6(t + ctx.Rules.Dt + cast * 0.25), ChainId = chainId });
        }

        private static void ExecuteNativeCasts(BattleContext ctx, double t, List<ScheduledCast> pending)
        {
            List<ScheduledCast> due = pending.Where(delegate(ScheduledCast cast) { return cast.Time <= t + 1e-9; }).ToList();
            foreach (ScheduledCast cast in due)
            {
                pending.Remove(cast);
                if (ctx.Ended) break;
                InternalItem item = cast.Item;
                if (item.AmmoMax.HasValue && item.Ammo <= 0) continue;
                BattleEvent activate = new BattleEvent { Time = t, Side = item.Side, SourceItemId = item.Id, SourceCardId = item.CardId, Kind = "activate", ChainId = cast.ChainId };
                ctx.Events.Add(Effects.FinishEvent(ctx, activate));
                foreach (Effect effect in item.Card.Effects)
                {
                    if (effect.Kind == "multicast" || effect.Kind == "crit" || effect.Kind == "lifesteal") continue;
                    Effects.ResolveEffect(ctx, effect, item, t, 0, cast.ChainId);
                    if (ctx.Ended) break;
                }
                TriggerPayload onUse = new TriggerPayload { Event = "onUse", SourceSide = item.Side, SourceItemId = item.Id, ChainId = cast.ChainId, Depth = 0 };
                Triggers.Enqueue(ctx, onUse);
                if (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0) CheckDeath(ctx, t);
                if (item.AmmoMax.HasValue) item.Ammo = Math.Max(0, item.Ammo - 1);
            }
        }

        private static void CheckDeath(BattleContext ctx, double t)
        {
            if (ctx.Ended) return;

            bool playerDead = ctx.Player.Hp <= 0;
            bool enemyDead = ctx.Enemy.Hp <= 0;
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
                    if (item.Side != killerSide) continue;
                    if (ctx.IsTrainingDummySource(item.Side)) continue;
                    foreach (TriggerDef trigger in item.Card.Triggers)
                    {
                        if (trigger.Event != "onKill") continue;
                        foreach (Effect effect in trigger.Effects)
                            Effects.ResolveEffect(ctx, effect, item, t, 1, killChain);
                    }
                }
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
                    Effects.ApplyDamage(ctx, source, side, hero.Burn, t, opts);
                    if (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0)
                    {
                        CheckDeath(ctx, t);
                        return;
                    }
                    hero.Burn = Math.Max(0, hero.Burn - ctx.Rules.BurnDecay);
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
                    Effects.ApplyDamage(ctx, source, side, hero.Poison, t, opts);
                    if (ctx.Player.Hp <= 0 || ctx.Enemy.Hp <= 0)
                    {
                        CheckDeath(ctx, t);
                        return;
                    }
                    hero.Poison = Math.Max(0, hero.Poison - ctx.Rules.PoisonDecay);
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
                        0);

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
                    NativeDotDamage(ctx, hero, "poison", hero.Poison, t, chainId);
                    if (ctx.Ended) return;
                }
                if (hero.Burn > 0)
                {
                    double opening = hero.Burn;
                    NativeDotDamage(ctx, hero, "burn", opening, t, chainId);
                    if (ctx.Ended) return;
                    hero.Burn = Math.Max(0, opening - Math.Max(1, Math.Floor(opening * 0.03)));
                }
                if (second && hero.Regen > 0)
                {
                    Effects.ApplyHeal(ctx, side, hero.Regen, t, hero.RegenSourceItemId, hero.RegenSourceCardId, chainId, 0);
                    BattleEvent tick = new BattleEvent { Time = t, Side = side, TargetSide = side, SourceItemId = hero.RegenSourceItemId, SourceCardId = hero.RegenSourceCardId, Kind = "dotTick", Value = hero.Regen, HpAfter = hero.Hp, ChainId = chainId, Note = "regen" };
                    ctx.Events.Add(Effects.FinishEvent(ctx, tick));
                }
            }
        }

        private static void NativeDotDamage(BattleContext ctx, InternalHero hero, string kind, double amount, double t, int chainId)
        {
            bool burn = kind == "burn";
            DamageSource source = new DamageSource
            {
                Id = (burn ? hero.BurnSourceItemId : hero.PoisonSourceItemId) ?? "dot:" + kind,
                CardId = (burn ? hero.BurnSourceCardId : hero.PoisonSourceCardId) ?? kind,
                Side = (burn ? hero.BurnSource : hero.PoisonSource) ?? SideUtil.Opposing(hero.Side)
            };
            Effects.ApplyDamage(ctx, source, hero.Side, amount, t, new DamageOptions { IsDot = true, DotKind = kind, ChainId = chainId });
            CheckDeath(ctx, t);
        }

        private static BattleResult SimulateUnsafe(BattleConfig config)
        {
            BattleContext ctx = InitBattle(config);
            double dt = ctx.Rules.Dt;
            double maxTime = Math.Min(config.MaxTime, ctx.Rules.MaxTime);
            List<ScheduledCast> pendingCasts = new List<ScheduledCast>();

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
            if (!config.NativeMechanics) Triggers.Drain(ctx, 0);
            CheckDeath(ctx, 0);

            for (int step = 0; step * dt <= maxTime + 1e-9; step++)
            {
                double t = JsMath.RoundTo6(step * dt);
                ctx.EventsThisTick = 0;
                // Native replay frame 0 already advances cooldowns by 50 ms.
                // The first 6 s card is readied on frame 119 and acts on frame
                // 120 (6.0 s); subsequent multicast casts are 5 frames apart.
                if (step > 0 || config.NativeMechanics) AdvanceCooldowns(ctx, t);
                if (config.NativeMechanics) Triggers.DrainNativeFrame(ctx, t);

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
                        if (ctx.IsTrainingDummySource(item.Side)) continue;
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
                    CheckDeath(ctx, t);
                }

                // An action can charge another item to ready on this frame;
                // native then schedules that item's action for the next frame.
                if (config.NativeMechanics) ExecuteNativeCasts(ctx, t, pendingCasts);

                List<InternalItem> ready = CollectReady(ctx);
                foreach (InternalItem item in ready)
                {
                    if (ctx.Ended) break;
                    if (config.NativeMechanics) ScheduleNativeCasts(ctx, item, t, pendingCasts);
                    else Activate(ctx, item, t);
                }

                if (!config.NativeMechanics) Triggers.Drain(ctx, t);
                CheckDeath(ctx, t);

                CaptureItemSnapshots(ctx, t);

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
            result.EnemyStartHp = config.EnemyStartHp.HasValue
                ? Math.Max(0, Math.Min(config.EnemyMaxHp, config.EnemyStartHp.Value))
                : config.EnemyMaxHp;
            result.EnemyStartShield = config.EnemyStartShield;
            foreach (InternalItem item in ctx.Items)
            {
                BattleItemView view = new BattleItemView();
                view.Id = item.Id;
                view.CardId = item.CardId;
                view.Slot = item.Slot;
                view.Side = item.Side;
                view.Card = item.Card;
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
            result.Warnings = ctx.Warnings;
            return result;
        }
    }
}
