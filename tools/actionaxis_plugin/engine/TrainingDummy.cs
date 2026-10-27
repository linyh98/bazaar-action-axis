using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public static class TrainingDummy
    {
        public const double MaxHp = 100000;

        // Synthetic targets preserve size and cooldown interactions without
        // borrowing any real opponent's identity, tags, abilities or artwork.
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
            config.EnemyStartShield = 0;
            config.EnemyStartRegen = 0;
            config.EnemyHero = null;
            config.MaxTime = 30;
            if (config.Rules == null) config.Rules = new RuleOverrides();
            config.Rules.MaxTime = 30;

            Rng rng = new Rng(seed);
            List<int> spans = new List<int> { 1, 2, 3 };
            for (int i = spans.Count - 1; i > 0; i--)
            {
                int other = rng.Int(i + 1);
                int span = spans[i];
                spans[i] = spans[other];
                spans[other] = span;
            }
            // Every board contains all three sizes and both cooldown modes.
            bool firstHasCooldown = rng.Chance(0.5);
            int slot = 0;
            int index = 0;
            while (slot < PanelModel.BoardSlots)
            {
                int span = index < spans.Count ? spans[index] :
                    1 + rng.Int(Math.Min(3, PanelModel.BoardSlots - slot));
                bool hasCooldown = index == 0 ? firstHasCooldown :
                    index == 1 ? !firstHasCooldown : rng.Chance(0.5);
                string size = span == 3 ? "Large" : span == 2 ? "Medium" : "Small";
                CardDef card = new CardDef
                {
                    Id = "dummy:" + slot + ":blank-" + size.ToLowerInvariant(),
                    NameCn = "空白卡牌", Size = size, ParseStatus = "structured",
                    Cooldown = hasCooldown ? 3 + rng.Int(6) : 0,
                    Passive = !hasCooldown, DataSource = "training-dummy"
                };
                config.Cards[card.Id] = card;
                config.Enemy.Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
                slot += span;
                index++;
            }
        }
    }
}
