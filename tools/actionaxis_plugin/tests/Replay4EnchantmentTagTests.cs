using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4EnchantmentTagTests
    {
        public static void Run()
        {
            // fa081871: Shielded Flagship must remain a Shield target for
            // Submersible after native remapping, not lose its enchant tag.
            CardDef card = new CardDef { Id = "shielded-native", Cooldown = 1, EnchantmentName = "Shielded" };
            card.NativeTemplate = Json.Parse(@"{""$type"":""TCardItem"",""HiddenTags"":[""Damage""],
                ""Tiers"":{""Bronze"":{""Attributes"":{""CooldownMax"":1000,""DamageAmount"":10},""AbilityIds"":[],""AuraIds"":[]}},
                ""Abilities"":{},""Auras"":{}}");
            card.NativeEnchantment = Json.Parse(@"{""HiddenTags"":[""Shield""],""Attributes"":{},""Abilities"":{},""Auras"":{}}");
            card.HiddenTags.Add("Damage"); card.HiddenTags.Add("Shield");
            BattleConfig config = new BattleConfig { NativeMechanics = true };
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            NativeCardAbilityMapper.Apply(config);
            InternalItem item = new InternalItem { Id = card.Id, CardId = card.Id, Card = config.Cards[card.Id] };
            Assert.True(NativeSkillSupport.CardMatches(item, Json.Parse(
                "{\"$type\":\"TCardConditionalHiddenTag\",\"Operator\":\"Any\",\"Tags\":[\"Shield\"]}")));
            Assert.True(item.Card.HiddenTags.Contains("Damage"));
        }
    }
}
