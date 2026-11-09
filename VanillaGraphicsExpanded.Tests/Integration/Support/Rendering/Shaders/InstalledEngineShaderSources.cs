using System.Text.RegularExpressions;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;

/// <summary>Expands and patches installed engine GLSL using the production shader preprocessing sequence.</summary>
internal static class InstalledEngineShaderSources
{
    #region Internal API
    /// <summary>Applies imports before expansion and material patches after expansion as the engine does.</summary>
    internal static string Build(string name, int shadow, int oit, int ssao, int ssbo, int depth, Action<ShaderCapability>? declare = null)
    {
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        string original = TestAssetSnapshot.ReadSourceText(Path.Combine(game, "assets/game/shaders", name));
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
            return included.Add(path) ? Expand(TestAssetSnapshot.ReadSourceText(path), Path.GetDirectoryName(path)!) : "";
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
    #endregion
}

