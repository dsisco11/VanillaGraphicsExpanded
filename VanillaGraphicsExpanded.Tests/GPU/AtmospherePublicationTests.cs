using System.Numerics;
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
    /// <summary>Same-size updates reuse storage; dimension changes retire it only after uploading the replacement.</summary>
    [Fact]
    public void CompletedDimensionsControlTextureReplacement()
    {
        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 0, 0, complete: true, width: 16, height: 8);
        owner.Publish(lookup.Current!);
        int first = AtmosphereModSystem.SkyTextureId;
        AssertTexture(lookup.Current!);
        lookup.Update(Vector3.UnitY, 0, 1, complete: true, width: 16, height: 8);
        owner.Publish(lookup.Current!);
        Assert.Equal(first, AtmosphereModSystem.SkyTextureId);
        AssertTexture(lookup.Current!);
        lookup.Update(Vector3.UnitY, 0, 1, complete: true, width: 17, height: 9);
        owner.Publish(lookup.Current!);
        Assert.NotEqual(first, AtmosphereModSystem.SkyTextureId);
        Assert.False(GL.IsTexture(first));
        AssertTexture(lookup.Current!);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Reads driver allocation and contents to verify upload completed before shared publication.</summary>
    private static void AssertTexture(AtmosphereLighting snapshot)
    {
        Assert.Same(snapshot, AtmosphereModSystem.Lighting);
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, AtmosphereModSystem.SkyTextureId);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
        Assert.Equal(snapshot.Width, width);
        Assert.Equal(snapshot.Height, height);
        float[] pixels = new float[width * height * 4];
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
        for (int i = 0; i < pixels.Length; i++)
            Assert.InRange(Math.Abs(pixels[i] - snapshot.Sky[i]), 0, Math.Max(.00001f, snapshot.Sky[i] * .001f));
    }
    #endregion
}
