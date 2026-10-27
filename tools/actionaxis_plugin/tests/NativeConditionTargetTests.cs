using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // 阶段 2：条件判定（Tier / Id / 附魔 / 同侧最高最低）与最左最右目标。
    public static class NativeConditionTargetTests
    {
        public static void Run()
        {
            TierIdAndEnchantmentConditions();
            AttributeHighestLowestUsesSameSide();
            PlayerHeroConditionUsesHeroIdentity();
            CardMapperCarriesEnchantmentName();
            XMostTargetsRespectFilters();
            StaticAuraUsesSameSideRanking();
            UnsupportedConditionsFailClosed();
        }

        private static CardDef Item(BattleConfig config, string id, Side side, int slot,
            string tier = "Bronze", string baseItemId = null, string enchantment = null)
        {
            CardDef card = new CardDef { Id = id, BaseItemId = baseItemId ?? id, Tier = tier, EnchantmentName = enchantment };
            config.Cards[card.Id] = card;
            (side == Side.Player ? config.Player : config.Enemy).Items.Add(new LoadoutItem { CardId = card.Id, Slot = slot });
            return card;
        }

        private static BattleConfig Config()
        {
            return new BattleConfig { NativeMechanics = true, PlayerMaxHp = 1000, EnemyMaxHp = 1000, MaxTime = 30 };
        }

        private static JsonValue Condition(string json) { return Json.Parse(json); }

        private static void TierIdAndEnchantmentConditions()
        {
            BattleConfig config = Config();
            CardDef plain = Item(config, "plain", Side.Player, 0, "Gold", "base-plain");
            CardDef fancy = Item(config, "fancy", Side.Player, 1, "Legendary", "base-fancy", "Icy");
            BattleContext ctx = BattleEngine.InitBattle(config);

            JsonValue notLegendary = Condition(@"{""$type"":""TCardConditionalTier"",""Tiers"":[""Legendary""],""IsNot"":true}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[0], notLegendary, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], notLegendary, ctx));

            JsonValue gold = Condition(@"{""$type"":""TCardConditionalTier"",""Tiers"":[""Gold"",""Diamond""],""IsNot"":false}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[0], gold, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], gold, ctx));

            JsonValue id = Condition(@"{""$type"":""TCardConditionalId"",""Id"":""base-plain"",""IsNot"":false}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[0], id, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], id, ctx));
            JsonValue notId = Condition(@"{""$type"":""TCardConditionalId"",""Id"":""base-plain"",""IsNot"":true}");
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[0], notId, ctx));
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[1], notId, ctx));

            // 原生 Enchantment:null 表示“任意附魔”，IsNot 再取反。
            JsonValue anyEnchant = Condition(@"{""$type"":""TCardConditionalHasEnchantment"",""Enchantment"":null,""IsNot"":true}");
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[0], anyEnchant, ctx));
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[1], anyEnchant, ctx));
            JsonValue noEnchant = Condition(@"{""$type"":""TCardConditionalHasEnchantment"",""Enchantment"":null,""IsNot"":false}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[0], noEnchant, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], noEnchant, ctx));
            JsonValue icy = Condition(@"{""$type"":""TCardConditionalHasEnchantment"",""Enchantment"":""Icy"",""IsNot"":false}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[1], icy, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[0], icy, ctx));
            JsonValue notIcy = Condition(@"{""$type"":""TCardConditionalHasEnchantment"",""Enchantment"":""Icy"",""IsNot"":true}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[0], notIcy, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], notIcy, ctx));

            // Id 缺失或品质未知时必须 fail-closed。
            Assert.False(NativeSkillSupport.ConditionSupported(Condition(@"{""$type"":""TCardConditionalId"",""Id"":""""}")));
            Assert.False(NativeSkillSupport.ConditionSupported(Condition(@"{""$type"":""TCardConditionalTier"",""Tiers"":[""Mythic""]}")));
            Assert.False(NativeSkillSupport.ConditionSupported(Condition(@"{""$type"":""TCardConditionalHasEnchantment"",""Enchantment"":3}")));
            Assert.True(plain.Tier == "Gold" && fancy.EnchantmentName == "Icy");
        }

        private static void AttributeHighestLowestUsesSameSide()
        {
            BattleConfig config = Config();
            CardDef low = Item(config, "low", Side.Player, 0);
            low.LiveAttributes["ShieldApplyAmount"] = 5;
            CardDef high = Item(config, "high", Side.Player, 1);
            high.LiveAttributes["ShieldApplyAmount"] = 9;
            CardDef enemy = Item(config, "enemy", Side.Enemy, 0);
            enemy.LiveAttributes["ShieldApplyAmount"] = 20;
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem lowItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "low"; });
            InternalItem highItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "high"; });
            InternalItem enemyItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "enemy"; });

            JsonValue highest = Condition(@"{""$type"":""TCardConditionalAttributeHighest"",""AttributeType"":""ShieldApplyAmount""}");
            JsonValue lowest = Condition(@"{""$type"":""TCardConditionalAttributeLowest"",""AttributeType"":""ShieldApplyAmount""}");
            Assert.False(NativeSkillSupport.CardMatches(lowItem, highest, ctx));
            Assert.True(NativeSkillSupport.CardMatches(highItem, highest, ctx));
            Assert.True(NativeSkillSupport.CardMatches(enemyItem, highest, ctx));
            Assert.True(NativeSkillSupport.CardMatches(lowItem, lowest, ctx));
            Assert.False(NativeSkillSupport.CardMatches(highItem, lowest, ctx));
            // 没有战斗上下文时不能假装知道排行。
            Assert.False(NativeSkillSupport.CardMatches(highItem, highest));
        }

        private static void PlayerHeroConditionUsesHeroIdentity()
        {
            BattleConfig config = Config();
            config.PlayerHero = "Vanessa";
            CardDef own = Item(config, "own", Side.Player, 0);
            own.Heroes.Add("Vanessa");
            CardDef foreign = Item(config, "foreign", Side.Player, 1);
            foreign.Heroes.Add("Pygmalien");
            CardDef common = Item(config, "common", Side.Player, 2);
            common.Heroes.Add("Common");
            CardDef unknownHero = Item(config, "unknown-hero", Side.Player, 3);
            BattleContext ctx = BattleEngine.InitBattle(config);

            JsonValue otherHero = Condition(@"{""$type"":""TCardConditionalPlayerHero"",""IsSameAsPlayerHero"":false}");
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[0], otherHero, ctx));
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[1], otherHero, ctx));
            // Common 是全英雄通用，不算“另一个英雄的物品”。
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[2], otherHero, ctx));

            JsonValue sameHero = Condition(@"{""$type"":""TCardConditionalPlayerHero"",""IsSameAsPlayerHero"":true}");
            Assert.True(NativeSkillSupport.CardMatches(ctx.Items[0], sameHero, ctx));
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], sameHero, ctx));

            // 无英雄列表或无玩家英雄时按不匹配处理，并留下可见警告。
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[3], otherHero, ctx));
            Assert.True(ctx.Warnings.Count > 0);
            config.PlayerHero = null;
            Assert.False(NativeSkillSupport.CardMatches(ctx.Items[1], otherHero, ctx));
        }

        private static void CardMapperCarriesEnchantmentName()
        {
            JsonValue template = Json.Parse(@"{""$type"":""TCardItem"",""Size"":""Small"",""Tags"":[],""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":1000},""AbilityIds"":[],""AuraIds"":[],""TooltipIds"":[]}},""Abilities"":{},""Auras"":{}}");
            LiveCardEntry entry = new LiveCardEntry { TemplateId = "enchanted", Tier = "Bronze", Socket = 0, Template = template, Enchantment = "Icy" };
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry> { entry },
                CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 30);
            CardDef card = mapped.Config.Cards[mapped.Config.Player.Items[0].CardId];
            Assert.Equal("Icy", card.EnchantmentName);
            Assert.Equal("Icy", card.Clone().EnchantmentName);
            entry.Enchantment = "None";
            mapped = CardMapper.Build(new List<LiveCardEntry> { entry },
                CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 30);
            Assert.True(mapped.Config.Cards[mapped.Config.Player.Items[0].CardId].EnchantmentName == null);
        }

        private static void XMostTargetsRespectFilters()
        {
            JsonValue left = Json.Parse(@"{""$type"":""TTargetCardXMost"",""TargetSection"":""SelfHand"",""TargetMode"":""LeftMostCard"",
              ""ExcludeSelf"":false,""Conditions"":{""$type"":""TCardConditionalTag"",""Tags"":[""Ammo""],""Operator"":""Any""}}");
            JsonValue right = Json.Parse(@"{""$type"":""TTargetCardXMost"",""TargetSection"":""SelfHand"",""TargetMode"":""RightMostCard"",
              ""ExcludeSelf"":false,""Conditions"":{""$type"":""TCardConditionalTag"",""Tags"":[""Ammo""],""Operator"":""Any""}}");
            Target leftTarget = SkillMapper.MapCardTarget(left);
            Target rightTarget = SkillMapper.MapCardTarget(right);
            Assert.Equal("leftMostSelf", leftTarget.Kind);
            Assert.Equal("rightMostSelf", rightTarget.Kind);
            Assert.Throws<FormatException>(delegate {
                SkillMapper.MapCardTarget(Json.Parse(@"{""$type"":""TTargetCardXMost"",""TargetSection"":""SelfHand"",""TargetMode"":""MiddleCard""}"));
            });

            BattleConfig config = Config();
            CardDef source = Item(config, "source", Side.Player, 3);
            CardDef leftAmmo = Item(config, "left-ammo", Side.Player, 0);
            leftAmmo.Tags.Add("Ammo");
            leftAmmo.AmmoMax = 2;
            leftAmmo.LiveAttributes["AmmoMax"] = 2;
            CardDef junk = Item(config, "junk", Side.Player, 1);
            CardDef rightAmmo = Item(config, "right-ammo", Side.Player, 2);
            rightAmmo.Tags.Add("Ammo");
            rightAmmo.AmmoMax = 2;
            rightAmmo.LiveAttributes["AmmoMax"] = 2;
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem sourceItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "source"; });
            InternalItem leftItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "left-ammo"; });
            InternalItem rightItem = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "right-ammo"; });
            leftItem.Ammo = 0;
            rightItem.Ammo = 0;

            Effects.ResolveEffect(ctx, new Effect { Kind = "reload", AmountValue = -1, Count = 1, Target = leftTarget },
                sourceItem, 1, 0, null);
            Assert.Equal(2, leftItem.Ammo);
            Assert.Equal(0, rightItem.Ammo);

            Effects.ResolveEffect(ctx, new Effect { Kind = "reload", AmountValue = -1, Count = 1, Target = rightTarget },
                sourceItem, 2, 0, null);
            Assert.Equal(2, rightItem.Ammo);

            // 排除自身后，最左的匹配卡变成右弹药。
            leftItem.Ammo = 0;
            leftTarget.ExcludeSelf = true;
            Effects.ResolveEffect(ctx, new Effect { Kind = "reload", AmountValue = -1, Count = 1, Target = leftTarget },
                leftItem, 3, 0, null);
            Assert.Equal(0, leftItem.Ammo);
            Assert.Equal(2, rightItem.Ammo);
            Assert.False(sourceItem.Card.Passive);
        }

        private static void StaticAuraUsesSameSideRanking()
        {
            Assert.Near(10, AuraDamage(5000, 9000));
            Assert.Near(17, AuraDamage(9000, 5000));
        }

        private const string HighestCooldownAura = @"{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,
              ""Action"":{""$type"":""TAuraActionCardModifyAttribute"",""AttributeType"":""DamageAmount"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":7},
                ""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",""ExcludeSelf"":false,
                  ""Conditions"":{""$type"":""TCardConditionalAttributeHighest"",""AttributeType"":""CooldownMax""}}}}";

        private static JsonValue AuraTemplate(int cooldown, bool withAura)
        {
            string json = "{\"$type\":\"TCardItem\",\"Size\":\"Small\",\"Tags\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CooldownMax\":" + cooldown +
                ",\"DamageAmount\":10},\"AbilityIds\":[],\"AuraIds\":[" + (withAura ? "\"0\"" : "") +
                "],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":" + (withAura ? "{\"0\":" + HighestCooldownAura + "}" : "{}") + "}";
            return Json.Parse(json);
        }

        private static double AuraDamage(int ownCooldownMs, int otherCooldownMs)
        {
            List<LiveCardEntry> entries = new List<LiveCardEntry>();
            entries.Add(new LiveCardEntry { TemplateId = "own", Tier = "Bronze", Socket = 0, Template = AuraTemplate(ownCooldownMs, true), Size = "Small", AttributesAreLive = false });
            entries.Add(new LiveCardEntry { TemplateId = "other", Tier = "Bronze", Socket = 1, Template = AuraTemplate(otherCooldownMs, false), Size = "Small", AttributesAreLive = false });
            BattleConfig config = CardMapper.Build(entries, CardCatalog.FromCards(new List<CardDef>()), 1000, 1000, 30).Config;
            config.NativeMechanics = true;
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            return config.Cards[config.Player.Items[0].CardId].LiveAttributes["DamageAmount"];
        }

        private static void UnsupportedConditionsFailClosed()
        {
            // 没有触发来源时，“不是触发源”恒为真；数值排行属性不再限定固定列表。
            Assert.True(NativeSkillSupport.CardMatches(new InternalItem { Id = "x", Card = new CardDef() },
                Condition(@"{""$type"":""TCardConditionalTriggerSource"",""IsNot"":true}")));
            Assert.False(NativeSkillSupport.CardMatches(new InternalItem { Id = "x", Card = new CardDef() },
                Condition(@"{""$type"":""TCardConditionalTriggerSource"",""IsNot"":false}")));
            Assert.True(NativeSkillSupport.ConditionSupported(Condition(@"{""$type"":""TCardConditionalAttributeHighest"",""AttributeType"":""SellPrice""}")));
            Assert.False(NativeSkillSupport.ConditionSupported(Condition(@"{""$type"":""TCardConditionalId""}")));
            Assert.False(NativeSkillSupport.ConditionSupported(Condition(@"{""$type"":""TCardConditionalTier"",""Tiers"":[]}")));
        }
    }
}
