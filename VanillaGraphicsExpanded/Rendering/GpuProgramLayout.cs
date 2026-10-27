using VanillaGraphicsExpanded.Rendering.RHI;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Captures a snapshot of binding-related program resources after link (UBO/SSBO bindings, sampler/image units).
/// </summary>
public partial class GpuProgramLayout
{
    #region Compiled contract ownership
    private bool compiledContract;
    /// <summary>Numeric reflection belongs to this layout and is replaced with each successful link.</summary>
    private Spirv.GpuProgramInterface? binaryInterface;
    private bool nativePreparationUnavailable;
    private bool nativeExecutableInvalidated;
    /// <summary>Rejects stale prepared tables until a coherent replacement is installed.</summary>
    internal Spirv.GpuProgramInterface? BinaryInterface
    {
        get => nativePreparationUnavailable
            ? throw new InvalidOperationException("The native program changed; its prepared interface is unavailable.") : binaryInterface;
        set { binaryInterface = value; nativePreparationUnavailable = false; nativeExecutableInvalidated = false; }
    }

    /// <summary>Requests the owner's existing preparation workflow after native executable or contract invalidation.</summary>
    internal bool NeedsNativePreparation => nativePreparationUnavailable;
    /// <summary>Identifies authored fixed-slot preparation even while its current table is withdrawn.</summary>
    internal bool HasPreparedInterface => binaryInterface is not null;

    /// <summary>Uses the same declarations that assign resource slots during offline compilation.</summary>
    internal void RegisterContract(Contracts.GpuBindingContract contract)
    {
        foreach (var (name, value) in contract.UniformBlocks) RegisterUniformBlockBinding(name, value.Slot, value.Required);
        foreach (var (name, value) in contract.StorageBlocks) RegisterShaderStorageBlockBinding(name, value.Slot, value.Required);
        foreach (var (name, value) in contract.Samplers) RegisterSamplerUnit(name, value.Slot, value.Required);
        foreach (var (name, value) in contract.Images) RegisterImageUnit(name, value.Slot, value.Required);
        compiledContract = true;
    }

    /// <summary>Copies declarations into a candidate layout without sharing installed interface caches.</summary>
    internal GpuProgramLayout CreateCandidate()
    {
        var candidate = new GpuProgramLayout { compiledContract = compiledContract };
        foreach (var pair in uniformBlockContract) candidate.uniformBlockContract.Add(pair.Key, pair.Value);
        foreach (var pair in shaderStorageBlockContract) candidate.shaderStorageBlockContract.Add(pair.Key, pair.Value);
        foreach (var pair in samplerContract) candidate.samplerContract.Add(pair.Key, pair.Value);
        foreach (var pair in imageContract) candidate.imageContract.Add(pair.Key, pair.Value);
        return candidate;
    }
    /// <summary>Installs prepared interface caches while retaining the owner's layout type, UBO state and external references.</summary>
    internal void InstallCandidate(GpuProgramLayout candidate)
    {
        BinaryInterface = candidate.BinaryInterface;
        nativeMetadata = candidate.nativeMetadata;
        NativeMetadataRevision = candidate.NativeMetadataRevision;
        SetActiveSnapshot(candidate.uniformBlockBindings, candidate.shaderStorageBlockBindings,
            candidate.samplerBindings, candidate.imageBindings);
        // Lazy lookup caches must never carry locations or block indices from the retired generation.
        uniformLocationCache.Clear(); uniformLocationCacheProgramId = 0;
        uniformBlockIndexCache.Clear(); uniformBlockIndexCacheProgramId = 0;
        shaderStorageBlockIndexCache.Clear(); shaderStorageBlockIndexCacheProgramId = 0;
        warnedOnce.Clear();
    }
    /// <summary>Resolves standalone locations from the owned binary interface or an external GLSL program.</summary>
    internal int GetUniformLocation(int programId, string name)
    {
#if DEBUG
        UniformNameResolutions++;
#endif
        return BinaryInterface?.GetUniformLocation(name) ?? FindPublishedUniform(name);
    }

#if DEBUG
    /// <summary>Counts named uniform resolution requests, including engine compatibility lookups.</summary>
    internal long UniformNameResolutions { get; private set; }
#endif

