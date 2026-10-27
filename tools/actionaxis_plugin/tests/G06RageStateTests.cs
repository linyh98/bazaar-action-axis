using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G06：双方开场属性、英雄身份、怒气、暴怒及相关事件。
    // 本轮只覆盖任务索引中的四个 G06 种子与 Wild Boar 怪物／物品；
    // 纯模板（AttributesAreLive=false）与实时快照（true）分开断言。
    public static class G06RageStateTests
    {
        private static JsonValue _fixtures;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());
        private const string FixturePath = "tools/actionaxis_plugin/tests/fixtures/native_g06_rage.json";
        private static readonly string[] Tiers = { "Silver", "Gold", "Diamond" };
        private static readonly int[] WarpaintAmounts = { 8, 10, 12 };
        private static readonly int[] SigilAmounts = { 4, 6, 8 };

        public static void Run()
        {
            _fixtures = Json.Parse(File.ReadAllText(FixturePath));
            NativeSeedTemplatesMatchTaskIndex();
            WarpaintTemplateAndLiveSnapshotSeparateLayers();
            VengefulSigilRageAndEnragedAura();
            VengefulSigilSilverReplayWindow();
            ShieldTriggersOncePerFight("Burning Shield", "Burn");
            ShieldTriggersOncePerFight("Toxic Shield", "Poison");
            OpeningAttributesSeparateCapsFromTemporaryState();
            WildBoarTemplateOpeningAndEventChain();
            WildBoarItemEnrageBuffIsTemporary();
            EnrageEndClosesSigilWindow();
        }

        private static JsonValue Row(string key) { return _fixtures[key]; }
        private static JsonValue Template(string key) { return Row(key)["template"]; }

        private static LiveCardEntry Real(string key, int slot, string tier)
        {
            JsonValue row = Row(key), template = row["template"];
            return new LiveCardEntry
            {
                TemplateId = row["id"].AsString(),
                Template = template,
                Tier = tier,
                Socket = slot,
                Size = template["Size"].AsString(),
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry Enchanted(string key, int slot, string tier, string enchantment)
        {
            LiveCardEntry entry = Real(key, slot, tier);
            entry.Enchantment = enchantment;
            entry.EnchantmentDefinition = entry.Template["Enchantments"][enchantment];
            return entry;
        }

        // 与 AuditRegression 共用同一套最小物品模板；不能依赖任何真实卡的主动效果。
        private const string Gem = @"{""$type"":""TCardItem"",""InternalName"":""G06 Gem"",""Size"":""Small"",""Tags"":[],
            ""Tiers"":{""Gold"":{""Attributes"":{""CooldownMax"":2000,""DamageAmount"":10},""AbilityIds"":[""0""],""AuraIds"":[],""TooltipIds"":[]}},
            ""Abilities"":{""0"":{""ActiveIn"":""HandOnly"",""WorksIn"":""CombatOnly"",""Trigger"":{""$type"":""TTriggerOnCardFired""},
              ""Action"":{""$type"":""TActionPlayerDamage"",""Target"":{""$type"":""TTargetPlayerRelative"",""TargetMode"":""Opponent""}}}},
            ""Auras"":{},""Enchantments"":{""Deadly"":{""Attributes"":{""CritChance"":100},""Abilities"":{},""Auras"":{},""Tags"":[]}}}";

        private static LiveCardEntry Donor(int slot)
        {
            JsonValue template = Json.Parse(Gem);
            return new LiveCardEntry
            {
                TemplateId = "g06-gem", Template = template, Tier = "Gold", Socket = slot, Size = "Small",
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Gold")
            };
        }

        private static BattleConfig Board(params LiveCardEntry[] cards)
        {
            BattleConfig config = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, 10).Config;
            config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(config);
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return config;
        }

        private static CardDef CardAt(BattleConfig config, Side side, int slot)
        {
            Loadout loadout = side == Side.Player ? config.Player : config.Enemy;
            foreach (LoadoutItem entry in loadout.Items)
                if (entry.Slot == slot) return config.Cards[entry.CardId];
            return null;
        }

        private static InternalItem ItemAt(BattleContext ctx, Side side, int slot)
        {
            foreach (InternalItem item in ctx.Items)
                if (item.Side == side && item.Slot == slot) return item;
            return null;
        }

        private static InternalItem SkillFor(BattleContext ctx, Side side)
        {
            foreach (InternalItem skill in ctx.Skills)
                if (skill.Side == side) return skill;
            return null;
        }

        private static List<BattleEvent> Events(BattleContext ctx, string kind, Side side)
        {
            return ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == kind && ev.Side == side; });
        }

        private static int CountEvents(BattleContext ctx, string kind, Side side)
        {
            return Events(ctx, kind, side).Count;
        }

        private static void TriggerHealthLoss(BattleContext ctx, Side side, double amount, double time)
        {
            ctx.TriggerQueue.Enqueue(new TriggerPayload
            {
                Event = "onPlayerAttributeChanged", SourceSide = side,
                AttributeName = "Health", ChangeType = "Loss", Amount = amount
            });
            Triggers.Drain(ctx, time);
        }

        private static LiveSkillEntry Skill(string key, string tier)
        {
            JsonValue row = Row(key), template = row["template"];
            return new LiveSkillEntry
            {
                TemplateId = row["id"].AsString(), Tier = tier, Template = template,
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static BattleConfig SkillBoard(Side side, string key)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 10, PlayerMaxHp = 100, EnemyMaxHp = 100 };
            SkillMappingResult mapped = SkillMapper.Apply(config, new[] { Skill(key, "Gold") }, null, side);
            Assert.Equal("simulated", mapped.Skills[0].Status);
            return config;
        }

        private static Dictionary<string, JsonValue> MonsterCards()
        {
            Dictionary<string, JsonValue> cards = new Dictionary<string, JsonValue>();
            foreach (KeyValuePair<string, JsonValue> pair in _fixtures.AsObject())
            {
                if (!pair.Key.StartsWith("cards:", StringComparison.Ordinal)) continue;
                cards[pair.Value["id"].AsString()] = pair.Value["template"];
            }
            return cards;
        }

        private static LiveMonsterEntry WildBoar()
        {
            Dictionary<string, JsonValue> cards = MonsterCards();
            return MonsterTemplateExtractor.FromJson(Template("monsters:Wild Boar"), delegate(string id)
            {
                return cards.ContainsKey(id) ? cards[id] : null;
            });
        }

        private static void NativeSeedTemplatesMatchTaskIndex()
        {
            Assert.Equal("153dfd6d-7dc8-4386-9703-ea6c99f92d4a", Row("cards:Warpaint")["id"].AsString());
            Assert.Equal("05a32402-598c-4bb5-a280-1a84f0ab84fa", Row("cards:Vengeful Sigil")["id"].AsString());
            Assert.Equal("7931571a-3d32-41e8-bf7b-ca6b4d1b7a3d", Row("cards:Burning Shield")["id"].AsString());
            Assert.Equal("a9b441e1-5824-4855-9359-2ab4643d9ff5", Row("cards:Toxic Shield")["id"].AsString());
            Assert.Equal("e01ac7ab-1ad5-4235-81b4-44d057878953", Row("cards:Wild Boar")["id"].AsString());
            Assert.Equal("5d779101-3fa5-44b7-b885-f8bb9b0f7438", Row("monsters:Wild Boar")["id"].AsString());

            JsonValue warpaint = Template("cards:Warpaint");
            Assert.Equal("TCardItem", warpaint["$type"].AsString());
            Assert.Equal(8, warpaint["Tiers"]["Silver"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal(10, warpaint["Tiers"]["Gold"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal(12, warpaint["Tiers"]["Diamond"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal("TTriggerOnItemUsed", warpaint["Abilities"]["0"]["Trigger"]["$type"].AsString());
            Assert.Equal("TTargetCardPositional", warpaint["Abilities"]["0"]["Trigger"]["Subject"]["$type"].AsString());
            Assert.Equal("TActionPlayerRageApply", warpaint["Abilities"]["0"]["Action"]["$type"].AsString());

            JsonValue sigil = Template("cards:Vengeful Sigil");
            Assert.Equal(4, sigil["Tiers"]["Silver"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal(6, sigil["Tiers"]["Gold"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal(8, sigil["Tiers"]["Diamond"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal("OpponentHand", sigil["Abilities"]["0"]["Trigger"]["Subject"]["TargetSection"].AsString());
            JsonValue lifesteal = sigil["Auras"]["1"]["Action"];
            Assert.Equal("Lifesteal", lifesteal["AttributeType"].AsString());
            Assert.Equal("Enraged", sigil["Auras"]["1"]["Prerequisites"][0]["Subject"]["Conditions"]["Attribute"].AsString());

            JsonValue shiny = warpaint["Enchantments"]["Shiny"]["Auras"]["e1"]["Action"];
            Assert.Equal("RageApplyAmount", shiny["AttributeType"].AsString());
            Assert.Equal("Multiply", shiny["Operation"].AsString());
            Assert.Near(2, shiny["Value"]["Value"].AsDouble());

            foreach (string key in new[] { "cards:Burning Shield", "cards:Toxic Shield" })
            {
                JsonValue shield = Template(key);
                Assert.Equal("TCardSkill", shield["$type"].AsString());
                Assert.Equal("Health", shield["Abilities"]["0"]["Trigger"]["AttributeType"].AsString());
                Assert.Equal("Loss", shield["Abilities"]["0"]["Trigger"]["ChangeType"].AsString());
                Assert.Equal("TActionPlayerShieldApply", shield["Abilities"]["0"]["Action"]["$type"].AsString());
                Assert.Equal("Custom_0", shield["Abilities"]["0"]["Prerequisites"][0]["Subject"]["Conditions"]["Attribute"].AsString());
                Assert.Equal(3, shield["Tiers"]["Gold"]["Attributes"]["Custom_1"].AsInt());
            }

            JsonValue boar = Template("cards:Wild Boar");
            Assert.Equal(8000, boar["Tiers"]["Silver"]["Attributes"]["CooldownMax"].AsInt());
            Assert.Equal(40, boar["Tiers"]["Silver"]["Attributes"]["Custom_0"].AsInt());
            Assert.Equal(60, boar["Tiers"]["Gold"]["Attributes"]["Custom_0"].AsInt());
            Assert.Equal(80, boar["Tiers"]["Diamond"]["Attributes"]["Custom_0"].AsInt());
            Assert.Equal("TTriggerOnPlayerEnraged", boar["Abilities"]["3"]["Trigger"]["$type"].AsString());

            JsonValue monster = Template("monsters:Wild Boar");
            Assert.Equal(650, monster["Player"]["Attributes"]["HealthMax"].AsInt());
            Assert.Equal(50, monster["Player"]["Attributes"]["RageMax"].AsInt());
            Assert.Equal(5000, monster["Player"]["Attributes"]["EnragedDurationMax"].AsInt());
            JsonValue feeding = Template("cards:Feeding Frenzy");
            Assert.Equal(15, feeding["Tiers"]["Silver"]["Attributes"]["RageApplyAmount"].AsInt());
            Assert.Equal("TTriggerOnPlayerEnraged", feeding["Abilities"]["1"]["Trigger"]["$type"].AsString());
        }

        private static void WarpaintTemplateAndLiveSnapshotSeparateLayers()
        {
            for (int tier = 0; tier < Tiers.Length; tier++)
            {
                foreach (bool shiny in new[] { false, true })
                {
                    LiveCardEntry card = shiny ? Enchanted("cards:Warpaint", 1, Tiers[tier], "Shiny") :
                        Real("cards:Warpaint", 1, Tiers[tier]);
                    BattleConfig config = Board(Donor(0), card);
                    CardDef paint = CardAt(config, Side.Player, 1);
                    int expected = WarpaintAmounts[tier] * (shiny ? 2 : 1);
                    Assert.True(paint != null);
                    Assert.Equal(expected, paint.LiveAttributes["RageApplyAmount"]);
                    Assert.Equal(1, paint.Triggers.Count);
                    Assert.Equal("rage", paint.Triggers[0].Effects[0].Kind);

                    BattleContext ctx = BattleEngine.InitBattle(config);
                    InternalItem runtime = ItemAt(ctx, Side.Player, 1);
                    InternalItem left = ItemAt(ctx, Side.Player, 0);
                    Assert.Near(expected, ItemAttributes.Get(runtime, "RageApplyAmount"));
                    BattleEngine.ForceUse(ctx, left, 1, 0);
                    List<BattleEvent> rage = Events(ctx, "rage", Side.Player);
                    Assert.Equal(1, rage.Count);
                    Assert.Near(expected, rage[0].Value.Value);
                    Assert.Equal(runtime.Id, rage[0].SourceItemId);
                    Assert.Near(expected, ctx.Player.Rage);
                }
            }

            // 实时快照已经含 Shiny ×2：InitBattle 反推再 Refresh，不能二次相乘。
            LiveCardEntry live = Enchanted("cards:Warpaint", 1, "Gold", "Shiny");
            live.AttributesAreLive = true;
            live.Attributes = new Dictionary<string, int> { { "RageApplyAmount", 20 } };
            BattleConfig liveConfig = Board(Donor(0), live);
            CardDef livePaint = CardAt(liveConfig, Side.Player, 1);
            Assert.Equal(20, livePaint.LiveAttributes["RageApplyAmount"]);
            BattleContext liveCtx = BattleEngine.InitBattle(liveConfig);
            InternalItem liveRuntime = ItemAt(liveCtx, Side.Player, 1);
            Assert.Near(20, ItemAttributes.Get(liveRuntime, "RageApplyAmount"));
            BattleEngine.ForceUse(liveCtx, ItemAt(liveCtx, Side.Player, 0), 1, 0);
            List<BattleEvent> liveRage = Events(liveCtx, "rage", Side.Player);
            Assert.Equal(1, liveRage.Count);
            Assert.Near(20, liveRage[0].Value.Value);
        }

        private static void VengefulSigilRageAndEnragedAura()
        {
            for (int tier = 0; tier < Tiers.Length; tier++)
            {
                BattleConfig config = Board(Enchanted("cards:Vengeful Sigil", 4, Tiers[tier], "Shiny"), Donor(6));
                CardDef weapon = CardAt(config, Side.Player, 6);
                weapon.Tags.Add("Weapon");
                CardDef other = new CardDef { Id = "g06-other", Cooldown = 100 };
                config.Cards[other.Id] = other;
                config.Player.Items.Add(new LoadoutItem { CardId = other.Id, Slot = 8 });
                CardDef enemyItem = new CardDef { Id = "enemy:g06-probe", Cooldown = 100 };
                config.Cards[enemyItem.Id] = enemyItem;
                config.Enemy.Items.Add(new LoadoutItem { CardId = enemyItem.Id, Slot = 0 });
                int expected = SigilAmounts[tier] * 2;
                config.PlayerRageMax = expected;

                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem sigil = ItemAt(ctx, Side.Player, 4);
                InternalItem weaponItem = ItemAt(ctx, Side.Player, 6);
                InternalItem otherItem = ItemAt(ctx, Side.Player, 8);
                InternalItem enemy = ItemAt(ctx, Side.Enemy, 0);
                Assert.Near(expected, ItemAttributes.Get(sigil, "RageApplyAmount"));
                Assert.Near(0, ItemAttributes.Get(weaponItem, "Lifesteal"));
                Assert.Near(0, ItemAttributes.Get(otherItem, "Lifesteal"));

                BattleEngine.ForceUse(ctx, enemy, 1, 0);
                List<BattleEvent> rage = Events(ctx, "rage", Side.Player);
                Assert.Equal(1, rage.Count);
                Assert.Near(expected, rage[0].Value.Value);
                Assert.Near(0, ctx.Player.Rage);
                Assert.True(ctx.Player.EnragedUntil > 0);
                NativeDynamicAuras.Refresh(ctx);
                // 只有 SelfHand 的 Weapon 获得 100 Lifesteal；暴怒结束立即撤销。
                Assert.Near(100, ItemAttributes.Get(weaponItem, "Lifesteal"));
                Assert.Near(0, ItemAttributes.Get(otherItem, "Lifesteal"));
                ctx.Player.EnragedUntil = -1;
                NativeDynamicAuras.Refresh(ctx);
                Assert.Near(0, ItemAttributes.Get(weaponItem, "Lifesteal"));
            }
        }

        private static void VengefulSigilSilverReplayWindow()
        {
            // 原生回放 9a1d87b3…：Silver Sigil 每次敌方物品使用 +4 怒气，
            // 100 怒气暴怒、5000 ms 窗口；这里用 RageMax=4 压缩到一次触发。
            BattleConfig config = Board(Real("cards:Vengeful Sigil", 4, "Silver"), Donor(6));
            CardDef weapon = CardAt(config, Side.Player, 6);
            weapon.Tags.Add("Weapon");
            CardDef enemyItem = new CardDef { Id = "enemy:g06-replay", Cooldown = 100 };
            config.Cards[enemyItem.Id] = enemyItem;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemyItem.Id, Slot = 0 });
            config.PlayerRageMax = 4;

            BattleContext ctx = BattleEngine.InitBattle(config);
            BattleEngine.ForceUse(ctx, ItemAt(ctx, Side.Enemy, 0), 1, 0);
            List<BattleEvent> rage = Events(ctx, "rage", Side.Player);
            Assert.Equal(1, rage.Count);
            Assert.Near(4, rage[0].Value.Value);
            Assert.Near(6, ctx.Player.EnragedUntil);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(100, ItemAttributes.Get(ItemAt(ctx, Side.Player, 6), "Lifesteal"));
            BattleEngine.TickEnrage(ctx, 6);
            NativeDynamicAuras.Refresh(ctx);
            Assert.Near(0, ItemAttributes.Get(ItemAt(ctx, Side.Player, 6), "Lifesteal"));
            Assert.Equal(1, Events(ctx, "enrageEnd", Side.Player).Count);
        }

        private static void ShieldTriggersOncePerFight(string name, string status)
        {
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                BattleConfig config = SkillBoard(side, "cards:" + name);
                BattleContext ctx = BattleEngine.InitBattle(config);
                InternalItem skill = SkillFor(ctx, side);
                double baseShield = ItemAttributes.Get(skill, "ShieldApplyAmount");
                double factor = ItemAttributes.Get(skill, "Custom_1");
                Assert.Near(0, baseShield);
                Assert.Near(3, factor);

                if (status == "Burn") ctx.Hero(SideUtil.Opposing(side)).Burn = 30;
                else ctx.Hero(SideUtil.Opposing(side)).Poison = 30;
                NativeDynamicAuras.Refresh(ctx);
                double expected = baseShield + 30 * factor;
                Assert.Near(expected, ItemAttributes.Get(skill, "ShieldApplyAmount"));

                // 半血以上不掉盾：技能条件要求 Health < HealthMax × 0.5。
                ctx.Hero(side).Hp = 60;
                TriggerHealthLoss(ctx, side, 1, 1);
                Assert.Near(0, ctx.Hero(side).Shield);
                Assert.Equal(0, CountEvents(ctx, "shield", side));

                // 第一次跌破半血后才结算，数值取实时敌方 Burn／Poison。
                ctx.Hero(side).Hp = 40;
                TriggerHealthLoss(ctx, side, 1, 2);
                Assert.Near(expected, ctx.Hero(side).Shield);
                Assert.Equal(1, CountEvents(ctx, "shield", side));

                // 第二次损失不能重复触发；Custom_0 只是本场战斗的计数器。
                ctx.Hero(side).Hp = 30;
                TriggerHealthLoss(ctx, side, 1, 3);
                Assert.Near(expected, ctx.Hero(side).Shield);
                Assert.Equal(1, CountEvents(ctx, "shield", side));

                BattleContext next = BattleEngine.InitBattle(config);
                InternalItem nextSkill = SkillFor(next, side);
                Assert.Near(0, ItemAttributes.Get(nextSkill, "Custom_0"));
                if (status == "Burn") next.Hero(SideUtil.Opposing(side)).Burn = 30;
                else next.Hero(SideUtil.Opposing(side)).Poison = 30;
                NativeDynamicAuras.Refresh(next);
                next.Hero(side).Hp = 40;
                TriggerHealthLoss(next, side, 1, 1);
                Assert.Near(expected, next.Hero(side).Shield);
                Assert.Equal(1, CountEvents(next, "shield", side));
            }
        }

        private static void OpeningAttributesSeparateCapsFromTemporaryState()
        {
            Dictionary<string, int> attrs = new Dictionary<string, int> {
                { "Health", 12 }, { "HealthMax", 900 }, { "Shield", 99 }, { "Burn", 7 }, { "Poison", 8 },
                { "Rage", 49 }, { "Enraged", 1 }, { "RageMax", 70 }, { "EnragedDurationMax", 3000 },
                { "HealthRegen", 5 } };
            BattleConfig player = new BattleConfig { NativeMechanics = true, PlayerMaxHp = 900, PlayerStartHp = 450 };
            MonsterOpponentMapper.ApplyHeroAttributes(player, attrs, Side.Player);
            // 快照的最大生命由 PlayerMaxHealth 单独传入；临时 Health／Shield／Rage 不能覆盖。
            Assert.Near(900, player.PlayerMaxHp);
            Assert.Near(450, player.PlayerStartHp.Value);
            Assert.Near(70, player.PlayerRageMax);
            Assert.Near(3000, player.PlayerEnragedDurationMs);
            Assert.Near(5, player.PlayerStartRegen);
            BattleContext pctx = BattleEngine.InitBattle(player);
            Assert.Near(900, pctx.Player.MaxHp);
            Assert.Near(450, pctx.Player.Hp);
            Assert.Near(0, pctx.Player.Shield);
            Assert.Near(0, pctx.Player.Burn);
            Assert.Near(0, pctx.Player.Poison);
            Assert.Near(0, pctx.Player.Rage);
            Assert.True(pctx.Player.EnragedUntil <= 0);

            JsonValue raw = Json.Parse(@"{""Id"":""g06-monster"",""InternalName"":""G06 Monster"",""Player"":{""Attributes"":{""HealthMax"":650,""Health"":123,""Shield"":9,""Burn"":7,""Poison"":8,""Rage"":49,""Enraged"":1,""RageMax"":70,""EnragedDurationMax"":3000},""Hand"":{""Items"":[]},""Skills"":[]}}");
            LiveMonsterEntry monster = MonsterTemplateExtractor.FromJson(raw, null);
            // 实时生命／开场盾是显式快照；怒气、燃烧、中毒、暴怒不进入开场。
            Assert.Near(650, monster.MaxHp);
            Assert.Near(123, monster.Health);
            BattleConfig enemy = new BattleConfig { NativeMechanics = true };
            MonsterOpponentMapper.Apply(enemy, monster, EmptyCatalog, null);
            Assert.Near(650, enemy.EnemyMaxHp);
            Assert.Near(123, enemy.EnemyStartHp.Value);
            Assert.Near(9, enemy.EnemyStartShield);
            Assert.Near(70, enemy.EnemyRageMax);
            Assert.Near(3000, enemy.EnemyEnragedDurationMs);
            BattleContext ectx = BattleEngine.InitBattle(enemy);
            Assert.Near(123, ectx.Enemy.Hp);
            Assert.Near(650, ectx.Enemy.MaxHp);
            Assert.Near(9, ectx.Enemy.Shield);
            Assert.Near(0, ectx.Enemy.Rage);
            Assert.Near(0, ectx.Enemy.Burn);
            Assert.Near(0, ectx.Enemy.Poison);
            Assert.True(ectx.Enemy.EnragedUntil <= 0);
        }

        private static void WildBoarTemplateOpeningAndEventChain()
        {
            LiveMonsterEntry monster = WildBoar();
            Assert.Near(650, monster.MaxHp);
            Assert.Near(650, monster.Health);
            Assert.Near(50, monster.Attributes["RageMax"]);
            Assert.Near(5000, monster.Attributes["EnragedDurationMax"]);

            BattleConfig config = new BattleConfig { PlayerMaxHp = 5000, NativeMechanics = true, MaxTime = 13.2 };
            CardDef player = new CardDef { Id = "g06-player", Cooldown = 100 };
            config.Cards[player.Id] = player;
            config.Player.Items.Add(new LoadoutItem { CardId = player.Id, Slot = 0 });
            MonsterOpponentMapper.Apply(config, monster, EmptyCatalog, null);
            Assert.Near(650, config.EnemyMaxHp);
            Assert.Near(650, config.EnemyStartHp.Value);
            Assert.Near(50, config.EnemyRageMax);
            Assert.Near(5000, config.EnemyEnragedDurationMs);
            Assert.Equal("", string.Join(" | ", NativeCardAbilityMapper.Apply(config).ToArray()));

            BattleContext opening = BattleEngine.InitBattle(config);
            Assert.Near(650, opening.Enemy.Hp);
            Assert.Near(0, opening.Enemy.Rage);
            Assert.Near(0, opening.Enemy.Shield);
            Assert.Near(0, opening.Enemy.Burn);
            Assert.Near(0, opening.Enemy.Poison);
            Assert.True(opening.Enemy.EnragedUntil <= 0);

            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> rage = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "rage" && ev.Side == Side.Enemy; });
            double[] expectedTimes = { 4.05, 5.05, 7.05, 8.05 };
            Assert.True(rage.Count >= expectedTimes.Length);
            for (int index = 0; index < expectedTimes.Length; index++)
            {
                Assert.Near(15, rage[index].Value.Value);
                Assert.Near(expectedTimes[index], rage[index].Time);
            }
            List<BattleEvent> enrage = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "enrage" && ev.Side == Side.Enemy; });
            Assert.Equal(1, enrage.Count);
            Assert.Near(8.05, enrage[0].Time);
            Assert.Near(5000, enrage[0].Value.Value);
            List<BattleEvent> shields = result.Events.FindAll(delegate(BattleEvent ev)
            {
                return ev.Kind == "modifyAttribute" && ev.Side == Side.Enemy && ev.Note == "ShieldApplyAmount" &&
                    Math.Abs(ev.Time - 8.1) < 1e-9;
            });
            Assert.Equal(4, shields.Count);
            foreach (BattleEvent ev in shields) Assert.Near(40, ev.Value.Value);
            List<BattleEvent> ends = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "enrageEnd" && ev.Side == Side.Enemy; });
            Assert.Equal(1, ends.Count);
            Assert.Near(13.05, ends[0].Time);
        }

        private static void WildBoarItemEnrageBuffIsTemporary()
        {
            BattleConfig config = Board(Real("cards:Wild Boar", 4, "Silver"));
            CardDef shieldCard = new CardDef { Id = "g06-shield-item", Cooldown = 100 };
            shieldCard.HiddenTags.Add("Shield");
            config.Cards[shieldCard.Id] = shieldCard;
            config.Player.Items.Add(new LoadoutItem { CardId = shieldCard.Id, Slot = 8 });
            CardDef plainCard = new CardDef { Id = "g06-plain-item", Cooldown = 100 };
            config.Cards[plainCard.Id] = plainCard;
            config.Player.Items.Add(new LoadoutItem { CardId = plainCard.Id, Slot = 9 });
            config.PlayerRageMax = 1;

            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem boar = ItemAt(ctx, Side.Player, 4);
            InternalItem shield = ItemAt(ctx, Side.Player, 8);
            InternalItem plain = ItemAt(ctx, Side.Player, 9);
            Assert.Near(20, ItemAttributes.Get(boar, "ShieldApplyAmount"));
            Assert.Near(0, ItemAttributes.Get(shield, "ShieldApplyAmount"));

            Effects.ResolveEffect(ctx, new Effect
            {
                Kind = "rage", Amount = new NumExpr { Type = "const", Value = 1 },
                Target = new Target { Kind = "selfHero" }
            }, boar, 1, 0, null);
            Triggers.Drain(ctx, 1);
            Assert.True(ctx.Player.EnragedUntil > 0);
            Assert.Near(60, ItemAttributes.Get(boar, "ShieldApplyAmount"));
            Assert.Near(40, ItemAttributes.Get(shield, "ShieldApplyAmount"));
            Assert.Near(0, ItemAttributes.Get(plain, "ShieldApplyAmount"));

            // 下一条战斗只读模板：+40 是 UntilEndOfCombat 运行时偏移。
            BattleContext next = BattleEngine.InitBattle(config);
            Assert.Near(20, ItemAttributes.Get(ItemAt(next, Side.Player, 4), "ShieldApplyAmount"));
            Assert.Near(0, ItemAttributes.Get(ItemAt(next, Side.Player, 8), "ShieldApplyAmount"));
        }

        private static void EnrageEndClosesSigilWindow()
        {
            BattleConfig config = Board(Enchanted("cards:Vengeful Sigil", 4, "Gold", "Shiny"), Donor(6));
            CardDef weapon = CardAt(config, Side.Player, 6);
            weapon.Tags.Add("Weapon");
            weapon.Cooldown = 0.5;
            weapon.LiveAttributes["CooldownMax"] = 500;
            if (weapon.NativeAuraSeedAttributes != null) weapon.NativeAuraSeedAttributes["CooldownMax"] = 500;
            CardDef enemyItem = new CardDef { Id = "enemy:g06-start", Cooldown = 100, StartReady = true };
            config.Cards[enemyItem.Id] = enemyItem;
            config.Enemy.Items.Add(new LoadoutItem { CardId = enemyItem.Id, Slot = 0 });
            config.PlayerRageMax = 12;
            config.PlayerMaxHp = 100;
            config.PlayerStartHp = 80;
            config.PlayerEnragedDurationMs = 1000;
            config.MaxTime = 2.5;

            BattleResult result = BattleEngine.Simulate(config);
            List<BattleEvent> enrage = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "enrage" && ev.Side == Side.Player; });
            Assert.Equal(1, enrage.Count);
            Assert.True(enrage[0].Time <= 0.1 + 1e-9);
            List<BattleEvent> ends = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "enrageEnd" && ev.Side == Side.Player; });
            Assert.Equal(1, ends.Count);
            Assert.Near(1, ends[0].Time - enrage[0].Time);

            List<BattleEvent> heals = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "heal" && ev.Side == Side.Player && ev.SourceCardId == weapon.Id; });
            Assert.True(heals.Count >= 1);
            foreach (BattleEvent heal in heals) Assert.True(heal.Time < ends[0].Time);
        }
    }
}
