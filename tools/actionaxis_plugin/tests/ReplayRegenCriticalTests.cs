using System.Collections.Generic;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class ReplayRegenCriticalTests
    {
        public static void Run()
        {
            RecordedRegenCriticalChangesAppliedStacks();
            GuaranteedRegenCriticalWorksWithoutReplay();
            SectionTargetLimitsReadLiveCounts();
            UnlimitedSectionModificationStillReachesEveryItem();
        }

        private static BattleContext Board(ReplayRandomTape tape = null)
        {
            BattleConfig config = new BattleConfig { NativeMechanics = true,
                PlayerMaxHp = 1000, EnemyMaxHp = 1000, ReplayRandom = tape };
            CardDef source = new CardDef { Id = "source", Passive = true };
            config.Cards[source.Id] = source;
            config.Player.Items.Add(new LoadoutItem { CardId = source.Id, Slot = 9 });
            foreach (string id in new[] { "first-ammo", "second-ammo" })
            {
                CardDef card = new CardDef { Id = id, Cooldown = 5, AmmoMax = 3 };
                config.Cards[id] = card;
                config.Player.Items.Add(new LoadoutItem { CardId = id, Slot = id == "first-ammo" ? 0 : 2 });
            }
            return BattleEngine.InitBattle(config);
        }

        private static InternalItem Source(BattleContext ctx)
        {
            return ctx.Items.Find(delegate(InternalItem item) { return item.Id == "source"; });
        }

        private static void RecordedRegenCriticalChangesAppliedStacks()
        {
            foreach (bool critical in new[] { false, true })
            {
                ReplayRandomTape tape = new ReplayRandomTape();
                tape.Criticals.Add(new ReplayRandomChoice { Frame = 131, Source = "source",
                    Ability = "0", Kind = "regen", TargetSide = "player", Critical = critical });
                BattleContext ctx = Board(tape);
                InternalItem source = Source(ctx);
                source.CritBonus = 0.08;
                ctx.Player.Regen = 36;
                Effects.ResolveEffect(ctx, new Effect { Kind = "regen", NativeAbilityId = "0",
                    Amount = new NumExpr { Value = 4 }, Target = new Target { Kind = "selfHero" } },
                    source, 6.55, 0, null);
                Assert.Near(critical ? 44 : 40, ctx.Player.Regen);
                Assert.Equal(1, ctx.ReplayRandom.Audit.CriticalApplied);
                Assert.Equal(0, ctx.ReplayRandom.Audit.CriticalFallbacks);
                Assert.Equal(critical ? 1 : 0, ctx.Events.FindAll(
                    delegate(BattleEvent ev) { return ev.Kind == "crit"; }).Count);
            }
        }

        private static void GuaranteedRegenCriticalWorksWithoutReplay()
        {
            BattleContext ctx = Board();
            InternalItem source = Source(ctx);
            source.CritBonus = 1;
            Effects.ResolveEffect(ctx, new Effect { Kind = "regen", Amount = new NumExpr { Value = 7 },
                Target = new Target { Kind = "selfHero" } }, source, 1, 0, null);
            Assert.Near(14, ctx.Player.Regen);
            Assert.Equal(1, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "crit"; }).Count);
        }

        private const string AmmoSection = @"{""$type"":""TTargetCardSection"",""TargetSection"":""SelfHand"",
            ""ExcludeSelf"":false,""Conditions"":{""$type"":""TCardConditionalAttribute"",""Attribute"":""AmmoMax"",
            ""ComparisonOperator"":""GreaterThan"",""ComparisonValue"":{""$type"":""TFixedValue"",""Value"":0}}}";

        private static void SectionTargetLimitsReadLiveCounts()
        {
            foreach (string kind in new[] { "reload", "charge" })
            {
                string attribute = kind == "reload" ? "ReloadTargets" : "ChargeTargets";
                Dictionary<string, double> attrs = new Dictionary<string, double> {
                    { attribute, 1 }, { "ChargeAmount", 1000 } };
                string action = kind == "reload" ? "TActionCardReload" : "TActionCardCharge";
                Effect effect = SkillMapper.MapAction(Json.Parse("{\"$type\":\"" + action +
                    "\",\"TargetCount\":null,\"Value\":null,\"Target\":" + AmmoSection + "}"), attrs);
                BattleContext ctx = Board();
                InternalItem source = Source(ctx);
                source.Card.LiveAttributes[attribute] = 1;
                source.Card.LiveAttributes["ChargeAmount"] = 1000;
                foreach (InternalItem item in ctx.Items) if (item.AmmoMax.HasValue) item.Ammo = 0;
                Effects.ResolveEffect(ctx, effect, source, 1, 0, null);
                List<BattleEvent> first = ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == kind; });
                Assert.Equal(1, first.Count);
                Assert.Equal("first-ammo", first[0].TargetItemId);

                // The count remains a runtime attribute reference after mapping.
                source.Card.LiveAttributes[attribute] = 2;
                ctx.Events.Clear();
                foreach (InternalItem item in ctx.Items)
                {
                    if (item.AmmoMax.HasValue) item.Ammo = 0;
                    item.Cooldown = 5;
                }
                Effects.ResolveEffect(ctx, effect, source, 1.5, 0, null);
                Assert.Equal(2, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == kind; }).Count);
            }
        }

        private static void UnlimitedSectionModificationStillReachesEveryItem()
        {
            Effect effect = SkillMapper.MapAction(Json.Parse(@"{""$type"":""TActionCardModifyAttribute"",
                ""AttributeType"":""DamageAmount"",""Operation"":""Add"",""Value"":{""$type"":""TFixedValue"",""Value"":3},
                ""TargetCount"":null,""Target"":" + AmmoSection + "}"), new Dictionary<string, double>());
            BattleContext ctx = Board();
            Effects.ResolveEffect(ctx, effect, Source(ctx), 1, 0, null);
            Assert.Equal(2, ctx.Events.FindAll(delegate(BattleEvent ev) { return ev.Kind == "modifyAttribute"; }).Count);
        }
    }
}