    #endregion

    #region Types

    private readonly record struct BindingSpec(int BindingOrUnit, bool Required);

    internal enum ResolutionState
    {
        Active,
        Missing,
        Unknown,
    }

    internal readonly record struct ResolutionInt(ResolutionState State, int Value)
    {
        public bool IsActive => State == ResolutionState.Active;
    }

    internal readonly record struct ResolutionBool(ResolutionState State, bool Value)
    {
        public bool IsActive => State == ResolutionState.Active;
    }

#if DEBUG
    internal sealed class LayoutDiagnostics
    {
        public long SkippedUniformBindsMissing;
        public long SkippedUboBindsMissing;
        public long SkippedSsboBindsMissing;
        public long SkippedSamplerBindsMissing;
        public long SkippedImageBindsMissing;
        public long SkippedDueToUnknown;
    }
#endif

    #endregion

    private static readonly IReadOnlyDictionary<string, int> EmptyBindings = new Dictionary<string, int>(StringComparer.Ordinal);

    public static GpuProgramLayout Empty { get; } = new();

    #region Contract

    private readonly Dictionary<string, BindingSpec> uniformBlockContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BindingSpec> shaderStorageBlockContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BindingSpec> samplerContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BindingSpec> imageContract = new(StringComparer.Ordinal);

    private readonly HashSet<string> warnedOnce = new(StringComparer.Ordinal);

#if DEBUG
    internal LayoutDiagnostics Diagnostics { get; } = new();
#endif

    #endregion

    #region Active Cache (Snapshot)

    private IReadOnlyDictionary<string, int> uniformBlockBindings = EmptyBindings;
    private IReadOnlyDictionary<string, int> shaderStorageBlockBindings = EmptyBindings;
    private IReadOnlyDictionary<string, int> samplerBindings = EmptyBindings;
    private IReadOnlyDictionary<string, int> imageBindings = EmptyBindings;

    // Resolution caches; cleared when the program id changes.
    private readonly Dictionary<string, int> uniformLocationCache = new(StringComparer.Ordinal);
    private int uniformLocationCacheProgramId;

    private readonly Dictionary<string, int> uniformBlockIndexCache = new(StringComparer.Ordinal);
    private int uniformBlockIndexCacheProgramId;

    private readonly Dictionary<string, int> shaderStorageBlockIndexCache = new(StringComparer.Ordinal);
    private int shaderStorageBlockIndexCacheProgramId;

    #endregion

    /// <summary>
    /// Gets cached uniform block binding points (<c>layout(binding=...)</c> for UBOs).
    /// </summary>
    public IReadOnlyDictionary<string, int> UniformBlockBindings => uniformBlockBindings;

    /// <summary>
    /// Gets cached shader storage block binding points (<c>layout(binding=...)</c> for SSBOs).
    /// </summary>
    public IReadOnlyDictionary<string, int> ShaderStorageBlockBindings => shaderStorageBlockBindings;

    /// <summary>
    /// Gets cached sampler uniform values (texture units). This is a snapshot at cache build time.
    /// </summary>
    public IReadOnlyDictionary<string, int> SamplerBindings => samplerBindings;

    /// <summary>
    /// Gets cached image uniform values (image units). This is a snapshot at cache build time.
    /// </summary>
    public IReadOnlyDictionary<string, int> ImageBindings => imageBindings;

    /// <summary>
    /// Creates a layout with an empty contract.
    /// </summary>
    public GpuProgramLayout()
    {
    }

