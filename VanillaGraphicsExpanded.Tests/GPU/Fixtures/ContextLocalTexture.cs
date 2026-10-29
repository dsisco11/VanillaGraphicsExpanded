using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Exercises the supplied initialization handoff without installing a registry through a public factory.</summary>
internal static class ContextLocalTexture
{
    #region Internal API
    /// <summary>Initializes a local owner from explicit facts and verifies that no lifecycle service was required.</summary>
    internal static DynamicTexture2D Create(int width, int height, TextureFormat format)
    {
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        var owner = new DynamicTexture2D();
        try
        {
            RHICommandContext.Current.AllocateTextureOwner(owner, new GpuTextureInitialization
            { Target = TextureDimension.Two, InternalFormat = format, Width = width, Height = height, Depth = 1,
                Levels = 1, AllocationMode = GpuTextureAllocationMode.Mutable, BaseLevel = 0, MaxLevel = 0 },
                TextureFilterMode.Nearest, null);
            Assert.Null(GpuResourceManagerSystem.CurrentManager);
            Assert.Equal(width, owner.Width);
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }
    #endregion
}
