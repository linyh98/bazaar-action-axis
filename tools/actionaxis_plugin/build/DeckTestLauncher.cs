using System;
using BazaarActionAxis.Tests;
public static class DeckTestLauncher { public static int Main() { NativeItemMapperTests.Run(); Console.WriteLine("Native item mapper test failures: " + Assert.Failures); return Assert.Failures; } }
