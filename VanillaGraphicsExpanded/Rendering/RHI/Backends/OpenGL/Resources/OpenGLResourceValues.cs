using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Lowers logical resource selectors to native enums only inside backend execution.</summary>
internal static class OpenGLResourceValues
{
    #region Internal API
    /// <summary>Lowers a logical FramebufferSlot selector to its native representation.</summary>
    internal static FramebufferAttachment Native(FramebufferSlot value) => value switch
    {
        FramebufferSlot.Color0 => FramebufferAttachment.ColorAttachment0,
        FramebufferSlot.Color1 => FramebufferAttachment.ColorAttachment1,
        FramebufferSlot.Color2 => FramebufferAttachment.ColorAttachment2,
        FramebufferSlot.Color3 => FramebufferAttachment.ColorAttachment3,
        FramebufferSlot.Color4 => FramebufferAttachment.ColorAttachment4,
        FramebufferSlot.Color5 => FramebufferAttachment.ColorAttachment5,
        FramebufferSlot.Color6 => FramebufferAttachment.ColorAttachment6,
        FramebufferSlot.Color7 => FramebufferAttachment.ColorAttachment7,
        FramebufferSlot.Color8 => FramebufferAttachment.ColorAttachment8,
        FramebufferSlot.Color9 => FramebufferAttachment.ColorAttachment9,
        FramebufferSlot.Color10 => FramebufferAttachment.ColorAttachment10,
        FramebufferSlot.Color11 => FramebufferAttachment.ColorAttachment11,
        FramebufferSlot.Color12 => FramebufferAttachment.ColorAttachment12,
        FramebufferSlot.Color13 => FramebufferAttachment.ColorAttachment13,
        FramebufferSlot.Color14 => FramebufferAttachment.ColorAttachment14,
        FramebufferSlot.Color15 => FramebufferAttachment.ColorAttachment15,
        FramebufferSlot.Color16 => FramebufferAttachment.ColorAttachment16,
        FramebufferSlot.Color17 => FramebufferAttachment.ColorAttachment17,
        FramebufferSlot.Color18 => FramebufferAttachment.ColorAttachment18,
        FramebufferSlot.Color19 => FramebufferAttachment.ColorAttachment19,
        FramebufferSlot.Color20 => FramebufferAttachment.ColorAttachment20,
        FramebufferSlot.Color21 => FramebufferAttachment.ColorAttachment21,
        FramebufferSlot.Color22 => FramebufferAttachment.ColorAttachment22,
        FramebufferSlot.Color23 => FramebufferAttachment.ColorAttachment23,
        FramebufferSlot.Color24 => FramebufferAttachment.ColorAttachment24,
        FramebufferSlot.Color25 => FramebufferAttachment.ColorAttachment25,
        FramebufferSlot.Color26 => FramebufferAttachment.ColorAttachment26,
        FramebufferSlot.Color27 => FramebufferAttachment.ColorAttachment27,
        FramebufferSlot.Color28 => FramebufferAttachment.ColorAttachment28,
        FramebufferSlot.Color29 => FramebufferAttachment.ColorAttachment29,
        FramebufferSlot.Color30 => FramebufferAttachment.ColorAttachment30,
        FramebufferSlot.Color31 => FramebufferAttachment.ColorAttachment31,
        FramebufferSlot.Depth => FramebufferAttachment.DepthAttachment,
        FramebufferSlot.Stencil => FramebufferAttachment.StencilAttachment,
        FramebufferSlot.DepthStencil => FramebufferAttachment.DepthStencilAttachment,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Lowers a logical FramebufferSelection selector to its native representation.</summary>
    internal static FramebufferTarget Native(FramebufferSelection value) => value switch
    {
        FramebufferSelection.Read => FramebufferTarget.ReadFramebuffer,
        FramebufferSelection.Draw => FramebufferTarget.DrawFramebuffer,
        FramebufferSelection.ReadAndDraw => FramebufferTarget.Framebuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Lowers a logical FramebufferMetadataField selector to its native representation.</summary>
    internal static FramebufferDefaultParameter Native(FramebufferMetadataField value) => value switch
    {
        FramebufferMetadataField.Width => FramebufferDefaultParameter.FramebufferDefaultWidth,
        FramebufferMetadataField.Height => FramebufferDefaultParameter.FramebufferDefaultHeight,
        FramebufferMetadataField.Layers => FramebufferDefaultParameter.FramebufferDefaultLayers,
        FramebufferMetadataField.Samples => FramebufferDefaultParameter.FramebufferDefaultSamples,
        FramebufferMetadataField.FixedSampleLocations => FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Lowers a logical ProgramBlockKind selector to its native representation.</summary>
    internal static ProgramInterface Native(ProgramBlockKind value) => value switch
    {
        ProgramBlockKind.Uniform => ProgramInterface.UniformBlock,
        ProgramBlockKind.Storage => ProgramInterface.ShaderStorageBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Translates a native FramebufferAttachment at the explicit integration boundary.</summary>
    internal static FramebufferSlot FromNative(FramebufferAttachment value) => value switch
    {
        FramebufferAttachment.ColorAttachment0 => FramebufferSlot.Color0,
        FramebufferAttachment.ColorAttachment1 => FramebufferSlot.Color1,
        FramebufferAttachment.ColorAttachment2 => FramebufferSlot.Color2,
        FramebufferAttachment.ColorAttachment3 => FramebufferSlot.Color3,
        FramebufferAttachment.ColorAttachment4 => FramebufferSlot.Color4,
        FramebufferAttachment.ColorAttachment5 => FramebufferSlot.Color5,
        FramebufferAttachment.ColorAttachment6 => FramebufferSlot.Color6,
        FramebufferAttachment.ColorAttachment7 => FramebufferSlot.Color7,
        FramebufferAttachment.ColorAttachment8 => FramebufferSlot.Color8,
        FramebufferAttachment.ColorAttachment9 => FramebufferSlot.Color9,
        FramebufferAttachment.ColorAttachment10 => FramebufferSlot.Color10,
        FramebufferAttachment.ColorAttachment11 => FramebufferSlot.Color11,
        FramebufferAttachment.ColorAttachment12 => FramebufferSlot.Color12,
        FramebufferAttachment.ColorAttachment13 => FramebufferSlot.Color13,
        FramebufferAttachment.ColorAttachment14 => FramebufferSlot.Color14,
        FramebufferAttachment.ColorAttachment15 => FramebufferSlot.Color15,
        FramebufferAttachment.ColorAttachment16 => FramebufferSlot.Color16,
        FramebufferAttachment.ColorAttachment17 => FramebufferSlot.Color17,
        FramebufferAttachment.ColorAttachment18 => FramebufferSlot.Color18,
        FramebufferAttachment.ColorAttachment19 => FramebufferSlot.Color19,
        FramebufferAttachment.ColorAttachment20 => FramebufferSlot.Color20,
        FramebufferAttachment.ColorAttachment21 => FramebufferSlot.Color21,
        FramebufferAttachment.ColorAttachment22 => FramebufferSlot.Color22,
        FramebufferAttachment.ColorAttachment23 => FramebufferSlot.Color23,
        FramebufferAttachment.ColorAttachment24 => FramebufferSlot.Color24,
        FramebufferAttachment.ColorAttachment25 => FramebufferSlot.Color25,
        FramebufferAttachment.ColorAttachment26 => FramebufferSlot.Color26,
        FramebufferAttachment.ColorAttachment27 => FramebufferSlot.Color27,
        FramebufferAttachment.ColorAttachment28 => FramebufferSlot.Color28,
        FramebufferAttachment.ColorAttachment29 => FramebufferSlot.Color29,
        FramebufferAttachment.ColorAttachment30 => FramebufferSlot.Color30,
        FramebufferAttachment.ColorAttachment31 => FramebufferSlot.Color31,
        FramebufferAttachment.DepthAttachment => FramebufferSlot.Depth,
        FramebufferAttachment.StencilAttachment => FramebufferSlot.Stencil,
        FramebufferAttachment.DepthStencilAttachment => FramebufferSlot.DepthStencil,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Translates a native FramebufferTarget at the explicit integration boundary.</summary>
    internal static FramebufferSelection FromNative(FramebufferTarget value) => value switch
    {
        FramebufferTarget.ReadFramebuffer => FramebufferSelection.Read,
        FramebufferTarget.DrawFramebuffer => FramebufferSelection.Draw,
        FramebufferTarget.Framebuffer => FramebufferSelection.ReadAndDraw,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Translates a native FramebufferDefaultParameter at the explicit integration boundary.</summary>
    internal static FramebufferMetadataField FromNative(FramebufferDefaultParameter value) => value switch
    {
        FramebufferDefaultParameter.FramebufferDefaultWidth => FramebufferMetadataField.Width,
        FramebufferDefaultParameter.FramebufferDefaultHeight => FramebufferMetadataField.Height,
        FramebufferDefaultParameter.FramebufferDefaultLayers => FramebufferMetadataField.Layers,
        FramebufferDefaultParameter.FramebufferDefaultSamples => FramebufferMetadataField.Samples,
        FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations => FramebufferMetadataField.FixedSampleLocations,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    /// <summary>Translates a native ProgramInterface at the explicit integration boundary.</summary>
    internal static ProgramBlockKind FromNative(ProgramInterface value) => value switch
    {
        ProgramInterface.UniformBlock => ProgramBlockKind.Uniform,
        ProgramInterface.ShaderStorageBlock => ProgramBlockKind.Storage,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    #endregion
}
