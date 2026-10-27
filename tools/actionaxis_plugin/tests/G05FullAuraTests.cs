using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G05 全量：人物属性引用、静态/动态人物光环、AbsolutePlayerSkills、Both。
    public static class G05FullAuraTests
    {
        private static JsonValue _templates;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _templates = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g05_full.json"));
            CashCannonReadsPlayerCustom3();
            HeavyMettleReadsPlayerLevel();
            WildStrengthAddsHeroCrit();
            PlayerShieldConditionMovesCooldown();
            RitualDaggerCountsAbsolutePlayerSkills();
            HealthyLifestyleCountsNonWeaponCards();
            WatersOfInfinityCountsNonWeaponCards();
            FireClawAggregatesOtherBurn();
            NanobotOrangeBuildsDamageFromOwnAura();
            GiantSubCopiesOtherFoodValues();
            ShurikenTurnsAmmoIntoMulticast();
            SantokuCopiesCritToLeftCard();
            GrowthSpurtScalesPlayerMaxHp();
            SwoleUsesCardDamageAggregate();
            IronSharpensIronUsesPlayerCustom4();
            EconomyAurasAreOutOfCombat();
            MixedStaticDynamicSkillsMap();
            NitroBurnsBothPlayers();
        }

        private static JsonValue Template(string name) { return _templates[name]; }
        private static string Id(JsonValue template) { return NativeSkillSupport.Text(template, "Id"); }

        private static BattleConfig Board(double playerHp = 1000, double enemyHp = 1000, double maxTime = 20)
        {
            return new BattleConfig
            {
                NativeMechanics = true, PlayerMaxHp = playerHp, EnemyMaxHp = enemyHp,
                PlayerStartHp = playerHp, EnemyStartHp = enemyHp, MaxTime = maxTime
            };
        }

        private static BattleConfig ItemBoard(JsonValue template, string tier, double maxTime = 20)
        {
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, tier);
            LiveCardEntry entry = new LiveCardEntry
            {
                TemplateId = Id(template), Template = template, Tier = tier,
                Size = NativeSkillSupport.Text(template, "Size"), Socket = 0,
                AttributesAreLive = false, Attributes = attrs
            };
            BattleConfig config = CardMapper.Build(new[] { entry }, EmptyCatalog, 1000, 1000, maxTime).Config;
            config.NativeMechanics = true;
            return config;
        }

        private static LiveSkillEntry Skill(JsonValue template, string tier)
        {
            return new LiveSkillEntry { TemplateId = Id(template), Template = template, Tier = tier,
                Name = NativeSkillSupport.Text(template, "InternalName"), TemplateBoard = true,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier) };
        }

        private static CardDef Card(string id, string hidden)
        {
            CardDef card = new CardDef { Id = id, Name = id, Cooldown = 0, NativeAttributesAreLive = false };
            if (!string.IsNullOrEmpty(hidden)) card.HiddenTags.Add(hidden);
            card.LiveAttributes["CooldownMax"] = 0;
            return card;
        }

        private static void CashCannonReadsPlayerCustom3()
        {
            BattleConfig config = ItemBoard(Template("Cash Cannon"), "Silver");
            // 回放 a9dc7a52：Custom_3=167 → DamageAmount 334。
            config.PlayerAttributes["Custom_3"] = 167;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            // Cash Cannon: DamageAmount += 2 × Player.Custom_3.
            Assert.Near(334, ItemAttributes.Get(item, "DamageAmount"));
        }

        private static void HeavyMettleReadsPlayerLevel()
        {
            BattleConfig config = Board();
            // 回放 ab8439ff：Level 13，Silver Custom_0=2 → +26 Shield。
            config.PlayerAttributes["Level"] = 13;
            CardDef shield = Card("shield-card", "Shield");
            config.Cards[shield.Id] = shield;
            config.Player.Items.Add(new LoadoutItem { CardId = shield.Id, Slot = 0 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Heavy Mettle"), "Silver") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == shield.Id; });
            // Heavy Mettle Silver: Custom_0=2, +2 × Level ShieldApply.
            Assert.Near(26, ItemAttributes.Get(item, "ShieldApplyAmount"));
        }

        private static void WildStrengthAddsHeroCrit()
        {
            BattleConfig config = Board();
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Wild Strength"), "Gold") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(5, ctx.Player.AuraCritChance);
        }

        private static void PlayerShieldConditionMovesCooldown()
        {
            BattleConfig config = ItemBoard(Template("Balloon Bot"), "Silver");
            config.PlayerStartShield = 1;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            Assert.Near(2, item.CooldownMax); // 4s - 2s while Shielded.
            config = ItemBoard(Template("Balloon Bot"), "Silver");
            config.PlayerStartShield = 0;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            ctx = BattleEngine.InitBattle(config);
            Assert.Near(4, ctx.Items[0].CooldownMax);
        }

        private static void RitualDaggerCountsAbsolutePlayerSkills()
        {
            const string requiredSkill = "0f1c6082-c742-4d6a-bf11-12ded9a8bb29";
            BattleConfig config = ItemBoard(Template("Ritual Dagger"), "Gold");
            CardDef skill = Card("required-skill", null);
            skill.BaseItemId = requiredSkill;
            config.PlayerSkills.Add(skill);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            // Gold Ritual Dagger: 8000ms - Custom_0(1000) = 7000ms when the skill exists.
            Assert.Near(7, item.CooldownMax);

            BattleConfig without = ItemBoard(Template("Ritual Dagger"), "Gold");
            Assert.Equal(0, NativeCardAbilityMapper.Apply(without).Count);
            BattleContext noSkill = BattleEngine.InitBattle(without);
            Assert.Near(8, noSkill.Items[0].CooldownMax);
        }

        private static void HealthyLifestyleCountsNonWeaponCards()
        {
            BattleConfig config = Board();
            CardDef weapon = Card("weapon", null);
            weapon.Tags.Add("Weapon");
            config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 0 });
            for (int i = 0; i < 5; i++)
            {
                CardDef item = Card("healthy-item-" + i, null);
                config.Cards[item.Id] = item;
                config.Player.Items.Add(new LoadoutItem { CardId = item.Id, Slot = i + 1 });
            }
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Healthy Lifestyle"), "Gold") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            NativeDynamicAuras.Refresh(ctx);
            // 回放 4ce4c775：Gold Custom_0=6，5 张非 Weapon → +30 Regen。
            Assert.Near(30, ctx.Player.Regen);
        }

        private static void WatersOfInfinityCountsNonWeaponCards()
        {
            BattleConfig config = Board();
            for (int i = 0; i < 3; i++)
            {
                CardDef item = Card("waters-item-" + i, null);
                config.Cards[item.Id] = item;
                config.Player.Items.Add(new LoadoutItem { CardId = item.Id, Slot = i });
            }
            CardDef weapon = Card("waters-weapon", null);
            weapon.Tags.Add("Weapon");
            config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 4 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Waters of Infinity"), "Silver") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            NativeDynamicAuras.Refresh(ctx);
            // 回放 ab8439ff／c61d5b0a：Silver Custom_0=10，非 Weapon 数 ×10。
            Assert.Near(30, ctx.Player.Regen);
        }

        private static void FireClawAggregatesOtherBurn()
        {
            BattleConfig config = ItemBoard(Template("Fire Claw"), "Diamond");
            CardDef furnace = Card("burn-534", null);
            furnace.LiveAttributes["BurnApplyAmount"] = 534;
            CardDef salt = Card("burn-6", null);
            salt.LiveAttributes["BurnApplyAmount"] = 6;
            config.Cards[furnace.Id] = furnace;
            config.Cards[salt.Id] = salt;
            config.Player.Items.Add(new LoadoutItem { CardId = furnace.Id, Slot = 1 });
            config.Player.Items.Add(new LoadoutItem { CardId = salt.Id, Slot = 2 });
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            // 回放 a94eda88：10 + 100% × (534 + 6) = 550。
            Assert.Near(550, ItemAttributes.Get(item, "BurnApplyAmount"));
        }

        private static void NanobotOrangeBuildsDamageFromOwnAura()
        {
            BattleConfig config = ItemBoard(Template("Nanobot Orange"), "Silver");
            for (int i = 0; i < 3; i++)
            {
                CardDef friend = Card("nanobot-friend-" + i, null);
                friend.Tags.Add("Friend");
                config.Cards[friend.Id] = friend;
                config.Player.Items.Add(new LoadoutItem { CardId = friend.Id, Slot = i + 1 });
            }
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            // 自身光环：Custom_0=15 × 4 个 Friend（Nanobot Orange 自身也是 Friend）= 60。
            Assert.Near(60, ItemAttributes.Get(ctx.Items[0], "DamageAmount"));
        }

        private static void GiantSubCopiesOtherFoodValues()
        {
            BattleConfig config = ItemBoard(Template("Giant Sub"), "Diamond");
            CardDef food = Card("giant-sub-food", null);
            food.Tags.Add("Food");
            food.LiveAttributes["ShieldApplyAmount"] = 62;
            food.LiveAttributes["RegenApplyAmount"] = 4;
            food.LiveAttributes["BurnApplyAmount"] = 4;
            config.Cards[food.Id] = food;
            config.Player.Items.Add(new LoadoutItem { CardId = food.Id, Slot = 1 });
            config.Cards[config.Player.Items[0].CardId].LiveAttributes["BurnApplyAmount"] = 0;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem sub = ctx.Items[0];
            Assert.Near(62, ItemAttributes.Get(sub, "ShieldApplyAmount"));
            Assert.Near(4, ItemAttributes.Get(sub, "RegenApplyAmount"));
            Assert.Near(4, ItemAttributes.Get(sub, "BurnApplyAmount"));
        }

        private static void ShurikenTurnsAmmoIntoMulticast()
        {
            BattleConfig config = ItemBoard(Template("Shuriken"), "Bronze");
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items[0];
            // Ammo 3 → Multicast + (3-1) = 2 extra casts.
            Assert.Near(3, item.Ammo);
            Assert.Equal(2, item.MulticastBonus);
        }

        private static void SantokuCopiesCritToLeftCard()
        {
            BattleConfig config = ItemBoard(Template("Santoku"), "Bronze");
            CardDef left = Card("santoku-left", "CanCrit");
            config.Cards[left.Id] = left;
            config.Player.Items[0].Slot = 1;
            config.Player.Items.Insert(0, new LoadoutItem { CardId = left.Id, Slot = 0 });
            config.Cards[config.Player.Items[1].CardId].LiveAttributes["CritChance"] = 10;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem leftItem = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == left.Id; });
            Assert.Near(10, ItemAttributes.Get(leftItem, "CritChance"));
        }

        private static void GrowthSpurtScalesPlayerMaxHp()
        {
            BattleConfig config = Board(1000, 1000, 20);
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Growth Spurt"), "Silver") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            // Silver Custom_0=10 → +10% Max Health.
            Assert.Near(1100, ctx.Player.MaxHp);
            Assert.Near(1100, ctx.Player.Hp);
        }

        private static void SwoleUsesCardDamageAggregate()
        {
            BattleConfig config = Board(1000, 1000, 20);
            CardDef first = Card("swole-20", null);
            first.LiveAttributes["DamageAmount"] = 20;
            CardDef second = Card("swole-30", null);
            second.LiveAttributes["DamageAmount"] = 30;
            config.Cards[first.Id] = first;
            config.Cards[second.Id] = second;
            config.Player.Items.Add(new LoadoutItem { CardId = first.Id, Slot = 0 });
            config.Player.Items.Add(new LoadoutItem { CardId = second.Id, Slot = 1 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Swole"), "Gold") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            // Swole Gold：HealthMax += SelfHand 的 DamageAmount 总和 × Custom_0(1)。
            Assert.Near(1050, ctx.Player.MaxHp);
            Assert.Near(1050, ctx.Player.Hp);
        }

        private static void IronSharpensIronUsesPlayerCustom4()
        {
            BattleConfig config = Board(1000, 1000, 20);
            // 人物战士数/武器计数由原生 Custom_4 提供；Gold Custom_0=4。
            config.PlayerAttributes["Custom_4"] = 20;
            CardDef weapon = Card("sharpened-weapon", null);
            weapon.Tags.Add("Weapon");
            weapon.LiveAttributes["DamageAmount"] = 15;
            config.Cards[weapon.Id] = weapon;
            config.Player.Items.Add(new LoadoutItem { CardId = weapon.Id, Slot = 0 });
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template("Iron Sharpens Iron"), "Gold") }, null, Side.Player);
            Assert.Equal(0, mapped.Warnings.Count);
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == weapon.Id; });
            // 最左 Weapon：15 + Custom_4(20) × Custom_0(4) = 95。
            Assert.Near(95, ItemAttributes.Get(item, "DamageAmount"));
        }

        private static void EconomyAurasAreOutOfCombat()
        {
            foreach (string name in new[] { "Arbitrage", "Open for Business" })
            {
                BattleConfig config = Board();
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template(name), "Gold") }, null, Side.Player);
                Assert.Equal(0, mapped.Warnings.Count);
                Assert.Equal(1, mapped.Skills.Count);
                Assert.Equal("out-of-combat", mapped.Skills[0].Status);
                Assert.Equal(0, config.PlayerSkills.Count);
            }
        }

        private static void MixedStaticDynamicSkillsMap()
        {
            foreach (string name in new[] { "First Strike", "Inexorable" })
            {
                BattleConfig config = Board();
                SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(Template(name), "Gold") }, null, Side.Enemy);
                Assert.Equal(0, mapped.Warnings.Count);
                Assert.True(mapped.Skills.Count == 1);
                Assert.Equal("simulated", mapped.Skills[0].Status);
                Assert.Equal(1, config.EnemySkills.Count);
            }
        }

        private static void NitroBurnsBothPlayers()
        {
            BattleConfig config = ItemBoard(Template("Nitro"), "Gold", 8);
            config.PlayerMaxHp = 1000;
            config.EnemyMaxHp = 1000;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            int playerBurn = 0, enemyBurn = 0;
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind != "burn") continue;
                if (ev.TargetSide == Side.Player) playerBurn++;
                if (ev.TargetSide == Side.Enemy) enemyBurn++;
            }
            Assert.True(playerBurn > 0);
            Assert.True(enemyBurn > 0);
        }
    }
}
