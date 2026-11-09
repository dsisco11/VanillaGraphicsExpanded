using VanillaGraphicsExpanded.Tests.Support.Terrain;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Terrain;

/// <summary>Checks tessellation source preparation and owned shader-prefix lifecycle.</summary>
[Collection("CPU state")]
public sealed class TerrainTessellationSourcePreparationTests
{
    #region Public API
    #region Runtime asset preparation
    /// <summary>Reload reads changed stage assets and expands the shared imports from the supplied asset API.</summary>
    [Fact]
    public void StageAssetsAreReloadedAndSharedImportsExpanded()
    {
        // Arrange the production tessellation loader with a controlled shader asset API.
        using var fixture = new BinaryShaderApiFixture();

        // Act by loading the baseline stage assets through the fixture.
        var initial = TerrainTessellationAssets.Load(fixture.Api.Assets);

        // Assert shared imports are expanded before installing the test override.
        Assert.Contains("VgeHeight", initial.Evaluation);
        Assert.DoesNotContain("@import", initial.Evaluation);
        const string path = "shaders/includes/tessellation/terrain.tesh";
        string original = TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets", path));
        fixture.Overrides[path] = System.Text.Encoding.UTF8.GetBytes(original + "\n// reload-marker\n");

        // Act by loading the same authored stages through the changed fixture asset.
        var reloaded = TerrainTessellationAssets.Load(fixture.Api.Assets);

        // Assert the first result remains an immutable snapshot and the second reflects the override.
        Assert.DoesNotContain("reload-marker", initial.Evaluation);
        Assert.Contains("reload-marker", reloaded.Evaluation);
    }

    /// <summary>A missing tessellation include retires previously prepared stages instead of using stale metadata.</summary>
    [Fact]
    public void MissingAssetRetiresPreparedStages()
    {
        // Arrange a controlled shader API and an owner with a valid initial vertex stage.
        using var fixture = new BinaryShaderApiFixture();
        var owner = new ShaderProgram
        {
            PassName = "chunkopaque", AssetDomain = "game",
            VertexShader = new Shader { Code = TerrainTessellationTestAssets.RequiredOutputs + "\nout vec4 rgba; void main() { gl_Position=vec4(0); }" },
            FragmentShader = new Shader()
        };

        // Act by preparing the valid stage and assert its generated metadata is admitted.
        TerrainTessellationPatches.Prepare(owner, fixture.Api.Assets);
        Assert.True(TerrainTessellationPatches.TryGet(owner.VertexShader, out _));

        // Act on a subsequent missing include, then reprepare to exercise retirement of stale state.
        fixture.MissingAssets.Add("shaders/includes/tessellation/terrain.tesh");
        Assert.Throws<InvalidOperationException>(() => TerrainTessellationAssets.Load(fixture.Api.Assets));
        TerrainTessellationPatches.Prepare(owner, fixture.Api.Assets);

        // Assert the prior prepared stage is no longer visible after the failed reload.
        Assert.False(TerrainTessellationPatches.TryGet(owner.VertexShader, out _));
    }
    #endregion

    #region Interface generation
    /// <summary>Interpolation qualifiers on declared outputs drive generated control and evaluation stages.</summary>
    [Fact]
    public void FlatAndSmoothDeclarationsDriveInterpolation()
    {
        // Arrange source with distinct flat and smooth output declarations.
        const string source = """
            #version 430 core
            flat /* metadata */ out vec2 customAtlas;
            smooth out vec3 customColor;
            void main() { gl_Position=vec4(0); }
            """ + TerrainTessellationTestAssets.RequiredOutputs;

        // Act by generating the linked tessellation stages from this declaration source.
        var stages = TerrainTessellationTestAssets.Generate(source);

        // Assert qualifiers and endpoint selection are carried into the appropriate generated stages.
        Assert.Contains("flat in vec2 customAtlas[];", stages.Control);
        Assert.Contains("customAtlas = tc_customAtlas[2];", stages.Evaluation);
        Assert.Contains("smooth out vec3 customColor;", stages.Evaluation);
        Assert.Contains("customColor = tc_customColor[0] * gl_TessCoord.x", stages.Evaluation);
    }

