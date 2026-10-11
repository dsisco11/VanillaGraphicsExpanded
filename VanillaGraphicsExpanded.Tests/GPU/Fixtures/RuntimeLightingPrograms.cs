using Moq;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes prepared VGE declarations after production consumers select shaders.</summary>
internal sealed class RuntimeLightingPrograms : IDisposable
{
    private ICoreClientAPI? owner;
    public IReadOnlyCollection<string> Loaded => owner is null ? [] : GpuShaderPrograms.GetAll(owner).Where(program => program.IsLinked).Select(program => program.PassName).ToArray();
    public IShaderAPI Api { get; }

    #region Declaration observation
    /// <summary>Rejects accidental engine shader calls through a strict native service fixture.</summary>
    public RuntimeLightingPrograms()
    {
        var shader = new Mock<IShaderAPI>(MockBehavior.Strict);
        Api = shader.Object;
    }
    /// <summary>Invokes the production registration entry without running unrelated mod UI or Harmony startup.</summary>
    public void Initialize(ICoreClientAPI api)
    {
        owner = api;
        Assert.True(VgeShaderPrograms.RegisterAll(api));
        Assert.NotEmpty(GpuShaderPrograms.GetAll(api));
        Assert.Empty(Loaded);
    }

    #endregion

    #region Lifetime
    /// <summary>Releases VGE-owned programs after production consumers have stopped.</summary>
    public void Dispose()
    {
        if (owner is not null) GpuShaderPrograms.Dispose(owner);
    }
    #endregion
}