    private void SetActiveSnapshot(
        IReadOnlyDictionary<string, int>? uniformBlockBindings,
        IReadOnlyDictionary<string, int>? shaderStorageBlockBindings,
        IReadOnlyDictionary<string, int>? samplerBindings,
        IReadOnlyDictionary<string, int>? imageBindings)
    {
        this.uniformBlockBindings = uniformBlockBindings ?? EmptyBindings;
        this.shaderStorageBlockBindings = shaderStorageBlockBindings ?? EmptyBindings;
        this.samplerBindings = samplerBindings ?? EmptyBindings;
        this.imageBindings = imageBindings ?? EmptyBindings;
    }

    /// <summary>
    /// Registers a uniform block binding point expectation for this program contract.
    /// </summary>
    public void RegisterUniformBlockBinding(string blockName, int bindingIndex, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        uniformBlockContract[blockName] = new BindingSpec(bindingIndex, required);
    }

    /// <summary>
    /// Registers a shader storage block binding point expectation for this program contract.
    /// </summary>
    public void RegisterShaderStorageBlockBinding(string blockName, int bindingIndex, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        shaderStorageBlockContract[blockName] = new BindingSpec(bindingIndex, required);
    }

    /// <summary>
    /// Registers an expected sampler uniform texture unit for this program contract.
    /// </summary>
    public void RegisterSamplerUnit(string samplerUniformName, int unit, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);
        if (unit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Texture unit must be >= 0.");
        }

        samplerContract[samplerUniformName] = new BindingSpec(unit, required);
    }


    /// <summary>
    /// Registers an expected sampler uniform array mapping starting at <paramref name="firstUnit"/>.
    /// </summary>
    /// <remarks>
    /// This is useful for declarations like <c>uniform sampler2D foo[8]</c> or <c>uniform uimage3D bar[8]</c>
    /// where explicit bindings may be unavailable on some GLSL fallback paths.
    /// </remarks>
    public void RegisterSamplerUnitArray(string samplerUniformBaseName, int firstUnit, int count, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformBaseName);
        if (firstUnit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstUnit), firstUnit, "Texture unit must be >= 0.");
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be > 0.");
        }

        for (int i = 0; i < count; i++)
        {
            // Avoid spamming required warnings per element; treat element 0 as the required sentinel.
            bool elementRequired = i == 0 && required;
            RegisterSamplerUnit($"{samplerUniformBaseName}[{i}]", firstUnit + i, elementRequired);
        }
    }

    /// <summary>
    /// Attempts to get a sampler unit from the registered contract only (ignores the active reflection snapshot).
    /// </summary>
    public bool TryGetContractSamplerUnit(string samplerUniformName, out int unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);

        if (samplerContract.TryGetValue(samplerUniformName, out var spec))
        {
            unit = spec.BindingOrUnit;
            return true;
        }

        unit = default;
        return false;
    }

    internal bool TryGetContractSamplerSpec(string samplerUniformName, out int unit, out bool required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);

        if (samplerContract.TryGetValue(samplerUniformName, out var spec))
        {
            unit = spec.BindingOrUnit;
            required = spec.Required;
            return true;
        }

        unit = default;
        required = false;
        return false;
    }

    internal bool TryGetContractImageSpec(string imageUniformName, out int unit, out bool required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);

        if (imageContract.TryGetValue(imageUniformName, out var spec))
        {
            unit = spec.BindingOrUnit;
            required = spec.Required;
            return true;
        }

        unit = default;
        required = false;
        return false;
    }

    /// <summary>
    /// Registers an expected image uniform image unit for this program contract.
    /// </summary>
    public void RegisterImageUnit(string imageUniformName, int unit, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);
        if (unit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Image unit must be >= 0.");
        }

        imageContract[imageUniformName] = new BindingSpec(unit, required);
    }

    /// <summary>
    /// Registers an expected image uniform array mapping starting at <paramref name="firstUnit"/>.
    /// </summary>
    public void RegisterImageUnitArray(string imageUniformBaseName, int firstUnit, int count, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformBaseName);
        if (firstUnit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstUnit), firstUnit, "Image unit must be >= 0.");
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be > 0.");
        }

        for (int i = 0; i < count; i++)
        {
            // Avoid spamming required warnings per element; treat element 0 as the required sentinel.
            bool elementRequired = i == 0 && required;
            RegisterImageUnit($"{imageUniformBaseName}[{i}]", firstUnit + i, elementRequired);
        }
    }

    /// <summary>
    /// Applies the registered contract to a linked program (UBO/SSBO binding points and sampler/image units).
    /// </summary>
    /// <remarks>
    /// This is intended to be called once after a successful link (and after any re-link/recompile).
    /// </remarks>
    public void ApplyContract(int programId, Action<string>? warn = null)
    {
        if (compiledContract && BinaryInterface is not null)
        {
            // Explicit source layouts already own these slots; only refresh active-resource caches.
            RebuildCache(programId);
            return;
        }
        if (programId == 0)
        {
            return;
        }

        // Assignment is an explicit external-program operation. Ordinary layout reads remain CPU-only.
        nativeMetadata = RHICommandContext.Current.ApplyProgramBindings(programId,
            uniformBlockContract.Select(pair => new ProgramBindingAssignment(ProgramResourceKind.UniformBlock, pair.Key, pair.Value.BindingOrUnit, pair.Value.Required))
            .Concat(shaderStorageBlockContract.Select(pair => new ProgramBindingAssignment(ProgramResourceKind.ShaderStorageBlock, pair.Key, pair.Value.BindingOrUnit, pair.Value.Required)))
            .Concat(samplerContract.Select(pair => new ProgramBindingAssignment(ProgramResourceKind.Uniform, pair.Key, pair.Value.BindingOrUnit, pair.Value.Required)))
            .Concat(imageContract.Select(pair => new ProgramBindingAssignment(ProgramResourceKind.Uniform, pair.Key, pair.Value.BindingOrUnit, pair.Value.Required))).ToArray(), warn);
        RebuildCache(programId);
    }

