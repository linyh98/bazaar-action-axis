using System;
using System.IO;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 审查发现的问题：真实模板不裁掉前提；同时检查入口、执行结果和历史视图。
    public static class AuditRegressionTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_audit_regressions.json"));
            RealPrinterAndMirrorExecute();
            WrongNeighborDoesNotTransform();
            PrinterEnchantsCopiesWithoutMovingRightCard();
            ControlGrowthUsesRuntimeValues();
            RealDryadGrowthAffectsCharge();
            CalibratedCoreKeepsRuntimeCharge();
            QueuedCastsCancelAfterRemoval();
            QueuedCastsCancelAfterTransformation();
            MonsterRageAttributesReachRuntime();
            HeroValueAurasFollowBothSides();
            RemovedCardsRetainHistory();
            WarpaintShinyScalesRageGain();
            LiveRageSnapshotIsNotScaledTwice();
            VengefulSigilShinyAndEnragedLifesteal();
            WildBoarRageAndEnrageEvents();
            OpeningAttributesIgnoreTemporaryState();
            HealthMaxGrowthIsNotHealing();
            HeroIdentityFollowsBothSides();
        }

        private static LiveCardEntry Real(string name, int slot, string tier = "Gold")
        {
            JsonValue row = _fixtures[name], template = row["template"];
            return new LiveCardEntry { TemplateId = row["id"].AsString(), Template = template,
                Tier = tier, Socket = slot, Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier) };
        }

        private const string Gem = @"{""$type"":""TCardItem"",""InternalName"":""Regression Gem"",""Size"":""Small"",""Tags"":[],
            ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":2000,""DamageAmount"":10},""AbilityIds"":[""0""],""AuraIds"":[],""TooltipIds"":[]}},
            ""Abilities"":{""0"":{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Trigger"":{""$type"":""TTriggerOnCardFired""},
              ""Action"":{""$type"":""TActionPlayerDamage"",""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Opponent""}}}},
            ""Auras"":{},""Enchantments"":{""Deadly"":{""Attributes"":{""CritChance"":100},""Abilities"":{},""Auras"":{},""Tags"":[]}}}";

        private static LiveCardEntry Donor(int slot, bool medium = false)
        {
            JsonValue template = Json.Parse(medium ? Gem.Replace("Small", "Medium") : Gem);
            return new LiveCardEntry { TemplateId = "gem", Template = template, Tier = "Gold", Socket = slot,
                Size = medium ? "Medium" : "Small", AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold") };
        }

        private static BattleConfig Board(params LiveCardEntry[] cards)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, 10).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static void RealPrinterAndMirrorExecute()
        {
            foreach (bool mirror in new[] { false, true })
            {
                int slot = mirror ? 2 : 1;
                BattleConfig config = Board(Donor(0, mirror), Real(mirror ? "Mirror" : "3D Printer", slot));
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem source = ctx.Items[1];
                Assert.True(source.Card.Effects[0].Prerequisites != null);
                string name = source.Card.Name;
                BattleEngine.ForceUse(ctx, source, 1, 0);
                BattleEngine.FlushPendingItems(ctx);
                Assert.Equal(mirror ? 3 : 5, ctx.Items.Count);
                InternalItem original = source;
                source = ctx.Items.Find(delegate(InternalItem item) { return !item.Destroyed && item.Side == original.Side && item.Slot == original.Slot; });
                Assert.Equal("gem#0", source.CardId);
                Assert.Equal(name, original.History.At(0.5).Card.Name);
                Assert.True(original.History.At(1) == null);
                Assert.Equal("gem", source.History.At(1).Card.BaseItemId);
                if (!mirror)
                {
                    Assert.True(ctx.Items[2].History.At(0.99) == null);
                    Assert.Equal(1, ctx.Items[2].History.At(1).Slot);
                }
                int hits = ctx.Events.Count;
                BattleEngine.ForceUse(ctx, source, 3, 0);
                Assert.True(ctx.Events.Count > hits);
                Assert.Near(10, ctx.Totals.Player.Damage);
            }
        }

        private static void WrongNeighborDoesNotTransform()
        {
            // 左边有小物品，但紧邻的是中型；不能越过中型去复制更远的牌。
            BattleConfig config = Board(Donor(0), Donor(1, true), Real("3D Printer", 3));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem printer = ctx.Items[2];
            string before = printer.CardId;
            BattleEngine.ForceUse(ctx, printer, 1, 0);
            Assert.Equal(before, printer.CardId);
            Assert.Equal(0, ctx.PendingItems.Count);
            JsonValue right = Json.Parse(@"[{""$type"":""TPrerequisiteCardCount"",""Subject"":{""$type"":""TTargetCardPositional"",""Origin"":""Self"",""TargetMode"":""RightCard"",""IncludeOrigin"":false},""Comparison"":""Equal"",""Amount"":1}]");
            Assert.True(NativeSkillSupport.PrerequisitesSupported(right));
            Assert.True(NativeSkillSupport.PrerequisitesMatch(ctx, ctx.Items[0], new NativeSkillRule { Prerequisites = right }));
            Assert.False(NativeSkillSupport.PrerequisitesMatch(ctx, printer, new NativeSkillRule { Prerequisites = right }));
        }

        private static void PrinterEnchantsCopiesWithoutMovingRightCard()
        {
            LiveCardEntry printerEntry = Real("3D Printer", 1);
            printerEntry.Enchantment = "Deadly";
            printerEntry.EnchantmentDefinition = printerEntry.Template["Enchantments"]["Deadly"];
            BattleConfig config = Board(Donor(0), printerEntry, Donor(9));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem donor = ctx.Items[0], printer = ctx.Items[1], right = ctx.Items[2];
            BattleEngine.ForceUse(ctx, printer, 1, 0);
            BattleEngine.FlushPendingItems(ctx);
            Assert.Equal(6, ctx.Items.Count);
            Assert.Equal(9, right.Slot);
            Assert.True(string.IsNullOrEmpty(donor.Card.EnchantmentName));
            HashSet<int> slots = new HashSet<int>();
            foreach (InternalItem item in ctx.Items)
            {
                if (item.Destroyed) continue;
                Assert.True(slots.Add(item.Slot));
                Assert.True(item.Slot + PanelModel.CardSpan(item.Card) <= 10);
                if (item == donor || item == right) continue;
                Assert.Equal("Deadly", item.Card.EnchantmentName);
                Assert.Near(1, item.CritBonus);
                BattleEngine.ForceUse(ctx, item, 3, 0);
            }
            Assert.Near(60, ctx.Totals.Player.Damage);
        }

        private static void ControlGrowthUsesRuntimeValues()
        {
            string[] kinds = { "Charge", "Haste", "Slow", "Freeze" };
            foreach (string kind in kinds)
            {
                BattleConfig config = Board(Donor(0), Donor(1), Donor(2));
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem source = ctx.Items[0];
                string amount = kind + "Amount", count = kind + "Targets";
                source.Card.LiveAttributes[amount] = 1000;
                source.Card.LiveAttributes[count] = 1;
                Dictionary<string, double> attrs = new Dictionary<string, double> { { amount, 1000 }, { count, 1 } };
                Effect effect = SkillMapper.MapAction(Json.Parse("{\"$type\":\"TActionCard" + kind +
                    "\",\"Target\":{\"$type\":\"TTargetCardRandom\",\"TargetSection\":\"SelfHand\",\"ExcludeSelf\":true}}"), attrs);
                ItemAttributes.AddDelta(source, amount, 1000);
                ItemAttributes.AddDelta(source, count, 1);
                foreach (InternalItem item in ctx.Items) item.Cooldown = 9;
                Effects.ResolveEffect(ctx, effect, source, 1, 0, null);
                int seen = 0;
                foreach (BattleEvent ev in ctx.Events)
                    if (ev.Kind == kind.ToLowerInvariant()) { seen++; Assert.Near(2, ev.Value.Value); }
                Assert.Equal(2, seen);
                if (kind == "Charge") Assert.Near(7, ctx.Items[1].Cooldown);
                ItemAttributes.AddDelta(source, count, -2);
                int eventsBefore = ctx.Events.Count;
                Effects.ResolveEffect(ctx, effect, source, 2, 0, null);
                Assert.Equal(eventsBefore, ctx.Events.Count);
            }
        }

        private static void RealDryadGrowthAffectsCharge()
        {
            BattleConfig config = Board(Real("Dryad", 0, "Bronze"), Donor(3));
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem dryad = ctx.Items[0], other = ctx.Items[1];
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onPlayerEnraged", SourceSide = Side.Player });
            Triggers.Drain(ctx, 1);
            Assert.Near(2000, ItemAttributes.Get(dryad, "ChargeAmount"));
            dryad.Cooldown = 6;
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = other.Id });
            Triggers.Drain(ctx, 2);
            Assert.Near(4, dryad.Cooldown);
        }

        private static void CalibratedCoreKeepsRuntimeCharge()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef core = new CardDef { Id = "core", BaseItemId = NativeItemMapper.LauncherCore, Cooldown = 6 };
            core.LiveAttributes["ChargeAmount"] = 1000;
            config.Cards[core.Id] = core;
            CardDef friend = new CardDef { Id = "friend", Cooldown = 3 };
            friend.Tags.Add("Friend");
            config.Cards[friend.Id] = friend;
            config.Player.Items.Add(new LoadoutItem { CardId = core.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = friend.Id, Slot = 3 });
            NativeItemMapper.Apply(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem runtime = ctx.Items[0];
            ItemAttributes.AddDelta(runtime, "ChargeAmount", 1000);
            ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = friend.Id });
            Triggers.Drain(ctx, 1);
            Assert.Near(4, runtime.Cooldown);
        }

        private static void QueuedCastsCancelAfterRemoval()
        {
            foreach (string kind in new[] { "destroy", "disable" })
            {
                BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 2.7 };
                CardDef card = new CardDef { Id = "self", Cooldown = 2, HasMulticastOverride = true, MulticastBonus = 2 };
                card.Effects.Add(new Effect { Kind = kind, Target = new Target { Kind = "selfItem" } });
                config.Cards[card.Id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                Assert.Equal(1, result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "activate"; }).Count);
            }
        }

        private static void QueuedCastsCancelAfterTransformation()
        {
            BattleConfig config = Board(Donor(0), Real("3D Printer", 1));
            CardDef printer = config.Cards[config.Player.Items[1].CardId];
            printer.HasMulticastOverride = true;
            printer.MulticastBonus = 2;
            config.MaxTime = printer.Cooldown + 0.6;
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<BattleEvent> casts = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "activate" && ev.SourceItemId == printer.Id; });
            Assert.Equal(1, casts.Count);
            BattleItemView history = result.PlayerItems.Find(delegate(BattleItemView item) { return item.Id == printer.Id; });
            Assert.Equal(printer.Id, history.At(0).Card.Id);
            Assert.True(history.At(printer.Cooldown + 0.1) == null);
            Assert.Near(printer.Cooldown, history.RemovedAt.Value);
            Assert.True(result.PlayerItems.Exists(delegate(BattleItemView item) { return item.Id != printer.Id && item.Slot == history.Slot && item.Card.BaseItemId == "gem"; }));
        }

        private static void MonsterRageAttributesReachRuntime()
        {
            JsonValue raw = _fixtures["monsters:Wild Boar"]["template"]["Player"]["Attributes"];
            LiveMonsterEntry monster = new LiveMonsterEntry { MaxHp = 650, Health = 650, Name = "Wild Boar" };
            foreach (KeyValuePair<string, JsonValue> attr in raw.AsObject()) monster.Attributes[attr.Key] = attr.Value.AsInt();
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            MonsterOpponentMapper.Apply(config, monster, EmptyCatalog, null);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(50, ctx.Enemy.RageMax);
            Assert.Near(5000, ctx.Enemy.EnragedDurationMs);
            MonsterOpponentMapper.ApplyHeroAttributes(config,
                new Dictionary<string, int> { { "RageMax", 70 }, { "HealthRegen", 5 }, { "EnragedDurationMax", 3000 } }, Side.Player);
            ctx = BattleEngine.InitBattle(config);
            Assert.Near(70, ctx.Player.RageMax);
            Assert.Near(5, ctx.Player.Regen);
            Assert.Near(3000, ctx.Player.EnragedDurationMs);
        }

        private static void HeroValueAurasFollowBothSides()
        {
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                BattleConfig config = Board(Real("Force Field", 0), Real("Spiky Shield", 3));
                if (side == Side.Enemy) { config.Enemy = config.Player; config.Player = new Loadout(); }
                BattleContext ctx = BattleEngine.InitBattle(config);
                double baseline = ItemAttributes.Get(ctx.Items[0], "DamageAmount");
                ctx.Hero(side).Shield = 100;
                NativeDynamicAuras.Refresh(ctx);
                Assert.True(ItemAttributes.Get(ctx.Items[0], "DamageAmount") > baseline);
                ctx.Hero(side).Shield = 0;
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(baseline, ItemAttributes.Get(ctx.Items[0], "DamageAmount"));
                foreach (string name in new[] { "Burning Shield", "Toxic Shield" })
                {
                    JsonValue row = _fixtures[name];
                    LiveSkillEntry skill = new LiveSkillEntry { TemplateId = row["id"].AsString(), Tier = "Gold", Template = row["template"] };
                    SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, side);
                    Assert.Equal("simulated", mapped.Skills[0].Status);
                    ctx = BattleEngine.InitBattle(config);
                    InternalItem skillItem = ctx.Skills.Find(delegate(InternalItem item) { return item.Side == side; });
                    double baseShield = ItemAttributes.Get(skillItem, "ShieldApplyAmount");
                    ctx.Hero(SideUtil.Opposing(side)).Burn = 50;
                    ctx.Hero(SideUtil.Opposing(side)).Poison = 50;
                    NativeDynamicAuras.Refresh(ctx);
                    double expected = baseShield + 50 * ItemAttributes.Get(skillItem, "Custom_1");
                    Assert.True(expected > baseShield);
                    Assert.Near(expected, ItemAttributes.Get(skillItem, "ShieldApplyAmount"));
                    ctx.Hero(side).Hp = 1;
                    ctx.TriggerQueue.Enqueue(new TriggerPayload { Event = "onPlayerAttributeChanged", SourceSide = side,
                        AttributeName = "Health", ChangeType = "Loss", Amount = 1 });
                    Triggers.Drain(ctx, 1);
                    Assert.Near(expected, ctx.Hero(side).Shield);
                }
            }
        }

        private static LiveCardEntry Enchanted(string name, int slot, string tier, string enchantment)
        {
            LiveCardEntry entry = Real(name, slot, tier);
            entry.Enchantment = enchantment;
            entry.EnchantmentDefinition = entry.Template["Enchantments"][enchantment];
            return entry;
        }

        private static List<BattleEvent> Events(BattleContext ctx, string kind, Side side)
        {
            return ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == kind && ev.Side == side; });
        }

        private static void WarpaintShinyScalesRageGain()
        {
            BattleConfig config = Board(Donor(0), Enchanted("Warpaint", 1, "Gold", "Shiny"));
            CardDef warpaint = config.Cards[config.Player.Items[1].CardId];
            // Gold 10 × Shiny 2；光环必须先落到 LiveAttributes 再进入战斗。
            Assert.Equal(20, warpaint.LiveAttributes["RageApplyAmount"]);
            Assert.Equal(1, warpaint.Triggers.Count);
            Assert.Equal("rage", warpaint.Triggers[0].Effects[0].Kind);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem paint = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == warpaint.Id; });
            InternalItem left = ctx.Items.Find(delegate(InternalItem item) { return item.Id != paint.Id; });
            Assert.Near(20, ItemAttributes.Get(paint, "RageApplyAmount"));
            for (int use = 1; use <= 5; use++) BattleEngine.ForceUse(ctx, left, use, 0);
            List<BattleEvent> rage = Events(ctx, "rage", Side.Player);
            Assert.Equal(5, rage.Count);
            for (int index = 0; index < rage.Count; index++)
            {
                Assert.Near(20, rage[index].Value.Value);
                Assert.Near(index + 1, rage[index].Time);
                Assert.Equal(paint.Id, rage[index].SourceItemId);
            }
            List<BattleEvent> enrage = Events(ctx, "enrage", Side.Player);
            Assert.Equal(1, enrage.Count);
            Assert.Near(5, enrage[0].Time);
            Assert.Near(5000, enrage[0].Value.Value);
            Assert.Near(0, ctx.Player.Rage);
            Assert.Near(10, ctx.Player.EnragedUntil);
        }

        private static void LiveRageSnapshotIsNotScaledTwice()
        {
            LiveCardEntry live = Enchanted("Warpaint", 1, "Gold", "Shiny");
            live.AttributesAreLive = true;
            // 实战快照里的 RageApplyAmount 已经包含 Shiny 光环。
            live.Attributes = new Dictionary<string, int> { { "RageApplyAmount", 20 } };
            BattleConfig config = Board(Donor(0), live);
            CardDef warpaint = config.Cards[config.Player.Items[1].CardId];
            Assert.Equal(20, warpaint.LiveAttributes["RageApplyAmount"]);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem paint = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == warpaint.Id; });
            InternalItem left = ctx.Items.Find(delegate(InternalItem item) { return item.Id != paint.Id; });
            Assert.Near(20, ItemAttributes.Get(paint, "RageApplyAmount"));
            BattleEngine.ForceUse(ctx, left, 1, 0);
            List<BattleEvent> rage = Events(ctx, "rage", Side.Player);
            Assert.Equal(1, rage.Count);
            Assert.Near(20, rage[0].Value.Value);
        }

        private static void VengefulSigilShinyAndEnragedLifesteal()
        {
            BattleConfig config = Board(Enchanted("Vengeful Sigil", 4, "Gold", "Shiny"), Donor(6));
            CardDef sigil = config.Cards[config.Player.Items[0].CardId];
            CardDef weapon = config.Cards[config.Player.Items[1].CardId];
            weapon.Tags.Add("Weapon");
            CardDef enemyItem = new CardDef { Id = "enemy:probe", Cooldown = 10 };
            config.Cards[enemyItem.Id] = enemyItem;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemyItem.Id, Slot = 0 });
            Assert.Equal(12, sigil.LiveAttributes["RageApplyAmount"]);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem sigilItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == sigil.Id; });
            InternalItem weaponItem = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == weapon.Id; });
            InternalItem enemy = ctx.Items.Find(delegate(InternalItem item) { return item.Side == Side.Enemy; });
            Assert.Near(12, ItemAttributes.Get(sigilItem, "RageApplyAmount"));
            BattleEngine.ForceUse(ctx, enemy, 1, 0);
            List<BattleEvent> rage = Events(ctx, "rage", Side.Player);
            Assert.Equal(1, rage.Count);
            Assert.Near(12, rage[0].Value.Value);
            Assert.Near(0, ItemAttributes.Get(weaponItem, "Lifesteal"));
            ctx.Player.EnragedUntil = 10;
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(100, ItemAttributes.Get(weaponItem, "Lifesteal"));
            ctx.Player.EnragedUntil = -1;
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(0, ItemAttributes.Get(weaponItem, "Lifesteal"));
        }

        private static LiveMonsterEntry WildBoar()
        {
            JsonValue fixture = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_wild_boar_monster.json"));
            return MonsterTemplateExtractor.FromJson(fixture["monster"], delegate(string id)
            {
                return fixture["cards"].Has(id) ? fixture["cards"][id] : null;
            });
        }

        private static void WildBoarRageAndEnrageEvents()
        {
            BattleConfig config = new BattleConfig { PlayerMaxHp = 5000, NativeMechanics = true, MaxTime = 8.5 };
            CardDef player = new CardDef { Id = "player", Cooldown = 100 };
            config.Cards[player.Id] = player;
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id, Slot = 0 });
            LiveMonsterEntry monster = WildBoar();
            Assert.Near(50, monster.Attributes["RageMax"]);
            MonsterOpponentMapper.Apply(config, monster, EmptyCatalog, null);
            Assert.Near(50, config.EnemyRageMax);
            Assert.Near(5000, config.EnemyEnragedDurationMs);
            Assert.Equal("", string.Join(" | ", NativeCardAbilityMapper.Apply(config).ToArray()));
            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> rage = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "rage" && ev.Side == Side.Enemy; });
            double[] expectedTimes = { 4.05, 5.05, 7.05, 8.05 };
            Assert.Equal(expectedTimes.Length, rage.Count);
            for (int index = 0; index < rage.Count; index++)
            {
                Assert.Near(15, rage[index].Value.Value);
                Assert.Near(expectedTimes[index], rage[index].Time);
            }
            List<BattleEvent> enrage = result.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "enrage" && ev.Side == Side.Enemy; });
            Assert.Equal(1, enrage.Count);
            Assert.Near(8.05, enrage[0].Time);
            Assert.Near(5000, enrage[0].Value.Value);
            // 暴怒事件本身在触发帧产生，暴怒被动按原生队列在下一帧结算。
            List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "modifyAttribute" && ev.Side == Side.Enemy && ev.Note == "ShieldApplyAmount" && Math.Abs(ev.Time - 8.1) < 1e-9;
            });
            Assert.Equal(4, shields.Count);
            foreach (BattleEvent ev in shields) Assert.Near(40, ev.Value.Value);
        }

        private static void OpeningAttributesIgnoreTemporaryState()
        {
            Dictionary<string, int> attributes = new Dictionary<string, int> {
                { "Health", 12 }, { "HealthMax", 900 }, { "Shield", 99 }, { "Burn", 7 }, { "Poison", 8 },
                { "Rage", 49 }, { "Enraged", 1 }, { "RageMax", 70 }, { "EnragedDurationMax", 3000 }, { "HealthRegen", 5 } };
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                BattleConfig config = new BattleConfig();
                MonsterOpponentMapper.ApplyHeroAttributes(config, attributes, side);
                // 战斗中的临时状态不能成为下一场战斗的开场。
                if (side == Side.Player)
                {
                    Assert.True(!config.PlayerStartHp.HasValue);
                    Assert.Near(100, config.PlayerMaxHp);
                    Assert.Near(70, config.PlayerRageMax);
                    Assert.Near(3000, config.PlayerEnragedDurationMs);
                    Assert.Near(5, config.PlayerStartRegen);
                }
                else
                {
                    Assert.True(!config.EnemyStartHp.HasValue);
                    Assert.Near(100, config.EnemyMaxHp);
                    Assert.Near(70, config.EnemyRageMax);
                    Assert.Near(3000, config.EnemyEnragedDurationMs);
                    Assert.Near(5, config.EnemyStartRegen);
                }
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalHero hero = ctx.Hero(side);
                Assert.Near(0, hero.Shield);
                Assert.Near(0, hero.Burn);
                Assert.Near(0, hero.Poison);
                Assert.Near(0, hero.Rage);
                Assert.True(hero.EnragedUntil <= 0);
            }
        }

        private static void HealthMaxGrowthIsNotHealing()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            CardDef source = new CardDef { Id = "source", Cooldown = 10 };
            config.Cards[source.Id] = source;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            ctx.Player.Hp = 50;
            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "playerAttribute", Attribute = "HealthMax", Operation = "Add",
                Amount = new NumExpr { Type = "const", Value = 50 }, Target = new Target { Kind = "selfHero" }
            }, ctx.Items[0], 1, 0, null);
            Assert.Near(150, ctx.Player.MaxHp);
            Assert.Near(100, ctx.Player.Hp);
            Assert.Near(0, ctx.Totals.Player.Heal);
            Assert.Equal(0, Events(ctx, "heal", Side.Player).Count);
            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "heal", Amount = new NumExpr { Type = "const", Value = 20 }, Target = new Target { Kind = "selfHero" }
            }, ctx.Items[0], 2, 0, null);
            Assert.Near(120, ctx.Player.Hp);
            Assert.Near(20, ctx.Totals.Player.Heal);
            Assert.Equal(1, Events(ctx, "heal", Side.Player).Count);
        }

        private static void HeroIdentityFollowsBothSides()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerHero = "Vanessa", EnemyHero = "Pygmalien" };
            CardDef playerOwn = HeroCard("player-own", "Vanessa");
            CardDef playerForeign = HeroCard("player-foreign", "Pygmalien");
            CardDef enemyOwn = HeroCard("enemy-own", "Pygmalien");
            CardDef enemyForeign = HeroCard("enemy-foreign", "Vanessa");
            foreach (CardDef card in new[] { playerOwn, playerForeign, enemyOwn, enemyForeign }) config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = playerOwn.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = playerForeign.Id, Slot = 1 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemyOwn.Id, Slot = 0 });
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemyForeign.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem pOwn = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == playerOwn.Id; });
            InternalItem pForeign = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == playerForeign.Id; });
            InternalItem eOwn = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == enemyOwn.Id; });
            InternalItem eForeign = ctx.Items.Find(delegate(InternalItem item) { return item.CardId == enemyForeign.Id; });
            JsonValue same = Json.Parse("{\"$type\":\"TCardConditionalPlayerHero\",\"IsSameAsPlayerHero\":true}");
            JsonValue other = Json.Parse("{\"$type\":\"TCardConditionalPlayerHero\",\"IsSameAsPlayerHero\":false}");
            // 两侧各自读取本方英雄，不能把玩家身份套到怪物/对手身上。
            Assert.True(NativeSkillSupport.CardMatches(pOwn, same, ctx));
            Assert.True(NativeSkillSupport.CardMatches(eOwn, same, ctx));
            Assert.True(NativeSkillSupport.CardMatches(pForeign, other, ctx));
            Assert.True(NativeSkillSupport.CardMatches(eForeign, other, ctx));
            Assert.False(NativeSkillSupport.CardMatches(pOwn, other, ctx));
            Assert.False(NativeSkillSupport.CardMatches(eOwn, other, ctx));
        }

        private static CardDef HeroCard(string id, string hero)
        {
            CardDef card = new CardDef { Id = id, Cooldown = 10 };
            card.Heroes.Add(hero);
            return card;
        }

        private static void RemovedCardsRetainHistory()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 3 };
            CardDef card = new CardDef { Id = "gone", Cooldown = 1 };
            card.Effects.Add(new Effect { Kind = "destroy", Target = new Target { Kind = "selfItem" } });
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(1, result.PlayerItems.Count);
            BattleItemView view = result.PlayerItems[0];
            Assert.Equal("gone", view.At(0.5).Card.Id);
            Assert.True(view.At(2) == null);
            CardStatsSeries series = Aggregator.Build(result, new[] { "gone" }, 0.05)["gone"];
            Assert.Equal(1, series.At(2).ActivateCount);
            Assert.Near(0, series.CooldownProgressAt(2));
        }
    }
}
