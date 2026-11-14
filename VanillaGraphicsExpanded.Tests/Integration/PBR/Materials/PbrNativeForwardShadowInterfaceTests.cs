using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.PBR.Materials;

/// <summary>Verifies installed native forward variants link the shared virtual shadow interface.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrNativeForwardShadowInterfaceTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Each native forward family must expose the same active shadow bindings after production patching.</summary>
    [Theory]
    [InlineData("standard")]
    [InlineData("entityanimated")]
    [InlineData("instanced")]
    [InlineData("chunktransparent")]
    public void InstalledForwardVariantLinksSharedShadowBindings(string family)
    {
        // Arrange: Build the installed family through production preprocessing with the shared shadow path enabled.
        EnsureContextValid();
        bool previousMode = ConfigModSystem.Config.LumOn.Enabled;
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        ShaderProgram? nativeProgram = null;
        try
        {
            ConfigModSystem.Config.LumOn.Enabled = true;
            PbrShaderLightingMode.GenerationLumOnEnabled = true;
            string vertex = InstalledEngineShaderSources.Build(family + ".vsh", 0, family == "chunktransparent" ? 1 : 0, 0, 0, 0);
            string fragment = InstalledEngineShaderSources.Build(family + ".fsh", 0, family == "chunktransparent" ? 1 : 0, 0, 0, 0);
            using var linked = GpuProgramObject.Adopt(TestShaderCache.CreateGraphics(vertex, fragment));

            // Act: Reflect the actual driver's linked native program interface.
            var layout = GpuProgramLayout.TryBuild(linked.ProgramId);

            // Assert: All families share the typed resource slots consumed by native surface binding.
            AssertBinding(layout.TryGetUniformBlockBinding("SurfaceShadowParameters", out int parameters), parameters, 18);
            AssertBinding(layout.TryGetShaderStorageBlockBinding("SurfaceShadowRequests", out int requests), requests, 0);
            AssertBinding(layout.TryGetShaderStorageBlockBinding("PageEntries", out int entries), entries, 1);
            AssertBinding(layout.TryGetShaderStorageBlockBinding("SurfaceShadowReadiness", out int readiness), readiness, 2);
            AssertBinding(layout.TryGetShaderStorageBlockBinding("SurfaceShadowDiagnostics", out int diagnostics), diagnostics, 5);
            AssertBinding(layout.TryGetSamplerUnit("shadowDepthPool", out int sampler), sampler, 33);

            // Register the linked executable as its native engine family and verify that the production
            // capability gate accepts this exact linked interface for native shadow binding.
            nativeProgram = new ShaderProgram { PassName = family, ProgramId = linked.ProgramId };
            SurfaceShadowPrograms.Register(nativeProgram);
            Assert.True(SurfaceShadowPrograms.Has(nativeProgram));

            // Bind a valid disabled publication through the explicit native-input seam. This exercises
            // the production UBO, SSBO and integer-array texture binding set without mutating global inputs.
            using var inputs = new SurfaceShadowInputSet(null, null, null, new ShadowPageSettings());
            SurfaceShadowBindings.BindNative(inputs);
        }
        finally
        {
            if (nativeProgram is not null) SurfaceShadowPrograms.Remove(nativeProgram);
            ConfigModSystem.Config.LumOn.Enabled = previousMode;
            PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration;
        }
    }
    #endregion

    #region Private
    /// <summary>Requires an active driver-reported resource and its exact shared binding point.</summary>
    private static void AssertBinding(bool active, int actual, int expected)
    {
        Assert.True(active, $"Expected active shadow interface resource at binding {expected}.");
        Assert.Equal(expected, actual);
    }
    #endregion
}
