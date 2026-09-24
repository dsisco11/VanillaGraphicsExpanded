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
