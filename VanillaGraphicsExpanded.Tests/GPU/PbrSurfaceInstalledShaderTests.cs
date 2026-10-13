using VanillaGraphicsExpanded.Rendering.Shaders;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compiles complete installed engine mesh shaders through the production patch sequence.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrSurfaceInstalledShaderTests : RenderTestBase
{
    /// <summary>Uses the shared headless driver context.</summary>
    public PbrSurfaceInstalledShaderTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Variant validation
    /// <summary>Installed forward vertex publishers select the actual uniform/accessor or terrain decoder before interpolation.</summary>
    [Theory]
    [InlineData("standard", false)]
    [InlineData("standard", true)]
    [InlineData("chunktransparent", false)]
    [InlineData("chunktransparent", true)]
    public void InstalledVertexPublishesCalibratedSource(string family, bool fullSky)
    {
        EnsureContextValid();
        int vertex = Compile(ShaderType.VertexShader, Build(family + ".vsh", 0, family == "chunktransparent" ? 1 : 0, 0, 0, 0));
        int program = GL.CreateProgram(), buffer = GL.GenBuffer(), vao = GL.GenVertexArray();
        try
        {
            GL.AttachShader(program, vertex);
            GL.TransformFeedbackVaryings(program, 2, ["vge_blockIrradiance", "vge_skyVisibility"], TransformFeedbackMode.InterleavedAttribs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var frame = new VgeFrameUniformBuffer();
            Assert.True(frame.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
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
            GL.DrawArrays(PrimitiveType.Points, 0, 1); GL.EndTransformFeedback();
            GL.Disable(EnableCap.RasterizerDiscard);
            float[] actual = new float[4];
            GL.GetBufferSubData(BufferTarget.TransformFeedbackBuffer, IntPtr.Zero, 16, actual);
            double expected = LightingCalibration.Default.BlockReferenceLux * (1 - LightingCalibration.Default.BlockNeutralFloor) / Math.PI;
            for (int channel = 0; channel < 3; channel++) Assert.InRange(actual[channel], expected * .99999, expected * 1.00001);
            Assert.InRange(actual[3], fullSky ? .99999f : 0, fullSky ? 1 : .00001f);
        }
        finally
        {
            GL.Disable(EnableCap.RasterizerDiscard); GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, 0);
            GL.DeleteBuffer(buffer); GL.DeleteVertexArray(vao); GL.DeleteProgram(program); GL.DeleteShader(vertex);
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Covers opaque, late and OIT families with cascades and first-person depth offsets.</summary>
    public static IEnumerable<object[]> Variants()
    {
        foreach (bool lumon in new[] { false, true })
        {
            foreach (string family in new[] { "standard", "entityanimated", "instanced", "chunktransparent", "chunkliquid" })
                foreach (int shadow in new[] { 0, 1, 2 })
                    foreach (int oit in family is "chunktransparent" or "chunkliquid" ? new[] { 1 } : new[] { 0, 1 })
                        yield return [family, shadow, oit, 0, 0, 1, lumon];
            foreach (string family in new[] { "standard", "entityanimated", "instanced", "chunktransparent", "chunkliquid" })
                yield return [family, 2, family is "chunktransparent" or "chunkliquid" ? 1 : 0, 1, family is "chunktransparent" or "chunkliquid" ? 1 : 0, 0, lumon];
            yield return ["standard", 4, 1, 2, 0, 1, lumon];
        }
    }

    /// <summary>Actual driver linking verifies complete declaration and vertex/fragment interface compatibility.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void InstalledVariantCompilesAndLinks(string family, int shadow, int oit, int ssao, int ssbo, int depth, bool lumon)
    {
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        bool previousMode = VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.LumOn.Enabled;
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.LumOn.Enabled = lumon;
            PbrShaderLightingMode.GenerationLumOnEnabled = null;
            vertex = Compile(ShaderType.VertexShader, Build(family + ".vsh", shadow, oit, ssao, ssbo, depth));
            fragment = Compile(ShaderType.FragmentShader, Build(family + ".fsh", shadow, oit, ssao, ssbo, depth));
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
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
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(ShaderType.VertexShader, Build("sky.vsh", 1, 0, ssao, 0, 0));
            fragment = Compile(ShaderType.FragmentShader, Build("sky.fsh", 1, 0, ssao, 0, 0));
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
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
        ShaderCapability capabilities = ShaderCapability.None;
        Build(name, 2, name == "chunktransparent.fsh" ? 1 : 0, 0, 0, 0, value => capabilities |= value);
        Assert.Equal(expected, capabilities.HasFlag(ShaderCapability.TwoSidedSurfaceNormals));
        Assert.Equal(name is "standard.fsh" or "chunktransparent.fsh",
            capabilities.HasFlag(ShaderCapability.SceneColorConvention));
        Assert.Equal(name is "chunkopaque.fsh" or "chunktopsoil.fsh",
            capabilities.HasFlag(ShaderCapability.SceneMaterialCapture));
    }
    #endregion

    #region Installed source expansion
    /// <summary>Applies imports before expansion and material patches after expansion as the engine does.</summary>
    internal static string Build(string name, int shadow, int oit, int ssao, int ssbo, int depth, Action<ShaderCapability>? declare = null)
    {
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        string original = File.ReadAllText(Path.Combine(game, "assets/game/shaders", name));
        // Representative alternate variants deliberately rename an incidental engine local.
        if (ssao > 0 && !name.StartsWith("chunkopaque", StringComparison.Ordinal) && !name.StartsWith("chunktopsoil", StringComparison.Ordinal))
            original = original.Replace("murkiness", "renamedWaterDensity");
        var tree = SyntaxTree.Parse(original, GlslSchema.Instance);
        VanillaShaderPatches.TryApplyPreProcessing(null, tree, name);
        if (name is "standard.fsh" or "entityanimated.fsh" or "instanced.fsh" or "chunktransparent.fsh")
            Assert.Contains($"#define VGE_PBR_FORWARD_LUMON {(VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.LumOn.Enabled ? 1 : 0)}", tree.ToText());
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Expand(string source, string directory) => Regex.Replace(source, "(?m)^\\s*(?:#include\\s+([^\\r\\n]+)|@import\\s+\"([^\"]+)\"[^\\r\\n]*)", match =>
        {
            string path = match.Groups[1].Success
                ? Path.Combine(game, "assets/game/shaderincludes", match.Groups[1].Value.Trim())
                : Path.Combine(directory, match.Groups[2].Value);
            path = Path.GetFullPath(path);
            return included.Add(path) ? Expand(File.ReadAllText(path), Path.GetDirectoryName(path)!) : "";
        });
        tree = SyntaxTree.Parse(Expand(tree.ToText(), Path.Combine(AppContext.BaseDirectory, "assets/shaders")), GlslSchema.Instance);
        if (name is "sky.vsh" or "sky.fsh")
        {
            // The engine still compiles its sky assets, but replacement output must not modify them.
            string unpatched = tree.ToText();
            Assert.False(VanillaShaderPatches.TryApplyPatches(null, tree, name, declare));
            Assert.Equal(unpatched, tree.ToText());
        }
        else if (name is not ("final.fsh" or "luma.fsh" or "godrays.fsh" or "colorgrade.fsh" or "blur.fsh" or "particlesquad2d.vsh" or "final.vsh" or "chunkshadowmap.fsh" or "chunkshadowmap.vsh" or "chunkliquid.vsh" or "chunkliquid.fsh")) Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, name, declare));
        if (name == "standard.fsh")
        {
            string main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single().ToText();
            Assert.Matches(@"#if BLOOM == 0\s+if\s*\(vge_pbrRoute == 0\)\s+outColor\.rgb \*= 1 \+ glowLevel;", main);
            Assert.True(main.IndexOf("vec3 vge_materialColor", StringComparison.Ordinal) > main.IndexOf("if (tempGlowMode == 1)", StringComparison.Ordinal));
        }
        if (name is "entityanimated.fsh" or "chunktransparent.fsh")
        {
            string main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Single().ToText();
            int finish = main.IndexOf("vec3 vge_params", StringComparison.Ordinal);
            int publication = main.LastIndexOf("OIT(", StringComparison.Ordinal);
            Assert.True(finish >= 0 && publication > finish);
            if (name == "entityanimated.fsh")
                Assert.True(main.LastIndexOf("#if USEOIT > 0", StringComparison.Ordinal) > finish);
        }
        string defines = $"\n#define SHADOWQUALITY {shadow}\n#define USEOIT {oit}\n#define ALLOWDEPTHOFFSET {depth}\n#define SSAOLEVEL {ssao}\n#define NORMALVIEW 0\n#define DYNLIGHTS 4\n#define MINBRIGHT 0\n#define SHINYEFFECT 1\n#define FOAMEFFECT 0\n#define USESSBO {ssbo}\n#define MAXANIMATEDELEMENTS 46\n";
        if (name.StartsWith("standard", StringComparison.Ordinal) && ssao > 0) defines += "#define GLOWSUB 1\n";
        string result = Regex.Replace(tree.ToText(), "(?m)^(#version[^\\r\\n]*)", "$1" + defines);
        if (name.StartsWith("chunkliquid", StringComparison.Ordinal) && ssao > 0)
            result = result.Replace("#define FOAMEFFECT 0", "#define FOAMEFFECT 1\n#define WAVINGSTUFF 1");
        // The engine upgrades SSBO variants before driver compilation, as documented in chunktransparent.vsh.
        return ssbo > 0 && name.EndsWith(".vsh", StringComparison.Ordinal) ? result.Replace("#version 330 core", "#version 430 core") : result;
    }

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
