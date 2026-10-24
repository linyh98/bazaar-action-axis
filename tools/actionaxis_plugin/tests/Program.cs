using System;

namespace BazaarActionAxis.Tests
{
    public static class Program
    {
        public static int Main()
        {
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
            return TestHarness.Finish();
        }
    }
}
