using System;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Supplies engine asset reads and client admission to the compute asset policy owner.</summary>
internal sealed partial class ComputeShaderAsset
{
    #region Internal API
    /// <summary>Loads a complete shader/pipeline set from typed package settings with original-context creation.</summary>
    internal static bool TryCreateFromAssets(ICoreAPI api, ShaderSettings settings,
        out ComputeShaderAsset? pipeline, out string infoLog, string? debugName = null,
        ILogger? log = null, CancellationToken ct = default, GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ct.ThrowIfCancellationRequested();
        pipeline = null;
        try
        {
            ReadOnlySpan<byte> Read(string path) => api.Assets.TryGet(AssetLocation.Create("shaders/" + path,
                PBR.ShaderImportsSystem.DefaultDomain), loadAsset: true)?.Data
                ?? throw new InvalidOperationException("Missing built compute shader asset: " + path);
            pipeline = Load(settings, Read, () => ShaderDigestIndexCache.ForAssets(api.Assets, PBR.ShaderImportsSystem.DefaultDomain),
                api.Assets, PBR.ShaderImportsSystem.DefaultDomain, layout, debugName);
            ConfigureReload(pipeline, api, settings, debugName, log, layout);
            infoLog = string.Empty;
            return true;
        }
        catch (OperationCanceledException) { pipeline?.Dispose(); pipeline = null; throw; }
        catch (Exception error) { pipeline?.Dispose(); pipeline = null; infoLog = error.Message; return false; }
    }

    /// <summary>Declares immutable package inputs without reading assets, creating a context or issuing native work.</summary>
    internal static ComputeShaderAsset DeclareFromAssets(ICoreAPI api, ShaderSettings settings,
        string? debugName = null, GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var plan = new ShaderLoadPlan(settings);
        if (plan.Stages.Count != 1 || plan.Stages[0].Stage.Kind != ShaderStageKind.Compute)
            throw new ArgumentException("A compute declaration requires one compute stage.", nameof(settings));
        var owner = new ComputeShaderAsset(layout);
        ConfigureReload(owner, api, settings, debugName, null, layout);
        owner.prepare = owner.Rebuild;
        return owner;
    }
    #endregion

    #region Private
    /// <summary>Retains feature-owned rebuild policy without retaining an engine native-program owner or creating another registry.</summary>
    private static void ConfigureReload(ComputeShaderAsset owner, ICoreAPI api, ShaderSettings settings,
        string? debugName, ILogger? log, GpuProgramLayout? layout)
    {
        owner.RenderingLifetime = api is Vintagestory.API.Client.ICoreClientAPI client ? Integration.ClientRenderingLifetime.Find(client) : null;
        owner.RebuildDiagnostic = () => owner.preparationLog;
        owner.Rebuild = () =>
        {
            TryCreateFromAssets(api, settings, out var candidate, out owner.preparationLog, debugName, log, layout: layout);
            return candidate;
        };
    }
    #endregion
}
