using System.Reflection;
using System.Runtime.Loader;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Resolves optional engine dependencies during complete installed-type inventory without copying the game.</summary>
internal sealed class EngineDependencyResolution : IDisposable
{
    private readonly string gamePath;

    #region Lifetime
    /// <summary>Uses the same installed game selected by the project's engine references.</summary>
    internal EngineDependencyResolution()
    {
        gamePath = Environment.GetEnvironmentVariable("VINTAGE_STORY")
            ?? throw new InvalidOperationException("VINTAGE_STORY is required for complete engine inventory tests.");
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    /// <summary>Removes this test's resolver after the inventory operation.</summary>
    public void Dispose() => AssemblyLoadContext.Default.Resolving -= Resolve;

    /// <summary>Probes only the installed engine's root and managed library directory.</summary>
    private Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (string directory in new[] { gamePath, Path.Combine(gamePath, "Lib") })
        {
            string path = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        return null;
    }
    #endregion
}
