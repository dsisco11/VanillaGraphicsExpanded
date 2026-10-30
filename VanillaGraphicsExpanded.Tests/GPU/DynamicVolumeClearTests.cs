using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks scalar and multichannel half-float volume clears through independent exact readback.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DynamicVolumeClearTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Preserves full float precision and OpenGL's missing blue/alpha defaults for an RG input pixel.</summary>
    [Theory]
    [InlineData(TextureFormat.Rg32f, 2)]
    [InlineData(TextureFormat.Rgba16f, 4)]
    [InlineData(TextureFormat.Rgba32f, 4)]
    public void RgFloatInputPreservesComponentsAndDefaults(TextureFormat format, int components)
    {
        fixture.EnsureContextValid();
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
            using var request = context.EnqueueReadback(new RHIReadbackDescription(texture, region,
                context.GetTextureEncoding(texture, PixelComponentType.Float)));
            Assert.Equal(CompletionStatus.Ready, request.Wait());
            using var mapped = request.MapReadyReadback();
            var actual = MemoryMarshal.Cast<byte, float>(mapped.Bytes);
            float[] expected = [r, g, 0f, 1f];
            Assert.Equal(16 * 66 * 16 * components, actual.Length);
            for (int index = 0; index < actual.Length; index++)
                Assert.Equal(expected[index % components], actual[index]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Consumes a caller's short-lived stack value and preserves all texels outside a partial clear.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ShortLivedClearValuePreservesRequestedRegion(bool wholeImage)
    {
        fixture.EnsureContextValid();
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
            using var request = context.EnqueueReadback(new RHIReadbackDescription(texture, full, encoding));
            Assert.Equal(CompletionStatus.Ready, request.Wait());
            using var mapped = request.MapReadyReadback();
            var actual = MemoryMarshal.Cast<byte, float>(mapped.Bytes);
            Assert.Equal(16 * 66 * 16 * 2, actual.Length);
            // Verify both the requested write and its untouched surroundings independently.
            for (int z = 0; z < 16; z++)
                for (int y = 0; y < 66; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        bool selected = x >= region.X && x < region.X + region.Width
                            && y >= region.Y && y < region.Y + region.Height
                            && z >= region.Z && z < region.Z + region.Depth;
                        int index = ((z * 66 + y) * 16 + x) * 2;
                        Assert.Equal(selected ? .5f : initial[0], actual[index]);
                        Assert.Equal(selected ? .125f : initial[1], actual[index + 1]);
                    }
            GC.KeepAlive(initial);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Compares clear entry points on one live object without resetting surrounding native state.</summary>
    [Fact]
    public void AlternatingRgClearsPreserveEachRequestedValue()
    {
        fixture.EnsureContextValid();
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

                using var request = context.EnqueueReadback(new RHIReadbackDescription(texture, region, encoding));
                Assert.Equal(CompletionStatus.Ready, request.Wait());
                using var mapped = request.MapReadyReadback();
                var actual = MemoryMarshal.Cast<byte, float>(mapped.Bytes);
                Assert.Equal(16 * 66 * 16 * 2, actual.Length);
                for (int index = 0; index < actual.Length; index++)
                {
                    if (actual[index] == value[index % 2]) continue;
                    failures.Add($"texture={texture.TextureId} entry={step.Entry} RG/Float extent=16x66x16 "
                        + $"requested={step.R},{step.G} bytes={Convert.ToHexString(MemoryMarshal.AsBytes(value.AsSpan()))} "
                        + $"firstMismatch={index} actual={actual[index]}");
                    break;
                }
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
        fixture.EnsureContextValid();
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

            // Read the complete allocation rather than relying on filtering or one edge sample.
            using var request = context.EnqueueReadback(new RHIReadbackDescription(texture, region, encoding));
            Assert.Equal(CompletionStatus.Ready, request.Wait());
            using var mapped = request.MapReadyReadback();
            var actual = MemoryMarshal.Cast<byte, float>(mapped.Bytes);
            Assert.Equal(16 * 66 * 16 * components, actual.Length);
            for (int index = 0; index < actual.Length; index++)
                Assert.Equal(expected[index % components], actual[index]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Returns before readback so native execution must consume the value during the clear call.</summary>
    private static void ClearFromLocalValue(GpuTextureRef texture, RHITextureRegion region, float r, float g)
    {
        Span<float> value = stackalloc float[2] { r, g };
        var context = RHICommandContext.Current;
        context.ClearTexture(texture, region, context.GetTextureEncoding(texture, PixelComponentType.Float), MemoryMarshal.AsBytes(value));
    }
    #endregion
}
