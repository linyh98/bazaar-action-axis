using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class CatalogTests
    {
        public static void Run()
        {
            var catalog = CardCatalog.Load("src/data/cards.json");
            Assert.Equal(3620, catalog.Cards.Count);

            var exact = catalog.Find("c984adcc-6a42-4b0b-a964-fd64fd2fdaa7", "Gold");
            Assert.True(exact != null);
            Assert.Equal("c984adcc-6a42-4b0b-a964-fd64fd2fdaa7@Gold", exact.Id);
            Assert.Near(7.0, exact.Cooldown);

            var fallback = catalog.Find("c984adcc-6a42-4b0b-a964-fd64fd2fdaa7", "NonExistentTier");
            Assert.True(fallback != null);
            Assert.Equal("Bronze", fallback.Tier);

            Assert.True(catalog.Find("", "Gold") == null);
            Assert.True(catalog.Find("does-not-exist", "Gold") == null);
            Assert.Equal(
                exact.Id,
                catalog.Find("C984ADCC-6A42-4B0B-A964-FD64FD2FDAA7", "Gold").Id);

            var over = exact.WithLiveOverrides(9.0, 3, 2);
            Assert.Near(9.0, over.Cooldown);
            Assert.Equal(3, over.AmmoMax.Value);
            Assert.Equal(2, over.MulticastBonus);
            Assert.Near(7.0, exact.Cooldown);

            var noOverrides = exact.WithLiveOverrides(null, null, null);
            Assert.Near(7.0, noOverrides.Cooldown);
            Assert.True(noOverrides.AmmoMax == exact.AmmoMax);
        }
    }
}