#if DEBUG
    /// <summary>
    /// Dev-mode validation pass that compares the expected contract against the linked program's
    /// reflected bindings/units. This never throws and only logs via <paramref name="warn"/>.
    /// </summary>
    public void ValidateContract(int programId, Action<string>? warn = null)
    {
        if (compiledContract || programId == 0 || warn is null)
        {
            return;
        }

        if (uniformBlockContract.Count == 0
            && shaderStorageBlockContract.Count == 0
            && samplerContract.Count == 0
            && imageContract.Count == 0)
        {
            return;
        }


        try
        {
            var activeUboBindings = TryBuildBufferBindingByName(programId, ProgramResourceKind.UniformBlock);
            var activeSsboBindings = TryBuildBufferBindingByName(programId, ProgramResourceKind.ShaderStorageBlock);
            var (activeSamplers, activeImages) = TryBuildTextureUnitBindings(programId);

            ValidateBufferContract(programId, "UBO", uniformBlockContract, activeUboBindings, warn);
            ValidateBufferContract(programId, "SSBO", shaderStorageBlockContract, activeSsboBindings, warn);
            ValidateUnitContract(programId, "Sampler", samplerContract, activeSamplers, warn);
            ValidateUnitContract(programId, "Image", imageContract, activeImages, warn);
        }
        catch (Exception ex)
        {
            WarnOnce(
                key: "validate:exception",
                message: $"Contract validation failed (programId={programId}): {ex.Message}",
                warn: warn);
        }
    }

    private void ValidateBufferContract(
        int programId,
        string kind,
        Dictionary<string, BindingSpec> contract,
        IReadOnlyDictionary<string, int> reflectedBindings,
        Action<string> warn)
    {
        foreach (var (name, spec) in contract)
        {
            if (!reflectedBindings.TryGetValue(name, out int actual))
            {
                if (spec.Required)
                {
                    WarnOnce(
                        key: $"validate:{kind}:missing:{name}",
                        message: $"Contract mismatch (programId={programId}): required {kind} '{name}' is not active.",
                        warn: warn);
                }
                continue;
            }

            int expected = spec.BindingOrUnit;
            if (actual != expected)
            {
                WarnOnce(
                    key: $"validate:{kind}:binding:{name}",
                    message: $"Contract mismatch (programId={programId}): {kind} '{name}' expected binding {expected}, got {actual}.",
                    warn: warn);
            }
        }
    }

    private void ValidateUnitContract(
        int programId,
        string kind,
        Dictionary<string, BindingSpec> contract,
        IReadOnlyDictionary<string, int> reflectedUnits,
        Action<string> warn)
    {
        foreach (var (rawName, spec) in contract)
        {
            string name = NormalizeUniformName(rawName);

            if (!reflectedUnits.TryGetValue(name, out int actual))
            {
                if (spec.Required)
                {
                    WarnOnce(
                        key: $"validate:{kind}:missing:{name}",
                        message: $"Contract mismatch (programId={programId}): required {kind} uniform '{name}' is not active.",
                        warn: warn);
                }
                continue;
            }

            int expected = spec.BindingOrUnit;
            if (actual != expected)
            {
                WarnOnce(
                    key: $"validate:{kind}:unit:{name}",
                    message: $"Contract mismatch (programId={programId}): {kind} uniform '{name}' expected unit {expected}, got {actual}.",
                    warn: warn);
            }
        }
    }
