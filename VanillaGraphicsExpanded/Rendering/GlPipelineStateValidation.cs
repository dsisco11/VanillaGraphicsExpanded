using System;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GlPipelineStateValidation
{
    public static void ValidateMasks(
        GlPipelineStateMask defaultMask,
        GlPipelineStateMask nonDefaultMask,
        GlPipelineStateMask valuesPresentMask)
    {
        ulong validBits = GlPipelineStateMask.ValidBits;

        ulong unknownBits = (defaultMask.Bits | nonDefaultMask.Bits | valuesPresentMask.Bits) & ~validBits;
        if (unknownBits != 0)
        {
            throw new ArgumentException($"Mask contains unknown bits: 0x{unknownBits:X} (validBits: 0x{validBits:X})");
        }

        if ((defaultMask.Bits & nonDefaultMask.Bits) != 0)
        {
            throw new ArgumentException("Invalid PSO intent: defaultMask and nonDefaultMask overlap.");
        }

        if ((nonDefaultMask.Bits & ~valuesPresentMask.Bits) != 0)
        {
            throw new ArgumentException("Invalid PSO intent: nonDefaultMask includes states without corresponding values.");
        }
    }
}

