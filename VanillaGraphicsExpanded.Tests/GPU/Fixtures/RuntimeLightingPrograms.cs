using Moq;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models the engine registry while production startup creates and registers every shader.</summary>
internal sealed class RuntimeLightingPrograms : IDisposable
{
    private readonly Dictionary<string, GpuProgram> programs = new();
    private readonly HashSet<string> requested = new();
    public IReadOnlyCollection<string> Loaded => requested;
    public IShaderAPI Api { get; }

    #region Engine registration
    /// <summary>Accepts the same registration and lookup calls as the game shader service.</summary>
    public RuntimeLightingPrograms()
    {
        var shader = new Mock<IShaderAPI>(MockBehavior.Strict);
        shader.Setup(api => api.NewShader(It.IsAny<EnumShaderType>())).Returns(() => new Vintagestory.Client.NoObf.Shader());
        shader.Setup(api => api.RegisterMemoryShaderProgram(It.IsAny<string>(), It.IsAny<IShaderProgram>()))
            .Returns((string name, IShaderProgram program) => Register(name, (GpuProgram)program));
        shader.Setup(api => api.GetProgramByName(It.IsAny<string>())).Returns((string name) => Get(name));
        Api = shader.Object;
    }
    /// <summary>Invokes the production registration entry without running unrelated mod UI or Harmony startup.</summary>
    public void Initialize(ICoreClientAPI api)
    {
        Assert.True(VgeShaderPrograms.RegisterAll(api));
        Assert.NotEmpty(programs);
        Assert.All(programs.Values, program => Assert.True(program.ProgramId != 0, $"Shader registration failed: {program.PassName}"));
    }

    /// <summary>Records actual consumer lookups separately from startup registration.</summary>
    private GpuProgram Get(string name)
    {
        requested.Add(name);
        return programs[name];
    }

    /// <summary>Retains the production-created program under its registered engine name.</summary>
    private int Register(string name, GpuProgram program)
    {
        programs.Add(name, program);
        return program.ProgramId;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases engine-owned programs after production consumers have stopped.</summary>
    public void Dispose()
    {
        foreach (var program in programs.Values) program.Dispose();
        programs.Clear();
    }
    #endregion
}
