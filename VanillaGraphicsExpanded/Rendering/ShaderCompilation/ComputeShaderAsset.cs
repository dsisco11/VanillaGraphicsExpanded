using System;
using System.IO;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Owns compute asset policy and an atomically prepared shader/pipeline set, without native resource registration.</summary>
internal sealed partial class ComputeShaderAsset : IDisposable
{
    private RHIShader? shader;
    private RHIComputePipeline? pipeline;
    private Func<ComputeShaderAsset?>? prepare;
    private bool disposed, preparationFailed, preparing;
    private string preparationLog = string.Empty;

    #region Public API
    /// <summary>Retires the exclusively owned executable and managed realization after active native borrowing ends.</summary>
    public void Dispose()
    {
        if (disposed) return;
        if (NativeProgram is { } native && native.UsageReferences.Pass != 0)
            throw new InvalidOperationException("The compute executable is borrowed by an active submission.");
        shader?.Dispose(); shader = null;
        pipeline?.Dispose(); pipeline = null;
        disposed = true;
    }
    #endregion

    #region Internal API
    #region Installed set
    /// <summary>Gets the existing native owner solely for manager-backed integration and lifetime borrowing.</summary>
    internal GpuProgramObject? NativeProgram => shader?.Program;
    /// <summary>Gets the exact installed executable for pipeline construction and resource-interface identity.</summary>
    internal RHIShaderRef Shader => shader;
    /// <summary>Gets the exact ready managed compute realization.</summary>
    internal RHIComputePipelineRef Pipeline => pipeline;
    /// <summary>Gets the executable's native identifier at the existing engine submission integration boundary.</summary>
    internal int ProgramId => NativeProgram?.ProgramId ?? 0;
    /// <summary>Gets the preparation-owned derived layout, never a second canonical snapshot authority.</summary>
    internal GpuProgramLayout ProgramLayout { get; private set; }
    /// <summary>Gets the exact asset selection used for the installed set.</summary>
    internal ShaderSettings? InstalledSettings { get; private set; }
    /// <summary>Reports owner retirement independently of native context availability.</summary>
    internal bool IsDisposed => disposed;
    /// <summary>Reports an installed, unchanged executable using manager validation without native queries or recovery.</summary>
    internal bool IsValid
    {
        get
        {
            if (disposed || shader == null) return false;
            try { shader.RequireUsable(); return true; }
            catch (InvalidOperationException) { return false; }
        }
    }
    /// <summary>Reports invalidation of an installed generation without silently reflecting or relinking it.</summary>
    internal bool NeedsNativePreparation => shader != null && !IsValid;
    /// <summary>Gets the policy owner's installation revision.</summary>
    internal ulong ExecutableRevision { get; private set; }
    /// <summary>Gets the original registered context of the installed executable.</summary>
    internal (nint Handle, long Generation) ExecutableContext { get; private set; }
    /// <summary>Gets the last preparation failure without repeating failed work.</summary>
    internal string PreparationLog => preparationLog;
    #endregion

