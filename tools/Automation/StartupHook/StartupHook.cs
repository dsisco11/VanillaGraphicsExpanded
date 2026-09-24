using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>Installs automation window policy before the game creates its first window.</summary>
internal static class StartupHook
{
    #region Startup entry point

    /// <summary>Activates only in the explicitly configured automation game process.</summary>
    public static void Initialize()
    {
        if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("AUTOMATION_ID"), "N", out _) ||
            !string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "Vintagestory", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The game's assembly resolver is not installed until Main. Resolve the same
        // installation's libraries while applying the hook, without copying engine binaries.
        AssemblyLoadContext.Default.Resolving += ResolveGameLibrary;
        Automation.WindowActivationPolicy.Install();
    }

    #endregion

    #region Dependency resolution

    /// <summary>Resolves dependencies from the running game's library directory.</summary>
    private static Assembly? ResolveGameLibrary(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name) || Path.GetFileName(name.Name) != name.Name) return null;
        string path = Path.Combine(AppContext.BaseDirectory, "Lib", name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }

    #endregion
}
