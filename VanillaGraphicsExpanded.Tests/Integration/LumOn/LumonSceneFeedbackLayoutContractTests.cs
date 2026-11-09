using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Checks compiled feedback bindings without depending on optional SPIR-V debug names.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumonSceneFeedbackLayoutContractTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context.</summary>
    public LumonSceneFeedbackLayoutContractTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Requires the mark program's parameters, input samplers and output image.</summary>
    [Fact]
    public void FeedbackMarkPages_SpirvPath_ExposesExpectedBindings()
    {
        // Arrange the production compute program from the built SPIR-V asset.
        EnsureContextValid();
        // Act by loading the compiled program and querying its linked resource interface.
        using var program = ComputeProgram.Create("lumonscene_feedback_mark_pages");
        // Assert its parameter block, unsigned input samplers, and output image bindings.
        AssertBuffer(program.ProgramId, ProgramInterface.UniformBlock, GpuBindingRegistry.Ubo.Object, 16);
        AssertTexture(program.ProgramId, ActiveUniformType.UnsignedIntSampler2D, 0);
        AssertTexture(program.ProgramId, ActiveUniformType.UnsignedIntSampler2D, 1);
        AssertTexture(program.ProgramId, ActiveUniformType.UnsignedIntImage2DArray, 0);
    }

    /// <summary>Requires the compact program's parameters, input samplers and output storage.</summary>
    [Fact]
    public void FeedbackCompactPages_SpirvPath_ExposesExpectedBindings()
    {
        // Arrange the production compute program from the built SPIR-V asset.
        EnsureContextValid();
        // Act by loading the compiled program and querying its linked resource interface.
        using var program = ComputeProgram.Create("lumonscene_feedback_compact_pages");
        // Assert its parameter block, output storage, and both array sampler bindings.
        AssertBuffer(program.ProgramId, ProgramInterface.UniformBlock, GpuBindingRegistry.Ubo.Object, 16);
        AssertBuffer(program.ProgramId, ProgramInterface.ShaderStorageBlock, 0);
        AssertTexture(program.ProgramId, ActiveUniformType.UnsignedIntSampler2DArray, 0);
        AssertTexture(program.ProgramId, ActiveUniformType.UnsignedIntSampler2DArray, 1);
    }

    #endregion

    #region Private
    /// <summary>Finds an active buffer by its baked binding and checks parameter size when specified.</summary>
    private static void AssertBuffer(int program, ProgramInterface kind, int binding, int? size = null)
    {
        GL.GetProgramInterface(program, kind, ProgramInterfaceParameter.ActiveResources, out int count);
        ProgramProperty[] properties = [ProgramProperty.BufferBinding, ProgramProperty.BufferDataSize];
        int[] values = new int[2];
        // SPIR-V preserves binding decorations even when the driver omits resource names.
        for (int i = 0; i < count; i++)
        {
            GL.GetProgramResource(program, kind, i, properties.Length, properties, values.Length, out _, values);
            if (values[0] != binding) continue;
            if (size.HasValue) Assert.Equal(size.Value, values[1]);
            return;
        }
        Assert.Fail($"Missing {kind} at binding {binding}.");
    }

    /// <summary>Checks the actual texture type and baked unit without rebinding or name lookup.</summary>
    private static void AssertTexture(int program, ActiveUniformType type, int unit)
    {
        GL.GetProgramInterface(program, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
        ProgramProperty[] properties = [ProgramProperty.Type, ProgramProperty.Location, ProgramProperty.BlockIndex];
        int[] values = new int[3];
        // Inspect only standalone opaque uniforms, excluding parameter-block members.
        for (int i = 0; i < count; i++)
        {
            GL.GetProgramResource(program, ProgramInterface.Uniform, i, properties.Length, properties, values.Length, out _, values);
            if (values[0] != (int)type || values[1] < 0 || values[2] != -1) continue;
            GL.GetUniform(program, values[1], out int actual);
            if (actual == unit) return;
        }
        Assert.Fail($"Missing {type} at unit {unit}.");
    }

    #endregion

}
