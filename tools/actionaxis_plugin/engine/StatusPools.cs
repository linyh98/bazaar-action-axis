using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Burn, Poison and Regen are scalar hero pools, but native applies each
    // stack to the item that created it. Contributions let a tick split its
    // measured result among the current owners without changing stack totals.
    public sealed class StatusContribution
    {
        public Side Side;
        public string SourceItemId;
        public string SourceCardId;
        public double Amount;
    }

    public static class StatusPools
    {
        public static List<StatusContribution> For(InternalHero hero, string kind)
        {
            if (kind == "burn") return hero.BurnPools;
            if (kind == "poison") return hero.PoisonPools;
            return hero.RegenPools;
        }

        public static void Add(InternalHero hero, string kind, Side side, string itemId, string cardId, double amount)
        {
            if (amount <= 0) return;
            List<StatusContribution> pools = For(hero, kind);
            foreach (StatusContribution pool in pools)
            {
                if (pool.Side != side || pool.SourceItemId != itemId || pool.SourceCardId != cardId) continue;
                pool.Amount += amount;
                return;
            }
            pools.Add(new StatusContribution
            {
                Side = side,
                SourceItemId = itemId,
                SourceCardId = cardId,
                Amount = amount
            });
        }

        public static void Consume(InternalHero hero, string kind, double amount)
        {
            if (amount <= 0) return;
            List<StatusContribution> pools = For(hero, kind);
            double sum = Sum(pools);
            if (sum <= 1e-9) return;
            if (amount >= sum - 1e-9)
            {
                pools.Clear();
                return;
            }
            double keep = Math.Max(0, 1 - amount / sum);
            for (int index = pools.Count - 1; index >= 0; index--)
            {
                pools[index].Amount *= keep;
                if (pools[index].Amount <= 1e-9) pools.RemoveAt(index);
            }
        }

        public static void Clear(InternalHero hero, string kind)
        {
            For(hero, kind).Clear();
        }

        // Splits a measured pool value by current ownership. Callers apply the
        // aggregate damage/heal once and then distribute the result; this keeps
        // shield cost and Max-HP capping from being evaluated per source.
        public static List<BattleEventShare> Allocate(InternalHero hero, string kind, double total)
        {
            List<BattleEventShare> shares = new List<BattleEventShare>();
            if (total <= 0) return shares;
            List<StatusContribution> pools = For(hero, kind);
            double sum = Sum(pools);
            if (sum <= 1e-9)
            {
                Side fallbackSide;
                string fallbackId;
                string fallbackCard;
                LegacySource(hero, kind, out fallbackSide, out fallbackId, out fallbackCard);
                shares.Add(new BattleEventShare
                {
                    Side = fallbackSide,
                    SourceItemId = fallbackId,
                    SourceCardId = fallbackCard,
                    Amount = total
                });
                return shares;
            }

            double allocated = 0;
            for (int index = 0; index < pools.Count; index++)
            {
                StatusContribution pool = pools[index];
                double amount = index == pools.Count - 1
                    ? Math.Max(0, total - allocated)
                    : total * pool.Amount / sum;
                allocated += amount;
                if (amount <= 0) continue;
                BattleEventShare share = Find(shares, pool.Side, pool.SourceItemId, pool.SourceCardId);
                if (share == null)
                {
                    share = new BattleEventShare
                    {
                        Side = pool.Side,
                        SourceItemId = pool.SourceItemId,
                        SourceCardId = pool.SourceCardId
                    };
                    shares.Add(share);
                }
                share.Amount += amount;
            }
            if (shares.Count == 0)
            {
                Side fallbackSide;
                string fallbackId;
                string fallbackCard;
                LegacySource(hero, kind, out fallbackSide, out fallbackId, out fallbackCard);
                shares.Add(new BattleEventShare
                {
                    Side = fallbackSide,
                    SourceItemId = fallbackId,
                    SourceCardId = fallbackCard,
                    Amount = total
                });
            }
            return shares;
        }

        private static BattleEventShare Find(List<BattleEventShare> shares, Side side, string itemId, string cardId)
        {
            foreach (BattleEventShare share in shares)
                if (share.Side == side && share.SourceItemId == itemId && share.SourceCardId == cardId) return share;
            return null;
        }

        private static double Sum(List<StatusContribution> pools)
        {
            double sum = 0;
            foreach (StatusContribution pool in pools) sum += pool.Amount;
            return sum;
        }

        private static void LegacySource(InternalHero hero, string kind, out Side side, out string itemId, out string cardId)
        {
            if (kind == "burn")
            {
                side = hero.BurnSource ?? SideUtil.Opposing(hero.Side);
                itemId = hero.BurnSourceItemId ?? "dot:burn";
                cardId = hero.BurnSourceCardId ?? "burn";
            }
            else if (kind == "poison")
            {
                side = hero.PoisonSource ?? SideUtil.Opposing(hero.Side);
                itemId = hero.PoisonSourceItemId ?? "dot:poison";
                cardId = hero.PoisonSourceCardId ?? "poison";
            }
            else
            {
                side = hero.Side;
                itemId = hero.RegenSourceItemId ?? "dot:regen";
                cardId = hero.RegenSourceCardId ?? "regen";
            }
        }
    }
}
