using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies that real binaries start with the shared GPU contract's slots before runtime binding.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderContractBindingTests : RenderTestBase
{
    /// <summary>Uses the common graphics fixture.</summary>
    public ShaderContractBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Compiled bindings
    /// <summary>Checks source-assigned sampler and UBO slots without runtime validation or rebinding.</summary>
    [Theory]
    [InlineData("lumon_combine")]
    [InlineData("lumon_probe_atlas_trace")]
    [InlineData("lumon_debug_worldprobe")]
    public void GraphicsBindingsAreCompiledFromSharedContract(string name)
    {
        EnsureContextValid();
        int vertex = BuiltShaderFixture.Load(name + ".vsh", ShaderType.VertexShader);
        int fragment = BuiltShaderFixture.Load(name + ".fsh", ShaderType.FragmentShader);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment);
            TestShaderInterfaces.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            var contract = GpuShaderContracts.Create(name);
            var layout = TestShaderInterfaces.BuildLayout(program);
            layout.RegisterContract(contract);
            int samplerCount = 0;
            foreach (var (uniform, expected) in contract.Samplers)
            {
                int location = TestShaderInterfaces.GetUniformLocation(program, uniform);
                if (location < 0) continue;
                GL.GetUniform(program, location, out int actual);
                Assert.Equal(expected.Slot, actual);
                samplerCount++;
            }
            Assert.True(samplerCount >= 2);
            foreach (var (block, expected) in contract.UniformBlocks)
            {
                int index = TestShaderInterfaces.GetUniformBlockIndex(program, block);
                if (index < 0) continue;
                GL.GetActiveUniformBlock(program, index, ActiveUniformBlockParameter.UniformBlockBinding, out int actual);
                Assert.Equal(expected.Slot, actual);
            }
            layout.ApplyContract(program);
            // Applying a compiled contract must not change existing resource slots.
            var active = contract.Samplers.First(p => TestShaderInterfaces.GetUniformLocation(program, p.Key) >= 0);
            int activeLocation = TestShaderInterfaces.GetUniformLocation(program, active.Key);
            GL.UseProgram(program); GL.Uniform1(activeLocation, active.Value.Slot + 1); GL.UseProgram(0);
            layout.ApplyContract(program);
            GL.GetUniform(program, activeLocation, out int unchanged);
            Assert.Equal(active.Value.Slot + 1, unchanged);
        }
        finally
        {
            GL.UseProgram(0);
            TestShaderInterfaces.DeleteProgram(program);
            TestShaderInterfaces.DeleteShader(vertex);
            TestShaderInterfaces.DeleteShader(fragment);
        }
    }
    #endregion
}
