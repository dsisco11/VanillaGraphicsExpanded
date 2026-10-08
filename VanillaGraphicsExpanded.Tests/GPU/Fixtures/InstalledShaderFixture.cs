using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Compiles patched installed vanilla GLSL through the existing driver fixture.</summary>
internal sealed class InstalledShaderFixture : IDisposable
{
    private readonly GpuProgramObject executable;
    internal ShaderProgram Engine { get; }
    internal GpuProgramLayout Layout { get; }

    #region Public API
    /// <summary>Uses original engine sources and reflected driver locations without a separate binary contract.</summary>
    internal InstalledShaderFixture(string name, int ssao = 0)
    {
        ShaderCapability capabilities = ShaderCapability.None;
        int oit = name == "particlesquad2d" ? 1 : 0;
        using var stages = new TerrainShaderTestFixture();
        int vertex = stages.Compile(ShaderType.VertexShader,
            PbrSurfaceInstalledShaderTests.Build(name + ".vsh", 0, oit, ssao, 0, 0));
        int fragment = stages.Compile(ShaderType.FragmentShader,
            PbrSurfaceInstalledShaderTests.Build(name + ".fsh", 0, oit, ssao, 0, 0, value => capabilities |= value));
        executable = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        try
        {
            Layout = GpuProgramLayout.TryBuild(executable.ProgramId);
            Engine = name switch
            {
                "standard" => new ShaderProgramStandard(),
                "particlescube" => new ShaderProgramParticlescube(),
                "sky" => new ShaderProgramSky(),
                "final" => new ShaderProgramFinal(),
                "particlesquad2d" => new ShaderProgramParticlesquad2d(),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            Engine.PassName = name;
            Engine.ProgramId = executable.ProgramId;
            // Vanilla setters use the linked GLSL names. Import actual active uniforms,
            // including initializer-backed uniforms, rather than assigning numeric slots.
            var uniforms = (Dictionary<string, int>)AccessTools.Field(typeof(ShaderProgramBase), "uniformLocations").GetValue(Engine)!;
            GL.GetProgram(executable.ProgramId, GetProgramParameterName.ActiveUniforms, out int count);
            for (int index = 0; index < count; index++)
            {
                string uniform = GL.GetActiveUniform(executable.ProgramId, index, out _, out _);
                int location = Layout.GetUniformLocation(executable.ProgramId, uniform);
                if (location >= 0) uniforms[uniform] = location;
            }
            ShaderCapabilities.Publish(Engine, capabilities);
        }
        catch { executable.Dispose(); throw; }
    }

    /// <summary>Withdraws capability metadata before retiring the executable owned exclusively by this fixture.</summary>
    public void Dispose()
    {
        ShaderCapabilities.Forget(Engine);
        Engine.ProgramId = 0;
        executable.Dispose();
    }
    #endregion
}