    /// <summary>Variant guards remain around generated interfaces and their copy statements.</summary>
    [Fact]
    public void ConditionalOutputsRetainPresenceGuards()
    {
        // Arrange guarded outputs and position assignments that differ by shader variant.
        const string source = """
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
            """ + TerrainTessellationTestAssets.RequiredOutputs;

        // Act by generating control and evaluation stages for the guarded interface.
        var stages = TerrainTessellationTestAssets.Generate(source);

        // Assert declaration guards and generated presence macros remain, while stage-local body code is not copied.
        Assert.Contains("#extension GL_ARB_shader_draw_parameters : enable", stages.Control);
        Assert.Contains("#if SSAOLEVEL > 0", stages.Control);
        Assert.Contains("#define VGE_TESS_OUTPUT_0 1", stages.Control);
        Assert.Contains("#if defined(VGE_TESS_OUTPUT_0)", stages.Control);
        Assert.Contains("#if defined(VGE_TESS_OUTPUT_1)", stages.Evaluation);
        Assert.DoesNotContain("gl_Position=vec4(1)", stages.Evaluation);
    }

    /// <summary>Comment text and input declarations do not become tessellation output interfaces.</summary>
    [Fact]
    public void CommentsAndInputsAreIgnored()
    {
        // Arrange a source containing a commented output, an input, and one real output.
        const string source = """
            #version 430 core
            // flat out int fakeOutput;
            in vec4 position;
            out vec4 rgba;
            void main() { gl_Position=position; }
            """ + TerrainTessellationTestAssets.RequiredOutputs;

        // Act by generating the control interface from source declarations.
        var stages = TerrainTessellationTestAssets.Generate(source);

        // Assert only the real output is represented in the generated interface.
        Assert.DoesNotContain("fakeOutput", stages.Control);
        Assert.DoesNotContain("tc_position", stages.Control);
        Assert.Contains("tc_rgba", stages.Control);
    }

    /// <summary>Unsupported output shapes fail during generation before a candidate can be admitted.</summary>
    [Theory]
    [InlineData("out vec4 colors[2];")]
    [InlineData("out mat4 transform;")]
    [InlineData("out int material;")]
    [InlineData("out Block { vec4 color; } instance;")]
    public void UnsupportedInterfacesAreRejected(string declaration)
    {
        // Arrange each unsupported declaration with an otherwise valid shader entry point.
        string source = declaration + "\nvoid main() { gl_Position=vec4(0); }";

        // Act and assert generation rejects unsupported type/interface shapes.
        Assert.Throws<NotSupportedException>(() => TerrainTessellationTestAssets.Generate(source));
    }
    #endregion

    #region Engine macro lifecycle
    /// <summary>Reconfiguration replaces only VGE's enable macro and preserves prefixes owned by other shaders.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConfigureDoesNotAccumulateDefinesOrEraseEnginePrefix(int ordinaryMode)
    {
        // Arrange an engine shader owner with unrelated vertex and fragment prefixes.
        var owner = new ShaderProgram
        {
            PassName = "chunkopaque", AssetDomain = "game",
            VertexShader = new Shader { Code = """
                out vec4 rgba;
                out vec4 worldPos;
                out vec3 normal;
                out vec2 uv;
                flat out vec2 vge_uvBase;
                flat out vec2 vge_uvExtent;
                flat out int renderFlags;
                void main() { gl_Position=vec4(0); }
                """, PrefixCode = "#define USESSBO 1\n" },
            FragmentShader = new Shader { PrefixCode = "#define SSAOLEVEL 1\n" }
        };
        var previousMode = ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable;
        bool previousMeshHook = TerrainTessellationPrograms.MeshDrawHookAvailable;
        try
        {
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = 2;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPrograms.DrawHookAvailable = true;
            TerrainTessellationPrograms.MeshDrawHookAvailable = true;

            // Act by applying configuration twice and then moving back to an ordinary mode.
            TerrainTessellationPatches.Configure(owner);
            string initial = owner.VertexShader.PrefixCode;
            TerrainTessellationPatches.Configure(owner);

            // Assert idempotent configuration and preservation of both foreign prefixes before disabling tessellation.
            Assert.Equal(initial, owner.VertexShader.PrefixCode);
            Assert.Equal("#define USESSBO 1\n" + TerrainTessellationPatches.EnabledDefine, initial);
            Assert.Equal("#define SSAOLEVEL 1\n" + TerrainTessellationPatches.EnabledDefine, owner.FragmentShader.PrefixCode);

            // Act by switching to the parameterized ordinary mode.
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = ordinaryMode;
            TerrainTessellationPatches.Configure(owner);

            // Assert VGE switches its own define off without erasing the engine-owned prefix.
            Assert.DoesNotContain("#define VGE_ENABLE_TESSELLATION 1", owner.VertexShader.PrefixCode);
            Assert.StartsWith("#define USESSBO 1\n", owner.VertexShader.PrefixCode);
            Assert.Contains("#define VGE_ENABLE_TESSELLATION 0", owner.VertexShader.PrefixCode);
        }
        finally
        {
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = previousMode;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook;
            TerrainTessellationPrograms.MeshDrawHookAvailable = previousMeshHook;
        }
    }
    #endregion
    #endregion
}
