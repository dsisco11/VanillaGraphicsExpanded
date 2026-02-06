using System;
using System.Collections.Generic;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Captures a snapshot of binding-related program resources after link (UBO/SSBO bindings, sampler/image units).
/// </summary>
public class GpuProgramLayout
{
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
    /// Applies the registered contract to a linked program (UBO/SSBO binding points and sampler/image units).
    /// </summary>
    /// <remarks>
    /// This is intended to be called once after a successful link (and after any re-link/recompile).
    /// </remarks>
    public void ApplyContract(int programId, Action<string>? warn = null)
    {
        if (programId == 0)
        {
            return;
        }

        try
        {
            ApplyUniformBlockContract(programId, warn);
            ApplyShaderStorageBlockContract(programId, warn);
            ApplyUniformUnitContract(programId, samplerContract, warn);
            ApplyUniformUnitContract(programId, imageContract, warn);
        }
        finally
        {
            RebuildCache(programId);
        }
    }

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

    private static bool SupportsProgramInterfaceQueries()
    {
        // Prefer cached support when available, but keep this method safe for unit tests
        // that create their own headless contexts without initializing GpuSupport.
        if (GpuSupport.IsInitialized)
        {
            return GpuSupport.SupportsArbProgramInterfaceQuery;
        }

        // Best-effort: query extensions directly.
        return GlExtensions.Supports("GL_ARB_program_interface_query");
    }

    /// <summary>
    /// Rebuilds the active binding snapshot for a linked program.
    /// </summary>
    public void RebuildCache(int programId)
    {
        if (programId == 0)
        {
            SetActiveSnapshot(null, null, null, null);
            return;
        }

        if (!SupportsProgramInterfaceQueries())
        {
            // Reflection snapshot relies on program interface queries; keep the cache empty
            // and let contract-based fallback (by name) handle binding where needed.
            SetActiveSnapshot(null, null, null, null);
            return;
        }

        try
        {
            var uniformBlocks = TryBuildBufferBindingByName(programId, ProgramInterface.UniformBlock);
            var storageBlocks = TryBuildBufferBindingByName(programId, ProgramInterface.ShaderStorageBlock);
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
    /// Returns an empty cache if program interface queries are unavailable or if the GL call fails.
    /// </summary>
    public static GpuProgramLayout TryBuild(int programId)
    {
        if (programId == 0)
        {
            return Empty;
        }

        var layout = new GpuProgramLayout();
        layout.RebuildCache(programId);
        return layout;
    }

    private void ApplyUniformBlockContract(int programId, Action<string>? warn)
    {
        if (uniformBlockContract.Count == 0)
        {
            return;
        }

        foreach (var (blockName, spec) in uniformBlockContract)
        {
            int blockIndex = GetUniformBlockIndexCached(programId, blockName);
            if (blockIndex < 0)
            {
                if (spec.Required)
                {
                    WarnOnce($"ubo:{blockName}", $"Program did not expose required uniform block '{blockName}'.", warn);
                }

#if DEBUG
                Diagnostics.SkippedUboBindsMissing++;
#endif
                continue;
            }

            // Prefer explicit bindings when present (skip redundant assignment if the block is already bound correctly).
            try
            {
                GL.GetActiveUniformBlock(programId, blockIndex, ActiveUniformBlockParameter.UniformBlockBinding, out int current);
                if (current == spec.BindingOrUnit)
                {
                    continue;
                }
            }
            catch
            {
                // Best-effort only.
            }

            try
            {
                GL.UniformBlockBinding(programId, blockIndex, spec.BindingOrUnit);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GpuProgramLayout] Failed to assign UBO binding for '{blockName}': {ex.Message}");
            }
        }
    }

    private void ApplyShaderStorageBlockContract(int programId, Action<string>? warn)
    {
        if (shaderStorageBlockContract.Count == 0)
        {
            return;
        }

        foreach (var (blockName, spec) in shaderStorageBlockContract)
        {
            int blockIndex = GetShaderStorageBlockIndexCached(programId, blockName);
            if (blockIndex < 0)
            {
                if (spec.Required)
                {
                    WarnOnce($"ssbo:{blockName}", $"Program did not expose required shader storage block '{blockName}'.", warn);
                }

#if DEBUG
                Diagnostics.SkippedSsboBindsMissing++;
#endif
                continue;
            }

            try
            {
                GL.ShaderStorageBlockBinding(programId, blockIndex, spec.BindingOrUnit);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GpuProgramLayout] Failed to assign SSBO binding for '{blockName}': {ex.Message}");
            }
        }
    }

