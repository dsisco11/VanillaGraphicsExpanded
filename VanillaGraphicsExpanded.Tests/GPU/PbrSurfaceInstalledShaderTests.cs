using VanillaGraphicsExpanded.Rendering.Shaders;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
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
    /// <summary>Covers opaque, late and OIT families with cascades and first-person depth offsets.</summary>
    public static IEnumerable<object[]> Variants()
    {
        foreach (bool lumon in new[] { false, true })
        {
            foreach (string family in new[] { "standard", "entityanimated", "instanced", "chunktransparent" })
                foreach (int shadow in new[] { 0, 1, 2 })
                    foreach (int oit in family == "chunktransparent" ? new[] { 1 } : new[] { 0, 1 })
                        yield return [family, shadow, oit, 0, 0, 1, lumon];
            foreach (string family in new[] { "standard", "entityanimated", "instanced", "chunktransparent" })
                yield return [family, 2, family == "chunktransparent" ? 1 : 0, 1, family == "chunktransparent" ? 1 : 0, 0, lumon];
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
            bool oitOutput = family == "chunktransparent" || (family == "entityanimated" && oit > 0);
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
    /// <summary>Compiles the actual sky dome and expanded engine sky lookup with atmosphere interception.</summary>
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
            Assert.True(GL.GetUniformLocation(program, "vge_atmosphereSky") >= 0);
            Assert.True(GL.GetUniformLocation(program, "vge_atmosphereLutHorizon") >= 0);
            // The expanded engine lookup precedes our helper definitions. Linking this real
            // ordering must retain the per-pixel Mie reconstruction and its sun input.
            Assert.True(GL.GetUniformLocation(program, "vge_atmosphereSunDirection") >= 0);
            Assert.Equal(6, GL.GetFragDataLocation(program, "vge_outPatchId"));
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
        Assert.Equal(expected ? ShaderCapability.TwoSidedSurfaceNormals : ShaderCapability.None, capabilities);
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
        if (name is not ("sky.vsh" or "chunkshadowmap.fsh" or "chunkshadowmap.vsh")) Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, name, declare));
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
