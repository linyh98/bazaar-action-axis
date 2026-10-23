using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Tests
{
    public static class Assert
    {
        public static int Failures;

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                Failures++;
                Console.WriteLine("  expected <" + Format(expected) + "> but was <" + Format(actual) + ">");
            }
        }

        public static void Near(double expected, double actual, double tolerance = 1e-9)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                Failures++;
                Console.WriteLine("  expected near <" + expected.ToString("R") + "> but was <" + actual.ToString("R") + ">");
            }
        }

        public static void True(bool condition)
        {
            if (!condition)
            {
                Failures++;
                Console.WriteLine("  expected true but was false");
            }
        }

        public static void False(bool condition)
        {
            True(!condition);
        }

        public static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception error)
            {
                Failures++;
                Console.WriteLine("  expected " + typeof(T).Name + " but got " + error.GetType().Name);
                return;
            }

            Failures++;
            Console.WriteLine("  expected " + typeof(T).Name + " but nothing was thrown");
        }

        private static string Format(object value)
        {
            return value == null ? "null" : value.ToString();
        }
    }

    public static class TestHarness
    {
        public static void Run(string name, Action test)
        {
            int before = Assert.Failures;
            try
            {
                test();
            }
            catch (Exception error)
            {
                Assert.Failures++;
                Console.WriteLine("  exception: " + error);
            }

            Console.WriteLine((Assert.Failures == before ? "PASS " : "FAIL ") + name);
        }

        public static int Finish()
        {
            if (Assert.Failures == 0)
            {
                Console.WriteLine("OK");
                return 0;
            }

            Console.WriteLine("FAILED (" + Assert.Failures + " assertion(s))");
            return 1;
        }
    }
}
