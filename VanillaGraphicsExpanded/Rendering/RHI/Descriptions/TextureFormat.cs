namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Names image representations independently of native API token values.</summary>
public enum TextureFormat
{
    Undefined,
    /// <summary>Selects CompressedRed storage.</summary>
    CompressedRed,
    /// <summary>Selects CompressedRedRgtc1 storage.</summary>
    CompressedRedRgtc1,
    /// <summary>Selects CompressedRg storage.</summary>
    CompressedRg,
    /// <summary>Selects CompressedRgRgtc2 storage.</summary>
    CompressedRgRgtc2,
    /// <summary>Selects CompressedRgb storage.</summary>
    CompressedRgb,
    /// <summary>Selects CompressedRgbBptcSignedFloat storage.</summary>
    CompressedRgbBptcSignedFloat,
    /// <summary>Selects CompressedRgbBptcUnsignedFloat storage.</summary>
    CompressedRgbBptcUnsignedFloat,
    /// <summary>Selects CompressedRgbS3tcDxt1Ext storage.</summary>
    CompressedRgbS3tcDxt1Ext,
    /// <summary>Selects CompressedRgba storage.</summary>
    CompressedRgba,
    /// <summary>Selects CompressedRgbaBptcUnorm storage.</summary>
    CompressedRgbaBptcUnorm,
    /// <summary>Selects CompressedRgbaS3tcDxt1Ext storage.</summary>
    CompressedRgbaS3tcDxt1Ext,
    /// <summary>Selects CompressedRgbaS3tcDxt3Ext storage.</summary>
    CompressedRgbaS3tcDxt3Ext,
    /// <summary>Selects CompressedRgbaS3tcDxt5Ext storage.</summary>
    CompressedRgbaS3tcDxt5Ext,
    /// <summary>Selects CompressedSignedRedRgtc1 storage.</summary>
    CompressedSignedRedRgtc1,
    /// <summary>Selects CompressedSignedRgRgtc2 storage.</summary>
    CompressedSignedRgRgtc2,
    /// <summary>Selects CompressedSrgb storage.</summary>
    CompressedSrgb,
    /// <summary>Selects CompressedSrgbAlpha storage.</summary>
    CompressedSrgbAlpha,
    /// <summary>Selects CompressedSrgbAlphaBptcUnorm storage.</summary>
    CompressedSrgbAlphaBptcUnorm,
    /// <summary>Selects CompressedSrgbAlphaS3tcDxt1Ext storage.</summary>
    CompressedSrgbAlphaS3tcDxt1Ext,
    /// <summary>Selects CompressedSrgbAlphaS3tcDxt3Ext storage.</summary>
    CompressedSrgbAlphaS3tcDxt3Ext,
    /// <summary>Selects CompressedSrgbAlphaS3tcDxt5Ext storage.</summary>
    CompressedSrgbAlphaS3tcDxt5Ext,
    /// <summary>Selects CompressedSrgbS3tcDxt1Ext storage.</summary>
    CompressedSrgbS3tcDxt1Ext,
    /// <summary>Selects Depth24Stencil8 storage.</summary>
    Depth24Stencil8,
    /// <summary>Selects Depth32fStencil8 storage.</summary>
    Depth32fStencil8,
    /// <summary>Selects DepthComponent storage.</summary>
    DepthComponent,
    /// <summary>Selects DepthComponent16 storage.</summary>
    DepthComponent16,
    /// <summary>Selects DepthComponent24 storage.</summary>
    DepthComponent24,
    /// <summary>Selects DepthComponent32 storage.</summary>
    DepthComponent32,
    /// <summary>Selects DepthComponent32f storage.</summary>
    DepthComponent32f,
    /// <summary>Selects DepthStencil storage.</summary>
    DepthStencil,
    /// <summary>Selects R11fG11fB10f storage.</summary>
    R11fG11fB10f,
    /// <summary>Selects R16 storage.</summary>
    R16,
    /// <summary>Selects R16Snorm storage.</summary>
    R16Snorm,
    /// <summary>Selects R16f storage.</summary>
    R16f,
    /// <summary>Selects R16i storage.</summary>
    R16i,
    /// <summary>Selects R16ui storage.</summary>
    R16ui,
    /// <summary>Selects R32f storage.</summary>
    R32f,
    /// <summary>Selects R32i storage.</summary>
    R32i,
    /// <summary>Selects R32ui storage.</summary>
    R32ui,
    /// <summary>Selects R8 storage.</summary>
    R8,
    /// <summary>Selects R8Snorm storage.</summary>
    R8Snorm,
    /// <summary>Selects R8i storage.</summary>
    R8i,
    /// <summary>Selects R8ui storage.</summary>
    R8ui,
    /// <summary>Selects Red storage.</summary>
    Red,
    /// <summary>Selects Rg storage.</summary>
    Rg,
    /// <summary>Selects Rg16 storage.</summary>
    Rg16,
    /// <summary>Selects Rg16Snorm storage.</summary>
    Rg16Snorm,
    /// <summary>Selects Rg16f storage.</summary>
    Rg16f,
    /// <summary>Selects Rg16i storage.</summary>
    Rg16i,
    /// <summary>Selects Rg16ui storage.</summary>
    Rg16ui,
    /// <summary>Selects Rg32f storage.</summary>
    Rg32f,
    /// <summary>Selects Rg32i storage.</summary>
    Rg32i,
    /// <summary>Selects Rg32ui storage.</summary>
    Rg32ui,
    /// <summary>Selects Rg8 storage.</summary>
    Rg8,
    /// <summary>Selects Rg8Snorm storage.</summary>
    Rg8Snorm,
    /// <summary>Selects Rg8i storage.</summary>
    Rg8i,
    /// <summary>Selects Rg8ui storage.</summary>
    Rg8ui,
    /// <summary>Selects Rgb storage.</summary>
    Rgb,
    /// <summary>Selects Rgb10A2 storage.</summary>
    Rgb10A2,
    /// <summary>Selects Rgb10A2ui storage.</summary>
    Rgb10A2ui,
    /// <summary>Selects Rgb16 storage.</summary>
    Rgb16,
    /// <summary>Selects Rgb16Snorm storage.</summary>
    Rgb16Snorm,
    /// <summary>Selects Rgb16f storage.</summary>
    Rgb16f,
    /// <summary>Selects Rgb16i storage.</summary>
    Rgb16i,
    /// <summary>Selects Rgb16ui storage.</summary>
    Rgb16ui,
    /// <summary>Selects Rgb32f storage.</summary>
    Rgb32f,
    /// <summary>Selects Rgb32i storage.</summary>
    Rgb32i,
    /// <summary>Selects Rgb32ui storage.</summary>
    Rgb32ui,
    /// <summary>Selects Rgb8 storage.</summary>
    Rgb8,
    /// <summary>Selects Rgb8Snorm storage.</summary>
    Rgb8Snorm,
    /// <summary>Selects Rgb8i storage.</summary>
    Rgb8i,
    /// <summary>Selects Rgb8ui storage.</summary>
    Rgb8ui,
    /// <summary>Selects Rgb9E5 storage.</summary>
    Rgb9E5,
    /// <summary>Selects Rgba storage.</summary>
    Rgba,
    /// <summary>Selects Rgba16 storage.</summary>
    Rgba16,
    /// <summary>Selects Rgba16Snorm storage.</summary>
    Rgba16Snorm,
    /// <summary>Selects Rgba16f storage.</summary>
    Rgba16f,
    /// <summary>Selects Rgba16i storage.</summary>
    Rgba16i,
    /// <summary>Selects Rgba16ui storage.</summary>
    Rgba16ui,
    /// <summary>Selects Rgba32f storage.</summary>
    Rgba32f,
    /// <summary>Selects Rgba32i storage.</summary>
    Rgba32i,
    /// <summary>Selects Rgba32ui storage.</summary>
    Rgba32ui,
    /// <summary>Selects Rgba8 storage.</summary>
    Rgba8,
    /// <summary>Selects Rgba8Snorm storage.</summary>
    Rgba8Snorm,
    /// <summary>Selects Rgba8i storage.</summary>
    Rgba8i,
    /// <summary>Selects Rgba8ui storage.</summary>
    Rgba8ui,
    /// <summary>Selects Srgb8 storage.</summary>
    Srgb8,
    /// <summary>Selects Srgb8Alpha8 storage.</summary>
    Srgb8Alpha8,
    /// <summary>Selects StencilIndex8 storage.</summary>
    StencilIndex8,
    /// <summary>Selects single-channel EAC storage.</summary>
    CompressedR11Eac,
    /// <summary>Selects signed single-channel EAC storage.</summary>
    CompressedSignedR11Eac,
    /// <summary>Selects two-channel EAC storage.</summary>
    CompressedRg11Eac,
    /// <summary>Selects signed two-channel EAC storage.</summary>
    CompressedSignedRg11Eac,
    /// <summary>Selects RGB ETC2 storage.</summary>
    CompressedRgb8Etc2,
    /// <summary>Selects sRGB ETC2 storage.</summary>
    CompressedSrgb8Etc2,
    /// <summary>Selects RGB ETC2 storage with one-bit alpha.</summary>
    CompressedRgb8PunchthroughAlpha1Etc2,
    /// <summary>Selects sRGB ETC2 storage with one-bit alpha.</summary>
    CompressedSrgb8PunchthroughAlpha1Etc2,
    /// <summary>Selects RGBA ETC2/EAC storage.</summary>
    CompressedRgba8Etc2Eac,
    /// <summary>Selects sRGB alpha ETC2/EAC storage.</summary>
    CompressedSrgb8Alpha8Etc2Eac,
}
