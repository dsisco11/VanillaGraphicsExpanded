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
    [InlineData("lumon_combine", true)]
    [InlineData("lumon_probe_atlas_trace", true)]
    [InlineData("lumon_probe_atlas_pis_mask", true)]
    [InlineData("lumon_debug_view_world_probe_irradiance_combined", true)]
    [InlineData("lumon_probe_anchor", true)]
    [InlineData("lumon_probe_atlas_temporal", true)]
    [InlineData("lumon_probe_atlas_filter", true)]
    [InlineData("lumon_probe_atlas_gather", true)]
    [InlineData("lumon_probe_sh9_gather", true)]
    [InlineData("lumon_upsample", true)]
    [InlineData("lumon_debug_view_direct_total", true)]
    [InlineData("lumon_combine", false)]
    [InlineData("lumon_debug_view_world_probe_irradiance_combined", false)]
    public void GraphicsBindingsAreCompiledFromSharedContract(string name, bool enabled)
    {
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            string vertexIdentity = name.StartsWith("lumon_debug_view_", StringComparison.Ordinal) ? "lumon_debug.vsh"
                : name == "lumon_probe_atlas_pis_mask" ? "lumon_probe_atlas_trace.vsh" : name + ".vsh";
            vertex = BuiltShaderFixture.Load(vertexIdentity, ShaderType.VertexShader);
            // World-disabled views intentionally omit the world samplers. Exercise their enabled interface.
            Dictionary<string, string?>? defines = name == "lumon_debug_view_world_probe_irradiance_combined"
                ? new() { ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1", ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
                    ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = "8", ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "16" }
                : null;
            if (!enabled)
            {
                defines ??= new();
                defines[name == "lumon_combine" ? "VGE_LUMON_ENABLED" : "VGE_LUMON_WORLDPROBE_ENABLED"] = "0";
            }
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
            Assert.True(samplerCount >= (enabled ? 2 : 1));
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

    #region Atmosphere storage
    /// <summary>Real atmosphere SPIR-V retains the owner-declared SSBO indices without runtime rebinding.</summary>
    [Theory]
    [InlineData("atmosphere_scattering", 2)]
    [InlineData("atmosphere_sky", 3)]
    [InlineData("atmosphere_lighting", 2)]
    public void AtmosphereStorageBindingsAreCompiledFromFields(string name, int expectedBlocks)
    {
        EnsureContextValid();
        int shader = 0, program = 0;
        try
        {
            shader = BuiltShaderFixture.Load(name + ".csh", ShaderType.ComputeShader);
            program = GL.CreateProgram();
            GL.AttachShader(program, shader);
            TestShaderInterfaces.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            var contract = GpuShaderContracts.Create(name);
            GL.GetProgramInterface(program, ProgramInterface.ShaderStorageBlock, ProgramInterfaceParameter.ActiveResources, out int count);
            Assert.Equal(expectedBlocks, count);
            var actual = new List<int>();
            // Enumerate the linked interface; checking only resolved names could skip an erased or
            // optimized resource and falsely accept a binary built from obsolete declarations.
            for (int index = 0; index < count; index++)
            {
                int[] binding = new int[1];
                GL.GetProgramResource(program, ProgramInterface.ShaderStorageBlock, index, 1,
                    [ProgramProperty.BufferBinding], 1, out _, binding);
                actual.Add(binding[0]);
            }
            Assert.Equal(contract.StorageBlocks.Values.Select(b => b.Slot).Order(), actual.Order());
        }
        finally
        {
            if (program != 0) TestShaderInterfaces.DeleteProgram(program);
            if (shader != 0) TestShaderInterfaces.DeleteShader(shader);
        }
    }
    #endregion
}
