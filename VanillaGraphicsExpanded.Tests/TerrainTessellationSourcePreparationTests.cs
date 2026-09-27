using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks typed interface generation and engine-owned prefix lifecycle.</summary>
[Collection("GPU")]
public sealed class TerrainTessellationSourcePreparationTests
{
    #region Interface generation
    /// <summary>Third-party names use their declared interpolation rather than a name allowlist.</summary>
    [Fact]
    public void FlatAndSmoothDeclarationsDriveInterpolation()
    {
        var stages = TerrainTessellationStages.Generate("""
            #version 430 core
            flat /* metadata */ out vec2 customAtlas;
            smooth out vec3 customColor;
            void main() { gl_Position=vec4(0); }
            """);
        Assert.Contains("flat in vec2 customAtlas[];", stages.Control);
        Assert.Contains("customAtlas = tc_customAtlas[2];", stages.Evaluation);
        Assert.Contains("smooth out vec3 customColor;", stages.Evaluation);
        Assert.Contains("customColor = tc_customColor[0] * gl_TessCoord.x", stages.Evaluation);
    }

    /// <summary>Variant guards remain around interfaces and guard generated copy statements.</summary>
    [Fact]
    public void ConditionalOutputsRetainPresenceGuards()
    {
        var stages = TerrainTessellationStages.Generate("""
            #version 430 core
            #extension GL_ARB_shader_draw_parameters : enable
            #if SSAOLEVEL > 0
            out vec4 gnormal;
            #else
            flat out int customFace;
            #endif
            void main() {
            #if USESSBO > 0
                gl_Position=vec4(1);
            #else
                gl_Position=vec4(0);
            #endif
            }
            """);
        Assert.Contains("#extension GL_ARB_shader_draw_parameters : enable", stages.Control);
        Assert.Contains("#if SSAOLEVEL > 0", stages.Control);
        Assert.Contains("#define VGE_TESS_OUTPUT_0 1", stages.Control);
        Assert.Contains("#if defined(VGE_TESS_OUTPUT_0)", stages.Control);
        Assert.Contains("#if defined(VGE_TESS_OUTPUT_1)", stages.Evaluation);
        Assert.DoesNotContain("gl_Position=vec4(1)", stages.Evaluation);
    }

    /// <summary>Comment text and input declarations do not become tessellation outputs.</summary>
    [Fact]
    public void CommentsAndInputsAreIgnored()
    {
        var stages = TerrainTessellationStages.Generate("""
            #version 430 core
            // flat out int fakeOutput;
            in vec4 position;
            out vec4 rgba;
            void main() { gl_Position=position; }
            """);
        Assert.DoesNotContain("fakeOutput", stages.Control);
        Assert.DoesNotContain("tc_position", stages.Control);
        Assert.Contains("tc_rgba", stages.Control);
    }

    /// <summary>Unsupported interfaces fail before a candidate can replace the ordinary executable.</summary>
    [Theory]
    [InlineData("out vec4 colors[2];")]
    [InlineData("out mat4 transform;")]
    [InlineData("out int material;")]
    [InlineData("out Block { vec4 color; } instance;")]
    public void UnsupportedInterfacesAreRejected(string declaration)
    {
        Assert.Throws<NotSupportedException>(() => TerrainTessellationStages.Generate(declaration + "\nvoid main() { gl_Position=vec4(0); }"));
    }
    #endregion

    #region Engine macro lifecycle
    /// <summary>Reconfiguration replaces only VGE's own enable macro and preserves other shader owners' prefixes.</summary>
    [Fact]
    public void ConfigureDoesNotAccumulateDefinesOrEraseEnginePrefix()
    {
        var owner = new ShaderProgram
        {
            PassName="chunkopaque", AssetDomain="game",
            VertexShader=new Shader { Code="out vec4 rgba; void main() { gl_Position=vec4(0); }", PrefixCode="#define USESSBO 1\n" },
            FragmentShader=new Shader { PrefixCode="#define SSAOLEVEL 1\n" }
        };
        int previousLevel=ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel;
        bool previousHook=TerrainTessellationPrograms.DrawHookAvailable;
        try
        {
            TerrainTessellationPatches.Prepare(owner);
            TerrainTessellationPrograms.DrawHookAvailable=true;
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel=4;
            TerrainTessellationPatches.Configure(owner);
            string initial=owner.VertexShader.PrefixCode;
            TerrainTessellationPatches.Configure(owner);
            Assert.Equal(initial, owner.VertexShader.PrefixCode);
            Assert.Equal("#define USESSBO 1\n" + TerrainTessellationPatches.EnabledDefine, initial);
            Assert.Equal("#define SSAOLEVEL 1\n" + TerrainTessellationPatches.EnabledDefine, owner.FragmentShader.PrefixCode);
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel=0;
            TerrainTessellationPatches.Configure(owner);
            Assert.DoesNotContain("#define VGE_ENABLE_TESSELLATION 1", owner.VertexShader.PrefixCode);
            Assert.StartsWith("#define USESSBO 1\n", owner.VertexShader.PrefixCode);
            Assert.Contains("#define VGE_ENABLE_TESSELLATION 0", owner.VertexShader.PrefixCode);
        }
        finally
        {
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel=previousLevel;
            TerrainTessellationPrograms.DrawHookAvailable=previousHook;
        }
    }
    #endregion
}
