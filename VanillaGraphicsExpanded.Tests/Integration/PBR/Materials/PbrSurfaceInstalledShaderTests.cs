using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;



namespace VanillaGraphicsExpanded.Tests.Integration.PBR.Materials;
/// <summary>Compiles complete installed engine mesh shaders through the production patch sequence.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrSurfaceInstalledShaderTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared headless driver context.</summary>
    public PbrSurfaceInstalledShaderTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Variant validation
    /// <summary>The native OIT diagnostic's early draw helper can call safety validation declared by later imports.</summary>
    [Fact]
    public void InstalledOitDiagnosticFragmentCompiles()
    {
        // Arrange: Load the installed transparent fragment source with its production patch configuration.
        EnsureContextValid();
        // Act: Compile the transformed installed fragment on the native driver.
        int shader = Compile(ShaderType.FragmentShader, InstalledEngineShaderSources.Build("woittest.fsh", 0, 1, 0, 0, 0));
        // Assert: The installed diagnostic stage compiles successfully.
        GL.DeleteShader(shader);
    }

    /// <summary>Installed forward vertex publishers retain raw block fields and the decoded sky availability before interpolation.</summary>
    [Theory]
    [InlineData("standard", false)]
    [InlineData("standard", true)]
    [InlineData("chunktransparent", false)]
    [InlineData("chunktransparent", true)]
    public void InstalledVertexPublishesRawField(string family, bool fullSky)
    {
        // Arrange: Compile the installed vertex variant and configure transform feedback for its raw field outputs.
        EnsureContextValid();
        int previousProgram = GL.GetInteger(GetPName.CurrentProgram);
        bool previousProgramPendingDeletion = false;
        if (previousProgram != 0)
        {
            GL.GetProgram(previousProgram, GetProgramParameterName.DeleteStatus, out int deleteStatus);
            previousProgramPendingDeletion = deleteStatus != 0;
        }
        int vertex = Compile(ShaderType.VertexShader, InstalledEngineShaderSources.Build(family + ".vsh", 0, family == "chunktransparent" ? 1 : 0, 0, 0, 0));
        int program = GL.CreateProgram(), buffer = GL.GenBuffer(), vao = GL.GenVertexArray();
        try
        {
            GL.AttachShader(program, vertex);
            GL.TransformFeedbackVaryings(program, 2, ["vge_blockField", "vge_skyVisibility"], TransformFeedbackMode.InterleavedAttribs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var frame = new VgeFrameUniformBuffer();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Frame, frame.Capture()));
            GL.UseProgram(program); GL.BindVertexArray(vao);
            float alpha = fullSky ? 1 : family == "standard" ? .03f : 7f / 255;
            if (family == "standard") GL.Uniform4(GL.GetUniformLocation(program, "rgbaLightIn"), 1f, 1f, 1f, alpha);
            else GL.VertexAttrib4(2, 1f, 1f, 1f, alpha);
            GL.Uniform1(GL.GetUniformLocation(program, "dontWarpVertices"), 1);
            GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, buffer);
            GL.BufferData(BufferTarget.TransformFeedbackBuffer, 16, IntPtr.Zero, BufferUsageHint.StreamRead);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, buffer);
            GL.Enable(EnableCap.RasterizerDiscard);
            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points);
            // Act: Run one point through the vertex stage and capture its pre-interpolation publications.
            GL.DrawArrays(PrimitiveType.Points, 0, 1); GL.EndTransformFeedback();
            GL.Disable(EnableCap.RasterizerDiscard);
            float[] actual = new float[4];
            GL.GetBufferSubData(BufferTarget.TransformFeedbackBuffer, IntPtr.Zero, 16, actual);
            // Assert: Block field remains unmodified while sky availability follows the authored input.
            double expected = 1;
            for (int channel = 0; channel < 3; channel++) Assert.InRange(actual[channel], expected * .99999, expected * 1.00001);
            Assert.InRange(actual[3], fullSky ? .99999f : 0, fullSky ? 1 : .00001f);
        }
        finally
        {
            GL.Disable(EnableCap.RasterizerDiscard); GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, 0);
            // Binding the test program retires a caller program that was already delete-pending.
            // Restore the neutral selection instead of rebinding that now-invalid object name.
            GL.UseProgram(previousProgramPendingDeletion ? 0 : previousProgram);
            GL.DeleteBuffer(buffer); GL.DeleteVertexArray(vao); GL.DeleteProgram(program); GL.DeleteShader(vertex);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Covers each installed family with representative cascade, OIT, lighting and depth modes.</summary>
    public static IEnumerable<object[]> Variants()
    {
        foreach (bool lumon in new[] { false, true })
        {
            string[] families = ["standard", "entityanimated", "instanced", "chunktransparent", "chunkliquid"];
            foreach (string family in families)
            {
                int oit = family is "chunktransparent" or "chunkliquid" ? 1 : 0;
                // Sample depth-enabled no-cascade admission on the standard family.
                if (family == "standard")
                    yield return [family, 0, oit, 0, 0, 1, lumon];
            }

            // Exercise the first-cascade branch while retaining the per-family shadow
            // values in the SSAO/SSBO checks below.
            if (lumon)
                yield return ["standard", 1, 0, 0, 0, 1, lumon];

            // Entity OIT is conditional; retain its enabled branch as an interaction witness.
            if (lumon)
                yield return ["entityanimated", 2, 1, 0, 0, 1, lumon];

            // Exercise the per-family SSAO/SSBO define path while disabling depth offset.
            foreach (string family in new[] { "standard", "entityanimated", "instanced", "chunktransparent", "chunkliquid" })
            {
                // Legacy liquid preprocessing is independent of the LumOn mode.
                if (family == "chunkliquid" && lumon)
                    continue;
                yield return [family, 2, family is "chunktransparent" or "chunkliquid" ? 1 : 0, 1, family is "chunktransparent" or "chunkliquid" ? 1 : 0, 0, lumon];
            }

            // Keep the high-cascade and stronger SSAO combination as a boundary case.
            if (lumon)
                yield return ["standard", 4, 1, 2, 0, 1, lumon];
        }
    }

    /// <summary>Actual driver linking verifies complete declaration and vertex/fragment interface compatibility.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void InstalledVariantCompilesAndLinks(string family, int shadow, int oit, int ssao, int ssbo, int depth, bool lumon)
    {
        // Arrange: Select the engine generation mode and compile matching installed vertex and fragment variants.
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        bool previousMode = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.LumOn.Enabled;
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.LumOn.Enabled = lumon;
            PbrShaderLightingMode.GenerationLumOnEnabled = null;
            vertex = Compile(ShaderType.VertexShader, InstalledEngineShaderSources.Build(family + ".vsh", shadow, oit, ssao, ssbo, depth));
            fragment = Compile(ShaderType.FragmentShader, InstalledEngineShaderSources.Build(family + ".fsh", shadow, oit, ssao, ssbo, depth));
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            // Act: Link the real driver program and query its public interface locations.
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            // Assert: The configured variant links and exposes only the outputs required by its route.
            if (family != "chunkliquid")
            {
                Assert.True(GL.GetUniformLocation(program, "vge_pbrRoute") >= 0);
                Assert.Equal(-1, GL.GetUniformLocation(program, "vge_sceneLinear"));
            }
            bool oitOutput = family is "chunktransparent" or "chunkliquid" || (family == "entityanimated" && oit > 0);
            Assert.Equal(oitOutput ? -1 : 4, GL.GetFragDataLocation(program, "vge_outNormal"));
            Assert.Equal(oitOutput ? -1 : 5, GL.GetFragDataLocation(program, "vge_outMaterial"));
            Assert.Equal(oitOutput ? -1 : 6, GL.GetFragDataLocation(program, "vge_outPatchId"));
            Assert.Equal(oitOutput ? -1 : 7, GL.GetFragDataLocation(program, "vge_outEnvironment"));
            if (depth > 0 && !oitOutput)
                Assert.Equal(3, GL.GetFragDataLocation(program, "outGPosition"));
            if (oitOutput)
            {
                Assert.Equal(4, GL.GetFragDataLocation(program, "OITaccumulation1"));
                Assert.Equal(5, GL.GetFragDataLocation(program, "OITaccumulation2"));
            }
        }
        finally
        {
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.LumOn.Enabled = previousMode;
            PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration;
            if (program != 0) GL.DeleteProgram(program);
            if (fragment != 0) GL.DeleteShader(fragment);
            if (vertex != 0) GL.DeleteShader(vertex);
        }
    }
    /// <summary>Preserves the engine asset lifecycle without injecting outputs owned by the replacement sky.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InstalledSkyLinks(int ssao)
    {
        // Arrange: Build the installed sky pair with the selected SSAO define.
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(ShaderType.VertexShader, InstalledEngineShaderSources.Build("sky.vsh", 1, 0, ssao, 0, 0));
            fragment = Compile(ShaderType.FragmentShader, InstalledEngineShaderSources.Build("sky.fsh", 1, 0, ssao, 0, 0));
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            // Act: Link the driver program and inspect uniforms and fragment outputs.
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            // Assert: The replacement sky leaves atmosphere and material-capture outputs absent.
            Assert.Equal(-1, GL.GetUniformLocation(program, "vge_atmosphereReady"));
            Assert.Equal(-1, GL.GetUniformLocation(program, "vge_atmosphereSky"));
            Assert.Equal(-1, GL.GetUniformLocation(program, "vge_atmosphereLutHorizon"));
            Assert.Equal(-1, GL.GetUniformLocation(program, "vge_atmosphereSunDirection"));
            foreach (string output in new[] { "vge_outNormal", "vge_outMaterial", "vge_outPatchId", "vge_outEnvironment" })
                Assert.Equal(-1, GL.GetFragDataLocation(program, output));
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            if (fragment != 0) GL.DeleteShader(fragment);
            if (vertex != 0) GL.DeleteShader(vertex);
        }
    }
    #endregion

    #region Capability declarations
    /// <summary>Only fragment patches which install two-sided normal handling declare that feature.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh", true)]
    [InlineData("chunktransparent.fsh", true)]
    [InlineData("chunktopsoil.fsh", false)]
    [InlineData("standard.fsh", false)]
    [InlineData("chunkopaque.vsh", false)]
    public void PatchesDeclareOnlyTheirInstalledCapabilities(string name, bool expected)
    {
        // Arrange: Start with no capabilities and select one installed shader asset and patch variant.
        ShaderCapability capabilities = ShaderCapability.None;
        // Act: Run the production asset build while capturing declared capabilities.
        InstalledEngineShaderSources.Build(name, 2, name == "chunktransparent.fsh" ? 1 : 0, 0, 0, 0, value => capabilities |= value);
        // Assert: Only capabilities installed by that asset are reported.
        Assert.Equal(expected, capabilities.HasFlag(ShaderCapability.TwoSidedSurfaceNormals));
        Assert.Equal(name is "standard.fsh" or "chunktransparent.fsh",
            capabilities.HasFlag(ShaderCapability.SceneColorConvention));
        Assert.Equal(name is "chunkopaque.fsh" or "chunktopsoil.fsh",
            capabilities.HasFlag(ShaderCapability.SceneMaterialCapture));
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Reports full driver errors without leaking failed shader objects.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        string log = GL.GetShaderInfoLog(shader);
        if (compiled == 0) GL.DeleteShader(shader);
        Assert.True(compiled != 0, log);
        return shader;
    }
    #endregion
}

