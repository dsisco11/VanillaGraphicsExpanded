using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using Vintagestory.Client;

namespace VanillaGraphicsExpanded.Tests.Component.Water.Support;

/// <summary>Checks reset and retirement contracts for class-owned numerical water fixture programs.</summary>
internal static class WaterNumericalProgramFixtureAssertions
{
    /// <summary>Creates a fresh GPU image for a fixture boundary control.</summary>
    /// <param name="format">The image format required by the tested sampler slot.</param>
    /// <returns>A case-owned texture whose reference must be cleared before its next lease.</returns>
    internal static DynamicTexture2D CreateTexture(TextureFormat format)
        => RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two,
            Format = format,
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Storage = ResourceStorageMode.Mutable,
            Filter = TextureFilterMode.Nearest,
            BaseLevel = 0,
            MaxLevel = 0,
            Label = null
        }).RequireResource();

    /// <summary>Verifies the complete packed uniform buffer has returned to zero, including unused batch storage.</summary>
    /// <param name="inputs">The actual wrapper-owned buffer exposed by its shader binding contract.</param>
    internal static void AssertZeroBytes(CpuUniformBuffer inputs)
        => Assert.All(inputs.Capture().Bytes.ToArray(), value => Assert.Equal((byte)0, value));
}