    #region Deferred asset policy
    /// <summary>Supplies replacement policy to the retained generated input owner.</summary>
    internal Func<ComputeShaderAsset?>? Rebuild { get; private set; }
    /// <summary>Supplies the most recent replacement diagnostic to the retained input owner.</summary>
    internal Func<string>? RebuildDiagnostic { get; private set; }
    /// <summary>Associates asset reload admission with the existing client lifetime.</summary>
    internal Integration.ClientRenderingLifetime? RenderingLifetime { get; private set; }
    /// <summary>Prepares the declared set once and transfers managed ownership without detaching or re-registering a native resource.</summary>
    internal bool EnsureReady(Func<bool>? admission = null)
    {
        if (disposed || admission?.Invoke() == false) return false;
        if (preparing) throw new InvalidOperationException("Compute asset preparation cannot recurse.");
        if (shader != null) return IsValid;
        if (preparationFailed || prepare == null) return false;
        var origin = Integration.RenderContextRegistry.Current();
        preparing = true;
        try
        {
            using var candidate = prepare();
            if (disposed || admission?.Invoke() == false || origin != Integration.RenderContextRegistry.Current()
                || RenderingLifetime is { IsClosed: true } or { Reloading: true }) return false;
            if (candidate == null) { preparationFailed = true; return false; }
            candidate.shader!.RequireUsable();
            shader = candidate.shader; candidate.shader = null;
            pipeline = candidate.pipeline; candidate.pipeline = null;
            ProgramLayout = candidate.ProgramLayout;
            InstalledSettings = candidate.InstalledSettings;
            ExecutableContext = candidate.ExecutableContext;
            ExecutableRevision++;
            prepare = null;
            return true;
        }
        finally { preparing = false; }
    }
    /// <summary>Allows a failed declaration to try its explicit asset policy again after invalidation.</summary>
    internal void RetryPreparation() => preparationFailed = false;
    #endregion

    #region File asset loading
    /// <summary>Loads an explicitly selected precompiled asset with its sibling digest manifest before any native allocation.</summary>
    internal static bool TryLoadFromSpirv(string spirvBinaryPath, ShaderSettings settings,
        out ComputeShaderAsset? pipeline, out string infoLog, string? debugName = null,
        GpuProgramLayout? layout = null, Action<string>? warn = null)
    {
        pipeline = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(spirvBinaryPath);
            var plan = new ShaderLoadPlan(settings);
            if (bytes.Length == 0) throw new InvalidOperationException($"Empty SPIR-V binary for '{plan.Stages[0].Stage.Identity}'.");
            string selected = plan.Stages[0].BinaryPath.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(spirvBinaryPath);
            string root = fullPath.EndsWith(selected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                ? fullPath[..^selected.Length] : Path.GetDirectoryName(fullPath)!;
            ReadOnlySpan<byte> Read(string _) => bytes;
            pipeline = Load(settings, Read, () => ProgramBinaries.ShaderDigestIndexCache.ForFile(Path.Combine(root, ShaderBinaryDigest.FileName)),
                null, string.Empty, layout, debugName);
            infoLog = string.Empty;
            return true;
        }
        catch (Exception error) { pipeline?.Dispose(); pipeline = null; infoLog = error.Message; return false; }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Constructs an unpublished policy owner; no native object is allocated by this constructor.</summary>
    private ComputeShaderAsset(GpuProgramLayout? layout) => ProgramLayout = layout?.CreateCandidate() ?? new();

    /// <summary>Builds the complete compatible set before publishing ownership to a feature or reload candidate.</summary>
    private static ComputeShaderAsset Load(ShaderSettings settings, ShaderAssetReader read,
        Func<ShaderBinaryDigest.Manifest?> digest, object? assets, string domain, GpuProgramLayout? layout, string? label)
    {
        var plan = new ShaderLoadPlan(settings);
        if (plan.Stages.Count != 1 || plan.Stages[0].Stage.Kind != ShaderStageKind.Compute)
            throw new ArgumentException("A compute asset requires exactly one compute stage.", nameof(settings));
        var context = RHICommandContext.Current;
        using var candidate = assets == null ? new ShaderAssetCandidate(plan, read, digest)
            : ShaderLinkBatch.Take(assets, domain, plan) ?? new ShaderAssetCandidate(plan, read, digest);
        candidate.Complete();
        var owner = new ComputeShaderAsset(layout);
        try
        {
            owner.shader = candidate.TakeShader();
            owner.pipeline = context.CreateComputePipeline(new(owner.shader, label)).RequireResource();
            owner.ProgramLayout.RegisterContract(owner.shader.Description.Bindings);
            owner.ProgramLayout.InstallFixedExecutable(owner.shader);
            owner.InstalledSettings = settings;
            owner.ExecutableContext = Integration.RenderContextRegistry.Current();
            owner.ExecutableRevision = 1;
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }
    #endregion
}
