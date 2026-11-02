using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns explicitly prepared production observation executables for one fixture context and manager.</summary>
internal sealed class GpuObservationPrograms : IDisposable
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private readonly RHIResourceManager manager = RHIResourceManagerSystem.EnsureCurrentContext();
    private static readonly Lazy<ShaderVariantResolver> declarations = new(ShaderBuildTool.Spirv.TestShaderPrograms.Create);
    private readonly Dictionary<(string Family, int Kind, int Expression), GpuAssertionProgram> programs = [];
    private bool disposed;

    #region Public API
    /// <summary>Attempts every original executable and scratch retirement even if a preceding owner fails.</summary>
    public void Dispose()
    {
        if (disposed) return;
        if (RenderContextRegistry.IsAlive(origin) && !RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("Cannot retire observation owners in a foreign live context.");
        List<Exception> failures = [];
        foreach (var program in programs.Values)
            try { program.Dispose(); } catch (Exception error) { failures.Add(error); }
        programs.Clear();
        disposed = true;
        if (failures.Count != 0) throw new AggregateException("Observation owner retirement failed.", failures);
    }
    #endregion

    #region Internal API
    /// <summary>Counts distinct successfully prepared production shader owners in this original context.</summary>
    internal int PreparedCount => programs.Count;
    /// <summary>Measures successful cold-owner preparation separately from observation execution.</summary>
    internal TimeSpan PreparationElapsed { get; private set; }

    /// <summary>Owns synchronous result storage independently of subjects and their snapshots.</summary>
    internal GpuObservationDispatch Dispatch { get; } = new();

    /// <summary>Reports whether the original manager can still authorize these preparations.</summary>
    internal bool IsRetired => disposed || manager.IsMetadataRegistryRetired;

    /// <summary>Prepares exactly one declared executable during ordinary fixture setup.</summary>
    internal void Prepare(string family, int kind = 0, int expression = 0)
    {
        RequireContext();
        var key = (family, kind, expression);
        if (programs.ContainsKey(key)) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var contract = declarations.Value.Programs[$"tests/assertion-{family}-{kind}-{expression}"];
        var settings = new ShaderSettings(contract);
        var selected = new ShaderLoadPlan(settings).Stages.Single();
        string binary = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", selected.BinaryPath);
        if (!ComputeShaderAsset.TryLoadFromSpirv(binary, settings, out var asset, out string log,
            fileSystem: VanillaGraphicsExpanded.Tests.Helpers.ShaderAssetFileSystem.Built))
            throw new InvalidOperationException("Could not prepare observation asset: " + log);
        try
        {
            programs.Add(key, new GpuAssertionProgram(asset!));
            PreparationElapsed += System.Diagnostics.Stopwatch.GetElapsedTime(started);
        }
        catch (Exception error)
        {
            try { asset!.Dispose(); } catch (Exception cleanup) { error.Data["ObservationAssetCleanup"] = cleanup; }
            throw;
        }
    }

    /// <summary>Borrows an already prepared executable; observation never triggers loading or compilation.</summary>
    internal GpuAssertionProgram Get(string family, int kind = 0, int expression = 0)
    {
        RequireContext();
        if (!programs.TryGetValue((family, kind, expression), out var program))
            throw new InvalidOperationException($"Prepare observation {family}/{kind}/{expression} before entering the observation boundary.");
        program.RequireContext();
        return program;
    }

    /// <summary>Rejects retired manager authority or a replacement physical context before touching resources.</summary>
    internal void RequireContext()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (manager.IsMetadataRegistryRetired || !RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("Observation requires its original fixture context and live resource manager.");
    }
    #endregion
}

