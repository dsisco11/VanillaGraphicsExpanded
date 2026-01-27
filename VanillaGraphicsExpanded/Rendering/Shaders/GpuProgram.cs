using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using StageShader = VanillaGraphicsExpanded.Rendering.Shaders.Stages.Shader;
using VanillaGraphicsExpanded.Rendering.Shaders.Stages;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Base class for all VGE-owned shader programs.
///
/// Responsibilities:
/// - Own a runtime define-map; <see cref="SetDefine"/> triggers a recompile only on changes
/// - Compile from asset sources using <see cref="ShaderSourceCode"/> (imports handled inline)
/// - Apply a GL debug label to the linked program
///
/// NOTE: When VGE shader programs inline imports themselves, the Harmony import hook should
/// skip these programs to avoid double-processing (deferred decision).
/// </summary>
public abstract class GpuProgram : ShaderProgram
{
    private readonly record struct ProgramBlockBindingSpec(int BindingIndex, bool Required);

    private readonly StageShader vertexStage;
    private readonly StageShader fragmentStage;
    private readonly StageShader geometryStage;

    private readonly Dictionary<string, string?> defines = new(StringComparer.Ordinal);
    private readonly object defineLock = new();

    private readonly Dictionary<string, int> uniformLocationCache = new(StringComparer.Ordinal);
    private int uniformLocationCacheProgramId;

    private GpuProgramLayout resourceBindings = GpuProgramLayout.Empty;

    private readonly Dictionary<string, ProgramBlockBindingSpec> uniformBlockBindingSpecs = new(StringComparer.Ordinal);
    private int uniformBlockBindingSpecsProgramId;
    private readonly HashSet<string> warnedMissingUniformBlocks = new(StringComparer.Ordinal);

    private ICoreClientAPI? capi;
    private ILogger? log;

    private int recompileQueued;

    /// <summary>
    /// Returns <c>true</c> when this program has a non-zero <see cref="ShaderProgram.ProgramId"/>.
    /// </summary>
    public bool IsLinked => ProgramId != 0;

    /// <summary>
    /// Shader name used for GL debug labels and as the default stage source base-name.
    /// Stage source base-names can be overridden per-stage.
    /// </summary>
    protected string ShaderName => PassName;

    /// <summary>
    /// Base name for the vertex stage source (without extension).
    /// Defaults to <see cref="ShaderName"/>.
    /// </summary>
    protected virtual string VertexStageShaderName => ShaderName;

    /// <summary>
    /// Base name for the fragment stage source (without extension).
    /// Defaults to <see cref="ShaderName"/>.
    /// </summary>
    protected virtual string FragmentStageShaderName => ShaderName;

    /// <summary>
    /// Base name for the geometry stage source (without extension).
    /// Defaults to <see cref="ShaderName"/>.
    /// </summary>
    protected virtual string GeometryStageShaderName => ShaderName;

    /// <summary>
    /// Optional hook for derived programs that need to refresh caches after (re)compile.
    /// </summary>
    protected virtual void OnAfterCompile() { }

