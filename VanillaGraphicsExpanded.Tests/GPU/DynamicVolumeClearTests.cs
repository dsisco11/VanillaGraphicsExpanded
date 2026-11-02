using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks complete volume clear contents through compact independent expected-value comparisons.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class DynamicVolumeClearTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Volume content and region preservation
    /// <summary>Preserves full float precision and OpenGL's missing blue/alpha defaults for an RG input pixel.</summary>
    [Theory]
    [InlineData(TextureFormat.Rg32f, 2)]
    [InlineData(TextureFormat.Rgba16f, 4)]
    [InlineData(TextureFormat.Rgba32f, 4)]
    public void RgFloatInputPreservesComponentsAndDefaults(TextureFormat format, int components)
    {
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateDynamicTexture3D(new RHITextureDescription
            {
                Dimension = TextureDimension.Three, Format = format, Width = 16, Height = 66, Depth = 16,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0
            }).RequireResource();
            var region = new RHITextureRegion(new(), 0, 0, 0, 16, 66, 16);
            float r = format == TextureFormat.Rgba16f ? .25f : .1234567f;
            float g = format == TextureFormat.Rgba16f ? .75f : .7654321f;
            float[] input = [r, g];
            context.ClearTexture(texture, region, new(PixelChannelLayout.Rg, PixelComponentType.Float),
                MemoryMarshal.AsBytes(input.AsSpan()));
            var compared = ImageAssertions.Comparisons.CompareVector(texture, new(r, g, 0, 1));
            compared.AssertEqual();
            Assert.Equal((uint)(16 * 66 * 16 * components), compared.ComparedComponents);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Consumes a caller's short-lived stack value and preserves all texels outside a partial clear.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ShortLivedClearValuePreservesRequestedRegion(bool wholeImage)
    {
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateDynamicTexture3D(new RHITextureDescription
            {
                Dimension = TextureDimension.Three, Format = TextureFormat.Rg16f, Width = 16, Height = 66, Depth = 16,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0
            }).RequireResource();
            var encoding = context.GetTextureEncoding(texture, PixelComponentType.Float);
            var full = new RHITextureRegion(new(), 0, 0, 0, 16, 66, 16);
            float[] initial = [3f, 5f];
            context.ClearTexture(texture, full, encoding, MemoryMarshal.AsBytes(initial.AsSpan()));
            var region = wholeImage ? full : new RHITextureRegion(new(), 2, 3, 4, 3, 4, 5);
            ClearFromLocalValue(texture, region, .5f, .125f);
            var compared = ImageAssertions.Comparisons.CompareRegions(texture, ClearRegions(region, 16, 66, 16));
            compared.AssertEqual();
            Assert.Equal((uint)(16 * 66 * 16 * 2), compared.ComparedComponents);
            GC.KeepAlive(initial);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Compares clear entry points on one live object without resetting surrounding native state.</summary>
    [Fact]
    public void AlternatingRgClearsPreserveEachRequestedValue()
    {
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateDynamicTexture3D(new RHITextureDescription
            {
                Dimension = TextureDimension.Three, Format = TextureFormat.Rg16f, Width = 16, Height = 66, Depth = 16,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0
            }).RequireResource();
            var encoding = context.GetTextureEncoding(texture, PixelComponentType.Float);
            var region = new RHITextureRegion(new(), 0, 0, 0, 16, 66, 16);
            var failures = new List<string>();
            // Different exactly representable pairs expose stale/skipped writes. Keep all
            // observations, including successes after a bad write, before deciding the result.
            (int Entry, float R, float G)[] steps =
            [ (1,.25f,.75f), (0,.5f,.125f), (1,1.5f,2.5f),
              (0,3f,5f), (1,.625f,.875f), (2,7f,9f), (0,11f,13f) ];
            foreach (var step in steps)
            {
                float[] value = [step.R, step.G];
                if (step.Entry == 1)
                    context.ClearTexture(texture, region, encoding, MemoryMarshal.AsBytes(value.AsSpan()));
                else if (step.Entry == 2)
                    texture.ClearImmediate(step.R, step.G);
                else
                    texture.Clear(step.R, step.G);

                var compared = ImageAssertions.Comparisons.CompareVector(texture, new(step.R, step.G, 0, 1));
                Assert.Equal((uint)(16 * 66 * 16 * 2), compared.ComparedComponents);
                if (compared.Mismatches != 0)
                    failures.Add($"texture={texture.TextureId} entry={step.Entry} RG/Float extent=16x66x16 "
                        + $"requested={step.R},{step.G} bytes={Convert.ToHexString(MemoryMarshal.AsBytes(value.AsSpan()))} {compared}");
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Preserves every requested clear component across all volume texels in either public entry point.</summary>
    [Theory]
    [InlineData(TextureFormat.R16f, 1, 0)]
    [InlineData(TextureFormat.Rg16f, 2, 0)]
    [InlineData(TextureFormat.Rgba16f, 4, 0)]
    [InlineData(TextureFormat.R16f, 1, 1)]
    [InlineData(TextureFormat.Rg16f, 2, 1)]
    [InlineData(TextureFormat.Rgba16f, 4, 1)]
    [InlineData(TextureFormat.R16f, 1, 2)]
    [InlineData(TextureFormat.Rg16f, 2, 2)]
    [InlineData(TextureFormat.Rgba16f, 4, 2)]
    public void NonzeroClearPreservesAllComponents(TextureFormat format, int components, int entryPoint)
    {
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateDynamicTexture3D(new RHITextureDescription
            {
                Dimension = TextureDimension.Three, Format = format, Width = 16, Height = 66, Depth = 16,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0
            }).RequireResource();
            float[] expected = [.25f, .75f, 3f, 5f];
            var encoding = context.GetTextureEncoding(texture, PixelComponentType.Float);
            var region = new RHITextureRegion(new(), 0, 0, 0, 16, 66, 16);
            if (entryPoint == 1)
                context.ClearTexture(texture, region, encoding, MemoryMarshal.AsBytes(expected.AsSpan(0, components)));
            else if (entryPoint == 2)
            {
                Span<float> value = stackalloc float[4] { .25f, .75f, 3f, 5f };
                context.ClearTexture(texture, region, encoding, MemoryMarshal.AsBytes(value[..components]));
            }
            else
                texture.Clear(expected[0], expected[1], expected[2], expected[3]);

            // Compare the complete original allocation, retaining every channel and processed-component count.
            var compared = ImageAssertions.Comparisons.CompareVector(texture, new(expected[0], expected[1], expected[2], expected[3]));
            compared.AssertEqual();
            Assert.Equal((uint)(16 * 66 * 16 * components), compared.ComparedComponents);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Prepares the suite's complete-domain comparison executable before native capture or clear execution.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare");
    }

    /// <summary>Partitions the original volume into the selected clear and six disjoint untouched slabs.</summary>
    private static IReadOnlyList<GpuVectorRegion> ClearRegions(RHITextureRegion selected, int width, int height, int depth)
    {
        List<GpuVectorRegion> regions = [new(selected, new(.5f, .125f, 0, 1), 3)];
        int right = selected.X + selected.Width, top = selected.Y + selected.Height, back = selected.Z + selected.Depth;
        RHITextureRegion[] outside = [
            new(new(), 0, 0, 0, selected.X, height, depth),
            new(new(), right, 0, 0, width - right, height, depth),
            new(new(), selected.X, 0, 0, selected.Width, selected.Y, depth),
            new(new(), selected.X, top, 0, selected.Width, height - top, depth),
            new(new(), selected.X, selected.Y, 0, selected.Width, selected.Height, selected.Z),
            new(new(), selected.X, selected.Y, back, selected.Width, selected.Height, depth - back)];
        // Empty slabs disappear for a whole-volume clear; otherwise every untouched texel belongs to exactly one slab.
        foreach (var slab in outside)
            if (slab.Width > 0 && slab.Height > 0 && slab.Depth > 0)
                regions.Add(new(slab, new(3, 5, 0, 1), 3));
        return regions;
    }

    /// <summary>Returns before readback so native execution must consume the value during the clear call.</summary>
    private static void ClearFromLocalValue(GpuTextureRef texture, RHITextureRegion region, float r, float g)
    {
        Span<float> value = stackalloc float[2] { r, g };
        var context = RHICommandContext.Current;
        context.ClearTexture(texture, region, context.GetTextureEncoding(texture, PixelComponentType.Float), MemoryMarshal.AsBytes(value));
        // Overwrite the caller storage immediately so deferred native reads cannot pass unnoticed.
        value.Fill(float.NaN);
    }
    #endregion
}
