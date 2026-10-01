using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies procedural engine labels on real compiled binary program objects.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineShaderDebugLabelsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Label lifecycle
    /// <summary>Names each stage and each replacement executable using engine pass metadata.</summary>
    [Theory]
    [InlineData("tests/render_infrastructure", "game", "chunkopaque")]
    [InlineData("vge_worldprobe_orbs_points", "examplemod", "custom-pass")]
    public void CompiledBinaryObjectsReceiveProceduralLabels(string shader, string domain, string pass)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram(shader);
        program.Initialize(assets.Api);
        int previous = 0;
        for (int generation = 0; generation < 2; generation++)
        {
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.NotEqual(previous, program.ProgramId);
            previous = program.ProgramId;
            // Borrow live binary handles without transferring ownership to this engine metadata object.
            var engine = new ShaderProgram
            {
                AssetDomain = domain, PassName = pass, ProgramId = program.ProgramId,
                VertexShader = program.VertexShader, FragmentShader = program.FragmentShader,
                GeometryShader = program.GeometryShader
            };
            EngineShaderDebugLabelsHook.Postfix(engine, true);
#if DEBUG
            Assert.Equal($"{domain}:{pass}", Label(ObjectLabelIdentifier.Program, engine.ProgramId));
            Assert.Equal($"{domain}:{pass}.vertex", Label(ObjectLabelIdentifier.Shader, engine.VertexShader.ShaderId));
            Assert.Equal($"{domain}:{pass}.fragment", Label(ObjectLabelIdentifier.Shader, engine.FragmentShader.ShaderId));
            if (engine.GeometryShader is { } geometry)
                Assert.Equal($"{domain}:{pass}.geometry", Label(ObjectLabelIdentifier.Shader, geometry.ShaderId));
            engine.AssetDomain = Constants.ModId;
            engine.PassName = "must-not-relabel";
            EngineShaderDebugLabelsHook.Postfix(engine, true);
            Assert.Equal($"{domain}:{pass}", Label(ObjectLabelIdentifier.Program, engine.ProgramId));
            engine.AssetDomain = domain;
            EngineShaderDebugLabelsHook.Postfix(engine, false);
            Assert.Equal($"{domain}:{pass}", Label(ObjectLabelIdentifier.Program, engine.ProgramId));
#endif
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
    }

    /// <summary>Reads the driver label to verify the abstraction reached the actual GL object.</summary>
    private static string Label(ObjectLabelIdentifier kind, int id)
    {
        GL.GetObjectLabel(kind, id, 512, out _, out string label);
        return label;
    }
    #endregion

    #region Binary fixture
    /// <summary>Loads existing SPIR-V assets through the production shader abstraction.</summary>
    private sealed class FixtureProgram : GpuProgram
    {
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override void Submit() { }
        #endregion

        /// <summary>Selects an existing binary contract and initializes engine stage holders.</summary>
        internal FixtureProgram(string name)
        {
            PassName = name;
            VertexShader = new Shader();
            FragmentShader = new Shader();
        }

        /// <summary>Uses the existing compiled resource contract for the selected fixture.</summary>
        protected override GpuProgramLayout CreateLayout()
        {
            var layout = new GpuProgramLayout();
            layout.RegisterContract(GpuShaderContracts.Create(ShaderName));
            return layout;
        }
    }
    #endregion
}

