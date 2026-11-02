using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Numerics;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks completed atmosphere snapshots replace or reuse their owned GPU texture coherently.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmospherePublicationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Publication lifetime
    /// <summary>Unsupported lighting and dimensionless transport never replace the complete generation with contained invalid values.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    public void InvalidSourceOrTransportRetainsCompleteGeneration(float invalid)
    {
        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        float[] radiance = Enumerable.Repeat(new[] { 32f, 64f, 128f, 1f }, AtmosphereAerialPerspective.Depth).SelectMany(value => value).ToArray();
        float[] attenuation = Enumerable.Repeat(new[] { .2f, .3f, .4f, 1f }, AtmosphereAerialPerspective.Depth).SelectMany(value => value).ToArray();
        var candidate = new AtmosphereLighting(Vector3.UnitY, new(100, 200, 300), new(1, 2, 3),
            Vector3.Zero, Vector3.Zero, ImmutableArray.Create(32f, 64f, 128f, 1f))
        {
            Width = 1, Height = 1, AerialRadiance = ImmutableArray.CreateRange(radiance),
            AerialAttenuation = ImmutableArray.CreateRange(attenuation)
        };
        owner.Publish(candidate);
        var previous = AtmosphereModSystem.Lighting;
        int sky = AtmosphereModSystem.SkyTextureId, aerial = AtmosphereModSystem.AerialRadianceTextureId;
        // Exercise actual source, packed luminance and optical-depth admission separately;
        // a finite neutral payload from a defensive clamp must never confer validity.
        foreach (var replacement in new[]
        {
            candidate with { Solar = new(invalid, 0, 0) },
            candidate with { Environment = new(0, invalid, 0) },
            candidate with { Sky = candidate.Sky.SetItem(0, invalid) },
            candidate with { SkyMie = ImmutableArray.Create(invalid, 0f, 0f, 1f) },
            candidate with { AerialRadiance = candidate.AerialRadiance.SetItem(0, invalid) },
            candidate with { AerialAttenuation = candidate.AerialAttenuation.SetItem(0, invalid) }
        })
        {
            Assert.ThrowsAny<ArgumentException>(() => owner.Publish(replacement));
            Assert.Same(previous, AtmosphereModSystem.Lighting);
            Assert.Equal(sky, AtmosphereModSystem.SkyTextureId);
            Assert.Equal(aerial, AtmosphereModSystem.AerialRadianceTextureId);
            AssertTexture(previous);
        }
    }

    /// <summary>Complete generations swap independent texture sets and retire incompatible spare storage.</summary>
    [Fact]
    public void CompletedDimensionsControlTextureReplacement()
    {
        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        var placeholder = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, Vector3.Zero, ImmutableArray.Create(0f, 0f, 0f, 1f)) { Width = 1, Height = 1 };
        owner.Publish(placeholder);
        int initial = AtmosphereModSystem.SkyTextureId;
        AssertTexture(placeholder);
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: 16, height: 8);
        owner.Publish(lookup.Current!);
        int first = AtmosphereModSystem.SkyTextureId;
        Assert.NotEqual(initial, first);
        Assert.True(GL.IsTexture(initial));
        AssertTexture(lookup.Current!);
        lookup.Update(Vector3.UnitY, 0, 1, complete: true, width: 16, height: 8);
        owner.Publish(lookup.Current!);
        Assert.NotEqual(first, AtmosphereModSystem.SkyTextureId);
        AssertTexture(lookup.Current!);
        int second = AtmosphereModSystem.SkyTextureId;
        lookup.Update(Vector3.UnitY, 0, 1, complete: true, width: 17, height: 9);
        owner.Publish(lookup.Current!);
        Assert.NotEqual(second, AtmosphereModSystem.SkyTextureId);
        AssertTexture(lookup.Current!);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A malformed replacement cannot publish mismatched lighting or disturb the previous GPU generation.</summary>
    [Fact]
    public void FailedVolumeUploadRetainsPublishedGeneration()
    {
        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 0, 0, width: 16, height: 8);
        var previous = lookup.Current!;
        owner.Publish(previous);
        previous = AtmosphereModSystem.Lighting;
        int sky = AtmosphereModSystem.SkyTextureId;
        int radiance = AtmosphereModSystem.AerialRadianceTextureId;
        int attenuation = AtmosphereModSystem.AerialAttenuationTextureId;
        Assert.Throws<ArgumentException>(() => owner.Publish(previous with { AerialAttenuation = ImmutableArray<float>.Empty }));
        Assert.Same(previous, AtmosphereModSystem.Lighting);
        Assert.Equal(sky, AtmosphereModSystem.SkyTextureId);
        Assert.Equal(radiance, AtmosphereModSystem.AerialRadianceTextureId);
        Assert.Equal(attenuation, AtmosphereModSystem.AerialAttenuationTextureId);
        AssertTexture(previous);
    }

    /// <summary>Malformed external extinction cannot replace any resource or metadata of the published generation.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.1f)]
    [InlineData(1.1f)]
    public void FailedExternalTransmissionRetainsWholePublication(float invalid)
    {
        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, .001f, .25f, width: 16, height: 8);
        owner.Publish(lookup.Current!);
        var previous = AtmosphereModSystem.Lighting;
        int sky = AtmosphereModSystem.SkyTextureId;
        var transmission = AtmosphereModSystem.ExternalTransmissionTexture!;
        using var contents = new Assertions.GpuImageSnapshot(transmission);
        Assert.Throws<ArgumentException>(() => owner.Publish(previous with
            { ExternalTransmission = previous.ExternalTransmission.SetItem(0, invalid) }));
        Assert.Throws<ArgumentException>(() => owner.Publish(previous with
            { ExternalTransmission = previous.ExternalTransmission.RemoveAt(0) }));
        Assert.Same(previous, AtmosphereModSystem.Lighting);
        Assert.Equal(sky, AtmosphereModSystem.SkyTextureId);
        Assert.Same(transmission, AtmosphereModSystem.ExternalTransmissionTexture);
        ImageAssertions.Comparisons.CompareHistory(transmission, contents).AssertEqual();
    }

    /// <summary>Physical LUT encoding preserves dimensionless attenuation and commits metadata only after valid upload.</summary>
    [Fact]
    public void PhysicalLookupEncodingPublishesMatchingMetadata()
    {
        EnsureContextValid();
        var snapshot = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, Vector3.Zero, ImmutableArray.Create(32f, 64f, 128f, 1f))
            { Width = 1, Height = 1, LookupStorageScale = LightingStorage.PersistentScale, CalibrationGeneration = 7 };
        using var textures = new AtmosphereTextureSet(snapshot);
        textures.Upload(snapshot);
        Assert.Equal(LightingStorage.PersistentScale, textures.StorageScale);
        Assert.Equal(7u, textures.CalibrationGeneration);
        ImageAssertions.Comparisons.Compare(textures.Sky,[2,4,8,1,0,0,0,1]).AssertEqual();
        // The attenuation fallback remains a local one-texel tuple.
        Assert.Equal(new float[]{0,0,0,1},textures.Attenuation.ReadPixels());
        Assert.Throws<ArgumentOutOfRangeException>(() => textures.Upload(snapshot with
            { Sky = ImmutableArray.Create(1048065f, 0f, 0f, 1f), CalibrationGeneration = 8 }));
        Assert.Equal(7u, textures.CalibrationGeneration);
        Assert.Equal(LightingStorage.PersistentScale, textures.StorageScale);
        Assert.Throws<ArgumentException>(() => textures.Upload(snapshot with { CalibrationGeneration = 0 }));
        Assert.Equal(7u, textures.CalibrationGeneration);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Reads driver allocation and contents to verify upload completed before shared publication.</summary>
    private void AssertTexture(AtmosphereLighting snapshot)
    {
        snapshot = AtmosphereModSystem.Lighting;
        Assert.Equal(LightingStorage.PersistentScale, snapshot.LookupStorageScale);
        Assert.Equal(1u, snapshot.CalibrationGeneration);
        Assert.True(float.IsFinite(snapshot.ReconstructionMaximumNits));
        using var binding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, AtmosphereModSystem.SkyTextureId);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
        Assert.Equal(snapshot.Width, width);
        Assert.Equal(snapshot.Height * 2, height);
        float[] packedSky=new float[width*height*4];
        AtmosphereMieTransport.Pack(snapshot.Sky.AsSpan(),snapshot.SkyMie.AsSpan(),packedSky,
            snapshot.Width,snapshot.Height,1,snapshot.Sun,snapshot.HorizonElevation,snapshot.LookupStorageScale);
        var sky=(GpuTexture)AtmosphereModSystem.SkyTexture.Resource!;
        ImageAssertions.Comparisons.CompareFloating(sky,packedSky,packedSky.Select(value=>Math.Max(.00001f,value*.001f)).ToArray()).AssertEqual();
        AssertVolume(AtmosphereModSystem.AerialRadianceTextureId, snapshot.AerialRadiance, snapshot, true);
        AssertVolume(AtmosphereModSystem.AerialAttenuationTextureId, snapshot.AerialAttenuation, snapshot);
        var external = AtmosphereModSystem.ExternalTransmissionTexture!;
        Assert.Equal(snapshot.ExternalTransmission.IsEmpty ? 1 : 512, external.Width);
        Assert.Equal(1, external.Height);
        Assert.Equal(TextureFormat.Rgba32f, external.InternalFormat);
        ImageAssertions.Comparisons.Compare(external,snapshot.ExternalTransmission.IsEmpty ? [0f,0f,0f,1f]
            : snapshot.ExternalTransmission.ToArray()).AssertEqual();
    }

    /// <summary>Verifies both cumulative transport volumes were fully uploaded with the published angular extent.</summary>
    private void AssertVolume(int id, ImmutableArray<float> expected, AtmosphereLighting snapshot, bool packedRadiance = false)
    {
        using var binding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, id);
        GL.GetTexLevelParameter(TextureTarget.Texture3D, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture3D, 0, GetTextureParameter.TextureHeight, out int height);
        GL.GetTexLevelParameter(TextureTarget.Texture3D, 0, GetTextureParameter.TextureDepth, out int depth);
        Assert.Equal(expected.IsEmpty ? 1 : snapshot.Width, width);
        Assert.Equal((expected.IsEmpty ? 1 : snapshot.Height) * (packedRadiance ? 2 : 1), height);
        Assert.Equal(expected.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth, depth);

        if (packedRadiance)
        {
            float[] packed = new float[width*height*depth*4];
            AtmosphereMieTransport.Pack(expected.IsEmpty ? new float[] { 0, 0, 0, 1 } : expected.AsSpan(), snapshot.AerialMie.AsSpan(), packed,
                width, height / 2, depth, snapshot.Sun, snapshot.HorizonElevation, snapshot.LookupStorageScale);
            expected = ImmutableArray.CreateRange(packed);
        }
        using var image=new BorrowedTexture(id);
        float[] wanted=expected.IsEmpty?[0,0,0,1]:expected.ToArray();
        if(expected.IsEmpty)ImageAssertions.Comparisons.Compare(image,wanted).AssertEqual();
        else ImageAssertions.Comparisons.CompareFloating(image,wanted,wanted.Select(value=>Math.Max(.00001f,value*.001f)).ToArray()).AssertEqual();
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-observation", 0, 0);
    }
    #endregion
}
