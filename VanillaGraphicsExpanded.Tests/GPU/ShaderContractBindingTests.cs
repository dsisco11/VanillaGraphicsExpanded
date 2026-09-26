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
    [InlineData("lumon_debug_view_world_probe_irradiance_combined")]
    [InlineData("lumon_probe_anchor")]
    [InlineData("lumon_probe_atlas_temporal")]
    [InlineData("lumon_probe_atlas_filter")]
    [InlineData("lumon_probe_atlas_gather")]
    [InlineData("lumon_probe_sh9_gather")]
    [InlineData("lumon_upsample")]
    [InlineData("lumon_debug_view_direct_total")]
    public void GraphicsBindingsAreCompiledFromSharedContract(string name)
    {
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = BuiltShaderFixture.Load(name.StartsWith("lumon_debug_view_", StringComparison.Ordinal) ? "lumon_debug.vsh" : name + ".vsh", ShaderType.VertexShader);
            // World-disabled views intentionally omit the world samplers. Exercise their enabled interface.
            Dictionary<string, string?>? defines = name == "lumon_debug_view_world_probe_irradiance_combined"
                ? new() { ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1", ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
                    ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = "8", ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "16" }
                : null;
            fragment = BuiltShaderFixture.Load(name + ".fsh", ShaderType.FragmentShader, defines);
            program = GL.CreateProgram();
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
            GL.GetProgramInterface(program, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int uniformCount);
            int[] locationValue = new int[1];
            for (int index = 0; index < uniformCount; index++)
            {
                GL.GetProgramResource(program, ProgramInterface.Uniform, index, 1,
                    [ProgramProperty.Location], 1, out _, locationValue);
                if (locationValue[0] >= 0)
                    Assert.Contains(locationValue[0], contract.UniformLocations.Values);
            }
            // Enumerate the actual driver resources as well: iterating only resolved contract
            // names could silently skip a block whose binary binding moved to an undeclared slot.
            GL.GetProgram(program, GetProgramParameterName.ActiveUniformBlocks, out int blockCount);
            for (int index = 0; index < blockCount; index++)
            {
                GL.GetActiveUniformBlock(program, index, ActiveUniformBlockParameter.UniformBlockBinding, out int actual);
                Assert.Contains(contract.UniformBlocks.Values, expected => expected.Slot == actual);
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
            if (program != 0) TestShaderInterfaces.DeleteProgram(program);
            if (vertex != 0) TestShaderInterfaces.DeleteShader(vertex);
            if (fragment != 0) TestShaderInterfaces.DeleteShader(fragment);
        }
    }
    #endregion
}
