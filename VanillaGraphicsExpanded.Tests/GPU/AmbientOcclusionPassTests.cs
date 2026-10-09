using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the owned horizon pass with independent analytic receiver geometry and no temporal history.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AmbientOcclusionPassTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Unoccluded flat and tilted planes, clear sky and invalid receivers remain neutral including image boundaries.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    public void UnoccludedReceiversRemainNeutral(int scene)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        float[] result = Render(owner, draw, 65, 37, 2, scene);
        AssertFinite(result);
        if (scene == 7) foreach (float depth in result.Where((_, index) => index % 4 == 1)) Assert.Equal(0, depth);
        foreach (float visibility in result.Where((_, index) => index % 4 == 0)) Assert.InRange(visibility, .995f, 1.001f);
    }

    /// <summary>Nearby foreground darkens a background receiver, transmission attenuates once, and the next plane frame has no residual history.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ContactTransmissionAndMotionRemainCurrentFrame(int quality)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        float[] opaque = Render(owner, draw, 65, 37, quality, 4);
        float[] transparentOccluder = Render(owner, draw, 65, 37, quality, 4, 1, 0);
        float[] partialReceiver = Render(owner, draw, 65, 37, quality, 4, 0, .5f);
        AssertFinite(opaque); AssertFinite(transparentOccluder); AssertFinite(partialReceiver);
        int strongest = Enumerable.Range(0, 65 * 37).Where(i => i % 65 >= 33).MinBy(i => opaque[i * 4]);
        Assert.True(opaque[strongest * 4] < .99f, $"Expected contact occlusion, got {opaque[strongest * 4]}");
        Assert.InRange(transparentOccluder[strongest * 4], .995f, 1.001f);
        float expected = .5f + .5f * opaque[strongest * 4];
        Assert.InRange(partialReceiver[strongest * 4], expected - .002f, expected + .002f);
        // The nearer front-facing plane has no evidence in front of it, despite its darkened neighboring receiver.
        for (int y = 0; y < 37; y++) Assert.InRange(opaque[(y * 65 + 30) * 4], .995f, 1.001f);
        float[] movedAway = Render(owner, draw, 65, 37, quality, 0);
        foreach (float visibility in movedAway.Where((_, index) => index % 4 == 0)) Assert.InRange(visibility, .995f, 1.001f);
    }

    /// <summary>Perpendicular ray-plane intersections form a concave corner; cutout gaps remain unoccluded.</summary>
    [Fact]
    public void PerpendicularCornerAndCutoutGapsRespectGeometry()
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        float[] corner = Render(owner, draw, 65, 37, 2, 5);
        AssertFinite(corner);
        Assert.True(corner.Where((_,i)=>i%4==0).Min()<.97f,$"Corner final minimum={corner.Where((_,i)=>i%4==0).Min():R}");
        float[] gaps = Render(owner, draw, 65, 37, 2, 6);
        AssertFinite(gaps);
        for (int y = 0; y < 37; y++) for (int x = 0; x < 65; x++)
            if (x % 4 < 2) Assert.Equal(1, gaps[(y * 65 + x) * 4]);
    }

    /// <summary>Reports realistic-resolution visibility near analytic corners at representative receiver distances.</summary>
    [Theory]
    [InlineData(4,3f)]
    [InlineData(4,10f)]
    [InlineData(4,20f)]
    [InlineData(5,3f)]
    [InlineData(5,10f)]
    [InlineData(5,20f)]
    public void RealisticResolutionContactStrength(int scene, float distance)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        float[] result = Render(owner,draw,1280,720,2,scene,receiverDistance:distance);
        AssertFinite(result);
        int center = scene == 4 ? 640 : (int)(640 + .5f / distance * 360);
        int radius = (int)MathF.Ceiling(1.25f / distance * 360);
        var values = new List<float>();
        for(int y=180;y<540;y++) for(int x=Math.Max(0,center-radius);x<Math.Min(1280,center+radius);x++) values.Add(result[(y*1280+x)*4]);
        values.Sort();
        log.WriteLine($"AO scene={scene} distance={distance} 1280x720 ROIcenter={center} radius={radius}: min={values[0]:R} p10={values[values.Count/10]:R} p50={values[values.Count/2]:R} p90={values[values.Count*9/10]:R} fractionBelow.99={values.Count(v=>v<.99f)/(double)values.Count:R}");
        Assert.True(values[0]<.99f);
    }

    /// <summary>Odd dimensions use ceil reductions, stable preparations reuse storage, and resize/disposal retire the publication.</summary>
    [Fact]
    public void HierarchyStorageAndPublicationFollowLifecycle()
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        Render(owner, draw, 65, 37, 1, 0);
        var first = owner.Texture;
        long expected = 8L * (33 * 19 + 17 * 10 + 9 * 5 + 2 * 33 * 19 + 65 * 37);
        Assert.Equal(expected, owner.StorageBytes);
        Render(owner, draw, 65, 37, 2, 0);
        Assert.Same(first, owner.Texture);
        Render(owner, draw, 17, 9, 2, 0);
        Assert.NotSame(first, owner.Texture);
        Assert.Equal(17, owner.Texture!.Width); Assert.Equal(9, owner.Texture.Height);
        owner.Dispose(); Assert.Null(owner.Texture); Assert.Equal(0, owner.StorageBytes);
        Assert.Equal(new AmbientOcclusionQuality(2, 3, 4), AmbientOcclusionQuality.FromNative(1));
        Assert.Equal(new AmbientOcclusionQuality(2, 6, 6), AmbientOcclusionQuality.FromNative(2));
    }
    /// <summary>Measures only warmed GPU submissions on a synthetic contact scene, separately from allocation and readback.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SyntheticPassTiming(int quality)
    {
        EnsureShaderTestAvailable();
        using var owner = new AmbientOcclusionPass();
        using var draw = new PostprocessDraw();
        AssertFinite(Render(owner, draw, 1280, 720, quality, 4, measure: true));
    }
    #endregion

    #region Private
    /// <summary>Builds depth from analytic view-space planes and stages material layers through existing typed textures.</summary>
    private float[] Render(AmbientOcclusionPass owner, PostprocessDraw draw, int width, int height, int quality, int scene,
        float occluderTransmission = 0, float receiverTransmission = 0, bool measure = false, float receiverDistance = 3)
    {
        var horizon = Programs.Create<PostSsaoShaderProgram>();
        var reduction = Programs.Create<AmbientOcclusionDepthShaderProgram>();
        var filter = Programs.Create<AmbientOcclusionFilterShaderProgram>();
        float aspect = (float)width / height;
        const float near = .1f, far = 100f;
        float a = -(far + near) / (far - near), b = -2 * far * near / (far - near);
        float[] inverse = [aspect,0,0,0, 0,1,0,0, 0,0,0,1/b, 0,0,-1,a/b];
        float[] view = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        float[] projection = [1/aspect,0,0,0, 0,1,0,0, 0,0,a,-1, 0,0,b,0];
        // The fixture depth is rendered by this camera; AO borrows the same universal snapshot.
        using var camera = new VgeFrameUniformBuffer();
        camera.Capture(projection, view, inverse, view, projection, projection,
            new Vector2(width, height), 0, 0, Vector3.Zero, Vector3.Zero, 0);
        float[] depths = new float[width * height], normals = new float[width * height * 4], materials = new float[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            bool front = (scene == 4 || scene == 8) && x < width / 2;
            float z = scene == 7 ? 97 : front ? receiverDistance-.3f : scene == 1 ? receiverDistance / (1 - .2f * ((x + .5f) / width * 2 - 1) * aspect) : receiverDistance;
            float rayX = ((x + .5f) / width * 2 - 1) * aspect;
            bool sideWall = scene == 5 && rayX > 0 && .5f / rayX < z;
            if (sideWall) z = .5f / rayX;
            depths[i] = scene == 2 || (scene == 6 && x % 4 < 2) ? 1 : .5f * (-a + b / z) + .5f;
            Vector3 n = sideWall ? -Vector3.UnitX : scene == 1 ? Vector3.Normalize(new(.2f, 0, 1)) : Vector3.UnitZ;
            normals[i * 4] = n.X * .5f + .5f; normals[i * 4 + 1] = n.Y * .5f + .5f;
            normals[i * 4 + 2] = n.Z * .5f + .5f; normals[i * 4 + 3] = scene == 3 || (scene == 8 && front) ? -1 : 1;
            materials[i * 4 + 3] = front ? occluderTransmission : receiverTransmission;
        }
        using var depth = TestFramework.CreateTexture(width, height, PixelInternalFormat.R32f, depths);
        using var normal = TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba32f, normals);
        using var material = TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba32f, materials);
        using var surface = LayeredTestTexture.Create(normal, material, null);
        var pipelines = owner.Prepare(draw, width, height, quality, horizon, reduction, filter);
        Assert.True(GraphicsCommandContext.TryRun("Tests.AmbientOcclusion", pipelines, true,
            commands => owner.Render(commands, draw, depth, surface, camera)));
        if (measure)
        {
            var samples = new List<double>();
            for (int sample = 0; sample < 5; sample++)
            {
                using var timer = GpuTimerQuery.Create();
                timer.Begin();
                Assert.True(GraphicsCommandContext.TryRun("Tests.AmbientOcclusionTiming", pipelines, true,
                    commands => owner.Render(commands, draw, depth, surface, camera)));
                timer.End(); samples.Add(timer.GetResultNanoseconds() / 1e6);
            }
            log.WriteLine($"Synthetic AO {width}x{height} quality{quality}, six draws, five warm GPU samples: min={samples.Min():F4}ms median={samples.Order().ElementAt(2):F4}ms max={samples.Max():F4}ms; owned payload={owner.StorageBytes} bytes. Not live cost or physical bandwidth.");
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return ((DynamicTexture2D)owner.Texture!).ReadPixels();
    }

    /// <summary>Rejects non-finite and out-of-range visibility while allowing depth and packed normals in other channels.</summary>
    private static void AssertFinite(float[] pixels)
    {
        Assert.All(pixels, value => Assert.True(float.IsFinite(value)));
        foreach (float visibility in pixels.Where((_, index) => index % 4 == 0)) Assert.InRange(visibility, 0, 1.001f);
    }
    #endregion
}