    /// <summary>
    /// Best-effort helper to assign a uniform-block binding point by block name (GLSL 330 friendly).
    /// </summary>
    protected bool TryAssignUniformBlockBinding(string blockName, int bindingIndex)
    {
        if (ProgramId == 0 || string.IsNullOrWhiteSpace(blockName))
        {
            return false;
        }

        try
        {
            int blockIndex = GL.GetUniformBlockIndex(ProgramId, blockName);
            if (blockIndex < 0)
            {
                return false;
            }

            GL.UniformBlockBinding(ProgramId, blockIndex, bindingIndex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers an expected uniform block binding for this program.
    /// </summary>
    /// <remarks>
    /// This is primarily intended for GLSL 330 where <c>layout(binding=...)</c> cannot be used without extensions.
    /// </remarks>
    protected void RegisterUniformBlockBinding(string blockName, int bindingIndex, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        uniformBlockBindingSpecs[blockName] = new ProgramBlockBindingSpec(bindingIndex, required);
    }

    /// <summary>
    /// Binds a UBO to the named uniform block for this program.
    /// </summary>
    internal bool TryBindUniformBlock(string blockName, GpuUniformBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(buffer);

        // Prefer the cached layout when available.
        if (resourceBindings.TryBindUniformBlock(blockName, buffer))
        {
            return true;
        }

        // Fallback: bind by the registered contract binding point (works even if interface queries are unavailable).
        if (uniformBlockBindingSpecs.TryGetValue(blockName, out var spec))
        {
            buffer.BindBase(spec.BindingIndex);
            return true;
        }

        return false;
    }

    private void ApplyRegisteredUniformBlockBindings()
    {
        if (ProgramId == 0 || uniformBlockBindingSpecs.Count == 0)
        {
            return;
        }

        // Reset warning cache when the program object changes (recompile).
        if (uniformBlockBindingSpecsProgramId != ProgramId)
        {
            uniformBlockBindingSpecsProgramId = ProgramId;
            warnedMissingUniformBlocks.Clear();
        }

        foreach (var (blockName, spec) in uniformBlockBindingSpecs)
        {
            bool ok = TryAssignUniformBlockBinding(blockName, spec.BindingIndex);
            if (ok || !spec.Required)
            {
                continue;
            }

            // Warn once per linked program.
            if (warnedMissingUniformBlocks.Add(blockName))
            {
                log?.Warning($"[VGE][{ShaderName}] Program did not expose required uniform block '{blockName}'.");
            }
        }
    }

    #region Texture Binding (Sampler-Aware)

    protected void BindTexture2D(string uniformName, GpuTexture? texture, int unit)
    {
        SetUniform(uniformName, unit);

        if (texture is null)
        {
            GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit, 0, sampler: null);
            return;
        }

        texture.Bind(unit);
    }

    protected void BindExternalTexture2D(string uniformName, int textureId, int unit, GpuSampler sampler)
    {
        SetUniform(uniformName, unit);
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, unit, textureId, sampler);
    }

    #endregion

    /// <summary>
    /// Gets cached binding-related resources for the currently linked program.
    /// Updated after successful <see cref="CompileAndLink"/>.
    /// </summary>
    internal GpuProgramLayout ResourceBindings => resourceBindings;

    /// <summary>
    /// Call from the program's Register method to enable define-triggered recompiles.
    /// </summary>
    public void Initialize(ICoreClientAPI api, ILogger? logger = null)
    {
        capi = api ?? throw new ArgumentNullException(nameof(api));
        log = logger ?? api.Logger;

        // Stage ownership is kept here, but stage behavior is fully encapsulated.
        // These delegates are safe because they are only invoked after Initialize() (when capi is set).
        // Vertex/Fragment/Geometry slots are provided by the engine ShaderProgram base.
        

        // Keep AssetDomain aligned with the import system default unless a derived program explicitly overrides it.
        if (string.IsNullOrWhiteSpace(AssetDomain))
        {
            AssetDomain = ShaderImportsSystem.DefaultDomain;
        }

        // IMPORTANT: Memory shader programs must provide stage instances themselves.
        // The engine may attempt to compile/validate registered programs and will log "shader missing" (and may NRE)
        // if these slots are null.
        VertexShader ??= (global::Vintagestory.Client.NoObf.Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader ??= (global::Vintagestory.Client.NoObf.Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }

    /// <summary>
    /// Updates a define value and schedules a recompile if it changed.
    /// A null value means <c>#define NAME</c> (no explicit value).
    /// </summary>
    public bool SetDefine(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool changed;
        lock (defineLock)
        {
            changed = !defines.TryGetValue(name, out var existing) || existing != value;
            if (changed)
            {
                defines[name] = value;
            }
        }

        if (changed)
        {
            RequestRecompile();
        }

        return changed;
    }


    /// <summary>
    /// Removes a define and schedules a recompile if it existed.
    /// </summary>
    public bool RemoveDefine(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool changed;
        lock (defineLock)
        {
            changed = defines.Remove(name);
        }

        if (changed)
        {
            RequestRecompile();
        }

        return changed;
    }


    #region Uniform Setters (VGE Numerics)

    /// <summary>
    /// Sets a <c>bool</c>/<c>int</c>-compatible uniform.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, bool value)
    {
        SetUniform1(uniformName, value ? 1 : 0);
    }

    /// <summary>
    /// Sets an <c>int</c>-compatible uniform.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, int value)
    {
        SetUniform1(uniformName, value);
    }

    /// <summary>
    /// Sets a <c>float</c>-compatible uniform.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, float value)
    {
        SetUniform1(uniformName, value);
    }

    /// <summary>
    /// Sets a <c>vec2</c>-compatible uniform from a Vintage Story <see cref="Vec2f"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in Vec2f value)
    {
        SetUniform2(uniformName, value.X, value.Y);
    }

    /// <summary>
    /// Sets a <c>vec3</c>-compatible uniform from a Vintage Story <see cref="Vec3f"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in Vec3f value)
    {
        SetUniform3(uniformName, value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Sets a <c>vec4</c>-compatible uniform from a Vintage Story <see cref="Vec4f"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in Vec4f value)
    {
        SetUniform4(uniformName, value.X, value.Y, value.Z, value.W);
    }

    /// <summary>
    /// Sets a <c>dvec3</c>/<c>vec3</c>-compatible uniform from a VGE <see cref="Vector3d"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in Vector3d value)
    {
        SetUniform3(uniformName, value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Sets a <c>dvec4</c>/<c>vec4</c>-compatible uniform from a VGE <see cref="Vector4d"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in Vector4d value)
    {
        SetUniform4(uniformName, value.X, value.Y, value.Z, value.W);
    }

    /// <summary>
    /// Sets an <c>ivec3</c>-compatible uniform from a VGE <see cref="VectorInt3"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in VectorInt3 value)
    {
        SetUniform3(uniformName, value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Sets an <c>ivec4</c>-compatible uniform from a VGE <see cref="VectorInt4"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in VectorInt4 value)
    {
        SetUniform4(uniformName, value.X, value.Y, value.Z, value.W);
    }

    /// <summary>
    /// Sets a <c>uvec3</c>-compatible uniform from a VGE <see cref="VectorUInt3"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in VectorUInt3 value)
    {
        SetUniform3u(uniformName, value.X, value.Y, value.Z);
    }

    /// <summary>
    /// Sets a <c>uvec4</c>-compatible uniform from a VGE <see cref="VectorUInt4"/>.
    /// No-ops if the uniform is not active in the linked program.
    /// </summary>
    protected void SetUniform(string uniformName, in VectorUInt4 value)
    {
        SetUniform4u(uniformName, value.X, value.Y, value.Z, value.W);
    }

    private void SetUniform1(string uniformName, int value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform1(loc, value);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (int): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform1(string uniformName, float value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform1(loc, value);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (float): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform2(string uniformName, float x, float y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform2(loc, x, y);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (vec2): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform3(string uniformName, float x, float y, float z)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform3(loc, x, y, z);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (vec3): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform4(string uniformName, float x, float y, float z, float w)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform4(loc, x, y, z, w);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (vec4): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform3(string uniformName, double x, double y, double z)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform3(loc, x, y, z);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (dvec3): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform4(string uniformName, double x, double y, double z, double w)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform4(loc, x, y, z, w);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (dvec4): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform3(string uniformName, int x, int y, int z)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform3(loc, x, y, z);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (ivec3): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform4(string uniformName, int x, int y, int z, int w)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform4(loc, x, y, z, w);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (ivec4): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform3u(string uniformName, uint x, uint y, uint z)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform3(loc, x, y, z);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (uvec3): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void SetUniform4u(string uniformName, uint x, uint y, uint z, uint w)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0) return;

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform4(loc, x, y, z, w);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set uniform '{uniformName}' (uvec4): {ex.Message}");
        }
        finally
        {
            TryRestoreProgram(prevProgram);
        }
    }

    private void TryRestoreProgram(int prevProgram)
    {
        try
        {
            if (prevProgram != 0 && prevProgram != ProgramId)
            {
                GL.UseProgram(prevProgram);
            }
        }
        catch
        {
            // Best-effort restore.
        }
    }

    #endregion

    #region Program Binding

    /// <summary>
    /// Binds this program (using the engine's <see cref="ShaderProgram.Use"/>), returning a scope that
    /// restores the previous program binding when disposed.
    /// </summary>
    public ProgramUseScope UseScope()
    {
        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
        }
        catch
        {
            prevProgram = 0;
        }

        try
        {
            Use();
        }
        catch
        {
            // Best-effort: some callers may run during shutdown/context loss.
        }

        return new ProgramUseScope(prevProgram, ProgramId);
    }

    /// <summary>
    /// Attempts to bind this program.
    /// </summary>
    public bool TryUse()
    {
        if (ProgramId == 0)
        {
            return false;
        }

        try
        {
            Use();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Unbinds any program (binds program 0).
    /// </summary>
    public static void Unuse()
    {
        try
        {
            GL.UseProgram(0);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Scope that restores the previous program binding when disposed.
    /// </summary>
    public readonly struct ProgramUseScope : IDisposable
    {
        private readonly int previousProgramId;
        private readonly int currentProgramId;

        public ProgramUseScope(int previousProgramId, int currentProgramId)
        {
            this.previousProgramId = previousProgramId;
            this.currentProgramId = currentProgramId;
        }

        public void Dispose()
        {
            if (previousProgramId == 0 || previousProgramId == currentProgramId)
            {
                return;
            }

            try
            {
                GL.UseProgram(previousProgramId);
            }
            catch
            {
            }
        }
    }

    #endregion


    #region Uniform Arrays

    /// <summary>
    /// Attempts to set a <c>vec3</c> uniform array element (e.g. <c>name[i]</c>) without relying on
    /// driver- and optimizer-sensitive per-element uniform naming.
    /// 
    /// This uses the OpenGL rule that array elements for basic types occupy consecutive locations,
    /// so <c>location(name[0]) + i</c> addresses <c>name[i]</c>.
    /// 
    /// The program must be bound via <see cref="ShaderProgram.Use"/> before calling.
    /// Returns false if the base array uniform is not present/active in the linked program.
    /// </summary>
    protected bool TryUniformArrayElement(string uniformName, int index, Vec3f value)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        int loc0 = GetUniformLocationOrArray0(uniformName);
        if (loc0 < 0)
        {
            return false;
        }

        int prevProgram = 0;
        try
        {
            prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform3(loc0 + index, value.X, value.Y, value.Z);
            return true;
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set vec3 array element '{uniformName}[{index}]': {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (prevProgram != 0 && prevProgram != ProgramId)
                {
                    GL.UseProgram(prevProgram);
                }
            }
            catch
            {
                // Best-effort restore.
            }
        }
    }

    /// <summary>
    /// Attempts to set a <c>vec3</c> uniform array starting at element 0.
    /// The program must be bound via <see cref="ShaderProgram.Use"/> before calling.
    /// Returns false if the uniform is not present/active in the linked program.
    /// </summary>
    protected bool TryUniformArray(string uniformName, ReadOnlySpan<Vec3f> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        if (values.Length == 0)
        {
            return true;
        }

        int loc0 = GetUniformLocationOrArray0(uniformName);
        if (loc0 < 0)
        {
            return false;
        }

        int floatCount = values.Length * 3;
        float[] buffer = ArrayPool<float>.Shared.Rent(floatCount);
        try
        {
            int j = 0;
            foreach (var v in values)
            {
                buffer[j++] = v.X;
                buffer[j++] = v.Y;
                buffer[j++] = v.Z;
            }

            int prevProgram = GL.GetInteger(GetPName.CurrentProgram);
            if (prevProgram != ProgramId)
            {
                GL.UseProgram(ProgramId);
            }

            GL.Uniform3(loc0, values.Length, buffer);

            if (prevProgram != ProgramId)
            {
                GL.UseProgram(prevProgram);
            }

            return true;
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set vec3 array '{uniformName}': {ex.Message}");
            return false;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Returns the cached uniform location for <paramref name="uniformName"/>.
    /// When <paramref name="uniformName"/> refers to an array, this method also tries <c>name[0]</c>,
    /// since different compilers expose either the base name or the explicit element name.
    /// </summary>
    protected int GetUniformLocationOrArray0(string uniformName)
    {
        if (ProgramId == 0)
        {
            return -1;
        }

        if (uniformLocationCacheProgramId != ProgramId)
        {
            uniformLocationCache.Clear();
            uniformLocationCacheProgramId = ProgramId;
        }

        if (uniformLocationCache.TryGetValue(uniformName, out int cached))
        {
            return cached;
        }

        // Spec allows querying the base name OR the [0] name.
        // Some compilers only expose one of these names.
        int loc = GL.GetUniformLocation(ProgramId, uniformName);
        if (loc < 0)
        {
            loc = GL.GetUniformLocation(ProgramId, $"{uniformName}[0]");
        }

        uniformLocationCache[uniformName] = loc;
        return loc;
    }

    #endregion

    /// <summary>
    /// Compiles and links using VGE's source pipeline (imports + AST define injection).
    /// Intended for memory shader programs.
    /// </summary>
    public bool CompileAndLink()
    {
        if (capi is null)
        {
            throw new InvalidOperationException("GpuProgram was not initialized. Call Initialize(api) first.");
        }

        try
        {
            // Preflight: avoid Vintage Story engine NREs by ensuring stage assets exist before we even attempt compilation.
            // This also provides a much clearer error message than the engine's generic "shader missing" logs.
            string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;

            string vshPath = $"shaders/{VertexStageShaderName}.vsh";
            string fshPath = $"shaders/{FragmentStageShaderName}.fsh";

            bool hasVsh = capi.Assets.TryGet(AssetLocation.Create(vshPath, domain), loadAsset: true) is not null;
            bool hasFsh = capi.Assets.TryGet(AssetLocation.Create(fshPath, domain), loadAsset: true) is not null;

            if (!hasVsh || !hasFsh)
            {
                if (!hasVsh)
                {
                    log?.Error($"[VGE][{ShaderName}] Missing vertex shader asset: {domain}:{vshPath}");
                }
                if (!hasFsh)
                {
                    log?.Error($"[VGE][{ShaderName}] Missing fragment shader asset: {domain}:{fshPath}");
                }

                log?.Error($"[VGE][{ShaderName}] Shader assets were not found. The engine may crash if compilation proceeds; skipping Compile().");
                return false;
            }

            IReadOnlyDictionary<string, string?> defineSnapshot;
            lock (defineLock)
            {
                defineSnapshot = new Dictionary<string, string?>(defines, StringComparer.Ordinal);
            }

            vertexStage.LoadAndApply(capi, VertexStageShaderName, defineSnapshot, log);
            fragmentStage.LoadAndApply(capi, FragmentStageShaderName, defineSnapshot, log);

            // Geometry stage is optional.
            geometryStage.TryLoadAndApplyOptional(capi, GeometryStageShaderName, defineSnapshot, log);

            bool ok = Compile();
            if (ok)
            {
                ApplyRegisteredUniformBlockBindings();
                resourceBindings = GpuProgramLayout.TryBuild(ProgramId);
                GlDebug.TryLabel(OpenTK.Graphics.OpenGL.ObjectLabelIdentifier.Program, ProgramId, ShaderName);
                OnAfterCompile();
            }
            else
            {
                resourceBindings = GpuProgramLayout.Empty;
                log?.Warning($"[VGE] Shader compile failed: {ShaderName}");
                LogDiagnostics();
            }

            return ok;
        }
        catch (Exception ex)
        {
            // Never let shader compilation exceptions bring down the client.
            log?.Error($"[VGE][{ShaderName}] Exception during CompileAndLink(): {ex}");
            return false;
        }
    }

    protected virtual void LogDiagnostics()
    {
        // Called only on compilation failures, on the render thread.
        // All OpenGL diagnostics are best-effort and must never throw.
        try
        {
            var v = vertexStage.CompileDiagnostics();
            var f = fragmentStage.CompileDiagnostics();
            var g = geometryStage.EmittedSource is null ? new StageShader.StageCompileDiagnostics { Success = true, InfoLog = string.Empty, ShaderId = 0 } : geometryStage.CompileDiagnostics();

            try
            {
                if (!string.IsNullOrWhiteSpace(v.InfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Vertex shader info log:\n{v.InfoLog}");
                }
                if (!string.IsNullOrWhiteSpace(f.InfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Fragment shader info log:\n{f.InfoLog}");
                }
                if (!string.IsNullOrWhiteSpace(g.InfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Geometry shader info log:\n{g.InfoLog}");
                }

                var link = ShaderProgramDiagnostics.LinkDiagnosticsOnly(v.ShaderId, f.ShaderId, g.ShaderId);
                if (!string.IsNullOrWhiteSpace(link.ProgramInfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Program link info log:\n{link.ProgramInfoLog}");
                }

                // Emit some context that will matter for future remapping.
                if (vertexStage.SourceCode?.ImportInlining.HadImports == true)
                {
                    log?.Notification($"[VGE][{ShaderName}] Vertex stage had imports; source-map available={vertexStage.SourceCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(vertexStage.SourceCode.ImportResult) is not null}");
                }
                if (fragmentStage.SourceCode?.ImportInlining.HadImports == true)
                {
                    log?.Notification($"[VGE][{ShaderName}] Fragment stage had imports; source-map available={fragmentStage.SourceCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(fragmentStage.SourceCode.ImportResult) is not null}");
                }
                if (geometryStage.SourceCode?.ImportInlining.HadImports == true)
                {
                    log?.Notification($"[VGE][{ShaderName}] Geometry stage had imports; source-map available={geometryStage.SourceCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(geometryStage.SourceCode.ImportResult) is not null}");
                }
            }
            finally
            {
                StageShader.DeleteDiagnosticsShader(v);
                StageShader.DeleteDiagnosticsShader(f);
                StageShader.DeleteDiagnosticsShader(g);
            }
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to capture GL shader diagnostics: {ex}");
        }
    }

    protected virtual void RequestRecompile()
    {
        var api = capi;
        if (api is null)
        {
            return;
        }

        // Collapse multiple SetDefine calls into a single recompile.
        if (Interlocked.Exchange(ref recompileQueued, 1) != 0)
        {
            return;
        }

        api.Event.EnqueueMainThreadTask(
            () =>
            {
                Interlocked.Exchange(ref recompileQueued, 0);

                try
                {
                    CompileAndLink();
                }
                catch (Exception ex)
                {
                    log?.Error($"[VGE] Shader recompile failed for '{ShaderName}': {ex}");
                }
            },
            $"vge:recompile:{ShaderName}");
    }

    protected GpuProgram()
    {
        vertexStage = new StageShader(
            stageExtension: "vsh",
            engineShaderType: EnumShaderType.VertexShader,
            getSlot: () => VertexShader,
            setSlot: s => VertexShader = (global::Vintagestory.Client.NoObf.Shader)s);

        fragmentStage = new StageShader(
            stageExtension: "fsh",
            engineShaderType: EnumShaderType.FragmentShader,
            getSlot: () => FragmentShader,
            setSlot: s => FragmentShader = (global::Vintagestory.Client.NoObf.Shader)s);

        geometryStage = new StageShader(
            stageExtension: "gsh",
            engineShaderType: EnumShaderType.GeometryShader,
            getSlot: () => GeometryShader,
            setSlot: s => GeometryShader = (global::Vintagestory.Client.NoObf.Shader)s);
    }
}
