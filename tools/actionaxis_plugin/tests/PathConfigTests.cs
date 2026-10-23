using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Tests
{
    public static class PathConfigTests
    {
        public static void Run()
        {
            Assert.Equal(
                "C:/plugins/BazaarActionAxis",
                PathConfig.ResolveDataDir("C:/plugins", ""));
            Assert.Equal(
                "C:/plugins/BazaarActionAxis",
                PathConfig.ResolveDataDir("C:/plugins", "BazaarActionAxis"));
            Assert.Equal("D:/data", PathConfig.ResolveDataDir("C:/plugins", "D:/data"));
        }
    }
}