#endif

    private void WarnOnce(string key, string message, Action<string>? warn)
    {
        if (warn is null)
        {
            return;
        }

        if (warnedOnce.Add(key))
        {
            warn(message);
        }
    }

    /// <summary>
    /// Rebuilds the active binding snapshot for a linked program.
    /// </summary>
    public void RebuildCache(int programId)
    {
        uniformLocationCache.Clear();
        uniformBlockIndexCache.Clear();
        shaderStorageBlockIndexCache.Clear();
        if (programId == 0)
        {
            SetActiveSnapshot(null, null, null, null);
            return;
        }

        if (BinaryInterface is { } prepared)
        {
            // The interface was inspected before publication. Reuse that generation's immutable
            // projections rather than enumerating the linked program again for compatibility caches.
            SetActiveSnapshot(prepared.ActiveBindings(Contracts.ShaderBindingKind.UniformBlock),
                prepared.ActiveBindings(Contracts.ShaderBindingKind.StorageBlock),
                prepared.ActiveBindings(Contracts.ShaderBindingKind.Sampler),
                prepared.ActiveBindings(Contracts.ShaderBindingKind.Image));
            return;
        }


        try
        {
            var uniformBlocks = TryBuildBufferBindingByName(programId, ProgramResourceKind.UniformBlock);
            var storageBlocks = TryBuildBufferBindingByName(programId, ProgramResourceKind.ShaderStorageBlock);
            var (samplers, images) = TryBuildTextureUnitBindings(programId);
            SetActiveSnapshot(uniformBlocks, storageBlocks, samplers, images);
        }
        catch
        {
            SetActiveSnapshot(null, null, null, null);
        }
    }

    /// <summary>
    /// Attempts to build a binding cache for a successfully linked program.
    /// Explicitly acquires unknown external facts once; ordinary cache rebuilding uses only supplied facts.
    /// </summary>
    public static GpuProgramLayout TryBuild(int programId)
    {
        if (programId == 0)
        {
            return Empty;
        }

        var layout = new GpuProgramLayout();
        layout.nativeMetadata = RHICommandContext.Current.AcquireProgramInterface(programId);
        layout.RebuildCache(programId);
        return layout;
    }

    private int GetUniformLocationOrArray0Cached(int programId, string uniformName)
    {
        if (uniformLocationCacheProgramId != programId)
        {
            uniformLocationCacheProgramId = programId;
            uniformLocationCache.Clear();
        }

        if (uniformLocationCache.TryGetValue(uniformName, out int cached))
        {
            return cached;
        }

        int loc = -1;
        try
        {
            loc = GetUniformLocation(programId, uniformName);
            if (loc < 0)
            {
                loc = GetUniformLocation(programId, $"{uniformName}[0]");
            }
        }
        catch
        {
            loc = -1;
        }

        uniformLocationCache[uniformName] = loc;
        return loc;
    }

    internal ResolutionInt ResolveUniformLocation(int programId, string uniformName)
    {
        if (programId == 0)
        {
            return new ResolutionInt(ResolutionState.Unknown, -1);
        }

        int loc = GetUniformLocationOrArray0Cached(programId, uniformName);
        return loc >= 0
            ? new ResolutionInt(ResolutionState.Active, loc)
            : new ResolutionInt(ResolutionState.Missing, -1);
    }

    internal ResolutionBool ResolveUniformBlockActive(int programId, string blockName)
    {
        if (programId == 0)
        {
            return new ResolutionBool(ResolutionState.Unknown, false);
        }

        int index = GetUniformBlockIndexCached(programId, blockName);
        return index >= 0
            ? new ResolutionBool(ResolutionState.Active, true)
            : new ResolutionBool(ResolutionState.Missing, false);
    }

    internal ResolutionBool ResolveShaderStorageBlockActive(int programId, string blockName)
    {
        if (programId == 0)
        {
            return new ResolutionBool(ResolutionState.Unknown, false);
        }


        int index = GetShaderStorageBlockIndexCached(programId, blockName);
        return index >= 0
            ? new ResolutionBool(ResolutionState.Active, true)
            : new ResolutionBool(ResolutionState.Missing, false);
    }

    private int GetUniformBlockIndexCached(int programId, string blockName)
    {
        if (uniformBlockIndexCacheProgramId != programId)
        {
            uniformBlockIndexCacheProgramId = programId;
            uniformBlockIndexCache.Clear();
        }

        if (uniformBlockIndexCache.TryGetValue(blockName, out int cached))
        {
            return cached;
        }

        int index = -1;
        try
        {
            index = BinaryInterface?.GetUniformBlockIndex(blockName) ?? FindPublishedBlock(ProgramResourceKind.UniformBlock, blockName);
        }
        catch
        {
            index = -1;
        }

        uniformBlockIndexCache[blockName] = index;
        return index;
    }

    private int GetShaderStorageBlockIndexCached(int programId, string blockName)
    {
        if (shaderStorageBlockIndexCacheProgramId != programId)
        {
            shaderStorageBlockIndexCacheProgramId = programId;
            shaderStorageBlockIndexCache.Clear();
        }

        if (shaderStorageBlockIndexCache.TryGetValue(blockName, out int cached))
        {
            return cached;
        }

        int index = -1;
        try
        {
            index = BinaryInterface?.GetStorageBlockIndex(blockName) ?? FindPublishedBlock(ProgramResourceKind.ShaderStorageBlock, blockName);
        }
        catch
        {
            index = -1;
        }

        shaderStorageBlockIndexCache[blockName] = index;
        return index;
    }

    /// <summary>Projects block bindings from supplied canonical metadata or fixed executable preparation.</summary>
    private IReadOnlyDictionary<string, int> TryBuildBufferBindingByName(int programId, ProgramResourceKind kind)
        => BinaryInterface is { } prepared ? prepared.ActiveBindings(kind == ProgramResourceKind.UniformBlock
            ? Contracts.ShaderBindingKind.UniformBlock : Contracts.ShaderBindingKind.StorageBlock) : ProjectBufferBindings(kind);

    /// <summary>Projects texture assignments without native reflection.</summary>
    private (IReadOnlyDictionary<string, int> Samplers, IReadOnlyDictionary<string, int> Images) TryBuildTextureUnitBindings(int programId)
        => ProjectTextureUnits();

    /// <summary>Resolves a block index from the supplied executable generation.</summary>
    private int FindPublishedBlock(ProgramResourceKind kind, string name)
    {
        if (nativeMetadata is not { ExecutableAvailable: true } data || data.Resources.IsDefault) return -1;
        foreach (var resource in data.Resources)
            if (resource.Interface == kind && resource.Name == name) return resource.Index;
        return -1;
    }

    /// <summary>Resolves scalar/array uniform locations using only published names and extents.</summary>
    private int FindPublishedUniform(string name)
    {
        if (nativeMetadata is not { ExecutableAvailable: true } data || data.Resources.IsDefault) return -1;
        foreach (var resource in data.Resources)
        {
            if (resource.Interface != ProgramResourceKind.Uniform || resource.Location is not { } location || location < 0 || resource.Name is null) continue;
            string baseName = NormalizeUniformName(resource.Name);
            if (name == resource.Name || name == baseName) return location;
            int bracket = name.LastIndexOf('[');
            if (bracket > 0 && name.EndsWith(']') && name[..bracket] == baseName
                && int.TryParse(name[(bracket + 1)..^1], out int element) && element >= 0 && element < resource.ArraySize)
                return location + element;
        }
        return -1;
    }

    /// <summary>Normalizes the reflected first array element to its authored base name.</summary>
    private static string NormalizeUniformName(string name)
    {
        // Drivers often expose arrays as "name[0]" via active uniform enumeration.
        const string array0Suffix = "[0]";
        if (name.EndsWith(array0Suffix, StringComparison.Ordinal))
        {
            return name[..^array0Suffix.Length];
        }

        return name;
    }

    /// <summary>Identifies semantic sampled-texture values.</summary>
    internal static bool IsSamplerType(ShaderValueType type)
    {
        // Keep this allocation-free; the set is stable across GL versions.
        return type is
            ShaderValueType.Sampler1D
            or ShaderValueType.Sampler2D
            or ShaderValueType.Sampler3D
            or ShaderValueType.SamplerCube
            or ShaderValueType.Sampler1DShadow
            or ShaderValueType.Sampler2DShadow
            or ShaderValueType.Sampler1DArray
            or ShaderValueType.Sampler2DArray
            or ShaderValueType.Sampler1DArrayShadow
            or ShaderValueType.Sampler2DArrayShadow
            or ShaderValueType.Sampler2DMultisample
            or ShaderValueType.Sampler2DMultisampleArray
            or ShaderValueType.SamplerCubeShadow
            or ShaderValueType.SamplerBuffer
            or ShaderValueType.Sampler2DRect
            or ShaderValueType.Sampler2DRectShadow
            or ShaderValueType.IntSampler1D
            or ShaderValueType.IntSampler2D
            or ShaderValueType.IntSampler3D
            or ShaderValueType.IntSamplerCube
            or ShaderValueType.IntSampler1DArray
            or ShaderValueType.IntSampler2DArray
            or ShaderValueType.IntSampler2DMultisample
            or ShaderValueType.IntSampler2DMultisampleArray
            or ShaderValueType.IntSamplerBuffer
            or ShaderValueType.IntSampler2DRect
            or ShaderValueType.UnsignedIntSampler1D
            or ShaderValueType.UnsignedIntSampler2D
            or ShaderValueType.UnsignedIntSampler3D
            or ShaderValueType.UnsignedIntSamplerCube
            or ShaderValueType.UnsignedIntSampler1DArray
            or ShaderValueType.UnsignedIntSampler2DArray
            or ShaderValueType.UnsignedIntSampler2DMultisample
            or ShaderValueType.UnsignedIntSampler2DMultisampleArray
            or ShaderValueType.UnsignedIntSamplerBuffer
            or ShaderValueType.UnsignedIntSampler2DRect
            or ShaderValueType.SamplerCubeMapArray
            or ShaderValueType.SamplerCubeMapArrayShadow
            or ShaderValueType.IntSamplerCubeMapArray
            or ShaderValueType.UnsignedIntSamplerCubeMapArray;
    }

    /// <summary>Classifies linked image types independently of the sampler unit namespace.</summary>
    internal static bool IsImageType(ShaderValueType type)
    {
        return type is
            ShaderValueType.Image1D
            or ShaderValueType.Image2D
            or ShaderValueType.Image3D
            or ShaderValueType.Image2DRect
            or ShaderValueType.ImageCube
            or ShaderValueType.ImageBuffer
            or ShaderValueType.Image1DArray
            or ShaderValueType.Image2DArray
            or ShaderValueType.Image2DMultisample
            or ShaderValueType.Image2DMultisampleArray
            or ShaderValueType.IntImage1D
            or ShaderValueType.IntImage2D
            or ShaderValueType.IntImage3D
            or ShaderValueType.IntImage2DRect
            or ShaderValueType.IntImageCube
            or ShaderValueType.IntImageBuffer
            or ShaderValueType.IntImage1DArray
            or ShaderValueType.IntImage2DArray
            or ShaderValueType.IntImage2DMultisample
            or ShaderValueType.IntImage2DMultisampleArray
            or ShaderValueType.UnsignedIntImage1D
            or ShaderValueType.UnsignedIntImage2D
            or ShaderValueType.UnsignedIntImage3D
            or ShaderValueType.UnsignedIntImage2DRect
            or ShaderValueType.UnsignedIntImageCube
            or ShaderValueType.UnsignedIntImageBuffer
            or ShaderValueType.UnsignedIntImage1DArray
            or ShaderValueType.UnsignedIntImage2DArray
            or ShaderValueType.UnsignedIntImage2DMultisample
            or ShaderValueType.UnsignedIntImage2DMultisampleArray
            or ShaderValueType.ImageCubeMapArray
            or ShaderValueType.IntImageCubeMapArray
            or ShaderValueType.UnsignedIntImageCubeMapArray;
    }

    /// <summary>
    /// Attempts to get the UBO binding point for a uniform block by name.
    /// </summary>
    public bool TryGetUniformBlockBinding(string blockName, out int bindingIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);

        if (uniformBlockContract.TryGetValue(blockName, out var spec))
        {
            bindingIndex = spec.BindingOrUnit;
            return true;
        }

        return uniformBlockBindings.TryGetValue(blockName, out bindingIndex);
    }

    /// <summary>
    /// Attempts to get the SSBO binding point for a shader storage block by name.
    /// </summary>
    public bool TryGetShaderStorageBlockBinding(string blockName, out int bindingIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);

        if (shaderStorageBlockContract.TryGetValue(blockName, out var spec))
        {
            bindingIndex = spec.BindingOrUnit;
            return true;
        }

        return shaderStorageBlockBindings.TryGetValue(blockName, out bindingIndex);
    }

    /// <summary>
    /// Attempts to get the texture unit for a sampler uniform by name.
    /// </summary>
    public bool TryGetSamplerUnit(string samplerUniformName, out int unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);

        if (samplerContract.TryGetValue(samplerUniformName, out var spec))
        {
            unit = spec.BindingOrUnit;
            return true;
        }

        return samplerBindings.TryGetValue(samplerUniformName, out unit);
    }

    /// <summary>
    /// Attempts to get the image unit for an image uniform by name.
    /// </summary>
    public bool TryGetImageUnit(string imageUniformName, out int unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);

        if (imageContract.TryGetValue(imageUniformName, out var spec))
        {
            unit = spec.BindingOrUnit;
            return true;
        }

        return imageBindings.TryGetValue(imageUniformName, out unit);
    }


}
