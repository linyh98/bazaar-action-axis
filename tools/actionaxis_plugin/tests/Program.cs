using System;

namespace BazaarActionAxis.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--coverage") return NativeCoverageRunner.Run(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--monsters")
                return MonsterCoverageRunner.Run(args[1], args[2], args[3]);
            if (args.Length == 5 && args[0] == "--ledger")
                return NativeReplayLedgerExport.Run(args[1], args[2], args[3], args[4]);
            if (args.Length == 5 && args[0] == "--ledger-batch")
                return NativeReplayLedgerExport.RunBatch(args[1], args[2], args[3], args[4]);
            if (args.Length > 0) throw new ArgumentException("用法：EngineTests.exe [--coverage 输入.jsonl 输出.tsv | --monsters 怪物.jsonl 卡牌.jsonl 输出.tsv | --ledger fixture templates catalog out | --ledger-batch fixture_dir templates catalog out_dir]");
            TestHarness.Run("Json", JsonTests.Run);
            TestHarness.Run("Rng", RngTests.Run);
            TestHarness.Run("Rules", RulesTests.Run);
            TestHarness.Run("Catalog", CatalogTests.Run);
            TestHarness.Run("Parity", ParityTests.Run);
            TestHarness.Run("Aggregate", AggregateTests.Run);
            TestHarness.Run("PanelModel", PanelModelTests.Run);
            TestHarness.Run("Reflection", ReflectionTests.Run);
            TestHarness.Run("IconCache", IconCacheTests.Run);
            TestHarness.Run("PathConfig", PathConfigTests.Run);
            TestHarness.Run("CardMapper", CardMapperTests.Run);
            TestHarness.Run("Skills", SkillsTests.Run);
            TestHarness.Run("LiveSkillReader", LiveSkillReaderTests.Run);
            TestHarness.Run("LiveMonsterReader", LiveMonsterReaderTests.Run);
            TestHarness.Run("DummyDamage", DummyDamageTests.Run);
            TestHarness.Run("NativeMechanics", NativeMechanicsTests.Run);
            TestHarness.Run("NativeCombatReplay", NativeCombatReplayTests.Run);
            TestHarness.Run("DockButtonLayout", DockButtonLayoutTests.Run);
            TestHarness.Run("MonsterOpponent", MonsterOpponentTests.Run);
            TestHarness.Run("NativeRuleExpansion", NativeRuleExpansionTests.Run);
            TestHarness.Run("NativeConditionTarget", NativeConditionTargetTests.Run);
            TestHarness.Run("NativeSkillTier", NativeSkillTierTests.Run);
            TestHarness.Run("NativeLegendaryTier", NativeLegendaryTierTests.Run);
            TestHarness.Run("NativeCanCrit", NativeCanCritTests.Run);
            TestHarness.Run("NativeSkillDynamicAura", NativeSkillDynamicAuraTests.Run);
            TestHarness.Run("DynamicAura", DynamicAuraTests.Run);
            TestHarness.Run("NativeRuntimeRules", NativeRuntimeRulesTests.Run);
            TestHarness.Run("Transform", TransformTests.Run);
            TestHarness.Run("G08Transform", G08TransformTests.Run);
            TestHarness.Run("G08PoolTransform", G08PoolTransformTests.Run);
            TestHarness.Run("G09Lifecycle", G09LifecycleTests.Run);
            TestHarness.Run("AuditRegression", AuditRegressionTests.Run);
            TestHarness.Run("G00Data", G00DataTests.Run);
            TestHarness.Run("G00Coverage", G00CoverageTests.Run);
            TestHarness.Run("G02StatusTiming", G02StatusTimingTests.Run);
            TestHarness.Run("G06RageState", G06RageStateTests.Run);
            TestHarness.Run("G06FullCoverage", G06FullCoverageTests.Run);
            TestHarness.Run("G03TriggerRules", G03TriggerRuleTests.Run);
            TestHarness.Run("G03FullCoverage", G03FullCoverageTests.Run);
            TestHarness.Run("G05DynamicAura", G05DynamicAuraTests.Run);
            TestHarness.Run("G05FullAura", G05FullAuraTests.Run);
            TestHarness.Run("G10DamageLedger", G10DamageLedgerTests.Run);
            TestHarness.Run("G10TaskIndex", G10TaskIndexTests.Run);
            TestHarness.Run("PositionalRules", PositionalRuleTests.Run);
            TestHarness.Run("AllHandsRules", AllHandsRuleTests.Run);
            TestHarness.Run("G07Enchantment", G07EnchantmentTests.Run);
            TestHarness.Run("G01TargetConsistency", G01TargetConsistencyTests.Run);
            TestHarness.Run("G01SocketEffect", G01SocketEffectTests.Run);
            TestHarness.Run("G01FullRules", G01FullRulesTests.Run);
            TestHarness.Run("G11Sandstorm", G11SandstormTests.Run);
            TestHarness.Run("G11Coverage", G11CoverageTests.Run);
            TestHarness.Run("G12UiHistory", G12UiHistoryTests.Run);
            TestHarness.Run("G12OpeningCooldown", G12OpeningCooldownTests.Run);
            TestHarness.Run("G12Determinism", G12DeterminismTests.Run);
            TestHarness.Run("ReplayBatchRegression", ReplayBatchRegressionTests.Run);
            TestHarness.Run("Replay4HealingRegression", Replay4HealingRegressionTests.Run);
            TestHarness.Run("ReplayFirstBattleRegression", ReplayFirstBattleRegressionTests.Run);
            TestHarness.Run("ReplaySecondBattleRegression", ReplaySecondBattleRegressionTests.Run);
            TestHarness.Run("ReplayThirdBattleRegression", ReplayThirdBattleRegressionTests.Run);
            TestHarness.Run("Replay3FirstBattle", Replay3FirstBattleTests.Run);
            TestHarness.Run("ReplayFourthCastQueue", ReplayFourthCastQueueTests.Run);
            TestHarness.Run("ReplayFourthAttributeTiming", ReplayFourthAttributeTimingTests.Run);
            TestHarness.Run("ReplayFourthTerminalAttribute", ReplayFourthTerminalAttributeTests.Run);
            TestHarness.Run("ReplayFourthStateExport", ReplayFourthStateExportTests.Run);
            TestHarness.Run("ReplayFirstCr1Priority", ReplayFirstCr1PriorityTests.Run);
            TestHarness.Run("ReplayFirstCr2Scheduling", ReplayFirstCr2SchedulingTests.Run);
            TestHarness.Run("Replay4ReloadRegression", Replay4ReloadRegressionTests.Run);
            TestHarness.Run("Replay4MappingRegression", Replay4MappingRegressionTests.Run);
            TestHarness.Run("Replay4Timing", Replay4TimingTests.Run);
            TestHarness.Run("Replay4AttributeRegression", Replay4AttributeRegressionTests.Run);
            TestHarness.Run("Replay4CriticalRegression", Replay4CriticalRegressionTests.Run);
            TestHarness.Run("Replay4RepairRegression", Replay4RepairRegressionTests.Run);
            TestHarness.Run("Replay4SandstormOrder", Replay4SandstormOrderTests.Run);
            TestHarness.Run("Replay4TerminalState", Replay4TerminalStateTests.Run);
            TestHarness.Run("Replay4OpeningExport", Replay4OpeningExportTests.Run);
            TestHarness.Run("Replay4ImmediateAction", Replay4ImmediateActionTests.Run);
            TestHarness.Run("Replay4OpeningStatus", Replay4OpeningStatusTests.Run);
            TestHarness.Run("Replay4EnchantmentTag", Replay4EnchantmentTagTests.Run);
            TestHarness.Run("Replay4HeroAuraSnapshot", Replay4HeroAuraSnapshotTests.Run);
            TestHarness.Run("ReplayReloadReadyCharge", ReplayReloadReadyChargeTests.Run);
            TestHarness.Run("ReplayImmediateEligibility", ReplayImmediateEligibilityTests.Run);
            TestHarness.Run("ReplayLowPriorityFrame", ReplayLowPriorityFrameTests.Run);
            TestHarness.Run("ReplayPeriodicFrameSettlement", ReplayPeriodicFrameSettlementTests.Run);
            TestHarness.Run("ReplayRegenCritical", ReplayRegenCriticalTests.Run);
            TestHarness.Run("ReplayTerminalQueuedAttribute", ReplayTerminalQueuedAttributeTests.Run);
            TestHarness.Run("ReplayLethalCriticalCounter", ReplayLethalCriticalCounterTests.Run);
            TestHarness.Run("ReplayLethalLowPhase", ReplayLethalLowPhaseTests.Run);
            TestHarness.Run("ReplayNativeWarningResolution", ReplayNativeWarningResolutionTests.Run);
            TestHarness.Run("ReplayHasteObserverOrder", ReplayHasteObserverOrderTests.Run);
            TestHarness.Run("ReplayCr2VirusTransform", ReplayCr2VirusTransformTests.Run);
            TestHarness.Run("ReplayRandom", ReplayRandomTests.Run);
            TestHarness.Run("ValidationReplay", ValidationReplayTests.Run);
            return TestHarness.Finish();
        }
    }
}
