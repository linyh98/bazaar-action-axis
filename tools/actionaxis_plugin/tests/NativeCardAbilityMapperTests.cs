using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeCardAbilityMapperTests
    {
        public static void Run()
        {
            MonsterNativeAbilitiesAndAuras();
            CounterstrikeIsEnemyScopedAndOnceOnly();
            EnemyStaticSkillAurasAreApplied();
            UnsupportedRuleCannotPartiallyApplyAura();
            CalibratedMonsterItemsEvaluateTheirOwnAuras();
            CrossCardAuraDoesNotDependOnCardOrder();
            CalibratedMonsterWithoutCatalogStillAttacks();
            MonsterSkillAuraSeesNativeTagsBeforeItemMapping();
        }

        private static void CalibratedMonsterWithoutCatalogStillAttacks()
        {
            LiveCardEntry spider = Entry("Trained Spider", "Silver", 0);
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry> { spider },
                CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 12).Config;
            config.NativeMechanics = true;
            config.Enemy = config.Player;
            config.Player = new Loadout();
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            Assert.Equal(0, NativeItemMapper.Apply(config).Count);
            CardDef card = config.Cards[config.Enemy.Items[0].CardId];
            Assert.Equal("poison", card.Effects[0].Kind);
            Assert.Near(spider.Attributes["PoisonApplyAmount"], card.Effects[0].Amount.Value);
            Assert.True(card.DisplayName().IndexOf("未知卡") < 0);
            Assert.True(BattleEngine.Simulate(config).Totals.Enemy.Damage > 0);
        }

        private static void MonsterSkillAuraSeesNativeTagsBeforeItemMapping()
        {
            LiveCardEntry bee = Entry("Busy Bee", "Gold", 0);
            JsonValue skill = Json.Parse(@"{""$type"":""TCardSkill"",""Tiers"":{""Gold"":{""Attributes"":{},""AbilityIds"":[],""AuraIds"":[""0""],""TooltipIds"":[]}},""Auras"":{
                ""0"":{""ActiveIn"":""HandOnly"",""Action"":{""$type"":""TAuraActionCardModifyAttribute"",""AttributeType"":""DamageAmount"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":7},""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",""Conditions"":{""$type"":""TCardConditionalTag"",""Tags"":[""Friend""],""Operator"":""Any""}}}}
            }}");
            LiveMonsterEntry monster = new LiveMonsterEntry { Id = "tags", MaxHp = 900, Health = 900 };
            monster.Cards.Add(bee);
            monster.Skills.Add(new LiveSkillEntry { TemplateId = "friend-aura", Tier = "Gold", Template = skill });
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            MonsterMappingResult mapped = MonsterOpponentMapper.Apply(config, monster,
                CardCatalog.FromCards(new List<CardDef>()), null);
            Assert.Equal(1, mapped.Skills.CombatCount);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            CardDef card = config.Cards[config.Enemy.Items[0].CardId];
            Assert.Near(17, card.Effects.Find(delegate(Effect effect) { return effect.Kind == "damage"; }).Amount.Value);
        }

        private static JsonValue Template(string name)
        {
            foreach (JsonValue value in Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_kyver_cards.json")).AsArray())
                if (NativeSkillSupport.Text(value, "InternalName") == name) return value;
            return JsonValue.Null;
        }

        private static LiveCardEntry Entry(string name, string tier, int slot)
        {
            JsonValue template = Template(name);
            return new LiveCardEntry { TemplateId = NativeSkillSupport.Text(template, "Id"), Tier = tier,
                Size = NativeSkillSupport.Text(template, "Size"), Socket = slot, Template = template, AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier) };
        }

        private static BattleConfig Board()
        {
            List<LiveCardEntry> entries = new List<LiveCardEntry> {
                Entry("Insect Wing", "Gold", 0), Entry("Insect Wing", "Gold", 1), Entry("Junkyard Lance", "Gold", 2),
                Entry("Busy Bee", "Gold", 5), Entry("Busy Bee", "Gold", 6), Entry("Honeycomb", "Gold", 7)
            };
            MappedBoard mapped = CardMapper.Build(entries, CardCatalog.FromCards(new List<CardDef>()), 10000, 900, 30);
            BattleConfig config = mapped.Config;
            config.NativeMechanics = true;
            config.Enemy = config.Player;
            config.Player = new Loadout();
            for (int slot = 0; slot < 4; slot++)
            {
                CardDef friend = new CardDef { Id = "player" + slot, Cooldown = 10 };
                config.Cards[friend.Id] = friend;
                config.Player.Items.Add(new LoadoutItem { CardId = friend.Id, Slot = slot });
            }
            return config;
        }

        private static void MonsterNativeAbilitiesAndAuras()
        {
            BattleConfig config = Board();
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            CardDef lance = config.Cards[config.Enemy.Items[2].CardId];
            Assert.Near(500, lance.Effects[0].Amount.Value); // Five Small items, Gold 100 each.
            Assert.Equal("native-template", lance.DataSource);
            Assert.Near(10, lance.Cooldown);
            CardDef honey = config.Cards[config.Enemy.Items[5].CardId];
            Assert.Near(5, honey.Cooldown);
            Assert.Equal(3, honey.Effects[0].Count.Value);
            Assert.Equal("slow", honey.Effects[0].Kind);
            Assert.Near(1, honey.Effects[0].Seconds);
            Assert.Equal("regen", honey.Effects[1].Kind);
            Assert.Near(3, honey.Effects[1].Amount.Value);
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            Assert.Near(500, config.Cards[lance.Id].Effects[0].Amount.Value); // Repeated refresh must not add aura again.
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem hive = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == honey.Id; });
            foreach (Effect effect in hive.Card.Effects) Effects.ResolveEffect(ctx, effect, hive, 5, 0, null);
            int slowed = 0;
            foreach (InternalItem item in ctx.Items) if (item.Side == Side.Player && item.SlowUntil > 5) slowed++;
            Assert.Equal(3, slowed);
            Assert.Near(3, ctx.Enemy.Regen);
            Assert.Near(0, ctx.Totals.Enemy.Damage);
            Assert.Near(0, ctx.Totals.Player.Damage);
            InternalItem bee = ctx.Items.Find(delegate(InternalItem i) { return i.Card.BaseItemId == NativeSkillSupport.Text(Template("Busy Bee"), "Id"); });
            foreach (Effect effect in bee.Card.Effects) Effects.ResolveEffect(ctx, effect, bee, 6, 0, null);
            Assert.True(bee.Flying);
            Assert.Near(10, ctx.Totals.Enemy.Damage);
        }

        private static void CounterstrikeIsEnemyScopedAndOnceOnly()
        {
            BattleConfig config = Board();
            JsonValue template = Template("Counterstrike");
            LiveSkillEntry skill = new LiveSkillEntry { Template = template, Tier = "Gold", InstanceId = "same-id",
                TemplateId = NativeSkillSupport.Text(template, "Id") };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { skill }, null, Side.Enemy);
            Assert.Equal(1, mapped.CombatCount);
            Assert.Equal(0, mapped.UnsupportedCount);
            Assert.Equal(0, config.PlayerSkills.Count);
            Assert.True(config.EnemySkills[0].Id.StartsWith("enemy:skill:"));
            NativeCardAbilityMapper.Apply(config);
            config.Cards["player0"].Tags.Add("Weapon");
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem source = ctx.Items.Find(delegate(InternalItem i) { return i.CardId == "player0"; });
            InternalItem hive = ctx.Items.Find(delegate(InternalItem i) { return i.Card.BaseItemId == NativeSkillSupport.Text(Template("Honeycomb"), "Id"); });
            foreach (int time in new[] { 1, 2 })
            {
                Triggers.Enqueue(ctx, new TriggerPayload { Event = "onUse", SourceSide = Side.Player, SourceItemId = source.Id });
                Triggers.Drain(ctx, time);
            }
            Assert.Near(4, hive.Cooldown); // Once, not every weapon or every target.
            Assert.Near(10, source.Cooldown);
        }

        private static void EnemyStaticSkillAurasAreApplied()
        {
            BattleConfig config = Board();
            config.EnemyStartHp = 900;
            JsonValue template = Json.Parse(@"{
              ""$type"":""TCardSkill"",""Tiers"":{""Gold"":{""Attributes"":{},""AbilityIds"":[],""AuraIds"":[""hp"",""regen"",""multi""],""TooltipIds"":[]}},
              ""Abilities"":{},""Auras"":{
                ""hp"":{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,""Action"":{""$type"":""TAuraActionPlayerModifyAttribute"",""AttributeType"":""HealthMax"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":100},""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self"",""Conditions"":null}}},
                ""regen"":{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,""Action"":{""$type"":""TAuraActionPlayerModifyAttribute"",""AttributeType"":""HealthRegen"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":2},""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Self"",""Conditions"":null}}},
                ""multi"":{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Prerequisites"":null,""Action"":{""$type"":""TAuraActionCardModifyAttribute"",""AttributeType"":""Multicast"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":1},""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",""Conditions"":null}}}
              }}");
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { new LiveSkillEntry { Template = template, TemplateId = "auras", Tier = "Gold" } }, null, Side.Enemy);
            Assert.Equal(1, mapped.CombatCount);
            Assert.Equal(0, mapped.UnsupportedCount);
            Assert.Near(1000, config.EnemyMaxHp);
            Assert.Near(1000, config.EnemyStartHp.Value);
            Assert.Near(2, config.EnemyStartRegen);
            NativeCardAbilityMapper.Apply(config);
            CardDef honey = config.Cards[config.Enemy.Items[5].CardId];
            Assert.Equal(1, honey.MulticastBonus);
            config.EnemyStartShield = 7;
            BattleContext ctx = BattleEngine.InitBattle(config);
            Assert.Near(7, ctx.Enemy.Shield);
            Assert.Near(2, ctx.Enemy.Regen);
            Assert.Near(1000, ctx.Enemy.Hp);
            Assert.Near(0, ctx.Totals.Enemy.Heal);
        }

        private static void UnsupportedRuleCannotPartiallyApplyAura()
        {
            BattleConfig config = Board();
            CardDef lance = config.Cards[config.Enemy.Items[2].CardId];
            JsonValue malformed = Template("Junkyard Lance");
            malformed["Abilities"].AsObject()["0"] = Json.Parse(@"{""ActiveIn"":""HandOnly"",""Trigger"":{""$type"":""TTriggerOnCardFired""},""Action"":{""$type"":""TActionUnknown""}}");
            lance.NativeTemplate = malformed;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal(1, warnings.Count);
            Assert.True(config.Cards[lance.Id].DataSource != "native-template");
            Assert.Equal(0, config.Cards[lance.Id].LiveAttributes["DamageAmount"]);
        }

        private static void CalibratedMonsterItemsEvaluateTheirOwnAuras()
        {
            CardCatalog catalog = CardCatalog.Load("src/data/cards.json");
            List<LiveCardEntry> entries = new List<LiveCardEntry> {
                Entry("Trained Spider", "Silver", 0), Entry("Soldering Gun", "Bronze", 1), Entry("Soldering Gun", "Bronze", 2),
                Entry("Aludel", "Bronze", 3), Entry("Drowsdrop", "Bronze", 5)
            };
            BattleConfig config = CardMapper.Build(entries, catalog, 10000, 10000, 30).Config;
            config.NativeMechanics = true;
            config.Enemy = config.Player;
            config.Player = new Loadout();
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            Assert.Equal(0, NativeItemMapper.Apply(config).Count);
            Assert.Equal(2, config.Cards[config.Enemy.Items[1].CardId].MulticastBonus); // Friend and Tool neighbors.
            Assert.Equal(1, config.Cards[config.Enemy.Items[3].CardId].MulticastBonus); // One adjacent Potion.
            Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
            NativeItemMapper.Apply(config);
            Assert.Equal(2, config.Cards[config.Enemy.Items[1].CardId].MulticastBonus);
            Assert.Equal(1, config.Cards[config.Enemy.Items[3].CardId].MulticastBonus);
        }

        private static void CrossCardAuraDoesNotDependOnCardOrder()
        {
            JsonValue aura = Json.Parse(@"{""$type"":""TCardItem"",""Tags"":[],""Tiers"":{""Bronze"":{""Attributes"":{},""AbilityIds"":[],""AuraIds"":[""0""]}},""Abilities"":{},""Auras"":{
                ""0"":{""ActiveIn"":""HandOnly"",""Prerequisites"":null,""Action"":{""$type"":""TAuraActionCardModifyAttribute"",""AttributeType"":""DamageAmount"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":7},""Target"":{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",""Conditions"":null}}}
            }}");
            foreach (bool first in new[] { true, false })
            {
                LiveCardEntry bee = Entry("Busy Bee", "Gold", first ? 0 : 1);
                LiveCardEntry buff = new LiveCardEntry { TemplateId = "damage-aura", Template = aura, Tier = "Bronze", Socket = first ? 1 : 0, AttributesAreLive = false };
                List<LiveCardEntry> entries = first ? new List<LiveCardEntry> { bee, buff } : new List<LiveCardEntry> { buff, bee };
                BattleConfig config = CardMapper.Build(entries, CardCatalog.FromCards(new List<CardDef>()), 10000, 10000, 30).Config;
                config.NativeMechanics = true;
                config.Enemy = config.Player;
                config.Player = new Loadout();
                Assert.Equal(0, NativeCardAbilityMapper.Apply(config).Count);
                foreach (LoadoutItem item in config.Enemy.Items)
                {
                    CardDef card = config.Cards[item.CardId];
                    if (card.BaseItemId != bee.TemplateId) continue;
                    Assert.Near(17, card.Effects.Find(delegate(Effect e) { return e.Kind == "damage"; }).Amount.Value);
                    Assert.Equal(17, card.LiveAttributes["DamageAmount"]);
                }
            }
        }
    }
}