    private void ApplyUniformUnitContract(int programId, Dictionary<string, BindingSpec> contract, Action<string>? warn)
    {
        if (contract.Count == 0)
        {
            return;
        }

        int prevProgram = 0;
        bool hasPrevProgram = false;

        try
        {
            GL.GetInteger(GetPName.CurrentProgram, out prevProgram);
            hasPrevProgram = true;
        }
        catch
        {
        }

        try
        {
            GL.UseProgram(programId);

            foreach (var (uniformName, spec) in contract)
            {
                int loc = GetUniformLocationOrArray0Cached(programId, uniformName);
                if (loc < 0)
                {
                    if (spec.Required)
                    {
                        WarnOnce($"uniform:{uniformName}", $"Program did not expose required uniform '{uniformName}'.", warn);
                    }

#if DEBUG
                    Diagnostics.SkippedUniformBindsMissing++;
#endif
                    continue;
                }

                // Prefer explicit bindings when present (skip redundant assignment if already correct).
                try
                {
                    GL.GetUniform(programId, loc, out int current);
                    if (current == spec.BindingOrUnit)
                    {
                        continue;
                    }
                }
                catch
                {
                    // Best-effort only.
                }

                try
                {
                    GL.Uniform1(loc, spec.BindingOrUnit);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GpuProgramLayout] Failed to set uniform '{uniformName}' to {spec.BindingOrUnit}: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                if (hasPrevProgram)
                {
                    GL.UseProgram(prevProgram);
                }
            }
            catch
            {
            }
        }
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
            loc = GL.GetUniformLocation(programId, uniformName);
            if (loc < 0)
            {
                loc = GL.GetUniformLocation(programId, $"{uniformName}[0]");
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

        if (!SupportsProgramInterfaceQueries())
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
            index = GL.GetUniformBlockIndex(programId, blockName);
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
            index = GL.GetProgramResourceIndex(programId, ProgramInterface.ShaderStorageBlock, blockName);
        }
        catch
        {
            index = -1;
        }

        shaderStorageBlockIndexCache[blockName] = index;
        return index;
    }

