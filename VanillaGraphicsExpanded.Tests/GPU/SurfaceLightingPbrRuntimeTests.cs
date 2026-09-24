using System.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures cache-originating lighting after the actual registered PBR renderer writes the engine target.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingPbrRuntimeTests : RenderTestBase
{
    /// <summary>Uses the exclusive material/graphics context.</summary>
    public SurfaceLightingPbrRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Composition and spatial material response
    /// <summary>Engine view-space lights retain their analytic response with eye height, bob, yaw and signed world origins.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(8192f, .25f, .6f)]
    [InlineData(-8192f, -.25f, -.5f)]
    public void RegisteredPointLightsUseViewSpaceReceivers(float playerX, float bob, float yaw)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { Position = new(playerX, 36, 5), Bob = 1.6f + bob, EyeOffsetX = .125f, Yaw = yaw };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, scene, pbrComposition: true);
        var material = new RuntimeReceiverSurface(new(.5f, .25f, .125f), Reflectivity: 1);
        runtime.Receiver = (_, _) => material;
        var light = new Vector3(.5f, 1.35f, -.25f);
        // Follow chunkopaque.vsh: applyLight receives modelViewMatrix * worldPos.
        // Transform the authored hand-height light through the actual scene matrix.
        var viewMatrix = scene.View();
        var lightVS = new Vector3(
            viewMatrix[0] * light.X + viewMatrix[4] * light.Y + viewMatrix[8] * light.Z + viewMatrix[12],
            viewMatrix[1] * light.X + viewMatrix[5] * light.Y + viewMatrix[9] * light.Z + viewMatrix[13],
            viewMatrix[2] * light.X + viewMatrix[6] * light.Y + viewMatrix[10] * light.Z + viewMatrix[14]);
        runtime.EngineUniforms.PointLightsCount = 1;
        runtime.EngineUniforms.PointLights3 = [lightVS.X, lightVS.Y, lightVS.Z];
        runtime.EngineUniforms.PointLightColors3 = [1f, 1f, 1f];
        runtime.Frame();

        // Compare to independently evaluated BRDF and attenuation using absolute scene points
        // only on the CPU. Production receives the engine's view-space light position.
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.Direct.DirectDiffuseTex!.ReadPixels(), (x, y) =>
        {
            int index = y * 4 + x;
            var point = scene.VisiblePoints[index];
            var toLight = scene.Position + light - point;
            float distanceSquared = toLight.LengthSquared();
            var direction = Vector3.Normalize(toLight);
            var eye = scene.Position + new Vector3(scene.EyeOffsetX, scene.Bob, 0);
            var view = Vector3.Normalize(eye - point);
            var halfway = Vector3.Normalize(direction + view);
            float fresnel = .04f + .96f * MathF.Pow(1 - Math.Clamp(Vector3.Dot(halfway, view), 0, 1), 5);
            float cosine = Math.Max(0, Vector3.Dot(scene.VisibleNormals[index], direction));
            return material.Albedo * ((1 - fresnel) * cosine * Math.Min(1 / distanceSquared, 1));
        }, .002f, "view-space point light");
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(runtime.Direct.DirectDiffuseTex.ReadPixels()) > .01f);
    }

    /// <summary>Four authored receiver regions constrain channel order, linear albedo scaling, black diffuse response and metallic rejection.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RegisteredCompositionRespectsReceiverRegions(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene, pbrComposition: true);
        runtime.Receiver = Receiver;
        SurfaceLightingNumericalRuntimeTests.SeedAndFreeze(runtime);
        for (int frame = 0; frame < 24; frame++) runtime.Frame();
        var incident = scene.SourceAlbedo.Value * (32 / MathF.PI);
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.FinalPixels(), (_, _) => incident, .08f, "incident radiance");
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(),
            (x, y) => DiffuseResponse(scene, x, y, Receiver(x, y), incident), .055f, "PBR material region");
        Assert.Contains("pbr_direct_lighting", runtime.LoadedPrograms);
        Assert.Contains("pbr_composite", runtime.LoadedPrograms);

        // Gather owns the artistic scale; final composition must not apply it again.
        runtime.Cache.Config.LumOn.Intensity = .5f;
        for (int frame = 0; frame < 24; frame++) runtime.Frame();
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.FinalPixels(), (_, _) => incident * .5f, .08f, "scaled incident radiance");
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(),
            (x, y) => DiffuseResponse(scene, x, y, Receiver(x, y), incident * .5f), .055f, "single intensity application");
    }

    /// <summary>Registered direct and emissive outputs are added exactly once to cache-originating diffuse light.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RegisteredCompositionAddsLightingTermsOnce(bool sh9)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene { SourceAlbedo = new(.125f, .25f, .5f) };
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(sh9, scene, pbrComposition: true);
        var receiver = new RuntimeReceiverSurface(new(.5f, .25f, .125f), Emission: .5f, Reflectivity: 1);
        runtime.Receiver = (_, _) => receiver;
        runtime.EngineUniforms.SunPosition3D = new(0, 0, 1);
        SurfaceLightingNumericalRuntimeTests.SeedAndFreeze(runtime);
        for (int frame = 0; frame < 24; frame++) runtime.Frame();
        var incident = scene.SourceAlbedo.Value * (32 / MathF.PI);
        var direct = runtime.Direct.DirectDiffuseTex!.ReadPixels();
        var specular = runtime.Direct.DirectSpecularTex!.ReadPixels();
        var emission = runtime.Direct.EmissiveTex!.ReadPixels();
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(direct) > .01f);
        // A fully rough dielectric has a small but nonzero direct specular lobe.
        Assert.True(SurfaceLightingConsumerRuntimeFixture.Energy(specular) > .00001f);
        SurfaceLightingNumericalRuntimeTests.AssertPixels(emission, (_, _) => receiver.Albedo * receiver.Emission, .002f, "emission radiance");
        SurfaceLightingNumericalRuntimeTests.AssertPixels(runtime.ComposedPixels(), (x, y) =>
        {
            int index = (y * 4 + x) * 4;
            var primary = new Vector3(direct[index] + specular[index] + emission[index],
                direct[index + 1] + specular[index + 1] + emission[index + 1],
                direct[index + 2] + specular[index + 2] + emission[index + 2]);
            return primary + DiffuseResponse(scene, x, y, receiver, incident);
        }, .055f, "direct plus emission plus indirect");
    }
    #endregion

    #region Analytic expectations
    /// <summary>Defines separate pixel regions; equal geometry must not smear receiver material response across their boundaries.</summary>
    private static RuntimeReceiverSurface Receiver(int x, int y) => (x < 2, y < 2) switch
    {
        (true, true) => new(new Vector3(.5f, .25f, .125f)),
        (false, true) => new(new Vector3(.25f, .125f, .0625f)),
        (true, false) => new(Vector3.Zero),
        _ => new(new Vector3(.5f, .25f, .125f), Metallic: 1)
    };

    /// <summary>Evaluates the documented rough diffuse response independently from GPU intermediate textures.</summary>
    internal static Vector3 DiffuseResponse(SpatialLightingScene scene, int x, int y, RuntimeReceiverSurface material, Vector3 incident)
    {
        if (material.Metallic == 1) return Vector3.Zero;
        int index = y * 4 + x;
        var normal = scene.VisibleNormals[index];
        var view = Vector3.Normalize(scene.Position - scene.VisiblePoints[index]);
        float cosine = Math.Clamp(Math.Max(Vector3.Dot(normal, view), normal.Z), 0, 1);
        float fresnel = .04f + .96f * MathF.Pow(1 - cosine, 5);
        // Roughness one suppresses the approximate specular lobe; no extra 1/pi belongs here.
        return incident * material.Albedo * (1 - fresnel);
    }
    #endregion
}
