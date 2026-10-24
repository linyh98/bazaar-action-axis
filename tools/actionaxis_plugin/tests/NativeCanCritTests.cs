using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class NativeCanCritTests
    {
        public static void Run()
        {
            ZeroChanceActiveAbilitiesCanReceiveCritAuras();
            NonActiveAndUtilityAbilitiesCannotCrit();
            ExplicitHiddenTagGrantsCapability();
            OnlyCurrentTierAndSelectedEnchantmentAbilitiesCount();
            LegacyActiveEffectsCanReceiveTheirFirstCritBonus();
        }

        private static bool Matches(InternalItem item, bool inverse)
        {
            return NativeSkillSupport.CardMatches(item, Json.Parse(
                "{\"$type\":\"TCardConditionalCanCrit\",\"IsNot\":" + (inverse ? "true" : "false") + "}"));
        }

        private static InternalItem Native(string trigger, string action)
        {
            CardDef card = new CardDef { Id = "crit-test", Tier = "Bronze", NativeTemplate = Json.Parse(
                "{\"$type\":\"TCardItem\",\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CritChance\":0},\"AbilityIds\":[\"0\"]}}," +
                "\"Abilities\":{\"0\":{\"Trigger\":{\"$type\":\"" + trigger + "\"},\"Action\":{\"$type\":\"" + action + "\"}}}}") };
            card.LiveAttributes["CritChance"] = 0;
            return new InternalItem { Id = card.Id, CardId = card.Id, Card = card };
        }

        private static void ZeroChanceActiveAbilitiesCanReceiveCritAuras()
        {
            string[] actions = { "TActionPlayerDamage", "TActionPlayerShieldApply", "TActionPlayerHeal",
                "TActionPlayerReviveHeal", "TActionPlayerBurnApply", "TActionPlayerPoisonApply", "TActionPlayerRegenApply" };
            foreach (string action in actions)
            {
                InternalItem item = Native("TTriggerOnCardFired", action);
                Assert.True(Matches(item, false));
                Assert.False(Matches(item, true));
            }
        }

        private static void NonActiveAndUtilityAbilitiesCannotCrit()
        {
            InternalItem passive = Native("TTriggerOnItemUsed", "TActionPlayerDamage");
            // Positive crit chance does not turn a passive-only ability into an
            // active numerical ability eligible for a crit aura.
            passive.CritBonus = 1;
            passive.Card.LiveAttributes["CritChance"] = 100;
            Assert.False(Matches(passive, false));
            Assert.True(Matches(passive, true));
            InternalItem charge = Native("TTriggerOnCardFired", "TActionCardCharge");
            Assert.False(Matches(charge, false));
            InternalItem skill = Native("TTriggerOnCardFired", "TActionPlayerDamage");
            skill.IsSkill = true;
            Assert.False(Matches(skill, false));
            InternalItem nonItem = Native("TTriggerOnCardFired", "TActionPlayerDamage");
            nonItem.Card.NativeTemplate = Json.Parse("{\"$type\":\"TCardEncounter\"}");
            Assert.False(Matches(nonItem, false));
        }

        private static void ExplicitHiddenTagGrantsCapability()
        {
            InternalItem charge = Native("TTriggerOnCardFired", "TActionCardCharge");
            charge.Card.HiddenTags.Add("CanCrit");
            Assert.True(Matches(charge, false));
            charge.IsSkill = true;
            Assert.True(Matches(charge, false));
            InternalItem enchanted = Native("TTriggerOnCardFired", "TActionCardCharge");
            enchanted.Card.NativeEnchantment = Json.Parse("{\"HiddenTags\":[\"CanCrit\"]}");
            Assert.True(Matches(enchanted, false));
        }

        private static void OnlyCurrentTierAndSelectedEnchantmentAbilitiesCount()
        {
            InternalItem item = Native("TTriggerOnCardFired", "TActionPlayerDamage");
            item.Card.NativeTemplate = Json.Parse(@"{
                ""$type"":""TCardItem"",
                ""Tiers"":{
                    ""Bronze"":{""AbilityIds"": [""damage""]},
                    ""Silver"":{""AbilityIds"": [""charge""]},
                    ""Gold"": {""Attributes"": {""CooldownMax"":1000}}
                },
                ""Abilities"":{
                    ""damage"":{""Trigger"":{""$type"":""TTriggerOnCardFired""},""Action"":{""$type"":""TActionPlayerDamage""}},
                    ""charge"":{""Trigger"":{""$type"":""TTriggerOnCardFired""},""Action"":{""$type"":""TActionCardCharge""}}
                },
                ""Enchantments"":{
                    ""Toxic"":{""Abilities"":{""e0"":{""Trigger"":{""$type"":""TTriggerOnCardFired""},""Action"":{""$type"":""TActionPlayerPoisonApply""}}}}
                }
            }");
            Assert.True(Matches(item, false));
            item.Card.Tier = "Silver";
            Assert.False(Matches(item, false));
            item.Card.Tier = "Gold";
            Assert.False(Matches(item, false));
            // Unselected enchantments are not active; the selected native
            // enchantment may grant the first eligible numerical ability.
            item.Card.NativeEnchantment = item.Card.NativeTemplate["Enchantments"]["Toxic"];
            Assert.True(Matches(item, false));
        }

        private static void LegacyActiveEffectsCanReceiveTheirFirstCritBonus()
        {
            CardDef card = new CardDef { Id = "legacy-damage" };
            card.Effects.Add(new Effect { Kind = "damage", Amount = new NumExpr { Type = "const", Value = 10 } });
            InternalItem item = new InternalItem { Id = card.Id, CardId = card.Id, Card = card };
            Assert.True(Matches(item, false));
            card.Effects.Clear();
            card.Triggers.Add(new TriggerDef { Event = "onUse", Effects = new List<Effect> {
                new Effect { Kind = "damage", Amount = new NumExpr { Type = "const", Value = 10 } } } });
            Assert.False(Matches(item, false));
        }
    }
}
