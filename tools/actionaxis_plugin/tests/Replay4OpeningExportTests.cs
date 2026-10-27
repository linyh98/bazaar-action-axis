using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class Replay4OpeningExportTests
    {
        public static void Run()
        {
            // fb7f06c3: Radiant enchant at frame 0 changes Boom Boom Bot's
            // RegenApplyAmount 0->8. The first post-tick snapshot is not the
            // unchanged opening, and must not swallow that transition.
            BattleResult result = new BattleResult();
            CardDef opening = new CardDef { Id = "opening-enchanted" };
            opening.LiveAttributes["RegenApplyAmount"] = 20;
            opening.LiveAttributes["DamageAmount"] = 83;
            opening.NativeAuraSeedAttributes = new Dictionary<string, int> {
                { "RegenApplyAmount", 0 }, { "DamageAmount", 83 }
            };
            BattleItemView history = new BattleItemView { Id = opening.Id, Card = opening };
            history.States.Add(new BattleItemState { Card = opening, Time = 0 });
            result.PlayerItems.Add(history);
            ItemSnapshot first = new ItemSnapshot { ItemId = opening.Id, Time = 0 };
            first.Attributes["RegenApplyAmount"] = 8;
            first.Attributes["DamageAmount"] = 83;
            result.ItemSnapshots.Add(first);
            result.Events.Add(new BattleEvent { Time = 0, Kind = "enchant", TargetItemId = opening.Id });
            MethodInfo export = typeof(NativeReplayLedgerExport).GetMethod("CardUpdates", BindingFlags.Static | BindingFlags.NonPublic);
            List<object> rows = (List<object>)export.Invoke(null, new object[] { result });
            // 原生附魔帧会重发整组元数据行（enchantment/tags/hiddentags，previous=null），
            // 加上 RegenApplyAmount 的属性变化共 4 行。
            Assert.Equal(4, rows.Count);
            Dictionary<string, object> row = null;
            foreach (object candidate in rows)
            {
                Dictionary<string, object> dict = (Dictionary<string, object>)candidate;
                if (dict.ContainsKey("attribute") && Convert.ToString(dict["attribute"]) == "RegenApplyAmount") row = dict;
            }
            Assert.True(row != null);
            if (row != null)
            {
                Assert.Equal(0, Convert.ToInt32(row["previous"]));
                Assert.Equal(8, Convert.ToInt32(row["current"]));
                Assert.Equal(0, Convert.ToInt32(row["frame"]));
            }
            // Initial continuous auras are already part of the opening state,
            // not an invented transition from the unscaled template value.
            result.Events.Clear();
            rows = (List<object>)export.Invoke(null, new object[] { result });
            Assert.Equal(0, rows.Count);
            FullHealthHealStillExportsItsHealthPacket();
        }

        private static void FullHealthHealStillExportsItsHealthPacket()
        {
            // b61c28ab frame 76 / f6a4d502 frame 82: a heal at full HP still
            // has a native health packet. Retain its unchanged state explicitly.
            BattleResult result = new BattleResult();
            result.HealthSnapshots.Add(new BattleHealthSnapshot { Time = -1,
                Player = new HeroVitals { Hp = 400, MaxHp = 400 }, Enemy = new HeroVitals { Hp = 550, MaxHp = 550 } });
            result.HealthSnapshots.Add(new BattleHealthSnapshot { Time = 4.1,
                Player = new HeroVitals { Hp = 400, MaxHp = 400 }, Enemy = new HeroVitals { Hp = 550, MaxHp = 550 } });
            BattleEvent heal = new BattleEvent { Time = 4.1, Side = Side.Enemy, TargetSide = Side.Enemy,
                Kind = "heal", NominalAmount = 20, Value = 0 };
            result.Events.Add(heal);
            MethodInfo export = typeof(NativeReplayLedgerExport).GetMethod("HealthChanges", BindingFlags.Static | BindingFlags.NonPublic);
            List<object> rows = (List<object>)export.Invoke(null, new object[] { result });
            Assert.Equal(1, rows.Count);
            heal.Note = "regen";
            rows = (List<object>)export.Invoke(null, new object[] { result });
            Assert.Equal(0, rows.Count);
        }
    }
}