    private static IReadOnlyDictionary<string, int> TryBuildBufferBindingByName(int programId, ProgramInterface programInterface)
    {
        try
        {
            GL.GetProgramInterface(programId, programInterface, ProgramInterfaceParameter.ActiveResources, out int count);
            if (count <= 0)
            {
                return EmptyBindings;
            }

            GL.GetProgramInterface(programId, programInterface, ProgramInterfaceParameter.MaxNameLength, out int maxNameLen);
            if (maxNameLen <= 0)
            {
                maxNameLen = 256;
            }

            var bindings = new Dictionary<string, int>(count, StringComparer.Ordinal);
            var props = new[] { ProgramProperty.BufferBinding };
            var values = new int[1];

            for (int i = 0; i < count; i++)
            {
                GL.GetProgramResourceName(programId, programInterface, i, maxNameLen, out _, out string name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                GL.GetProgramResource(programId, programInterface, i, props.Length, props, values.Length, out _, values);
                bindings[name] = values[0];
            }

            return bindings;
        }
        catch
        {
            return EmptyBindings;
        }
    }

    private static (IReadOnlyDictionary<string, int> Samplers, IReadOnlyDictionary<string, int> Images) TryBuildTextureUnitBindings(int programId)
    {
        try
        {
            GL.GetProgramInterface(programId, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
            if (count <= 0)
            {
                return (EmptyBindings, EmptyBindings);
            }

            GL.GetProgramInterface(programId, ProgramInterface.Uniform, ProgramInterfaceParameter.MaxNameLength, out int maxNameLen);
            if (maxNameLen <= 0)
            {
                maxNameLen = 256;
            }

            // Query uniform type + location. Skip uniforms in blocks (BlockIndex != -1).
            var props = new[] { ProgramProperty.Type, ProgramProperty.Location, ProgramProperty.BlockIndex };
            var values = new int[props.Length];

            Dictionary<string, int>? samplerBindings = null;
            Dictionary<string, int>? imageBindings = null;

            for (int i = 0; i < count; i++)
            {
                GL.GetProgramResource(programId, ProgramInterface.Uniform, i, props.Length, props, values.Length, out _, values);

                int typeValue = values[0];
                int location = values[1];
                int blockIndex = values[2];

                if (blockIndex != -1 || location < 0)
                {
                    continue;
                }

                var type = (ActiveUniformType)typeValue;
                bool isSampler = IsSamplerType(type);
                bool isImage = !isSampler && IsImageType(type);
                if (!isSampler && !isImage)
                {
                    continue;
                }

                GL.GetProgramResourceName(programId, ProgramInterface.Uniform, i, maxNameLen, out _, out string name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                name = NormalizeUniformName(name);

                GL.GetUniform(programId, location, out int unitOrBinding);

                if (isSampler)
                {
                    samplerBindings ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    samplerBindings[name] = unitOrBinding;
                }
                else
                {
                    imageBindings ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    imageBindings[name] = unitOrBinding;
                }
            }

            return (samplerBindings ?? EmptyBindings, imageBindings ?? EmptyBindings);
        }
        catch
        {
            return (EmptyBindings, EmptyBindings);
        }
    }

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

    private static bool IsSamplerType(ActiveUniformType type)
    {
        // Keep this allocation-free; the set is stable across GL versions.
        return type is
            ActiveUniformType.Sampler1D
            or ActiveUniformType.Sampler2D
            or ActiveUniformType.Sampler3D
            or ActiveUniformType.SamplerCube
            or ActiveUniformType.Sampler1DShadow
            or ActiveUniformType.Sampler2DShadow
            or ActiveUniformType.Sampler1DArray
            or ActiveUniformType.Sampler2DArray
            or ActiveUniformType.Sampler1DArrayShadow
            or ActiveUniformType.Sampler2DArrayShadow
            or ActiveUniformType.Sampler2DMultisample
            or ActiveUniformType.Sampler2DMultisampleArray
            or ActiveUniformType.SamplerCubeShadow
            or ActiveUniformType.SamplerBuffer
            or ActiveUniformType.Sampler2DRect
            or ActiveUniformType.Sampler2DRectShadow
            or ActiveUniformType.IntSampler1D
            or ActiveUniformType.IntSampler2D
            or ActiveUniformType.IntSampler3D
            or ActiveUniformType.IntSamplerCube
            or ActiveUniformType.IntSampler1DArray
            or ActiveUniformType.IntSampler2DArray
            or ActiveUniformType.IntSampler2DMultisample
            or ActiveUniformType.IntSampler2DMultisampleArray
            or ActiveUniformType.IntSamplerBuffer
            or ActiveUniformType.IntSampler2DRect
            or ActiveUniformType.UnsignedIntSampler1D
            or ActiveUniformType.UnsignedIntSampler2D
            or ActiveUniformType.UnsignedIntSampler3D
            or ActiveUniformType.UnsignedIntSamplerCube
            or ActiveUniformType.UnsignedIntSampler1DArray
            or ActiveUniformType.UnsignedIntSampler2DArray
            or ActiveUniformType.UnsignedIntSampler2DMultisample
            or ActiveUniformType.UnsignedIntSampler2DMultisampleArray
            or ActiveUniformType.UnsignedIntSamplerBuffer
            or ActiveUniformType.UnsignedIntSampler2DRect
            or ActiveUniformType.SamplerCubeMapArray
            or ActiveUniformType.SamplerCubeMapArrayShadow
            or ActiveUniformType.IntSamplerCubeMapArray
            or ActiveUniformType.UnsignedIntSamplerCubeMapArray;
    }

    private static bool IsImageType(ActiveUniformType type)
    {
        return type is
            ActiveUniformType.Image1D
            or ActiveUniformType.Image2D
            or ActiveUniformType.Image3D
            or ActiveUniformType.Image2DRect
            or ActiveUniformType.ImageCube
            or ActiveUniformType.ImageBuffer
            or ActiveUniformType.Image1DArray
            or ActiveUniformType.Image2DArray
            or ActiveUniformType.Image2DMultisample
            or ActiveUniformType.Image2DMultisampleArray
            or ActiveUniformType.IntImage1D
            or ActiveUniformType.IntImage2D
            or ActiveUniformType.IntImage3D
            or ActiveUniformType.IntImage2DRect
            or ActiveUniformType.IntImageCube
            or ActiveUniformType.IntImageBuffer
            or ActiveUniformType.IntImage1DArray
            or ActiveUniformType.IntImage2DArray
            or ActiveUniformType.IntImage2DMultisample
            or ActiveUniformType.IntImage2DMultisampleArray
            or ActiveUniformType.UnsignedIntImage1D
            or ActiveUniformType.UnsignedIntImage2D
            or ActiveUniformType.UnsignedIntImage3D
            or ActiveUniformType.UnsignedIntImage2DRect
            or ActiveUniformType.UnsignedIntImageCube
            or ActiveUniformType.UnsignedIntImageBuffer
            or ActiveUniformType.UnsignedIntImage1DArray
            or ActiveUniformType.UnsignedIntImage2DArray
            or ActiveUniformType.UnsignedIntImage2DMultisample
            or ActiveUniformType.UnsignedIntImage2DMultisampleArray
            or ActiveUniformType.ImageCubeMapArray
            or ActiveUniformType.IntImageCubeMapArray
            or ActiveUniformType.UnsignedIntImageCubeMapArray;
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

    /// <summary>
    /// Binds a UBO to the binding point for the named uniform block.
    /// </summary>
    internal bool TryBindUniformBlock(int programId, string blockName, GpuUniformBuffer buffer, Action<string>? warn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(buffer);

        var active = ResolveUniformBlockActive(programId, blockName);
        if (active.State == ResolutionState.Missing)
        {
            if (uniformBlockContract.TryGetValue(blockName, out var spec) && spec.Required)
            {
                WarnOnce($"ubo:{blockName}", $"Uniform block '{blockName}' is inactive/optimized-away; skipping bind.", warn);
            }

#if DEBUG
            Diagnostics.SkippedUboBindsMissing++;
#endif
            return false;
        }

        if (active.State == ResolutionState.Unknown)
        {
#if DEBUG
            Diagnostics.SkippedDueToUnknown++;
#endif
        }

        if (!TryGetUniformBlockBinding(blockName, out int binding))
        {
            return false;
        }

        buffer.BindBase(binding);
        return true;
    }

    /// <summary>
    /// Binds an SSBO to the binding point for the named shader storage block.
    /// </summary>
    internal bool TryBindShaderStorageBlock(int programId, string blockName, GpuShaderStorageBuffer buffer, Action<string>? warn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(buffer);

        var active = ResolveShaderStorageBlockActive(programId, blockName);
        if (active.State == ResolutionState.Missing)
        {
            if (shaderStorageBlockContract.TryGetValue(blockName, out var spec) && spec.Required)
            {
                WarnOnce($"ssbo:{blockName}", $"Shader storage block '{blockName}' is inactive/optimized-away; skipping bind.", warn);
            }

#if DEBUG
            Diagnostics.SkippedSsboBindsMissing++;
#endif
            return false;
        }

        if (active.State == ResolutionState.Unknown)
        {
            // Can't prove the resource is inactive; allow the bind to preserve behavior.
#if DEBUG
            Diagnostics.SkippedDueToUnknown++;
#endif
        }

        if (!TryGetShaderStorageBlockBinding(blockName, out int binding))
        {
            return false;
        }

        buffer.BindBase(binding);
        return true;
    }

    /// <summary>
    /// Binds a texture (and optional sampler object) to the texture unit used by the named sampler uniform.
    /// </summary>
    /// <remarks>
    /// The sampler unit is read from the linked program at cache-build time; if the program later changes
    /// sampler uniforms via <c>glUniform1i</c>, the cache can become stale.
    /// </remarks>
    public bool TryBindSamplerTexture(string samplerUniformName, TextureTarget target, int textureId, int samplerId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);

        if (textureId == 0)
        {
            return false;
        }

        if (!TryGetSamplerUnit(samplerUniformName, out int unit))
        {
            return false;
        }

        GlStateCache.Current.BindTexture(target, unit, textureId);
        if (samplerId != 0)
        {
            GlStateCache.Current.BindSampler(unit, samplerId);
        }

        return true;
    }

    /// <summary>
    /// Binds a texture to the sampler unit for <paramref name="samplerUniformName"/> but only if the uniform is active.
    /// This avoids issuing GL bind calls for uniforms optimized away.
    /// </summary>
    internal bool TryBindSamplerTextureActive(
        int programId,
        string samplerUniformName,
        TextureTarget target,
        int textureId,
        int samplerId,
        Action<string>? warn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);

        var loc = ResolveUniformLocation(programId, samplerUniformName);
        if (loc.State == ResolutionState.Missing)
        {
            if (samplerContract.TryGetValue(samplerUniformName, out var spec) && spec.Required)
            {
                WarnOnce($"sampler:{samplerUniformName}", $"Sampler uniform '{samplerUniformName}' is inactive/optimized-away; skipping bind.", warn);
            }

#if DEBUG
            Diagnostics.SkippedSamplerBindsMissing++;
#endif
            return false;
        }

        if (!TryGetSamplerUnit(samplerUniformName, out int unit))
        {
            return false;
        }

        GlStateCache.Current.BindTexture(target, unit, textureId);
        if (samplerId != 0)
        {
            GlStateCache.Current.BindSampler(unit, samplerId);
        }
        else
        {
            GlStateCache.Current.UnbindSampler(unit);
        }

        return true;
    }

    public bool TryBindSamplerTexture(string samplerUniformName, TextureTarget target, int textureId, GpuSampler sampler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);
        ArgumentNullException.ThrowIfNull(sampler);

        if (textureId == 0)
        {
            return false;
        }

        if (!TryGetSamplerUnit(samplerUniformName, out int unit))
        {
            return false;
        }

        GlStateCache.Current.BindTexture(target, unit, textureId, sampler);
        return true;
    }

    /// <summary>
    /// Binds a texture to the image unit used by the named image uniform via <c>glBindImageTexture</c>.
    /// </summary>
    public bool TryBindImageTexture(
        string imageUniformName,
        int textureId,
        int level,
        bool layered,
        int layer,
        TextureAccess access,
        SizedInternalFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);

        if (textureId == 0)
        {
            return false;
        }

        if (!TryGetImageUnit(imageUniformName, out int unit))
        {
            return false;
        }

        GL.BindImageTexture(unit, textureId, level, layered, layer, access, format);
        return true;
    }

