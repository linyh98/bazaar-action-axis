using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class RngTests
    {
        public static void Run()
        {
            var r = new Rng(1);
            Assert.Near(0.627073940588, r.Next(), 1e-12);
            Assert.Near(0.002735721180, r.Next(), 1e-12);
            Assert.Near(0.527447039960, r.Next(), 1e-12);
            Assert.Near(0.981050967472, r.Next(), 1e-12);
            Assert.Near(0.968377898214, r.Next(), 1e-12);

            var r2 = new Rng(1);
            Assert.Equal(6, r2.Int(10));
            Assert.Equal(0, r2.Int(10));
            Assert.Equal(5, r2.Int(10));
            Assert.Equal(9, r2.Int(10));

            Assert.False(new Rng(1).Chance(0));
            Assert.True(new Rng(1).Chance(1));
        }
    }
}
