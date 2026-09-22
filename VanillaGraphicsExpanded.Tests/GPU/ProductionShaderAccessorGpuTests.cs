using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks one real generated production accessor through binary loading and replacement ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ProductionShaderAccessorGpuTests : RenderTestBase
{
    /// <summary>Uses the existing shared headless context and production asset fixture.</summary>
    public ProductionShaderAccessorGpuTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Production loading
    /// <summary>A typed structural update selects a real PBR variant and preserves the installed program if loading fails.</summary>
    [Fact]
    public void CompositeGeneratedAccessorSelectsBinaryAndPreservesFailedReplacement()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity,
            VertexShader = new Vintagestory.Client.NoObf.Shader(),
            FragmentShader = new Vintagestory.Client.NoObf.Shader()
        };
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            int first = program.ProgramId;
            program.EnableShortRangeAo = false;
            Assert.Single(assets.ScheduledTasks)();
            assets.ScheduledTasks.Clear();
            Assert.NotEqual(first, program.ProgramId);
            Assert.False(GL.IsProgram(first));
            int installed = program.ProgramId;
            program.EnableShortRangeAo = true;
            assets.Overrides["shaders/pbr_composite.fsh.spv"] = new byte[20];
            Assert.Single(assets.ScheduledTasks)();
            assets.ScheduledTasks.Clear();
            Assert.Equal(installed, program.ProgramId);
            Assert.True(GL.IsProgram(installed));
            assets.Overrides.Clear();
            Assert.True(program.Compile(), string.Join('\n', assets.Logs));
        }
        finally { program.Dispose(); }
    }
    #endregion
}

