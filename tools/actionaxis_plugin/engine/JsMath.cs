using System;

namespace BazaarActionAxis.Engine
{
    public static class JsMath
    {
        // JS Math.round: floor(x + 0.5), including negative halves.
        public static double Round(double value)
        {
            return Math.Floor(value + 0.5);
        }

        // JS Number((x).toFixed(6)); times are non-negative in the engine.
        public static double RoundTo6(double value)
        {
            return Math.Floor(value * 1000000.0 + 0.5) / 1000000.0;
        }

        public static double FloorEps(double value)
        {
            return Math.Floor(value + 1e-9);
        }
    }
}
