using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public static class TrainingDummy
    {
        public const double MaxHp = 100000;

        // The random cards are a visible target board, not a prediction of the
        // real opponent. Their source effects are disabled by the dummy flag.
        public static void Populate(BattleConfig config, CardCatalog catalog, double seed)
        {
            if (config == null) throw new ArgumentNullException("config");
            foreach (LoadoutItem entry in config.Enemy.Items)
                if (entry.CardId != null && entry.CardId.StartsWith("dummy:", StringComparison.Ordinal))
                    config.Cards.Remove(entry.CardId);
            config.Enemy.Items.Clear();
            config.EnemySkills.Clear();
            config.EnemyIsTrainingDummy = true;
            config.EnemyMaxHp = MaxHp;
            config.EnemyStartHp = MaxHp;

            List<CardDef> pool = new List<CardDef>();
            if (catalog != null)
                foreach (CardDef card in catalog.Cards)
                    if (card != null && !string.IsNullOrEmpty(card.Id) &&
                        string.Equals(card.ParseStatus, "structured", StringComparison.OrdinalIgnoreCase))
                        pool.Add(card);
            // Sorting keeps a seed stable even if catalog enumeration changes.
            pool.Sort(delegate(CardDef a, CardDef b) { return string.CompareOrdinal(a.Id, b.Id); });

            Rng rng = new Rng(seed);
            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int slot = 0;
            while (slot < PanelModel.BoardSlots)
            {
                List<CardDef> fitting = new List<CardDef>();
                foreach (CardDef candidate in pool)
                    if (!selected.Contains(BaseKey(candidate)) &&
                        slot + PanelModel.CardSpan(candidate) <= PanelModel.BoardSlots)
                        fitting.Add(candidate);
                if (fitting.Count == 0) break;

                CardDef original = fitting[rng.Int(fitting.Count)];
                selected.Add(BaseKey(original));
                CardDef card = original.Clone();
                card.Id = "dummy:" + slot + ":" + original.Id;
                config.Cards[card.Id] = card;
                config.Enemy.Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
                slot += PanelModel.CardSpan(card);
            }
        }

        private static string BaseKey(CardDef card)
        {
            return string.IsNullOrEmpty(card.BaseItemId) ? card.Id : card.BaseItemId;
        }
    }
}
