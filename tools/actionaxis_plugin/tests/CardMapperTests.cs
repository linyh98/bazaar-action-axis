using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class CardMapperTests
    {
        public static void Run()
        {
            LiveItemOverridesTests.Run();
            EnchantmentMapperTests.Run();
            NativeItemMapperTests.Run();
            NativeCardAbilityMapperTests.Run();
            CardDef known = new CardDef();
            known.Id = "abc@Gold";
            known.BaseItemId = "abc";
            known.NameCn = "测试卡";
            known.Tier = "Gold";
            known.Cooldown = 5;
            known.ParseStatus = "structured";

            CardDef multi = new CardDef();
            multi.Id = "multi@Bronze";
            multi.BaseItemId = "multi";
            multi.NameCn = "多重卡";
            multi.Tier = "Bronze";
            multi.Cooldown = 5;
            multi.ParseStatus = "structured";
            Effect multicast = new Effect();
            multicast.Kind = "multicast";
            multicast.Count = 3;
            multi.Effects.Add(multicast);

            List<CardDef> cards = new List<CardDef>();
            cards.Add(known);
            cards.Add(multi);
            CardCatalog catalog = CardCatalog.FromCards(cards);

            LiveCardEntry first = new LiveCardEntry();
            first.TemplateId = "ABC";
            first.Tier = "Gold";
            first.Size = "Medium";
            first.Socket = 2;
            first.Attributes["CooldownMax"] = 7000;
            first.Attributes["AmmoMax"] = 3;
            first.Attributes["Multicast"] = 2;
            first.Attributes["ChargeAmount"] = 1500;
            first.Enchantment = "Toxic";

            LiveCardEntry second = new LiveCardEntry();
            second.TemplateId = "zzz";
            second.Tier = "Bronze";
            second.Size = "Large";
            second.Socket = 4;

            LiveCardEntry third = new LiveCardEntry();
            third.TemplateId = "multi";
            third.Tier = "Bronze";
            third.Socket = 6;
            third.Attributes["Multicast"] = 3;

            List<LiveCardEntry> entries = new List<LiveCardEntry>();
            entries.Add(first);
            entries.Add(second);
            entries.Add(third);

            MappedBoard mapped = CardMapper.Build(entries, catalog, 100, 100000, 60);
            Assert.Equal(3, mapped.Config.Cards.Count);
            Assert.Equal(3, mapped.Config.Player.Items.Count);
            Assert.Equal(1, mapped.Unknown.Count);
            Assert.Equal("Toxic", mapped.Enchantments["abc@Gold#0"]);

            CardDef live = mapped.Config.Cards["abc@Gold#0"];
            Assert.Near(7.0, live.Cooldown);
            Assert.Equal("Medium", live.Size);
            Assert.Equal("Small", known.Size);
            Assert.Equal(3, live.AmmoMax.Value);
            Assert.Equal(1, live.MulticastBonus);
            Assert.Equal(1500, live.LiveAttributes["ChargeAmount"]);
            first.Attributes["ChargeAmount"] = 2000;
            Assert.Equal(1500, live.LiveAttributes["ChargeAmount"]);

            CardDef unknown = mapped.Config.Cards["zzz#1"];
            Assert.True(unknown.Passive);
            Assert.Equal("Large", unknown.Size);
            Assert.Equal(0, unknown.Cooldown);
            Assert.True(unknown.DisplayName().StartsWith("未知卡"));

            CardDef liveMulti = mapped.Config.Cards["multi@Bronze#2"];
            Assert.Equal(2, liveMulti.MulticastBonus);
            Assert.True(liveMulti.HasMulticastOverride);

            BattleResult result = BattleEngine.Simulate(mapped.Config);
            Assert.Near(60, result.EndTime);
            Assert.Equal(3, result.PlayerItems.Count);

            int casts = 0;
            foreach (BattleEvent ev in result.Events)
            {
                if (ev.Kind == "activate" && ev.SourceItemId == "multi@Bronze#2" &&
                    ev.Time == 5)
                    casts++;
            }
            Assert.Equal(3, casts);
        }
    }
}
