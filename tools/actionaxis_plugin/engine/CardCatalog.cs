using System;
using System.Collections.Generic;
using System.IO;

namespace BazaarActionAxis.Engine
{
    public sealed class CardCatalog
    {
        private static readonly string[] Tiers =
        {
            "Bronze", "Silver", "Gold", "Diamond", "Legendary"
        };

        private readonly Dictionary<string, List<CardDef>> _byBaseId =
            new Dictionary<string, List<CardDef>>();

        public List<CardDef> Cards { get; private set; }

        private CardCatalog(List<CardDef> cards)
        {
            Cards = cards;
            foreach (CardDef card in cards)
            {
                string key = Normalize(card.BaseItemId);
                if (key.Length == 0) continue;
                List<CardDef> group;
                if (!_byBaseId.TryGetValue(key, out group))
                {
                    group = new List<CardDef>();
                    _byBaseId[key] = group;
                }
                group.Add(card);
            }

            foreach (List<CardDef> group in _byBaseId.Values)
            {
                group.Sort(delegate(CardDef a, CardDef b)
                {
                    return TierRank(a.Tier).CompareTo(TierRank(b.Tier));
                });
            }
        }

        public static CardCatalog Load(string path)
        {
            string text = File.ReadAllText(path);
            JsonValue root = Json.Parse(text);
            return new CardCatalog(CardData.ParseCards(root));
        }

        public static CardCatalog FromCards(List<CardDef> cards)
        {
            return new CardCatalog(cards);
        }

        public CardDef Find(string templateId, string tier)
        {
            string key = Normalize(templateId);
            if (key.Length == 0) return null;

            List<CardDef> group;
            if (!_byBaseId.TryGetValue(key, out group)) return null;

            foreach (CardDef card in group)
            {
                if (string.Equals(card.Tier, tier, StringComparison.Ordinal)) return card;
            }

            return group.Count > 0 ? group[0] : null;
        }

        public static int TierRank(string tier)
        {
            int index = Array.IndexOf(Tiers, tier);
            return index < 0 ? 99 : index;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.ToLowerInvariant();
        }
    }
}
