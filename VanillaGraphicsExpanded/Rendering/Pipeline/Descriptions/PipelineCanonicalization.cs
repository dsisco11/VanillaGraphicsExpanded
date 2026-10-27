using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Normalizes equivalent representations without erasing dormant authored intent.</summary>
internal static class PipelineCanonicalization
{
    #region Public API
    #region State normalization
    /// <summary>Normalizes stencil masks to the supported eight-bit representation without replacing face policy.</summary>
    public static DepthStencilDesc DepthStencil(DepthStencilDesc value) => value with
    {
        Front = Stencil(value.Front), Back = Stencil(value.Back)
    };

    /// <summary>Removes only equivalent all-ones mask suffixes, including while sampling controls are disabled.</summary>
    public static SamplingDesc Sampling(SamplingDesc value) => value with
    {
        Masks = Masks(value.Masks)
    };

    #endregion


    #endregion

    #region Private
    /// <summary>Removes only an all-ones suffix, whose values are supplied explicitly by GetMaskWord.</summary>
    private static PipelineValues<uint> Masks(PipelineValues<uint>? values)
    {
        int count = values?.Count ?? 0;
        while (count > 0 && values![count - 1] == uint.MaxValue) count--;
        return new(values is null ? [] : values.Take(count));
    }

    /// <summary>Resolves all-ones masks to the eight-bit stencil formats supported by target signatures.</summary>
    private static StencilFaceDesc Stencil(StencilFaceDesc value) => value with
    {
        ReadMask = value.ReadMask & 255, WriteMask = value.WriteMask & 255
    };
    #endregion
}
