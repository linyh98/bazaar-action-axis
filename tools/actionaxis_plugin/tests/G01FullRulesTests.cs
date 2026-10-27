using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G01 剩余规则的真实模板行为回归：Both、SellPrice 触发、Fairy Haze 状态计数、Fire Hose 半量清除。
    public static class G01FullRulesTests
    {
        private static JsonValue _data;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _data = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g01_full.json"));
            FrozenHorrorPoisonsBothPlayers();
            HypergreensNeighborSellPriceTriggersWeapons();
            FairyHazeCountsFrozenAndSlowedItems();
            FireHoseClearsHalfBurn();
            WeaponizedCoreBuffsBeforeDamage();
        }

        private static void FrozenHorrorPoisonsBothPlayers()
        {
            BattleConfig config = ItemBoard("Frozen Horror", "Gold", 3);
            CardDef card = config.Cards[IdOf(config, _data["Frozen Horror"]["id"].AsString())];
            BattleContext ctx = BattleEngine.InitBattle(config);
            JsonValue action = _data["Frozen Horror"]["template"]["Abilities"]["1"]["Action"];
            Dictionary<string, double> attrs = new Dictionary<string, double>();
            foreach (KeyValuePair<string, int> attr in card.LiveAttributes) attrs[attr.Key] = attr.Value;
            Effect effect = SkillMapper.MapAction(action, attrs);
            Effects.ResolveEffect(ctx, effect, ctx.Items.Find(delegate(InternalItem item) { return item.CardId == card.Id; }), 0, 0, null);
            Assert.Near(100, ctx.Player.Poison);
            Assert.Near(100, ctx.Enemy.Poison);
            Assert.Equal(2, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "poison"; }).Count);
        }

        private static void HypergreensNeighborSellPriceTriggersWeapons()
        {
            BattleConfig config = ItemBoard("Hypergreens", "Gold", 3);
            CardDef weapon = new CardDef { Id = "g01:weapon", BaseItemId = "g01:weapon", Cooldown = 5, NativeAttributesAreLive = false };
            weapon.Tags.Add("Weapon");
            weapon.LiveAttributes["DamageAmount"] = 10;
            weapon.LiveAttributes["CooldownMax"] = 5000;
            CardDef heal = new CardDef { Id = "g01:heal", BaseItemId = "g01:heal", Cooldown = 5, NativeAttributesAreLive = false };
            heal.HiddenTags.Add("Heal");
            heal.LiveAttributes["HealAmount"] = 10;
            heal.LiveAttributes["CooldownMax"] = 5000;
            Map(config);
            config.Cards[weapon.Id] = weapon;
            config.Cards[heal.Id] = heal;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 4 });
            config.Player.Items.Add(new LoadoutItem { CardId = heal.Id, Slot = 5 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem neighbor = ctx.Items.Find(delegate(InternalItem item) { return item.Slot == 4; });
            InternalItem weaponItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == weapon.Id; });
            InternalItem healItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == heal.Id; });
            InternalItem source = ctx.Items.Find(delegate(InternalItem item) { return item.Card.BaseItemId == _data["Hypergreens"]["id"].AsString(); });
            Effects.ResolveEffect(ctx, new Effect { Kind = "modifyAttribute", Attribute = "SellPrice", Operation = "Add",
                Amount = new NumExpr { Value = 1 }, Target = new Target { Kind = "selfItem", ItemId = neighbor.Id }, Count = 1 },
                neighbor, 0, 0, null);
            Triggers.DrainNativeFrame(ctx, 1);
            Assert.Near(25, ItemAttributes.Get(weaponItem, "DamageAmount"));
            Assert.Near(25, ItemAttributes.Get(healItem, "HealAmount"));
        }

        private static void FairyHazeCountsFrozenAndSlowedItems()
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(), EmptyCatalog, 10000, 10000, 20).Config;
            config.NativeMechanics = true;
            JsonValue template = _data["Fairy Haze"]["template"];
            LiveSkillEntry skill = new LiveSkillEntry { TemplateId = _data["Fairy Haze"]["id"].AsString(), Tier = "Gold",
                Template = template, TemplateBoard = true, AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold") };
            SkillMapper.Apply(config, new[] { skill }, null, Side.Player);
            CardDef frozen = new CardDef { Id = "g01:frozen", BaseItemId = "g01:frozen", Cooldown = 5, NativeAttributesAreLive = false };
            frozen.LiveAttributes["CooldownMax"] = 5000;
            CardDef slowed = new CardDef { Id = "g01:slowed", BaseItemId = "g01:slowed", Cooldown = 5, NativeAttributesAreLive = false };
            slowed.LiveAttributes["CooldownMax"] = 5000;
            config.Cards[frozen.Id] = frozen;
            config.Cards[slowed.Id] = slowed;
            config.Player.Items.Add(new LoadoutItem { CardId = frozen.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = slowed.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem frozenItem = ctx.Items.Find(delegate(InternalItem item) { return item.Id == frozen.Id; });
            InternalItem slowedItem = ctx.Items.Find(delegate(InternalItem item) { return item.Id == slowed.Id; });
            frozenItem.FrozenUntil = 10;
            slowedItem.SlowUntil = 10;
            ctx.Now = 1;
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(50, ctx.Player.Regen);
        }

        private static void FireHoseClearsHalfBurn()
        {
            BattleConfig config = ItemBoard("Fire Hose", "Silver", 0);
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            ctx.Player.Burn = 10;
            InternalItem hose = ctx.Items.Find(delegate(InternalItem item) { return item.Card.BaseItemId == _data["Fire Hose"]["id"].AsString(); });
            BattleEngine.ForceUse(ctx, hose, 1, 0);
            Assert.Near(5, ctx.Player.Burn);
        }

        private static void WeaponizedCoreBuffsBeforeDamage()
        {
            BattleConfig config = ItemBoard("Weaponized Core", "Diamond", 3);
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem core = ctx.Items.Find(delegate(InternalItem item) { return item.Card.BaseItemId == _data["Weaponized Core"]["id"].AsString(); });
            double before = ItemAttributes.Get(core, "DamageAmount");
            int gain;
            core.Card.LiveAttributes.TryGetValue("Custom_0", out gain);
            BattleEngine.ForceUse(ctx, core, 1, 0);
            BattleEvent damage = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage" && ev.SourceItemId == core.Id; });
            Assert.True(damage != null);
            if (damage != null) Assert.Near(before + gain, damage.Value.GetValueOrDefault());
        }

        private static BattleConfig ItemBoard(string name, string tier, int slot)
        {
            JsonValue template = _data[name]["template"];
            LiveCardEntry entry = new LiveCardEntry { TemplateId = _data[name]["id"].AsString(), Template = template, Socket = slot,
                Tier = tier, Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier) };
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { entry }, EmptyCatalog, 10000, 10000, 20).Config;
            config.NativeMechanics = true;
            return config;
        }

        private static void Map(BattleConfig config)
        {
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            warnings.AddRange(NativeItemMapper.Apply(config));
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
        }

        private static string IdOf(BattleConfig config, string baseItemId)
        {
            foreach (KeyValuePair<string, CardDef> pair in config.Cards)
                if (pair.Value.BaseItemId == baseItemId || pair.Value.Id == baseItemId) return pair.Key;
            throw new ArgumentException("棋盘缺少 " + baseItemId);
        }
    }
}
