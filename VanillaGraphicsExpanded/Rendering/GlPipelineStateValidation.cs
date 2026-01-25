using System;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GlPipelineStateValidation
{
    public static void ValidateMaskBits(GlPipelineStateMask defaultMask, GlPipelineStateMask nonDefaultMask)
    {
        ulong validBits = GlPipelineStateMask.ValidBits;

        ulong unknownBits = (defaultMask.Bits | nonDefaultMask.Bits) & ~validBits;
        if (unknownBits != 0)
        {
            throw new ArgumentException($"Mask contains unknown bits: 0x{unknownBits:X} (validBits: 0x{validBits:X})");
        }

        if ((defaultMask.Bits & nonDefaultMask.Bits) != 0)
        {
            throw new ArgumentException("Invalid PSO intent: defaultMask and nonDefaultMask overlap.");
        }
    }

    public static void ValidateDesc(in GlPipelineDesc desc)
    {
        ValidateMaskBits(desc.DefaultMask, desc.NonDefaultMask);

        // Value payload presence: required when the corresponding knob bit is set in NonDefaultMask.
        if (desc.NonDefaultMask.Contains(GlPipelineStateId.DepthFunc) && desc.DepthFunc is null)
        {
            throw new ArgumentException("Missing DepthFunc value for DepthFunc bit.");
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.DepthWriteMask) && desc.DepthWriteMask is null)
        {
            throw new ArgumentException("Missing DepthWriteMask value for DepthWriteMask bit.");
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.BlendFunc) && desc.BlendFunc is null)
        {
            throw new ArgumentException("Missing BlendFunc value for BlendFunc bit.");
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.ColorMask) && desc.ColorMask is null)
        {
            throw new ArgumentException("Missing ColorMask value for ColorMask bit.");
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.LineWidth) && desc.LineWidth is null)
        {
            throw new ArgumentException("Missing LineWidth value for LineWidth bit.");
        }

        if (desc.NonDefaultMask.Contains(GlPipelineStateId.PointSize) && desc.PointSize is null)
        {
            throw new ArgumentException("Missing PointSize value for PointSize bit.");
        }

        // Indexed blend needs attachment indices even for baseline-default restores.
        bool hasBlendEnableIndexedIntent =
            desc.DefaultMask.Contains(GlPipelineStateId.BlendEnableIndexed)
            || desc.NonDefaultMask.Contains(GlPipelineStateId.BlendEnableIndexed);

        if (hasBlendEnableIndexedIntent)
        {
            if (desc.BlendEnableIndexedAttachments is null || desc.BlendEnableIndexedAttachments.Length == 0)
            {
                throw new ArgumentException("Missing BlendEnableIndexedAttachments for BlendEnableIndexed bit.");
            }

            ValidateUniqueSorted(desc.BlendEnableIndexedAttachments, "BlendEnableIndexedAttachments");
        }

        bool hasBlendFuncIndexedIntent =
            desc.DefaultMask.Contains(GlPipelineStateId.BlendFuncIndexed)
            || desc.NonDefaultMask.Contains(GlPipelineStateId.BlendFuncIndexed);

        if (hasBlendFuncIndexedIntent)
        {
            if (desc.BlendFuncIndexed is null || desc.BlendFuncIndexed.Length == 0)
            {
                throw new ArgumentException("Missing BlendFuncIndexed values for BlendFuncIndexed bit.");
            }

            ValidateUniqueSorted(desc.BlendFuncIndexed, "BlendFuncIndexed");
        }
    }

    private static void ValidateUniqueSorted(byte[] sorted, string name)
    {
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] == sorted[i - 1])
            {
                throw new ArgumentException($"{name} contains duplicate attachment index {sorted[i]}.");
            }
        }
    }

    private static void ValidateUniqueSorted(GlBlendFuncIndexed[] sorted, string name)
    {
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].AttachmentIndex == sorted[i - 1].AttachmentIndex)
            {
                throw new ArgumentException($"{name} contains duplicate attachment index {sorted[i].AttachmentIndex}.");
            }
        }
    }
}
