using System;

namespace BazaarActionAxis.Engine
{
    // Deterministic mulberry32 port of src/engine/rng.ts.
    public sealed class Rng
    {
        private uint _state;

        public Rng(double seed)
        {
            _state = unchecked((uint)(long)seed);
        }

        public double Next()
        {
            unchecked
            {
                _state += 0x6d2b79f5u;
                uint t = (_state ^ (_state >> 15)) * (1u | _state);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public int Int(int maxExclusive)
        {
            return (int)Math.Floor(Next() * maxExclusive);
        }

        public bool Chance(double probability)
        {
            if (probability <= 0) return false;
            if (probability >= 1) return true;
            return Next() < probability;
        }
    }
}
