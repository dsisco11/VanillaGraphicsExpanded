using System;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Composes resolved state and binding ownership before invoking an optional engine interruption.</summary>
internal static class EngineBoundaryExecution
{
    #region Public API
    /// <summary>Rejects unavailable incoming shader footprints before work; propagates operation and handoff failures.</summary>
    /// <remarks>Shader activation inside the operation uses existing UseScope owners registered for Shader cleanup.</remarks>
    internal static bool TryRun(EngineBoundaryDeclaration declaration, EngineBoundaryResources resources,
        Action<EngineBoundaryScope> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(resources);
        if (!NativeShaderHandoff.IsInactive) return false;
        var cache = StateCache.Current;
        if (!cache.TryResolveBoundaryProgram(out int nativeProgram)) return false;
        var previous = StateCache.ActiveProgram;
        if (previous is GpuProgram program)
        {
            if (program.RequiresPreparation || program.IsRetired) return false;
            var prepared = program.ProgramLayout.BinaryInterface?.PreparedBindings;
            if (prepared is null) return false;
            resources = resources.Union(EngineBoundaryResources.From(prepared));
        }
        else if (previous is not null) return false;
        // An unowned raw/compute executable has no declared reactivation footprint in this adapter.
        if (previous is null && nativeProgram != 0) return false;
        if (!cache.TryBeginEngineBoundary(declaration, out var scope, resources)) return false;
        scope!.Run(() => operation(scope));
        return true;
    }
    #endregion
}
