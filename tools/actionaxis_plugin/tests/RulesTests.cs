using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class RulesTests
    {
        public static void Run()
        {
            var defaults = Rules.Resolve(new RuleOverrides());
            Assert.Near(0.1, defaults.Dt);
            Assert.Near(60, defaults.MaxTime);
            Assert.Near(2, defaults.HasteMultiplier);
            Assert.Near(0.5, defaults.SlowMultiplier);
            Assert.Near(2, defaults.CritMultiplier);
            Assert.Equal(500, defaults.MaxEventsPerTick);
            Assert.Equal(20, defaults.MaxTriggerDepth);
            Assert.Equal(10, defaults.MaxSlots);

            var invalid = Rules.Resolve(new RuleOverrides
            {
                Dt = 0,
                MaxTime = -1,
                HasteMultiplier = 0
            });
            Assert.Near(0.1, invalid.Dt);
            Assert.Near(60, invalid.MaxTime);
            Assert.Near(2, invalid.HasteMultiplier);

            Assert.Near(3, JsMath.Round(2.5));
            Assert.Near(2, JsMath.Round(2.4999999999));
            Assert.Near(-2, JsMath.Round(-2.5));
        }
    }
}
