using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public static class CardData
    {
        public static List<CardDef> ParseCards(JsonValue root)
        {
            List<CardDef> cards = new List<CardDef>();
            foreach (JsonValue item in root.AsArray())
            {
                CardDef card = FromJson(item);
                if (card != null && !string.IsNullOrEmpty(card.Id)) cards.Add(card);
            }
            return cards;
        }

        public static CardDef FromJson(JsonValue value)
        {
            if (value == null || value.Kind != JsonKind.Object) return null;

            CardDef card = new CardDef();
            card.Id = value["id"].AsString();
            card.BaseItemId = value["baseItemId"].AsString();
            card.Name = value["name"].AsString();
            card.NameCn = value["nameCn"].AsString();
            card.Image = value["image"].AsString();
            if (value.Has("size")) card.Size = value["size"].AsString();
            if (value.Has("tier")) card.Tier = value["tier"].AsString();
            card.Cooldown = value["cooldown"].AsDouble();
            if (value["ammoMax"].Kind == JsonKind.Number) card.AmmoMax = value["ammoMax"].AsInt();
            card.StartReady = value["startReady"].AsBool();
            card.Passive = value["passive"].AsBool();
            if (value.Has("parseStatus")) card.ParseStatus = value["parseStatus"].AsString();
            card.DataSource = value["dataSource"].AsString();

            foreach (JsonValue effect in value["effects"].AsArray())
                card.Effects.Add(ParseEffect(effect));

            foreach (JsonValue trigger in value["triggers"].AsArray())
                card.Triggers.Add(ParseTrigger(trigger));

            foreach (JsonValue tag in value["tags"].AsArray())
            {
                string text = tag.AsString();
                if (text != null) card.Tags.Add(text);
            }

            foreach (JsonValue line in value["rawTooltips"].AsArray())
            {
                string text = line.AsString();
                if (text != null) card.RawTooltips.Add(text);
            }

            return card;
        }

        public static Effect ParseEffect(JsonValue value)
        {
            Effect effect = new Effect();
            effect.Kind = value["kind"].AsString();
            if (value["amount"].Kind == JsonKind.Object)
                effect.Amount = ParseNumExpr(value["amount"]);
            else if (value["amount"].Kind == JsonKind.Number)
                effect.AmountValue = value["amount"].AsDouble();
            if (value["target"].Kind == JsonKind.Object)
                effect.Target = ParseTarget(value["target"]);
            effect.Seconds = value["seconds"].AsDouble();
            if (value["count"].Kind == JsonKind.Number) effect.Count = value["count"].AsInt();
            effect.Chance = value["chance"].AsDouble();
            effect.Percent = value["percent"].AsDouble();
            effect.Crit = value["crit"].AsBool();
            return effect;
        }

        public static NumExpr ParseNumExpr(JsonValue value)
        {
            NumExpr expr = new NumExpr();
            if (value["type"].Kind == JsonKind.String) expr.Type = value["type"].AsString();
            expr.Value = value["value"].AsDouble();
            expr.Basis = value["basis"].AsString();
            expr.Stack = value["stack"].AsString();
            expr.Per = value["per"].AsDouble();
            expr.Tag = value["tag"].AsString();
            return expr;
        }

        public static Target ParseTarget(JsonValue value)
        {
            Target target = new Target();
            if (value["kind"].Kind == JsonKind.String) target.Kind = value["kind"].AsString();
            target.ItemId = value["itemId"].AsString();
            return target;
        }

        public static TriggerDef ParseTrigger(JsonValue value)
        {
            TriggerDef trigger = new TriggerDef();
            trigger.Event = value["event"].AsString();
            if (value["everySeconds"].Kind == JsonKind.Number)
                trigger.EverySeconds = value["everySeconds"].AsDouble();

            JsonValue filter = value["filter"];
            if (filter.Kind == JsonKind.Object)
            {
                trigger.Filter = new TriggerFilter();
                trigger.Filter.SourceTag = filter["sourceTag"].AsString();
                trigger.Filter.SourceItemId = filter["sourceItemId"].AsString();
                trigger.Filter.Self = filter["self"].AsBool();
                string targetSide = filter["targetSide"].AsString();
                if (targetSide == "player") trigger.Filter.TargetSide = Side.Player;
                else if (targetSide == "enemy") trigger.Filter.TargetSide = Side.Enemy;
            }

            foreach (JsonValue effect in value["effects"].AsArray())
                trigger.Effects.Add(ParseEffect(effect));

            return trigger;
        }
    }
}
