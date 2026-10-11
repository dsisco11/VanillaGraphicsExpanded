using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Installs a fixture-owned handle for generated publication tests without transferring native deletion.</summary>
internal sealed class BorrowedProgramFixture : IDisposable
{
    private static readonly FieldInfo Executable = typeof(GpuProgram).GetField("executable", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly GpuProgram owner;
    private readonly GpuProgramObject resource;

    #region Public API
    /// <summary>Temporarily supplies an already linked fixture executable.</summary>
    internal BorrowedProgramFixture(GpuProgram owner, int program)
    {
        this.owner = owner;
        resource = GpuProgramObject.Adopt(program);
        Executable.SetValue(owner, resource);
    }
    /// <summary>Withdraws the borrowed native name before the fixture deletes it.</summary>
    public void Dispose()
    {
        resource.Detach();
        Executable.SetValue(owner, null);
    }
    /// <summary>Withdraws a deliberately externally deleted handle so teardown cannot delete a recycled name.</summary>
    internal static void ForgetDeleted(GpuProgram owner)
    {
        ((GpuProgramObject?)Executable.GetValue(owner))?.Detach();
        Executable.SetValue(owner, null);
    }
    #endregion
}
