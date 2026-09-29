using System.Numerics;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks completed atmosphere snapshots replace or reuse their owned GPU texture coherently.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmospherePublicationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Publication lifetime
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

    /// <summary>Reads driver allocation and contents to verify upload completed before shared publication.</summary>
    private static void AssertTexture(AtmosphereLighting snapshot)
    {
        Assert.Same(snapshot, AtmosphereModSystem.Lighting);
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, AtmosphereModSystem.SkyTextureId);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
        Assert.Equal(snapshot.Width, width);
        Assert.Equal(snapshot.Height * 2, height);
        float[] pixels = new float[width * height * 4];
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
        float[] packedSky = new float[pixels.Length];
        AtmosphereMieTransport.Pack(snapshot.Sky.AsSpan(), snapshot.SkyMie.AsSpan(), packedSky,
            snapshot.Width, snapshot.Height, 1, snapshot.Sun, snapshot.HorizonElevation);
        for (int i = 0; i < pixels.Length; i++)
            Assert.InRange(Math.Abs(pixels[i] - packedSky[i]), 0, Math.Max(.00001f, packedSky[i] * .001f));
        AssertVolume(AtmosphereModSystem.AerialRadianceTextureId, snapshot.AerialRadiance, snapshot, true);
        AssertVolume(AtmosphereModSystem.AerialAttenuationTextureId, snapshot.AerialAttenuation, snapshot);
    }

    /// <summary>Verifies both cumulative transport volumes were fully uploaded with the published angular extent.</summary>
    private static void AssertVolume(int id, ImmutableArray<float> expected, AtmosphereLighting snapshot, bool packedRadiance = false)
    {
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, id);
        GL.GetTexLevelParameter(TextureTarget.Texture3D, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture3D, 0, GetTextureParameter.TextureHeight, out int height);
        GL.GetTexLevelParameter(TextureTarget.Texture3D, 0, GetTextureParameter.TextureDepth, out int depth);
        Assert.Equal(expected.IsEmpty ? 1 : snapshot.Width, width);
        Assert.Equal((expected.IsEmpty ? 1 : snapshot.Height) * (packedRadiance ? 2 : 1), height);
        Assert.Equal(expected.IsEmpty ? 1 : AtmosphereAerialPerspective.Depth, depth);
        float[] pixels = new float[width * height * depth * 4];
        GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
        if (packedRadiance)
        {
            float[] packed = new float[pixels.Length];
            AtmosphereMieTransport.Pack(expected.IsEmpty ? new float[] { 0, 0, 0, 1 } : expected.AsSpan(), snapshot.AerialMie.AsSpan(), packed,
                width, height / 2, depth, snapshot.Sun, snapshot.HorizonElevation);
            expected = ImmutableArray.CreateRange(packed);
        }
        if (expected.IsEmpty) Assert.Equal(new[] { 0f, 0f, 0f, 1f }, pixels);
        else for (int i = 0; i < pixels.Length; i++)
            Assert.InRange(Math.Abs(pixels[i] - expected[i]), 0, Math.Max(.00001f, expected[i] * .001f));
    }
    #endregion
}
