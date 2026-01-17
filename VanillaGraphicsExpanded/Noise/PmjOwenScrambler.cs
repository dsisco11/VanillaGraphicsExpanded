using System;

namespace VanillaGraphicsExpanded.Noise;

internal static class PmjOwenScrambler
{
    public static uint ScrambleBits(uint value, uint seed)
    {
        // Owen-style hierarchical bit scrambling:
        // for each bit, decide whether to flip it based on the already-emitted prefix.
        // This is deterministic and suitable for decorrelating temporal sequences.

        uint result = 0;
        uint prefix = 0;

        for (int bit = 31; bit >= 0; bit--)
        {
            uint inBit = (value >> bit) & 1u;
            prefix = (prefix << 1) | inBit;

            uint h = Squirrel3Noise.HashU(seed, prefix);
            uint outBit = inBit ^ (h & 1u);

            result = (result << 1) | outBit;
        }

        return result;
    }
}
