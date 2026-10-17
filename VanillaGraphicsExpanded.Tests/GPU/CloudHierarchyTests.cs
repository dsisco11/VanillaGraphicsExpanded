using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual GPU min/max reduction and exact-source publication against independent native descendants.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudHierarchyTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    #region Reduction and storage
    /// <summary>Empty, sparse and dense native maps, including odd boundaries, reduce to exact conservative extrema.</summary>
    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(32)]
    [InlineData(81)]
    [InlineData(129)]
    [InlineData(200)]
    public void EveryMipMatchesIndependentDescendantOracle(int width)
    {
        EnsureShaderTestAvailable();
        using var owner = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var native = TestFramework.CreateTexture(width, width, PixelInternalFormat.Rgba32f);
        var input = Input(native, width);
        DynamicTexture2D? previous = null;
        for (int pattern = 0; pattern < 3; pattern++)
        {
            var columns = Columns(width, pattern);
            native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
            input = input with { Generation = input.Generation + 1, Frame = (uint)(pattern + 1) };
            using (var hostile = new HostileFullscreenState())
            {
                Assert.True(owner.TryBuild(input, () => true), owner.Failure);
                hostile.AssertRestored();
            }
            Assert.True(owner.TryGetCurrent(input, out var texture));
            Assert.Equal(PixelInternalFormat.Rg32f, texture!.InternalFormat);
            Assert.Equal(owner.Layout!.LeafWidth / 2, texture.Width);
            Assert.Equal(owner.Layout.MipCount, texture.MipLevels);
            if (previous is not null) Assert.Same(previous, texture);
            previous = texture;
            AssertMips(texture, columns, width);
        }
        owner.Dispose();
        Assert.False(previous!.IsValid);
        Assert.True(native.IsValid);
        Assert.True(GL.IsTexture(native.TextureId));
        Assert.False(owner.TryGetCurrent(input, out _));
        Assert.False(owner.TryBuild(input, () => true));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>External image units are restored after every mip dispatch, including access modes and bound levels.</summary>
    [Fact]
    public void ComputeRestoresBorrowedImageBindings()
    {
        EnsureShaderTestAvailable();
        using var owner = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var native = TestFramework.CreateTexture(21, 21, PixelInternalFormat.Rgba32f);
        using var external = DynamicTexture2D.CreateMipmapped(16, 16, PixelInternalFormat.Rg32f, 5);
        using var source = GpuImageUnitBinding.Bind(0, external, TextureAccess.ReadWrite, level: 2);
        using var target = GpuImageUnitBinding.Bind(1, external, TextureAccess.ReadOnly, level: 3);
        StateCache.Current.InvalidateAll();
        Assert.True(owner.TryBuild(Input(native, 21), () => true), owner.Failure);
        for (int unit = 0; unit < 2; unit++)
        {
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, unit, out int name);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLevel, unit, out int level);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, unit, out int access);
            Assert.Equal(external.TextureId, name);
            Assert.Equal(unit + 2, level);
            Assert.Equal((int)(unit == 0 ? TextureAccess.ReadWrite : TextureAccess.ReadOnly), access);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Publication and lifetime
    /// <summary>Animated in-place generations and camera movement rebuild while allocation/shader/owner changes retire storage.</summary>
    [Fact]
    public void GenerationRefreshAndLifetimeReplacementCannotPublishStaleBounds()
    {
        EnsureShaderTestAvailable();
        using var owner = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var native = TestFramework.CreateTexture(21, 21, PixelInternalFormat.Rgba32f);
        var input = Input(native, 21);
        var columns = Columns(21, 1);
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.True(owner.TryGetCurrent(input, out var first));
        var old = input;
        columns[^1] = new(1, 0, -50, 60);
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
        input = input with { Generation = input.Generation + 1, Placement = input.Placement with { Clock = 2, Offset = Vector3.One } };
        Assert.Equal(old.Upload, input.Upload);
        Assert.False(owner.TryGetCurrent(input, out _));
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.True(owner.TryGetCurrent(input, out var animated));
        Assert.Same(first, animated);
        Assert.False(owner.TryGetCurrent(old, out _));
        AssertMips(animated!, columns, 21);

        // Epoch changes deliberately reuse texture names and dimensions, exercising identity over GL names.
        foreach (var replacement in new[]
                 { input with { Allocation = 2 }, input with { Shader = 2 },
                     input with { World = 2 }, input with { Owner = new object() } })
        {
            Assert.True(owner.TryGetCurrent(input, out var retired));
            // Source observation runs before depth/lighting admission, so retirement cannot wait for a draw.
            owner.ObserveSource(replacement);
            Assert.False(retired!.IsValid);
            Assert.Null(owner.Layout);
            Assert.True(owner.TryBuild(replacement, () => true), owner.Failure);
            Assert.False(retired!.IsValid);
            Assert.False(owner.TryGetCurrent(input, out _));
            Assert.True(owner.TryGetCurrent(replacement, out _));
            input = replacement;
        }
        using var resized = TestFramework.CreateTexture(81, 81, PixelInternalFormat.Rgba32f);
        Assert.True(owner.TryGetCurrent(input, out var oldSize));
        input = input with { Storage = input.Storage with { Map = resized.TextureId, Width = 81 }, Allocation = 3 };
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.False(oldSize!.IsValid);
        Assert.True(owner.TryGetCurrent(input, out var current));
        Assert.Equal(64, current!.Width);
        owner.Reset();
        Assert.False(current.IsValid);
        Assert.True(GL.IsTexture(native.TextureId));
        Assert.True(GL.IsTexture(resized.TextureId));
        Assert.False(owner.TryGetCurrent(input, out _));
    }

    /// <summary>Failed generation and revocation during submission withdraw old publication and allow a clean rebuild.</summary>
    [Fact]
    public void RevokedNativeBorrowNeverCertifiesGpuWork()
    {
        EnsureShaderTestAvailable();
        using var owner = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var native = TestFramework.CreateTexture(20, 20, PixelInternalFormat.Rgba32f);
        var input = Input(native, 20);
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.True(owner.TryGetCurrent(input, out var retired));
        Assert.False(owner.TryBuild(input, () => false));
        Assert.False(retired!.IsValid);
        Assert.False(owner.TryGetCurrent(input, out _));
        int validations = 0;
        Assert.False(owner.TryBuild(input, () => ++validations < 3));
        Assert.Equal(3, validations);
        Assert.Null(owner.Layout);
        Assert.False(owner.TryGetCurrent(input, out _));
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.True(owner.TryGetCurrent(input, out _));
    }

    /// <summary>Unavailable compute, boundary errors and retired executable generations cannot leave a publication behind.</summary>
    [Fact]
    public void BuildFailuresAndExecutableInvalidationFallBackUntilReset()
    {
        EnsureShaderTestAvailable();
        using var native = TestFramework.CreateTexture(20, 20, PixelInternalFormat.Rgba32f);
        var input = Input(native, 20);
        bool unavailable = true;
        CloudHierarchyComputeShader? shader = null;
        using var owner = new CloudHierarchy(() => unavailable
            ? throw new InvalidOperationException("Unavailable compute fixture")
            : shader = CloudHierarchyComputeShader.Create(Programs.Api));
        Assert.False(owner.TryBuild(input, () => true));
        Assert.Contains("Unavailable compute", owner.Failure);
        Assert.Null(owner.Layout);
        Assert.False(owner.TryGetCurrent(input, out _));
        unavailable = false;
        Assert.False(owner.TryBuild(input, () => true));
        owner.Reset();
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.True(owner.TryGetCurrent(input, out var retired));
        shader!.Dispose();
        Assert.False(owner.TryGetCurrent(input, out _));
        Assert.False(retired!.IsValid);
        Assert.Null(owner.Layout);
        Assert.True(owner.TryBuild(input, () => true), owner.Failure);
        Assert.True(owner.TryGetCurrent(input, out retired));
        // A pre-existing GL error cannot be consumed as evidence of successful construction.
        GL.Enable((EnableCap)(-1));
        Assert.False(owner.TryBuild(input with { Generation = 2 }, () => true));
        Assert.False(retired!.IsValid);
        Assert.False(owner.TryGetCurrent(input, out _));
        Assert.True(GL.IsTexture(native.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Checks all coarse texels against direct native descendant enumeration, including completely padded nodes.</summary>
    private static void AssertMips(DynamicTexture2D texture, Vector4[] columns, int nativeWidth)
    {
        // Production publishes sampler visibility. This test additionally reads through a framebuffer.
        GL.MemoryBarrier(MemoryBarrierFlags.FramebufferBarrierBit);
        for (int mip = 0; mip < texture.MipLevels; mip++)
        {
            float[] actual = texture.ReadPixels(mip);
            int width = texture.Width >> mip, span = 1 << (mip + 1);
            Assert.Equal(width * width * 2, actual.Length);
            for (int z = 0; z < width; z++)
            for (int x = 0; x < width; x++)
            {
                var expected = CloudHierarchyBoundsOracle.Bounds(columns, nativeWidth, x * span, z * span, span);
                int index = (z * width + x) * 2;
                Assert.Equal(expected.Lower, actual[index]);
                Assert.Equal(expected.Upper, actual[index + 1]);
            }
        }
    }

    /// <summary>Produces independent height and extinction variation, invalid controls and a surviving odd-edge column.</summary>
    private static Vector4[] Columns(int width, int pattern)
    {
        var columns = new Vector4[width * width];
        if (pattern == 0) return columns;
        for (int i = 0; i < columns.Length; i++)
        {
            if (pattern == 1 && i % 17 != 0) continue;
            float lower = (i % 31 - 15) * .125f;
            columns[i] = new(i % 11 == 0 ? 1e-20f : 4, 0, lower, lower + .125f + (i % 7) * .25f);
        }
        columns[^1] = new(1, 0, -7.5f, 13.25f);
        columns[^2] = new(1, float.NaN, -100, 100);
        columns[^3] = new(1, 0, float.NegativeInfinity, 100);
        columns[^4] = new(1, 0, 5, 4);
        columns[^5] = new(-1, 0, -100, 100);
        columns[^6] = new(float.PositiveInfinity, 0, -100, 100);
        columns[^7] = new(0, 0, -100, 100);
        return columns;
    }

    /// <summary>Models a certified native publication without transferring ownership of its texture.</summary>
    private static CloudMapInput Input(GpuTexture map, int width) => new(new object(), 1, 1, 1, 1, 1, 1,
        new(map.TextureId, 2, 3, 4, width, 50, 5), new(0, 0, 0, 0, 0, 0, 0, 0, Vector3.Zero, 0));
    #endregion
}
