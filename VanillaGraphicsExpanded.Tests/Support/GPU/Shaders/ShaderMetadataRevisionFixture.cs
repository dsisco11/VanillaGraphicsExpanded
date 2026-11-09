using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

/// <summary>Creates the mutable texture used by shader metadata publication tests.</summary>
internal static class ShaderMetadataRevisionFixture
{
    #region Public API
    /// <summary>Creates a mutable 4 by 4 texture under the current headless GPU context.</summary>
    internal static DynamicTexture2D CreateTexture() => RHICommandContext.Current.CreateDynamicTexture2D(new() {
        Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
        MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
    #endregion
}