    internal bool TryBindImageTextureActive(
        int programId,
        string imageUniformName,
        int textureId,
        int level,
        bool layered,
        int layer,
        TextureAccess access,
        SizedInternalFormat format,
        Action<string>? warn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);

        var loc = ResolveUniformLocation(programId, imageUniformName);
        if (loc.State == ResolutionState.Missing)
        {
            if (imageContract.TryGetValue(imageUniformName, out var spec) && spec.Required)
            {
                WarnOnce($"image:{imageUniformName}", $"Image uniform '{imageUniformName}' is inactive/optimized-away; skipping bind.", warn);
            }

#if DEBUG
            Diagnostics.SkippedImageBindsMissing++;
#endif
            return false;
        }

        if (!TryGetImageUnit(imageUniformName, out int unit))
        {
            return false;
        }

        GL.BindImageTexture(unit, textureId, level, layered, layer, access, format);
        return true;
    }

    /// <summary>
    /// Binds a <see cref="GpuTexture"/> to the image unit used by the named image uniform.
    /// </summary>
    public bool TryBindImageTexture(
        string imageUniformName,
        GpuTexture texture,
        TextureAccess access = TextureAccess.ReadOnly,
        int level = 0,
        bool layered = false,
        int layer = 0,
        SizedInternalFormat? formatOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);
        ArgumentNullException.ThrowIfNull(texture);

        if (!texture.IsValid)
        {
            return false;
        }

        var format = formatOverride ?? (SizedInternalFormat)texture.InternalFormat;
        return TryBindImageTexture(imageUniformName, texture.TextureId, level, layered, layer, access, format);
    }

    /// <summary>
    /// Binds a <see cref="GpuBufferView"/> to the image unit used by the named image uniform.
    /// </summary>
    public bool TryBindImageTexture(
        string imageUniformName,
        GpuBufferView bufferTexture,
        TextureAccess access = TextureAccess.ReadOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);
        ArgumentNullException.ThrowIfNull(bufferTexture);

        if (!bufferTexture.IsValid)
        {
            return false;
        }

        return TryBindImageTexture(
            imageUniformName,
            bufferTexture.TextureId,
            level: 0,
            layered: false,
            layer: 0,
            access: access,
            format: bufferTexture.Format);
    }
}
