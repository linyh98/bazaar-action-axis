using System;
using System.IO;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class IconCacheTests
    {
        public static void Run()
        {
            Assert.Equal("C:/icons/abc.png", IconCachePaths.PathFor("C:/icons", "abc"));
            Assert.Equal("C:/icons/_unknown.png", IconCachePaths.PathFor("C:/icons", ""));
            Assert.Equal("C:/icons/a_b_c.png", IconCachePaths.PathFor("C:/icons", "a/b c"));
            Assert.Equal("C:/icons/abc.png", IconCachePaths.PathFor("C:/icons", "abc", "None"));
            Assert.Equal("C:/icons/abc.png", IconCachePaths.PathFor("C:/icons", "abc", "unenchanted"));
            Assert.Equal("C:/icons/abc--enchant-toxic.png", IconCachePaths.PathFor("C:/icons", "abc", " Toxic "));
            Assert.Equal(IconCachePaths.KeyFor("ABC", "Toxic"), IconCachePaths.KeyFor("abc", "toxic"));
            Assert.False(IconCachePaths.PathFor("C:/icons", "abc", "Toxic") ==
                IconCachePaths.PathFor("C:/icons", "abc", "Fiery"));
            Assert.False(IconCachePaths.PathFor("C:/icons", "abc", "a/b") ==
                IconCachePaths.PathFor("C:/icons", "abc", "a b"));
            StorePreservesExistingArt();
            Assert.Equal("bronze", ThemeKeys.TierColorKey("Bronze"));
            Assert.Equal("legendary", ThemeKeys.TierColorKey("Legendary"));
            Assert.Equal("bronze", ThemeKeys.TierColorKey("Unknown"));
        }

        private static void StorePreservesExistingArt()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ActionAxisIconTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                string original = IconCachePaths.PathFor(directory, "abc");
                string enchanted = IconCachePaths.PathFor(directory, "abc", "Toxic");
                Assert.False(IconCacheFiles.TryStore(original, new byte[0]));
                Assert.False(File.Exists(original));
                Assert.True(IconCacheFiles.TryStore(original, new byte[] { 1, 2, 3 }));
                Assert.False(IconCacheFiles.TryStore(original, new byte[] { 9, 9 }));
                Assert.Equal(3, File.ReadAllBytes(original).Length);
                Assert.Equal((byte)1, File.ReadAllBytes(original)[0]);
                Assert.True(IconCacheFiles.TryStore(enchanted, new byte[] { 4, 5 }));
                Assert.Equal((byte)4, File.ReadAllBytes(enchanted)[0]);
                Assert.Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                // Only this uniquely created temporary test folder is removed.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
