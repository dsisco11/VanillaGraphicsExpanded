using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;


using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks shared immutable jitter provisioning independently of either rendering consumer.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
public sealed class PmjJitterTextureUploadTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{


    #region Public API
    /// <summary>Every normalized texel matches independent sequence packing and ordinary frames retain owned storage.</summary>
    [Fact]
    public void UploadMatchesPackedSequenceAndRetainsIndependentStorage()
    {
        // Arrange: Prepare shared jitter storage and retain its native texture identity.
        // Act: Read every normalized texel and request the texture again during ordinary frame use.
        // Assert: Texels match independent sequence packing and consumers do not replace owned storage.
        EnsureContextValid();const int count=256;const uint seed=0xA5B35705u;
        var config=new PmjConfig(count,seed,PmjVariant.Pmj02,PmjOutputKind.Vector2F32,true,0,false);
        ushort[] expected=PmjConversions.ToRg16UNormInterleaved(PmjGenerator.Generate(config));
        using var texture=PmjJitterTexture.Create(count,seed);
        Assert.Equal(TextureFormat.Rg16,texture.InternalFormat);
        Assert.Equal(count,texture.Width);Assert.Equal(1,texture.Height);
        Assert.Equal(TextureFilterMode.Nearest,texture.FilterMode);
        using var oracle=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension=TextureDimension.Two,Format=TextureFormat.Rg32f,Width=count,Height=1,MipLevels=1,
            Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest,Label="Tests.Pmj.Expected"
        }).RequireResource();
        oracle.UploadDataImmediate(expected.Select(value=>(float)value).ToArray());
        ImageAssertions.Predicates.Predicate(texture, LightingPredicates.PmjJitterTextureUploadTests_UploadMatchesPackedSequenceAndRetainsIndependentStorage,oracle).AssertEqual();
        using var original=new Assertions.GpuImageSnapshot(texture);
        int id=texture.TextureId;texture.EnsureCreated();Assert.Equal(id,texture.TextureId);
        using var independent=PmjJitterTexture.Create(count,seed);
        Assert.NotEqual(id,independent.TextureId);texture.Dispose();Assert.False(texture.IsValid);
        Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        Assert.True(independent.IsValid);ImageAssertions.Comparisons.CompareHistory(independent,original).AssertEqual();
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-lighting", 0, 0);
    }
    #endregion
}
