using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    // G02：成长后的充能/加速/减速/冻结必须改变下一次真实出手，
    // 不能只改面板字段或在事件里报一个未参与冷却的数值。
    public static class G02StatusTimingTests
    {
        private static JsonValue _kyver;
        private static JsonValue _audit;
        private static JsonValue _seeds;
        private static JsonValue _shinyEvidence;
        private static JsonValue _resolvedBlockers;
        private static readonly CardCatalog EmptyCatalog = CardCatalog.FromCards(new List<CardDef>());

        public static void Run()
        {
            _kyver = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_kyver_cards.json"));
            _audit = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_audit_regressions.json"));
            _seeds = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g02_seeds.json"));
            _shinyEvidence = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g02_shiny_evidence.json"));
            _resolvedBlockers = Json.Parse(File.ReadAllText("tools/actionaxis_plugin/tests/fixtures/native_g02_resolved_blockers.json"));
            CogGrowthMovesNeighborActivation();
            DryadRageGrowthMovesItsOwnActivation();
            LauncherCoreFlightsChargeAlliesButNotSelf();
            DrowsdropReloadRestoresAmmoAndSlowDelaysEnemy();
            FreezeAndFlyingResistanceMoveActivation();
            FrozenCardWaitsOneFrameAfterFreezeExpires();
            LauncherCorePassiveChargesOnNextFrame();
            DryadShinyCastsTwice();
            LiveShinyMulticastUsesBaseOne();
            LiveShinyWithoutSnapshotMulticastKeepsBase();
            CogShinyHastesTwicePerActivation();
            DrowsdropShinyConsumesAmmoPerCast();
            LauncherCoreShinyChargesAlliesTwice();
            RuntimeZeroCooldownIsNotAnEnchantmentTarget();
            ResolvedBlockerTimingTests();
            SeedTierGrowthMovesActivation();
            SeedStatusChangesMoveActivation();
        }

        private static void CogGrowthMovesNeighborActivation()
        {
            JsonValue template = Kyver("Cog");
            BattleConfig grown = Board(10, Real(template, 0, "Bronze"), Fake("g02:cog-neighbor", 1, 10000, "[]"));
            string cogId = IdOf(grown, template["Id"].AsString());
            // 战斗中把 HasteAmount 从 1 秒长到 2 秒；执行时取值，不能停在映射快照。
            grown.Cards[cogId].Triggers.Add(AtStart(Modify("HasteAmount", "Add", 1000)));
            BattleResult grownResult = BattleEngine.Simulate(grown);
            Assert.Equal(0, grownResult.Warnings.Count);
            BattleEvent haste = grownResult.Events.First(delegate(BattleEvent ev)
                { return ev.Kind == "haste" && Math.Abs(ev.Time - 4) < 1e-9; });
            Assert.Near(2, haste.Value.Value);
            Assert.Near(8.05, Activations(grownResult, grown, "g02:cog-neighbor").Single());

            // 目标数同样是执行时取值：长到 0 张就不能再给邻居加速。
            BattleConfig zero = Board(10, Real(template, 0, "Bronze"), Fake("g02:cog-neighbor", 1, 10000, "[]"));
            string zeroCogId = IdOf(zero, template["Id"].AsString());
            zero.Cards[zeroCogId].Triggers.Add(AtStart(Modify("HasteAmount", "Add", 1000)));
            zero.Cards[zeroCogId].Triggers.Add(AtStart(Modify("HasteTargets", "Subtract", 1)));
            BattleResult zeroResult = BattleEngine.Simulate(zero);
            Assert.Equal(0, zeroResult.Warnings.Count);
            Assert.Equal(0, zeroResult.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "haste"; }));
            Assert.Near(10, Activations(zeroResult, zero, "g02:cog-neighbor").Single());

            // 邻居的 CooldownMax 在战斗中被减到 0 后不再是有效目标；
            // 原生条件看运行时属性，不能沿用开场的静态冷却。
            BattleConfig runtime = Board(10, Real(template, 0, "Bronze"), Fake("g02:cog-neighbor", 1, 10000, "[]"));
            string liveNeighborId = IdOf(runtime, "g02:cog-neighbor");
            runtime.Cards[liveNeighborId].Triggers.Add(AtStart(Modify("CooldownMax", "Subtract", 10000)));
            BattleResult runtimeResult = BattleEngine.Simulate(runtime);
            Assert.Equal(0, runtimeResult.Warnings.Count);
            Assert.Equal(0, runtimeResult.Events.Count(delegate(BattleEvent ev) { return ev.Kind == "haste"; }));
        }

        private static void DryadRageGrowthMovesItsOwnActivation()
        {
            JsonValue template = _audit["Dryad"]["template"];
            BattleConfig baseline = Board(7, Real(template, 0, "Bronze"), Fake("g02:dryad-generator", 1, 2000, "[]"));
            BattleResult baselineResult = BattleEngine.Simulate(baseline);
            Assert.Equal(0, baselineResult.Warnings.Count);
            BattleEvent normalCharge = ChargesTo(baselineResult, baseline, template["Id"].AsString()).First();
            Assert.Near(1, normalCharge.Value.Value);
            Assert.Near(5, Activations(baselineResult, baseline, template["Id"].AsString()).First());

            BattleConfig enraged = Board(7, Real(template, 0, "Bronze"), Fake("g02:dryad-generator", 1, 2000, "[]"));
            string generatorId = IdOf(enraged, "g02:dryad-generator");
            TriggerDef rage = new TriggerDef { Event = "atCombatStart" };
            rage.Effects.Add(new Effect { Kind = "rage", Amount = Fixed(100) });
            enraged.Cards[generatorId].Triggers.Add(rage);
            BattleResult enragedResult = BattleEngine.Simulate(enraged);
            Assert.Equal(0, enragedResult.Warnings.Count);
            BattleEvent grownCharge = ChargesTo(enragedResult, enraged, template["Id"].AsString()).First();
            Assert.Near(2, grownCharge.Value.Value);
            // 1 秒 -> 2 秒后，Dryad 在第二个被动充能生效当帧就绪，次帧出手；
            // 暴怒期间 Base Rage Effect（原生 4472da8a）再给 10% 冷却缩减，
            // 首次出手从 4.1s 提前到 3.7s。
            Assert.Near(3.7, Activations(enragedResult, enraged, template["Id"].AsString()).First());
        }

        private static void LauncherCoreFlightsChargeAlliesButNotSelf()
        {
            JsonValue template = Kyver("Launcher Core");
            BattleConfig config = Board(14.5, Real(template, 0, "Silver"),
                Fake("g02:core-friend", 1, 16000, "[\"Friend\"]"),
                Fake("g02:core-wing", 2, 16000, "[]"));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string coreId = IdOf(config, template["Id"].AsString());
            string friendId = IdOf(config, "g02:core-friend");
            string wingId = IdOf(config, "g02:core-wing");
            List<double> coreUses = Activations(result, config, template["Id"].AsString());
            Assert.Equal(2, coreUses.Count);
            Assert.Near(6, coreUses[0]);
            // 自己的使用不能触发“友方/飞行时充能自己”，否则第二次会在 11 秒。
            Assert.Near(12, coreUses[1]);
            Assert.Near(14, Activations(result, config, "g02:core-friend").Single());
            Assert.Near(14, Activations(result, config, "g02:core-wing").Single());

            List<BattleEvent> firstFlights = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "fly" && ev.SourceCardId == coreId && Math.Abs(ev.Time - 6) < 1e-9; });
            Assert.Equal(2, firstFlights.Count);
            Assert.True(firstFlights.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == friendId; }));
            Assert.True(firstFlights.Exists(delegate(BattleEvent ev) { return ev.TargetItemId == wingId; }));
            List<BattleEvent> firstCharges = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.SourceItemId == coreId && Math.Abs(ev.Time - 6) < 1e-9; });
            Assert.Equal(2, firstCharges.Count);
            foreach (BattleEvent charge in firstCharges) Assert.Near(1, charge.Value.Value);
        }

        private static void DrowsdropReloadRestoresAmmoAndSlowDelaysEnemy()
        {
            JsonValue template = Kyver("Drowsdrop");
            BattleConfig config = Board(11, Real(template, 0, "Bronze"),
                Fake("g02:drowsdrop-ally", 1, 6000, "[]"), Fake("g02:drowsdrop-enemy", 0, 6000, "[]"));
            string friendlyId = IdOf(config, "g02:drowsdrop-ally");
            config.Cards[friendlyId].Effects.Add(new Effect {
                Kind = "slow", Seconds = 1, Count = 1,
                Target = new Target { Kind = "randomSelfItem", ExcludeSelf = true }
            });
            MoveToEnemy(config, "g02:drowsdrop-enemy");
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string potionId = IdOf(config, template["Id"].AsString());
            BattleEvent slow = result.Events.Single(delegate(BattleEvent ev)
                { return ev.Kind == "slow" && ev.SourceItemId == potionId && Math.Abs(ev.Time - 5) < 1e-9; });
            Assert.Near(5, slow.Time);
            Assert.Near(1, slow.Value.Value);
            Assert.Near(6.5, Activations(result, config, "g02:drowsdrop-enemy").Single());

            BattleEvent reload = result.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "reload"; });
            Assert.Near(6.05, reload.Time);
            Assert.Equal(potionId, reload.TargetItemId);
            Assert.Equal(1, (int)reload.Value.Value);
            List<double> potionUses = Activations(result, config, template["Id"].AsString());
            Assert.Equal(2, potionUses.Count);
            Assert.Near(5, potionUses[0]);
            Assert.Near(10.5, potionUses[1]);

            // 没有友方减速就没有装填；弹药耗尽的 Drowsdrop 即使冷却归零也不能再次出手。
            BattleConfig dry = Board(11, Real(template, 0, "Bronze"), Fake("g02:drowsdrop-enemy", 0, 6000, "[]"));
            MoveToEnemy(dry, "g02:drowsdrop-enemy");
            BattleResult dryResult = BattleEngine.Simulate(dry);
            Assert.Equal(0, dryResult.Warnings.Count);
            Assert.Equal(1, Activations(dryResult, dry, template["Id"].AsString()).Count);
        }

        private static void FreezeAndFlyingResistanceMoveActivation()
        {
            BattleConfig normal = FreezeBoard(false, true);
            BattleResult normalResult = BattleEngine.Simulate(normal);
            Assert.Equal(0, normalResult.Warnings.Count);
            BattleEvent normalFreeze = normalResult.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "freeze"; });
            Assert.Near(1, normalFreeze.Value.Value);
            Assert.Near(3, Activations(normalResult, normal, "g02:freeze-target").Single());

            BattleConfig flying = FreezeBoard(true, true);
            BattleResult flyingResult = BattleEngine.Simulate(flying);
            Assert.Equal(0, flyingResult.Warnings.Count);
            BattleEvent flyingFreeze = flyingResult.Events.Single(delegate(BattleEvent ev) { return ev.Kind == "freeze"; });
            Assert.Near(0.5, flyingFreeze.Value.Value);
            Assert.Near(2.5, Activations(flyingResult, flying, "g02:freeze-target").Single());

            BattleConfig unfrozen = FreezeBoard(false, false);
            BattleResult unfrozenResult = BattleEngine.Simulate(unfrozen);
            Assert.Equal(0, unfrozenResult.Warnings.Count);
            Assert.Near(2, Activations(unfrozenResult, unfrozen, "g02:freeze-target").Single());
        }

        // 原生回放 034c3648 帧 141/152/153：Catfish 在冻结期间被充能到就绪，
        // 快照读到 Freeze 50 -> 0 的帧（152）仍不能出手，真正开火在下一帧
        // （153）。在归零同帧开火会让后续充能串行队列整体前移一环（261 帧
        // 多出一次自充能，Launcher Core 提前 5 帧开火）。
        private static void FrozenCardWaitsOneFrameAfterFreezeExpires()
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true, MaxTime = 1.2,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000 };
            CardDef card = new CardDef { Id = "g02:frozen-ready", Cooldown = 1.5 };
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            start.Effects.Add(new Effect { Kind = "freeze", Seconds = 1,
                Target = new Target { Kind = "selfItem" } });
            start.Effects.Add(new Effect { Kind = "charge", Seconds = 2,
                Target = new Target { Kind = "selfItem" } });
            card.Triggers.Add(start);
            config.Cards[card.Id] = card;
            config.Player.Items.Add(new LoadoutItem { CardId = card.Id, Slot = 0 });
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<double> activations = result.Events
                .Where(delegate(BattleEvent ev) { return ev.Kind == "activate" && ev.SourceCardId == card.Id; })
                .Select(delegate(BattleEvent ev) { return ev.Time; }).ToList();
            Assert.Equal(1, activations.Count);
            // Freeze 0.95 归零 -> 冷却重置 1.00 -> 能力结算 1.05。
            Assert.Near(1.05, activations[0]);
        }

        // 原生回放（9bbabba4 / 5682f3cd）：Firefly/Spider 生效后，Core 的
        // 被动充能在下一 50ms 帧才出现，不是同帧；校准保持 onUse。
        private static void LauncherCorePassiveChargesOnNextFrame()
        {
            JsonValue template = _seeds["Launcher Core"]["template"];
            BattleConfig config = Board(5, Seed("Launcher Core", 0, "Silver"),
                Fake("g02:core-early-friend", 1, 2000, "[\"Friend\"]"));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string coreId = IdOf(config, template["Id"].AsString());
            string friendId = IdOf(config, "g02:core-early-friend");
            BattleEvent friendUse = result.Events.First(delegate(BattleEvent ev)
                { return ev.Kind == "activate" && ev.SourceCardId == friendId; });
            BattleEvent coreCharge = result.Events.First(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.TargetItemId == coreId; });
            Assert.Near(friendUse.Time + 0.05, coreCharge.Time);
            // 2 秒伙伴在 4 秒前两次使用后充能，使 6 秒 Core 在 4.1 秒出手。
            Assert.Near(4.1, Activations(result, config, template["Id"].AsString()).First());
        }

        // Shiny 的 +1 Multicast 必须叠加在隐含基底 1 上；Dryad 没有显式
        // Multicast 属性，不能用 0 当基底，否则只出手一次。
        private static void DryadShinyCastsTwice()
        {
            JsonValue template = _seeds["Dryad"]["template"];
            BattleConfig config = Board(6, Seed("Dryad", 0, "Bronze", "Shiny"),
                Fake("g02:dryad-shiny-generator", 1, 2000, "[]"));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string dryadId = IdOf(config, template["Id"].AsString());
            List<double> uses = Activations(result, config, template["Id"].AsString());
            Assert.Equal(2, uses.Count);
            Assert.Near(0.25, uses[1] - uses[0]);
            Assert.Equal(2, result.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "heal" && ev.SourceCardId == dryadId; }));
        }

        // 真实回放 e8e2fc61 的 Waterskin/Shiny：实时属性已有 Multicast=2，
        // 引擎不能把 Shiny 光环再叠一次成 3；两次施放间隔 250ms。
        private static void LiveShinyMulticastUsesBaseOne()
        {
            JsonValue row = _shinyEvidence["Waterskin"];
            JsonValue template = row["template"];
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, "Bronze");
            attrs["Multicast"] = 2;
            attrs["CooldownMax"] = 4000;
            attrs["Cooldown"] = 4000;
            LiveCardEntry entry = new LiveCardEntry {
                TemplateId = row["id"].AsString(), Template = template, Tier = "Bronze", Socket = 1,
                Size = template["Size"].AsString(), AttributesAreLive = true, Attributes = attrs,
                Enchantment = "Shiny", EnchantmentDefinition = template["Enchantments"]["Shiny"]
            };
            BattleConfig config = Board(5, entry, Fake("g02:waterskin-left", 0, 10000, "[]"));
            string waterskinId = IdOf(config, row["id"].AsString());
            BattleContext ctx = BattleEngine.InitBattle(config);
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == waterskinId; });
            Assert.Equal(1, item.MulticastBonus);
            BattleResult result = BattleEngine.Simulate(config);
            string leftId = IdOf(config, "g02:waterskin-left");
            List<BattleEvent> charges = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.TargetItemId == leftId && ev.Time < 5; });
            Assert.Equal(2, charges.Count);
            Assert.Near(4, charges[0].Time);
            Assert.Near(0.25, charges[1].Time - charges[0].Time);
        }

        // 实时属性缺 Multicast 时，Initialize 的继承光环反推不能把隐含基底
        // 1 再减成 0；Dryad Shiny 仍应 2 次施放。
        private static void LiveShinyWithoutSnapshotMulticastKeepsBase()
        {
            JsonValue template = _seeds["Dryad"]["template"];
            Dictionary<string, int> attrs = MonsterTemplateExtractor.TierAttributes(template, "Bronze");
            attrs.Remove("Multicast");
            LiveCardEntry entry = new LiveCardEntry {
                TemplateId = template["Id"].AsString(), Template = template, Tier = "Bronze", Socket = 0,
                Size = template["Size"].AsString(), AttributesAreLive = true, Attributes = attrs,
                Enchantment = "Shiny", EnchantmentDefinition = template["Enchantments"]["Shiny"]
            };
            BattleConfig config = Board(6, entry, Fake("g02:dryad-live-generator", 1, 2000, "[]"));
            BattleContext ctx = BattleEngine.InitBattle(config);
            string dryadId = IdOf(config, template["Id"].AsString());
            InternalItem item = ctx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == dryadId; });
            Assert.Equal(1, item.MulticastBonus);
            BattleResult result = BattleEngine.Simulate(config);
            List<double> uses = Activations(result, config, template["Id"].AsString());
            Assert.Equal(2, uses.Count);
            Assert.Near(0.25, uses[1] - uses[0]);
        }

        // Shiny Cog 每次出手放两次 Haste，目标实际冷却条必须比单次更快。
        private static void CogShinyHastesTwicePerActivation()
        {
            JsonValue template = _seeds["Cog"]["template"];
            BattleConfig config = Board(10, Seed("Cog", 0, "Bronze", "Shiny"),
                Fake("g02:cog-shiny-neighbor", 1, 12000, "[]"));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string neighborId = IdOf(config, "g02:cog-shiny-neighbor");
            List<BattleEvent> hastes = result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "haste" && ev.TargetItemId == neighborId && ev.Time < 5; });
            Assert.Equal(2, hastes.Count);
            Assert.Near(1, hastes[0].Value.Value);
            Assert.Near(1, hastes[1].Value.Value);
            Assert.Near(0.25, hastes[1].Time - hastes[0].Time);
            Assert.Near(9.05, Activations(result, config, "g02:cog-shiny-neighbor").First());
        }

        // Shiny Drowsdrop 有两次 Multicast，但每发都消耗弹药；补到 2 发时
        // 才能真的打出两次，且两次都发 Poison/Slow。
        private static void DrowsdropShinyConsumesAmmoPerCast()
        {
            JsonValue template = _seeds["Drowsdrop"]["template"];
            BattleConfig config = Board(8, Seed("Drowsdrop", 0, "Bronze", "Shiny"),
                Fake("g02:drowsdrop-shiny-enemy", 0, 6000, "[]"));
            string potionId = IdOf(config, template["Id"].AsString());
            config.Cards[potionId].Triggers.Add(StartWith(
                Modify("AmmoMax", "Add", 1),
                new Effect { Kind = "reload", AmountValue = 1, Target = new Target { Kind = "selfItem" } }));
            MoveToEnemy(config, "g02:drowsdrop-shiny-enemy");
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            List<double> uses = Activations(result, config, template["Id"].AsString());
            Assert.Equal(2, uses.Count);
            Assert.Near(0.25, uses[1] - uses[0]);
            Assert.Equal(2, result.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "poison" && ev.SourceCardId == potionId; }));
            Assert.Equal(2, result.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "slow" && ev.SourceItemId == potionId; }));
        }

        // Shiny Core 每次主动施放两次：两次都充能飞行盟友，把 16 秒盟友
        // 从 14 秒提前到 12.3 秒；第二次施放不再产生新的飞行目标。
        private static void LauncherCoreShinyChargesAlliesTwice()
        {
            JsonValue template = _seeds["Launcher Core"]["template"];
            BattleConfig config = Board(13, Seed("Launcher Core", 0, "Silver", "Shiny"),
                Fake("g02:core-shiny-a", 1, 16000, "[]"), Fake("g02:core-shiny-b", 2, 16000, "[]"));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            string coreId = IdOf(config, template["Id"].AsString());
            List<double> coreUses = Activations(result, config, template["Id"].AsString());
            Assert.Equal(4, coreUses.Count);
            Assert.Near(6, coreUses[0]);
            Assert.Near(6.25, coreUses[1]);
            Assert.Near(12, coreUses[2]);
            Assert.Near(12.25, coreUses[3]);
            Assert.Near(12.3, Activations(result, config, "g02:core-shiny-a").Single());
            Assert.Near(12.3, Activations(result, config, "g02:core-shiny-b").Single());
            foreach (string ally in new[] { "g02:core-shiny-a", "g02:core-shiny-b" })
            {
                string allyId = IdOf(config, ally);
                Assert.Equal(4, result.Events.Count(delegate(BattleEvent ev)
                    { return ev.Kind == "charge" && ev.TargetItemId == allyId && ev.Time < 13; }));
            }
        }

        // Dryad 的 CooldownMax、Core 的 FlyingTargets、Drowsdrop 的 SlowTargets。
        // 运行时把 CooldownMax 降到 0 后，原生条件 CooldownMax > 0 必须读到 0；
        // 否则 Heavy/Turbo 附魔仍会选中已经不再冷却的相邻卡。
        private static void RuntimeZeroCooldownIsNotAnEnchantmentTarget()
        {
            BattleConfig config = Board(8, Seed("Cog", 0, "Bronze", "Heavy"),
                Fake("g02:cog-zero-neighbor", 1, 12000, "[]"));
            string neighborId = IdOf(config, "g02:cog-zero-neighbor");
            config.Cards[neighborId].Triggers.Add(StartWith(Modify("CooldownMax", "Subtract", 12000)));
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            Assert.Equal(0, result.Events.Count(delegate(BattleEvent ev)
                { return (ev.Kind == "haste" || ev.Kind == "slow") && ev.TargetItemId == neighborId; }));
        }

        private static void ResolvedBlockerTimingTests()
        {
            // Spark Plug / Radiant: adjacent Haste 2s 减半为 1s，首次出手 2s→3s。
            foreach (bool radiant in new[] { false, true })
            {
                BattleConfig config = Board(3.5,
                    Fake("g02:spark-neighbor", 0, 4000, "[]"),
                    Resolved("Spark Plug", 1, "Bronze", radiant ? "Radiant" : null));
                string sparkId = IdOf(config, _resolvedBlockers["Spark Plug"]["id"].AsString());
                config.Cards[sparkId].Cooldown = 100;
                config.Cards[sparkId].LiveAttributes["CooldownMax"] = 100000;
                string neighborId = IdOf(config, "g02:spark-neighbor");
                config.Cards[neighborId].Triggers.Add(StartWith(new Effect {
                    Kind = "haste", Seconds = 2, Target = new Target { Kind = "selfItem" }
                }));
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                Assert.Near(radiant ? 3.05 : 2.05, Activations(result, config, "g02:spark-neighbor").Single());
            }

            // Stopwatch / Deadly: 敌方手牌 CritChance 50 - 30 = 20。
            BattleConfig watch = BoardRaw(8,
                Resolved("Stopwatch", 0, "Gold", "Deadly"),
                FakeStats("g02:stopwatch-enemy", 0, 6000, "[]", 50));
            MoveToEnemy(watch, "g02:stopwatch-enemy");
            List<string> watchWarnings = NativeCardAbilityMapper.Apply(watch);
            watchWarnings.AddRange(NativeItemMapper.Apply(watch));
            Assert.Equal("", string.Join(" | ", watchWarnings.ToArray()));
            BattleContext watchCtx = BattleEngine.InitBattle(watch);
            string enemyId = IdOf(watch, "g02:stopwatch-enemy");
            InternalItem enemyItem = watchCtx.Items.Find(delegate(InternalItem candidate) { return candidate.CardId == enemyId; });
            Assert.Near(0.2, enemyItem.CritBonus);

            // Poke: 获得 CritChance 时给自己充能 1s，6s 冷却提前到 5s。
            BattleConfig poke = Board(8, Resolved("Poke", 0, "Silver"),
                Fake("g02:poke-bystander", 1, 100000, "[]"));
            string pokeId = IdOf(poke, _resolvedBlockers["Poke"]["id"].AsString());
            poke.Cards[pokeId].Triggers.Add(StartWith(Modify("CritChance", "Add", 10)));
            BattleResult pokeResult = BattleEngine.Simulate(poke);
            Assert.Equal(0, pokeResult.Warnings.Count);
            Assert.Near(5, Activations(pokeResult, poke, _resolvedBlockers["Poke"]["id"].AsString()).First());
        }

        private static void SeedTierGrowthMovesActivation()
        {
            string[] tiers = { "Bronze", "Silver", "Gold", "Diamond" };
            JsonValue cog = _seeds["Cog"]["template"];
            double[] cogTimes = { 10.1, 9.05, 8.55, 8.05 };
            for (int i = 0; i < tiers.Length; i++)
            {
                BattleConfig config = Board(14, Seed("Cog", 0, tiers[i]),
                    Fake("g02:cog-tier-neighbor", 1, 12000, "[]"));
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                Assert.Near(cogTimes[i], Activations(result, config, "g02:cog-tier-neighbor").First());
                BattleEvent haste = result.Events.First(delegate(BattleEvent ev)
                    { return ev.Kind == "haste" && ev.TargetItemId == IdOf(config, "g02:cog-tier-neighbor"); });
                Assert.Near(i + 1, haste.Value.Value);
            }

            JsonValue dryad = _seeds["Dryad"]["template"];
            double[] dryadTimes = { 5, 4.1, 4, 3 };
            for (int i = 0; i < tiers.Length; i++)
            {
                BattleConfig config = Board(12, Seed("Dryad", 0, tiers[i]),
                    Fake("g02:dryad-tier-generator", 1, 2000, "[]"));
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                Assert.Near(dryadTimes[i], Activations(result, config, dryad["Id"].AsString()).First());
            }

            string[] coreTiers = { "Silver", "Gold", "Diamond" };
            int[] atFourteen = { 2, 3, 4 };
            for (int i = 0; i < coreTiers.Length; i++)
            {
                BattleConfig config = Board(16, Seed("Launcher Core", 0, coreTiers[i]),
                    Fake("g02:core-tier-a", 1, 16000, "[]"), Fake("g02:core-tier-b", 2, 16000, "[]"),
                    Fake("g02:core-tier-c", 3, 16000, "[]"), Fake("g02:core-tier-d", 4, 16000, "[]"));
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                int early = 0, late = 0;
                for (int slot = 0; slot < 4; slot++)
                {
                    List<double> times = Activations(result, config, "g02:core-tier-" + ((char)('a' + slot)));
                    if (times.Count == 1 && Math.Abs(times[0] - 14) < 1e-9) early++;
                    else if (times.Count == 1 && Math.Abs(times[0] - 15) < 1e-9) late++;
                }
                Assert.Equal(atFourteen[i], early);
                Assert.Equal(4 - atFourteen[i], late);
            }

            string[] dropTiers = { "Bronze", "Silver" };
            for (int i = 0; i < dropTiers.Length; i++)
            {
                BattleConfig config = Board(8, Seed("Drowsdrop", 0, dropTiers[i]),
                    Fake("g02:drowsdrop-tier-a", 1, 6000, "[]"), Fake("g02:drowsdrop-tier-b", 2, 6000, "[]"));
                MoveToEnemy(config, "g02:drowsdrop-tier-a");
                MoveToEnemy(config, "g02:drowsdrop-tier-b");
                BattleResult result = BattleEngine.Simulate(config);
                Assert.Equal(0, result.Warnings.Count);
                int delayed = 0, normal = 0;
                foreach (string card in new[] { "g02:drowsdrop-tier-a", "g02:drowsdrop-tier-b" })
                {
                    double time = Activations(result, config, card).Single();
                    if (Math.Abs(time - 6.5) < 1e-9) delayed++;
                    else if (Math.Abs(time - 6) < 1e-9) normal++;
                }
                Assert.Equal(i + 1, delayed);
                Assert.Equal(1 - i, normal);
            }
        }

        // 状态时长必须真的作用在目标冷却条上：1 秒 Slow/Freeze/Haste 分别把
        // 首次出手推后 0.5/1 秒或提前 1 秒；重叠两次 Slow 再推后 1 秒。
        private static void SeedStatusChangesMoveActivation()
        {
            string[] names = { "Cog", "Dryad", "Launcher Core", "Drowsdrop" };
            string[] tiers = { "Bronze", "Bronze", "Silver", "Bronze" };
            double[] slow = { 4.5, 7.5, 6.5, 5.5 };
            double[] freeze = { 5, 8, 7, 6 };
            double[] haste = { 3, 6, 5, 4 };
            for (int i = 0; i < names.Length; i++)
            {
                Assert.Near(slow[i], FirstActivation(StatusBoard(names[i], tiers[i], "slow", 1), names[i]));
                // Freeze 50 -> 0 still blocks this interval (b1621d7b frame
                // 140); opening Freeze therefore delays the full second.
                Assert.Near(freeze[i], FirstActivation(StatusBoard(names[i], tiers[i], "freeze", 1), names[i]));
                Assert.Near(haste[i] + 0.05, FirstActivation(StatusBoard(names[i], tiers[i], "haste", 1), names[i]));
            }

            // Drowsdrop 的开局 Slow 会尝试装填，但开局已经满弹药，
            // 原生不会发布没有实际弹药变化的 Reload 事件。
            BattleConfig drows = StatusBoard("Drowsdrop", "Bronze", "slow", 1);
            BattleResult drowsResult = BattleEngine.Simulate(drows);
            Assert.Equal(0, drowsResult.Events.Count(delegate(BattleEvent ev)
                { return ev.Kind == "reload" && Math.Abs(ev.Time) < 1e-9; }));

            // 重叠与到期：两次 1 秒 Slow 合计 2 秒，不能只保留一次或永久生效。
            Assert.Near(5, FirstActivation(StatusBoard("Cog", "Bronze", "slow", 2), "Cog"));
        }

        private static double FirstActivation(BattleConfig config, string name)
        {
            BattleResult result = BattleEngine.Simulate(config);
            Assert.Equal(0, result.Warnings.Count);
            return Activations(result, config, _seeds[name]["id"].AsString()).First();
        }

        private static BattleConfig StatusBoard(string name, string tier, string status, int count)
        {
            BattleConfig config = Board(14, Seed(name, 0, tier), Fake("g02:status-bystander", 1, 100000, "[]"));
            string id = IdOf(config, _seeds[name]["id"].AsString());
            TriggerDef trigger = new TriggerDef { Event = "atCombatStart" };
            for (int i = 0; i < count; i++)
                trigger.Effects.Add(new Effect { Kind = status, Seconds = 1, Target = new Target { Kind = "selfItem" } });
            config.Cards[id].Triggers.Add(trigger);
            return config;
        }


        private static BattleConfig FreezeBoard(bool targetFlying, bool applyFreeze)
        {
            BattleConfig config = Board(3.5, Fake("g02:freezer", 0, 0, "[]"), Fake("g02:freeze-target", 1, 2000, "[]"));
            string freezerId = IdOf(config, "g02:freezer");
            if (!targetFlying && !applyFreeze) return config;
            TriggerDef start = new TriggerDef { Event = "atCombatStart" };
            if (targetFlying)
                start.Effects.Add(new Effect {
                    Kind = "fly", Count = 1,
                    Target = new Target { Kind = "randomSelfItem", ExcludeSelf = true }
                });
            if (applyFreeze)
                start.Effects.Add(new Effect {
                    Kind = "freeze", Seconds = 1, Count = 1,
                    Target = new Target { Kind = "randomSelfItem", ExcludeSelf = true }
                });
            config.Cards[freezerId].Triggers.Add(start);
            return config;
        }

        private static BattleConfig BoardRaw(double maxTime, params LiveCardEntry[] cards)
        {
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, maxTime);
            mapped.Config.NativeMechanics = true;
            return mapped.Config;
        }

        private static BattleConfig Board(double maxTime, params LiveCardEntry[] cards)
        {
            MappedBoard mapped = CardMapper.Build(new List<LiveCardEntry>(cards), EmptyCatalog, 10000, 10000, maxTime);
            mapped.Config.NativeMechanics = true;
            List<string> warnings = NativeCardAbilityMapper.Apply(mapped.Config);
            warnings.AddRange(NativeItemMapper.Apply(mapped.Config));
            Assert.Equal("", string.Join(" | ", warnings.ToArray()));
            return mapped.Config;
        }

        private static LiveCardEntry Real(JsonValue template, int slot, string tier)
        {
            return new LiveCardEntry {
                TemplateId = template["Id"].AsString(), Template = template, Tier = tier, Socket = slot,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
        }

        private static LiveCardEntry Seed(string name, int slot, string tier, string enchantment = null)
        {
            JsonValue row = _seeds[name];
            JsonValue template = row["template"];
            LiveCardEntry entry = new LiveCardEntry {
                TemplateId = row["id"].AsString(), Template = template, Tier = tier, Socket = slot,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
            if (!string.IsNullOrEmpty(enchantment))
            {
                entry.Enchantment = enchantment;
                entry.EnchantmentDefinition = template["Enchantments"][enchantment];
                foreach (KeyValuePair<string, JsonValue> attr in entry.EnchantmentDefinition["Attributes"].AsObject())
                    entry.Attributes[attr.Key] = attr.Value.AsInt();
            }
            return entry;
        }

        private static LiveCardEntry Resolved(string name, int slot, string tier, string enchantment = null)
        {
            JsonValue row = _resolvedBlockers[name];
            JsonValue template = row["template"];
            LiveCardEntry entry = new LiveCardEntry {
                TemplateId = row["id"].AsString(), Template = template, Tier = tier, Socket = slot,
                Size = template["Size"].AsString(), AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, tier)
            };
            if (!string.IsNullOrEmpty(enchantment))
            {
                entry.Enchantment = enchantment;
                entry.EnchantmentDefinition = template["Enchantments"][enchantment];
                foreach (KeyValuePair<string, JsonValue> attr in entry.EnchantmentDefinition["Attributes"].AsObject())
                    entry.Attributes[attr.Key] = attr.Value.AsInt();
            }
            return entry;
        }

        private static LiveCardEntry FakeStats(string id, int slot, int cooldownMs, string tags, int critChance)
        {
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"Small\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CooldownMax\":" + cooldownMs +
                ",\"CritChance\":" + critChance + "},\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = slot, Size = "Small",
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
        }

        private static TriggerDef StartWith(params Effect[] effects)
        {
            TriggerDef trigger = new TriggerDef { Event = "atCombatStart" };
            foreach (Effect effect in effects) trigger.Effects.Add(effect);
            return trigger;
        }

        private static LiveCardEntry Fake(string id, int slot, int cooldownMs, string tags)
        {
            string json = "{\"$type\":\"TCardItem\",\"InternalName\":\"" + id + "\",\"Size\":\"Small\",\"Tags\":" + tags +
                ",\"HiddenTags\":[],\"Heroes\":[],\"Tiers\":{\"Bronze\":{\"Attributes\":{\"CooldownMax\":" + cooldownMs +
                "},\"AbilityIds\":[],\"AuraIds\":[],\"TooltipIds\":[]}},\"Abilities\":{},\"Auras\":{}}";
            JsonValue template = Json.Parse(json);
            return new LiveCardEntry {
                TemplateId = id, Template = template, Tier = "Bronze", Socket = slot, Size = "Small",
                AttributesAreLive = false,
                Attributes = MonsterTemplateExtractor.TierAttributes(template, "Bronze")
            };
        }

        private static JsonValue Kyver(string name)
        {
            foreach (JsonValue template in _kyver.AsArray())
                if (template["InternalName"].AsString() == name) return template;
            throw new ArgumentException("夹具缺少 " + name);
        }

        private static string IdOf(BattleConfig config, string baseItemId)
        {
            foreach (KeyValuePair<string, CardDef> pair in config.Cards)
                if (pair.Value.BaseItemId == baseItemId) return pair.Key;
            throw new ArgumentException("棋盘缺少 " + baseItemId);
        }

        private static void MoveToEnemy(BattleConfig config, string baseItemId)
        {
            string id = IdOf(config, baseItemId);
            LoadoutItem entry = config.Player.Items.Find(delegate(LoadoutItem item) { return item.CardId == id; });
            config.Player.Items.Remove(entry);
            entry.Slot = 0;
            config.Enemy.Items.Add(entry);
        }

        private static NumExpr Fixed(double value)
        {
            return new NumExpr { Type = "const", Value = value };
        }

        private static Effect Modify(string attribute, string operation, double value)
        {
            return new Effect {
                Kind = "modifyAttribute", Attribute = attribute, Operation = operation, Amount = Fixed(value),
                Seconds = 0, Count = 1, Target = new Target { Kind = "selfItem" }
            };
        }

        private static TriggerDef AtStart(Effect effect)
        {
            TriggerDef trigger = new TriggerDef { Event = "atCombatStart" };
            trigger.Effects.Add(effect);
            return trigger;
        }

        private static List<double> Activations(BattleResult result, BattleConfig config, string baseItemId)
        {
            string id = IdOf(config, baseItemId);
            return result.Events.Where(delegate(BattleEvent ev) { return ev.Kind == "activate" && ev.SourceCardId == id; })
                .Select(delegate(BattleEvent ev) { return ev.Time; }).ToList();
        }

        private static List<BattleEvent> ChargesTo(BattleResult result, BattleConfig config, string targetBaseId)
        {
            string targetId = IdOf(config, targetBaseId);
            return result.Events.FindAll(delegate(BattleEvent ev)
                { return ev.Kind == "charge" && ev.TargetItemId == targetId; });
        }
    }
}
