using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G07：附魔主动/被动/光环、战斗内附魔、暴击、抗性与免疫。
    // 所有行为用例都从安装包导出的完整原生模板出发，不只核对属性字典。
    public static class G07EnchantmentTests
    {
        private static JsonValue _fixtures;
        private static JsonValue _remaining;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g07_enchantments.json"));
            _remaining = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g07_remaining.json"));
            RadiantDirectAttributesBlockControlAndDestroy();
            RadiantNeighborAuraHalvesControl();
            RadiantHandAuraReachesEveryCard();
            DeadlyDamageCritAddsToCritMultiplier();
            DeadlyAuraDrivesActualCritRoll();
            DeadlyAuraOnlyMatchesCanCritTargets();
            EnchantmentEligibilityFiltersCombatTargets();
            GoldenAuraScalesSellPrice();
            CalibrationKeepsEnchantmentAura();
            CalibrationKeepsGoldenSellPriceAura();
            CalibrationKeepsEnchantmentActive();
            CalibrationPreservesEnchantmentPassiveTrigger();
            InCombatEnchantRadiantAppliesAndRemoves();
            InCombatEnchantGoldenKeepsBaseSellPrice();
            InCombatEnchantShinyKeepsBaseMulticast();
            InCombatEnchantDeadlyAddsToBaseCrit();
            InCombatPassiveEnchantActuallyTriggers();
            SpiritDiffuserFightStartEnchantIsExecuted();
            PreventOverrideBlocksInCombatEnchant();
            LegacyRadiantEnchantmentExecutesMechanics();
            LegacyPassiveEnchantmentMapsTrigger();
            SubtractAurasReduceCritAndCounters();
            RadiantHasteReductionHalvesHaste();
            ShinyFlyingTargetsAddsTargets();
            ShinyEnchantTargetsIncreasesEnchants();
            ShowcaseDeadlyPicksOneTargetStably();
            RadiantXMostAuraTargetsRightmost();
            TrailMarkersCooldownSubtractIncreasesCooldown();
            SlingshotGoldenIncomeReferenceMaps();
            LichenCleanseUsesRuntimeRemoveAmount();
            DishwasherHeatedRadiantAuraExecutes();
            ShurikenUsesRuntimeAmmoForMulticast();
            SantokuCopiesCritToLeftCard();
            MoneyFurnaceZeroMultiplierSetsPriceToZero();
        }

        private static LiveCardEntry Real(string name, int slot, string tier, string enchantment = null)
        {
            JsonValue row = _fixtures.Has(name) ? _fixtures[name] : _remaining[name], template = row["template"];
            LiveCardEntry entry = new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier),
                Enchantment = enchantment
            };
            if (!string.IsNullOrEmpty(enchantment))
            {
                entry.EnchantmentDefinition = template["Enchantments"][enchantment];
                foreach (KeyValuePair<string, JsonValue> attr in entry.EnchantmentDefinition["Attributes"].AsObject())
                    entry.Attributes[attr.Key] = attr.Value.AsInt();
            }
            return entry;
        }

        private static BattleConfig Build(params LiveCardEntry[] entries)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(entries), EmptyCatalog, 100000, 100000, 20).Config;
            config.NativeMechanics = true;
            return config;
        }

        private static BattleConfig Map(BattleConfig config, bool clean = true)
        {
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            warnings.AddRange(NativeItemMapper.Apply(config));
            if (clean) Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static InternalItem At(BattleContext ctx, int slot, Side side = Side.Player)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.Side == side && item.Slot == slot; });
        }

        private static CardDef Enemy(BattleConfig config, int slot = 0)
        {
            CardDef card = new CardDef
            {
                Id = "enemy:" + slot, BaseItemId = "enemy", Name = "Enemy", Cooldown = 10,
                Size = "Small", NativeAttributesAreLive = false
            };
            config.Cards[card.Id] = card;
            config.Enemy.Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
            return card;
        }

        private static Effect SelfEffect(string kind, double seconds)
        {
            return new Effect { Kind = kind, Seconds = seconds, Target = new Target { Kind = "selfItem" } };
        }

        private static void RadiantDirectAttributesBlockControlAndDestroy()
        {
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                BattleConfig config = Build(Real("Gamma Ray", 0, "Gold", "Radiant"));
                if (side == Side.Enemy) { config.Enemy = config.Player; config.Player = new Loadout(); }
                Map(config);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem item = At(ctx, 0, side);
                Assert.Near(100, NativeDynamicAuras.SlowResistance(item));
                Assert.Near(100, NativeDynamicAuras.FreezeResistance(item));
                Assert.Near(1, NativeDynamicAuras.DestroyImmunity(item));

                Effects.ResolveEffect(ctx, SelfEffect("slow", 2), item, 1, 0, null);
                Effects.ResolveEffect(ctx, SelfEffect("freeze", 2), item, 2, 0, null);
                Assert.Near(1, item.SlowUntil);
                Assert.Near(2, item.FrozenUntil);

                Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Target = new Target { Kind = "selfItem" } }, item, 3, 0, null);
                Assert.False(item.Destroyed);
                Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "destroyBlocked" && Math.Abs(ev.Time - 3) < 1e-9; }));
            }
        }

        private static void RadiantNeighborAuraHalvesControl()
        {
            BattleConfig config = Build(Real("Security Blanket", 0, "Gold", "Radiant"),
                Real("Gamma Ray", 1, "Gold"), Real("Cog", 2, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, 0), neighbor = At(ctx, 1), far = At(ctx, 2);
            Assert.Near(0, NativeDynamicAuras.SlowResistance(source));
            Assert.Near(50, NativeDynamicAuras.SlowResistance(neighbor));
            Assert.Near(50, NativeDynamicAuras.FreezeResistance(neighbor));
            Assert.Near(0, NativeDynamicAuras.SlowResistance(far));

            Effects.ResolveEffect(ctx, SelfEffect("slow", 2), neighbor, 1, 0, null);
            Effects.ResolveEffect(ctx, SelfEffect("freeze", 2), neighbor, 1, 0, null);
            Assert.Near(2, neighbor.SlowUntil);
            Assert.Near(2, neighbor.FrozenUntil);
            Effects.ResolveEffect(ctx, SelfEffect("slow", 2), far, 1, 0, null);
            Assert.Near(3, far.SlowUntil);
        }

        private static void RadiantHandAuraReachesEveryCard()
        {
            BattleConfig config = Build(Real("Spirit Diffuser", 0, "Diamond", "Radiant"),
                Real("Gamma Ray", 1, "Gold"), Real("Cog", 2, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            foreach (int slot in new[] { 0, 1, 2 })
            {
                Assert.Near(50, NativeDynamicAuras.SlowResistance(At(ctx, slot)));
                Assert.Near(50, NativeDynamicAuras.FreezeResistance(At(ctx, slot)));
            }
            Effects.ResolveEffect(ctx, SelfEffect("freeze", 2), At(ctx, 1), 1, 0, null);
            Assert.Near(2, At(ctx, 1).FrozenUntil);
        }

        private static void DeadlyDamageCritAddsToCritMultiplier()
        {
            BattleConfig config = Build(Real("Letter Opener", 0, "Gold", "Deadly"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Near(150, ItemAttributes.Get(item, "CritChance"));
            Assert.Near(100, ItemAttributes.Get(item, "DamageCrit"));
            Assert.Near(1.5, item.CritBonus);
            BattleEngine.ForceUse(ctx, item, 1, 0);
            BattleEvent hit = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            Assert.True(hit != null);
            // 回放：DC=100 为 3×；40 base × 3 = 120。
            Assert.Near(120, hit.Value.Value);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "crit" && Math.Abs(ev.Value.Value - 120) < 1e-9; }));
        }

        private static void DeadlyAuraDrivesActualCritRoll()
        {
            BattleConfig config = Build(Real("Security Blanket", 0, "Gold", "Deadly"));
            config.Seed = 7; // 第一次 50% 判定为暴击。
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Near(0.5, item.CritBonus);
            DamageSource source = new DamageSource
            {
                Id = item.Id, CardId = item.CardId, Side = item.Side,
                CritBonus = item.CritBonus, DamageCrit = ItemAttributes.Get(item, "DamageCrit")
            };
            Effects.ApplyDamage(ctx, source, Side.Enemy, 25, 5, null);
            BattleEvent hit = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "damage"; });
            BattleEvent crit = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "crit"; });
            Assert.True(hit != null && crit != null);
            Assert.Near(50, hit.Value.Value);
            Assert.Near(50, crit.Value.Value);
        }

        private static void DeadlyAuraOnlyMatchesCanCritTargets()
        {
            BattleConfig config = Build(Real("Gamma Ray", 0, "Gold", "Deadly"), Real("Cog", 1, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(0.25, At(ctx, 0).CritBonus);
            Assert.Near(0, At(ctx, 1).CritBonus);
        }

        private static void EnchantmentEligibilityFiltersCombatTargets()
        {
            BattleConfig config = Build(Real("Gamma Ray", 0, "Gold", "Deadly"), Real("Cog", 1, "Gold"));
            Map(config);
            JsonValue toxic = Json.Parse("{\"$type\":\"TCardConditionalEnchantmentEligible\",\"Enchantment\":\"Toxic\"}");
            Assert.False(NativeSkillSupport.CardMatches(new InternalItem { Card = config.Cards[config.Player.Items[0].CardId] }, toxic));
            Assert.True(NativeSkillSupport.CardMatches(new InternalItem { Card = config.Cards[config.Player.Items[1].CardId] }, toxic));
            JsonValue own = Json.Parse("{\"$type\":\"TCardConditionalEnchantmentEligible\",\"Enchantment\":\"Deadly\"}");
            Assert.True(NativeSkillSupport.CardMatches(new InternalItem { Card = config.Cards[config.Player.Items[0].CardId] }, own));
            JsonValue missing = Json.Parse("{\"$type\":\"TCardConditionalEnchantmentEligible\",\"Enchantment\":\"NotAnEnchantment\"}");
            Assert.False(NativeSkillSupport.CardMatches(new InternalItem { Card = config.Cards[config.Player.Items[1].CardId] }, missing));
        }

        private static void GoldenAuraScalesSellPrice()
        {
            BattleConfig config = Build(Real("Eye of the Colossus", 0, "Diamond", "Golden"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(64, ItemAttributes.Get(At(ctx, 0), "SellPrice"));
        }

        private static void CalibrationKeepsEnchantmentAura()
        {
            BattleConfig config = Build(Real("Drowsdrop", 0, "Gold", "Heavy"));
            Map(config);
            CardDef target = Enemy(config, 0);
            target.LiveAttributes["CooldownMax"] = 10000;
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            // Heavy 光环把 SlowAmount 从 1000 翻倍；校准路径不能把附魔光环清掉。
            Assert.Near(2000, ItemAttributes.Get(item, "SlowAmount"));
            BattleEngine.ForceUse(ctx, item, 1, 0);
            BattleEvent slow = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "slow"; });
            Assert.True(slow != null);
            Assert.Near(2, slow.Value.Value);
        }

        private static void CalibrationKeepsGoldenSellPriceAura()
        {
            LiveCardEntry entry = Real("Drowsdrop", 0, "Gold", "Golden");
            entry.Attributes["SellPrice"] = 5; // 实时快照里的永久售价成长。
            BattleConfig config = Build(entry);
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(10, ItemAttributes.Get(At(ctx, 0), "SellPrice"));
        }

        private static void CalibrationKeepsEnchantmentActive()
        {
            BattleConfig config = Build(Real("Cog", 0, "Gold", "Deadly"), Real("Gamma Ray", 1, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            BattleEngine.ForceUse(ctx, At(ctx, 0), 1, 0);
            // Cog 的 Deadly 主动给相邻可暴击卡 +20% CritChance；校准替换基础 haste 后必须仍保留。
            Assert.Near(0.2, At(ctx, 1).CritBonus);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute" && ev.Note == "CritChance"; }));
        }

        private static void CalibrationPreservesEnchantmentPassiveTrigger()
        {
            LiveCardEntry entry = Real("Drowsdrop", 0, "Gold");
            JsonValue definition = Json.Parse(@"{
                ""Attributes"":{""ShieldApplyAmount"":10},""Tags"":[],""HiddenTags"":[],
                ""Abilities"":{""e1"":{""Trigger"":{""$type"":""TTriggerOnItemUsed""},
                    ""Action"":{""$type"":""TActionPlayerShieldApply"",""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self"",""Conditions"":null}},
                    ""Prerequisites"":null,""ActiveIn"":""HandOnly"",""WorksIn"":""Anywhere""}},
                ""Auras"":{}}");
            entry.Template["Enchantments"].AsObject()["PassiveTest"] = definition;
            entry.Enchantment = "PassiveTest";
            entry.EnchantmentDefinition = definition;
            BattleConfig config = Build(entry);
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            BattleEngine.ForceUse(ctx, At(ctx, 0), 1, 0);
            // 校准 Drowsdrop 会重建基础触发器；FromEnchantment 的被动必须保留并执行。
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "shield"; }));
        }

        private static void InCombatEnchantRadiantAppliesAndRemoves()
        {
            BattleConfig config = Build(Real("Gamma Ray", 0, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.True(NativeEnchant.Apply(ctx, item, item, "Radiant", false, 1, 0, 0));
            Assert.Near(100, NativeDynamicAuras.SlowResistance(item));
            Assert.Near(1, NativeDynamicAuras.DestroyImmunity(item));
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "enchant" && ev.Note == "Radiant" && Math.Abs(ev.Time - 1) < 1e-9; }));
            Assert.True(NativeEnchant.Remove(ctx, item, item, 2, 0, 1));
            Assert.Near(0, NativeDynamicAuras.SlowResistance(item));
            Assert.Near(0, NativeDynamicAuras.DestroyImmunity(item));
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "enchantRemove" && ev.Note == "Radiant" && Math.Abs(ev.Time - 2) < 1e-9; }));
        }

        private static void InCombatEnchantGoldenKeepsBaseSellPrice()
        {
            BattleConfig config = Build(Real("Eye of the Colossus", 0, "Diamond"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Near(32, ItemAttributes.Get(item, "SellPrice"));
            Assert.True(NativeEnchant.Apply(ctx, item, item, "Golden", false, 1, 0, 0));
            Assert.Near(64, ItemAttributes.Get(item, "SellPrice"));
            Assert.True(NativeEnchant.Remove(ctx, item, item, 2, 0, 1));
            Assert.Near(32, ItemAttributes.Get(item, "SellPrice"));
        }

        private static void InCombatEnchantShinyKeepsBaseMulticast()
        {
            BattleConfig config = Build(Real("Gamma Ray", 0, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Equal(0, item.MulticastBonus);
            Assert.True(NativeEnchant.Apply(ctx, item, item, "Shiny", false, 1, 0, 0));
            Assert.Equal(1, item.MulticastBonus);
            Assert.True(NativeEnchant.Remove(ctx, item, item, 2, 0, 1));
            Assert.Equal(0, item.MulticastBonus);
        }

        private static void InCombatEnchantDeadlyAddsToBaseCrit()
        {
            BattleConfig config = Build(Real("Letter Opener", 0, "Bronze"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Near(1.0, item.CritBonus);
            Assert.True(NativeEnchant.Apply(ctx, item, item, "Deadly", false, 1, 0, 0));
            Assert.Near(1.0, item.CritBonus); // Deadly 增加的是暴击伤害，不是暴击率。
            Assert.Near(100, ItemAttributes.Get(item, "DamageCrit"));
            Assert.True(NativeEnchant.Remove(ctx, item, item, 2, 0, 1));
            Assert.Near(1.0, item.CritBonus);
            Assert.Near(0, ItemAttributes.Get(item, "DamageCrit"));
        }

        private static void InCombatPassiveEnchantActuallyTriggers()
        {
            BattleConfig config = Build(Real("Tinfoil Hat", 0, "Diamond", "Deadly"), Real("Gamma Ray", 2, "Gold"));
            Map(config);
            Enemy(config, 0);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem ray = At(ctx, 2);
            Triggers.Enqueue(ctx, new TriggerPayload
            {
                Event = "onUse", SourceSide = Side.Enemy, SourceItemId = "enemy:0",
                TargetSide = Side.Enemy, TargetItemId = "enemy:0"
            });
            Triggers.Drain(ctx, 1);
            // Deadly 是 TTriggerOnItemUsed 的被动附魔：敌人使用物品后给本方可暴击卡 +5% CritChance。
            Assert.Near(0.05, ray.CritBonus);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute" && ev.Note == "CritChance"; }));
        }

        private static void SpiritDiffuserFightStartEnchantIsExecuted()
        {
            BattleConfig config = Build(Real("Spirit Diffuser", 0, "Diamond"), Real("Gamma Ray", 1, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem target = At(ctx, 1);
            int effects = target.Card.Effects.Count, triggers = target.Card.Triggers.Count, auras = target.Card.NativeRuntimeAuras.Count;
            int chain = 0;
            foreach (InternalItem item in ctx.Items)
            {
                foreach (TriggerDef trigger in item.Card.Triggers)
                {
                    if (trigger.Event != "atCombatStart") continue;
                    if (!NativeSkillSupport.PrerequisitesMatch(ctx, item, trigger.SkillRule)) continue;
                    foreach (Effect effect in trigger.Effects) Effects.ResolveEffect(ctx, effect, item, 0, 1, chain);
                }
            }
            BattleEvent enchant = ctx.Events.Find(delegate(BattleEvent ev) { return ev.Kind == "enchant"; });
            Assert.True(enchant != null);
            Assert.Near(0, enchant.Time);
            Assert.True(!string.IsNullOrEmpty(enchant.Note));
            Assert.Equal(enchant.Note, target.Card.EnchantmentName);
            Assert.False(NativeSkillSupport.Empty(target.Card.NativeEnchantment));
            bool applied = target.Card.NativeRuntimeAuras.Count > auras || target.Card.Effects.Count > effects ||
                target.Card.Triggers.Count > triggers || NativeDynamicAuras.SlowResistance(target) > 0 ||
                NativeDynamicAuras.FreezeResistance(target) > 0 || target.CritBonus > 0 || target.MulticastBonus > 0;
            Assert.True(applied);
        }

        private static void PreventOverrideBlocksInCombatEnchant()
        {
            BattleConfig config = Build(Real("Gamma Ray", 0, "Gold", "Deadly"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Equal("Deadly", item.Card.EnchantmentName);
            Assert.False(NativeEnchant.Apply(ctx, item, item, "Radiant", true, 1, 0, 0));
            Assert.Equal("Deadly", item.Card.EnchantmentName);
            Assert.True(ctx.Warnings.Count > 0);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "enchantBlocked" && Math.Abs(ev.Value.Value) < 1e-9; }));
            Assert.False(NativeEnchant.Remove(ctx, item, item, 2, 0, 1));
            Assert.Equal("Deadly", item.Card.EnchantmentName);
        }

        private static void LegacyRadiantEnchantmentExecutesMechanics()
        {
            CardDef card = new CardDef { Id = "legacy-radiant", Cooldown = 5, Size = "Small" };
            LiveCardEntry entry = Real("Gamma Ray", 0, "Gold", "Radiant");
            entry.Template = JsonValue.Null;
            List<string> warnings = new List<string>();
            CardDef mapped = EnchantmentMapper.Apply(card, entry, warnings);
            Assert.Equal(0, warnings.Count);
            Assert.Near(100, mapped.PercentSlowReduction);
            Assert.Near(100, mapped.PercentFreezeReduction);
            Assert.Equal(1, mapped.DestroyImmunity);

            BattleConfig config = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000, MaxTime = 10 };
            config.Cards[mapped.Id] = mapped;
            config.Player.Items.Add(new LoadoutItem { CardId = mapped.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            Effects.ResolveEffect(ctx, SelfEffect("freeze", 2), item, 1, 0, null);
            Assert.Near(1, item.FrozenUntil);
            Effects.ResolveEffect(ctx, new Effect { Kind = "destroy", Target = new Target { Kind = "selfItem" } }, item, 2, 0, null);
            Assert.False(item.Destroyed);
            Assert.True(ctx.Events.Exists(delegate(BattleEvent ev) { return ev.Kind == "destroyBlocked"; }));
        }

        private static void LegacyPassiveEnchantmentMapsTrigger()
        {
            CardDef card = new CardDef { Id = "legacy-heavy", Cooldown = 5, Size = "Small" };
            LiveCardEntry entry = Real("Dishwasher", 0, "Gold", "Heavy");
            entry.Template = JsonValue.Null;
            List<string> warnings = new List<string>();
            CardDef mapped = EnchantmentMapper.Apply(card, entry, warnings);
            Assert.Equal(0, warnings.Count);
            Assert.True(mapped.Triggers.Exists(delegate(TriggerDef trigger) { return trigger.Event == "onUse" && trigger.FromEnchantment; }));
        }

        private static void SubtractAurasReduceCritAndCounters()
        {
            BattleConfig config = Build(Real("Foul Mushroom", 0, "Diamond", "Deadly"));
            Map(config);
            CardDef enemy = Enemy(config, 0);
            enemy.LiveAttributes["CritChance"] = 100;
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(0.6, At(ctx, 0, Side.Enemy).CritBonus);

            BattleConfig piggy = Build(Real("Piggy Bank", 0, "Gold", "Golden"));
            Map(piggy);
            BattleContext piggyCtx = BattleEngine.InitBattle(piggy);
            Assert.Near(0, ItemAttributes.Get(At(piggyCtx, 0), "Custom_1"));
            Assert.Near(1, ItemAttributes.Get(At(piggyCtx, 0), "Custom_0"));
        }

        private static void RadiantHasteReductionHalvesHaste()
        {
            BattleConfig config = Build(Real("Spark Plug", 0, "Gold", "Radiant"), Real("Gamma Ray", 1, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, 0), neighbor = At(ctx, 1);
            Assert.Near(50, NativeDynamicAuras.HasteResistance(neighbor));
            Assert.Near(0, NativeDynamicAuras.HasteResistance(source));
            Effects.ResolveEffect(ctx, new Effect { Kind = "haste", Seconds = 2, Target = new Target { Kind = "selfItem" } }, neighbor, 1, 0, null);
            Assert.Near(2, neighbor.HasteUntil);
            Effects.ResolveEffect(ctx, new Effect { Kind = "haste", Seconds = 2, Target = new Target { Kind = "selfItem" } }, source, 1, 0, null);
            Assert.Near(3, source.HasteUntil);
        }

        private static void ShinyFlyingTargetsAddsTargets()
        {
            BattleConfig config = Build(Real("Hover Tech", 0, "Silver", "Shiny"),
                Real("Gamma Ray", 2, "Gold"), Real("Cog", 3, "Gold"), Real("Letter Opener", 4, "Bronze"),
                Real("Tinfoil Hat", 5, "Diamond"), Real("Chunk of Gold", 6, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem hover = At(ctx, 0);
            Assert.Near(13, ItemAttributes.Get(hover, "FlyingTargets"));
            BattleEngine.ForceUse(ctx, hover, 1, 0);
            int flying = 0;
            foreach (InternalItem item in ctx.Items)
                if (item.Side == Side.Player && item.Id != hover.Id && item.Flying) flying++;
            Assert.Equal(5, flying);
        }

        private static void ShinyEnchantTargetsIncreasesEnchants()
        {
            BattleConfig config = Build(Real("Skyspirit Ashes", 0, "Gold", "Shiny"),
                Real("Gamma Ray", 3, "Gold"), Real("Cog", 4, "Gold"), Real("Letter Opener", 5, "Bronze"),
                Real("Tinfoil Hat", 6, "Diamond"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = At(ctx, 0);
            Assert.Near(3, ItemAttributes.Get(source, "EnchantTargets"));
            int chain = 0;
            foreach (TriggerDef trigger in source.Card.Triggers)
            {
                if (trigger.Event != "atCombatStart") continue;
                foreach (Effect effect in trigger.Effects) Effects.ResolveEffect(ctx, effect, source, 0, 1, chain);
            }
            List<BattleEvent> enchants = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "enchant"; });
            Assert.Equal(3, enchants.Count);
            HashSet<string> targets = new HashSet<string>();
            foreach (BattleEvent ev in enchants) targets.Add(ev.TargetItemId);
            Assert.Equal(3, targets.Count);
        }

        private static void ShowcaseDeadlyPicksOneTargetStably()
        {
            BattleConfig config = Build(Real("Showcase", 0, "Gold", "Deadly"));
            Map(config);
            CardDef weapon = new CardDef { Id = "g07-weapon", BaseItemId = "g07-weapon", Size = "Small", Cooldown = 10, NativeAttributesAreLive = false };
            weapon.Tags.Add("Weapon");
            weapon.HiddenTags.Add("CanCrit");
            weapon.LiveAttributes["CritChance"] = 0;
            CardDef shield = new CardDef { Id = "g07-shield", BaseItemId = "g07-shield", Size = "Small", Cooldown = 10, NativeAttributesAreLive = false };
            shield.HiddenTags.Add("Shield");
            shield.HiddenTags.Add("CanCrit");
            shield.LiveAttributes["CritChance"] = 0;
            config.Cards[weapon.Id] = weapon; config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 2 });
            config.Cards[shield.Id] = shield; config.Player.Items.Add(new LoadoutItem { CardId = shield.Id, Slot = 3 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem first = At(ctx, 2), second = At(ctx, 3);
            Assert.True((Math.Abs(first.CritBonus - 0.4) < 1e-9) != (Math.Abs(second.CritBonus - 0.4) < 1e-9));
            string chosen = Math.Abs(first.CritBonus - 0.4) < 1e-9 ? first.Id : second.Id;
            for (int i = 0; i < 8; i++) NativeDynamicAuras.Refresh(ctx);
            Assert.Equal(chosen, Math.Abs(first.CritBonus - 0.4) < 1e-9 ? first.Id : second.Id);
        }

        private static void RadiantXMostAuraTargetsRightmost()
        {
            BattleConfig config = Build(Real("Fiber Optics", 0, "Gold", "Radiant"),
                Real("Gamma Ray", 1, "Gold"), Real("Cog", 2, "Gold"), Real("Letter Opener", 3, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem right = At(ctx, 3);
            Assert.Near(50, NativeDynamicAuras.SlowResistance(right));
            Assert.Near(50, NativeDynamicAuras.FreezeResistance(right));
            Assert.Near(0, NativeDynamicAuras.SlowResistance(At(ctx, 1)));
            Effects.ResolveEffect(ctx, SelfEffect("slow", 2), right, 1, 0, null);
            Assert.Near(2, right.SlowUntil);
        }

        private static void TrailMarkersCooldownSubtractIncreasesCooldown()
        {
            BattleConfig config = Build(Real("Trail Markers", 0, "Gold"),
                Real("Gamma Ray", 1, "Gold"), Real("Cog", 2, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            // Aura 0 全局 +1s，Aura 2 最左 +1 秒缩减；Cog 4s 只承受 +1s。
            Assert.Near(5, At(ctx, 2).CooldownMax);
            Assert.Near(6, At(ctx, 1).CooldownMax);
        }

        private static void SlingshotGoldenIncomeReferenceMaps()
        {
            BattleConfig config = Build(Real("Slingshot", 0, "Gold", "Golden"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.True(ItemAttributes.Get(At(ctx, 0), "AmmoMax") >= 0);
        }

        private static void LichenCleanseUsesRuntimeRemoveAmount()
        {
            BattleConfig config = Build(Real("Lichen", 0, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem lichen = At(ctx, 0);
            ctx.Player.Burn = 10;
            ctx.Player.Poison = 10;
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(5, ItemAttributes.Get(lichen, "BurnRemoveAmount"));
            Assert.Near(5, ItemAttributes.Get(lichen, "PoisonRemoveAmount"));
            Effect burnRemove = null;
            foreach (TriggerDef trigger in lichen.Card.Triggers)
            {
                if (trigger.Event != "onPlayerEnraged") continue;
                foreach (Effect effect in trigger.Effects) if (effect.Kind == "burnRemove") burnRemove = effect;
            }
            Assert.True(burnRemove != null);
            Effects.ResolveEffect(ctx, burnRemove, lichen, 1, 0, null);
            Assert.Near(5, ctx.Player.Burn);
        }

        private static void DishwasherHeatedRadiantAuraExecutes()
        {
            BattleConfig config = Build(Real("Dishwasher", 0, "Gold", "Radiant"), Real("Letter Opener", 1, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem radish = At(ctx, 0), weapon = At(ctx, 1);
            Assert.Near(0, NativeDynamicAuras.SlowResistance(weapon));
            BattleEngine.ForceUse(ctx, radish, 1, 0);
            // Ability 0 heats the other Tool/Weapon; Radiant's Heated-conditioned aura then applies.
            Assert.Near(1, ItemAttributes.Get(weapon, "Heated"));
            Assert.Near(50, NativeDynamicAuras.SlowResistance(weapon));
            Assert.Near(50, NativeDynamicAuras.FreezeResistance(weapon));
        }

        private static void ShurikenUsesRuntimeAmmoForMulticast()
        {
            BattleConfig config = Build(Real("Shuriken", 0, "Gold"));
            Map(config);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = At(ctx, 0);
            Assert.Near(5, ItemAttributes.Get(item, "Ammo"));
            Assert.Near(5, ItemAttributes.Get(item, "Multicast"));
            Assert.Equal(4, item.MulticastBonus);
        }

        private static void SantokuCopiesCritToLeftCard()
        {
            BattleConfig config = Build(Real("Santoku", 0, "Gold"));
            Map(config);
            CardDef left = new CardDef { Id = "g07-santoku-left", BaseItemId = "g07-santoku-left", Size = "Small", Cooldown = 10, NativeAttributesAreLive = false };
            left.HiddenTags.Add("CanCrit");
            left.LiveAttributes["CritChance"] = 0;
            config.Cards[left.Id] = left;
            config.Player.Items[0].Slot = 1;
            config.Player.Items.Add(new LoadoutItem { CardId = left.Id, Slot = 0 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem santoku = At(ctx, 1), target = At(ctx, 0);
            BattleEngine.ForceUse(ctx, santoku, 1, 0);
            Assert.Near(0.15, target.CritBonus);
        }

        private static void MoneyFurnaceZeroMultiplierSetsPriceToZero()
        {
            BattleConfig config = Build(Real("Money Furnace", 0, "Gold"));
            Map(config);
            CardDef spare = new CardDef { Id = "g07-spare-change", BaseItemId = "f212afe2-08d1-40e3-978b-762d345bc7e5", Size = "Small", Cooldown = 0, NativeAttributesAreLive = false };
            spare.LiveAttributes["SellPrice"] = 1;
            config.Cards[spare.Id] = spare;
            config.Player.Items.Add(new LoadoutItem { CardId = spare.Id, Slot = 1 });
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(0, ItemAttributes.Get(At(ctx, 1), "SellPrice"));
        }
    }
}
