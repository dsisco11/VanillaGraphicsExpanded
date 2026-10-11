using System;
using System.Collections.Generic;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using Vintagestory.API.Common;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Adapts engine asset services and feature lifetime participation to compute binary preparation.</summary>
internal sealed partial class GpuComputePipeline
{
    #region Public API
    /// <summary>Loads a typed, explicitly declared compute program from packaged assets.</summary>
    public static bool TryCreateFromAssets(ICoreAPI api, ShaderSettings settings,
        out GpuComputePipeline? pipeline, out string infoLog, string? debugName = null,
        ILogger? log = null, CancellationToken ct = default, GpuProgramLayout? layout = null)
    {
        ct.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> Read(string path) => api.Assets.TryGet(AssetLocation.Create("shaders/" + path,
            PBR.ShaderImportsSystem.DefaultDomain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built compute shader asset: " + path);
        bool success = TryCreateFromBinaryContract(settings, Read, out pipeline, out infoLog,
            debugName, layout, message => (log ?? api.Logger)?.Warning(message),
            () => ShaderDigestIndexCache.ForAssets(api.Assets, PBR.ShaderImportsSystem.DefaultDomain), api.Assets);
        if (pipeline is not null)
        {
            pipeline.RenderingLifetime = api is Vintagestory.API.Client.ICoreClientAPI client ? Integration.ClientRenderingLifetime.Find(client) : null;
            var sourceOwner = pipeline;
            pipeline.RebuildDiagnostic = () => sourceOwner.preparationLog;
            pipeline.Rebuild = () =>
            {
                TryCreateFromAssets(api, settings, out var candidate, out sourceOwner.preparationLog, debugName, log, layout: layout);
                return candidate;
            };
        }
        return success;
    }
    /// <summary>
    /// Creates a compute pipeline from a required packaged SPIR-V binary. The legacy preference parameter is ignored; GLSL fallback is never used.
    /// </summary>
    /// <remarks>
    /// This is the recommended runtime entry point for VGE-owned compute shaders.
    /// </remarks>
    public static bool TryCreateFromAssets(
        ICoreAPI api,
        string shaderName,
        out GpuComputePipeline? pipeline,
        out global::VanillaGraphicsExpanded.ShaderSourceCode? sourceCode,
        out string infoLog,
        bool preferSpirv = true,
        string stageExtension = "csh",
        IReadOnlyDictionary<string, string?>? defines = null,
        string? debugName = null,
        ILogger? log = null,
        CancellationToken ct = default,
        GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);

        pipeline = null;
        sourceCode = null;
        infoLog = string.Empty;

        ct.ThrowIfCancellationRequested();
        try
        {
            if (stageExtension != "csh") throw new ArgumentException("Compute programs require a compute stage.");
            var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram(shaderName), defines);
            return TryCreateFromAssets(api, settings, out pipeline, out infoLog, debugName, log, ct, layout);
        }
        catch (Exception ex) { infoLog = ex.Message; return false; }
    }
    [Obsolete("Use TryCreateFromAssets(..., preferSpirv: true/false, ...) instead.")]
    public static bool TryCreateFromAssetsPreferSpirv(
        ICoreAPI api,
        string shaderName,
        out GpuComputePipeline? pipeline,
        out global::VanillaGraphicsExpanded.ShaderSourceCode? sourceCode,
        out string infoLog,
        string stageExtension = "csh",
        IReadOnlyDictionary<string, string?>? defines = null,
        string? debugName = null,
        ILogger? log = null,
        CancellationToken ct = default)
        => TryCreateFromAssets(
            api: api,
            shaderName: shaderName,
            pipeline: out pipeline,
            sourceCode: out sourceCode,
            infoLog: out infoLog,
            preferSpirv: true,
            stageExtension: stageExtension,
            defines: defines,
            debugName: debugName,
            log: log,
            ct: ct);
    #endregion
    #region Internal API
    /// <summary>Declares immutable compute inputs without reading assets or issuing GL commands.</summary>
    internal static GpuComputePipeline DeclareFromAssets(ICoreAPI api, ShaderSettings settings,
        string? debugName = null, GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var plan = new ShaderLoadPlan(settings);
        if (plan.Stages.Count != 1 || plan.Stages[0].Stage.Kind != ShaderStageKind.Compute)
            throw new ArgumentException("A compute declaration requires one compute stage.", nameof(settings));
        var owner = new GpuComputePipeline(0, layout?.CreateCandidate() ?? new GpuProgramLayout(), message => api.Logger.Warning(message));
        owner.RenderingLifetime = api is Vintagestory.API.Client.ICoreClientAPI client ? Integration.ClientRenderingLifetime.Find(client) : null;
        owner.RebuildDiagnostic = () => owner.preparationLog;
        owner.Rebuild = owner.prepare = () =>
        {
            TryCreateFromAssets(api, settings, out var candidate, out owner.preparationLog, debugName, layout: layout);
            return candidate;
        };
        return owner;
    }

    #endregion
}
