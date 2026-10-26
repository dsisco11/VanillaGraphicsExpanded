namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes an explicit buffer interval or image region copied to privately owned staging.</summary>
public readonly struct RHIReadbackDescription
{
    #region Public API
    /// <summary>Creates a buffer readback request without inspecting current storage.</summary>
    public RHIReadbackDescription(RHIBufferRange source)
    { Buffer = source; Texture = default; Region = default; Encoding = default; RowPitch = SlicePitch = 0; IsTexture = false; }
    /// <summary>Creates a view-relative image readback with explicit output layout.</summary>
    public RHIReadbackDescription(GpuTextureRef source, RHITextureRegion region, RHIPixelEncoding encoding, int rowPitch = 0, int slicePitch = 0)
    { Buffer = default; Texture = source; Region = region; Encoding = encoding; RowPitch = rowPitch; SlicePitch = slicePitch; IsTexture = true; }
    /// <summary>Reports which explicitly supplied source category is selected.</summary>
    public bool IsTexture { get; }
    /// <summary>Gets the supplied buffer interval.</summary>
    public RHIBufferRange Buffer { get; }
    /// <summary>Gets the supplied image reference.</summary>
    public GpuTextureRef Texture { get; }
    /// <summary>Gets the image region in view-relative coordinates.</summary>
    public RHITextureRegion Region { get; }
    /// <summary>Gets the requested CPU representation.</summary>
    public RHIPixelEncoding Encoding { get; }
    /// <summary>Gets the explicit row byte pitch, or zero for tight packing.</summary>
    public int RowPitch { get; }
    /// <summary>Gets the explicit slice byte pitch, or zero for tight packing.</summary>
    public int SlicePitch { get; }
    #endregion
}
