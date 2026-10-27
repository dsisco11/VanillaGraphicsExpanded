using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Integration;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Resolves borrowed binding footprints in the existing cache and composes their restoration owners.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly HashSet<(EPipelineState Kind, int Name)> boundaryRetiredResources = new();

    #region Internal API
    /// <summary>Resolves an unowned incoming executable without accepting failed-query defaults.</summary>
    internal bool TryResolveBoundaryProgram(out int program)
    {
        program = 0;
        try
        {
            GpuSupport.EnsureCurrentContext();
            currentProgram ??= QueryBoundary(() => GL.GetInteger(GetPName.CurrentProgram));
            program = currentProgram.Value;
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { BoundaryEntryFailure = error; return false; }
    }
    #endregion

    #region Private
    #region Capture
    /// <summary>Rejects unsupported resource slots before allocating cache arrays or selecting native units.</summary>
    private void ValidateBoundaryResourceLimits(EngineBoundaryResources footprint)
    {
        GpuSupport.EnsureCurrentContext();
        foreach (var texture in footprint.Textures)
            ValidateBoundarySlot(texture.Unit, GpuSupport.MaxCombinedTextureImageUnits);
        foreach (int image in footprint.Images) ValidateBoundarySlot(image, GpuSupport.MaxImageUnits);
        foreach (var buffer in footprint.Buffers)
            ValidateBoundarySlot(buffer.Slot, buffer.Target switch
            {
                BufferRangeTarget.UniformBuffer => GpuSupport.MaxUniformBufferBindings,
                BufferRangeTarget.ShaderStorageBuffer => GpuSupport.MaxShaderStorageBufferBindings,
                BufferRangeTarget.AtomicCounterBuffer => GpuSupport.MaxAtomicCounterBufferBindings,
                _ => throw new InvalidOperationException("Unsupported indexed resource footprint.")
            });
    }

    /// <summary>Rejects unavailable capabilities and slots outside the shared capability limit.</summary>
    private static void ValidateBoundarySlot(int slot, int limit)
    {
        if (limit <= 0) throw new InvalidOperationException("Unavailable resource-slot capability.");
        if (slot < 0 || slot >= limit) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    /// <summary>Resolves every borrowed slot before work and registers independent cleanup after shader ownership.</summary>
    private void CaptureBoundaryBindings(EngineBoundaryScope scope, EngineBoundaryResources footprint)
    {
        ValidateBoundaryResourceLimits(footprint);
        var bindings = new BoundaryBindingRestoration(this);
        bool textureSelection = footprint.Textures.Count != 0;
        int active = 0;
        if (textureSelection)
        {
            activeTextureUnit ??= QueryBoundary(() => GL.GetInteger(GetPName.ActiveTexture) - (int)TextureUnit.Texture0);
            active = activeTextureUnit.Value;
        }
        if (footprint.ProgramSelection)
        {
            currentProgram ??= QueryBoundary(() => GL.GetInteger(GetPName.CurrentProgram));
            int program = currentProgram.Value;
            bindings.Add(() =>
            {
                RequireBoundaryResource(EPipelineState.Program, program);
                OpenGLProgramSelection.Select(this, program);
            }, () => { currentProgram = null; currentProgramStagesKnown = false; },
                () => !currentProgramStagesKnown || currentProgram != program
                    || boundaryRetiredResources.Contains((EPipelineState.Program, program)));
        }
        if (footprint.Geometry)
        {
        currentVao ??= QueryBoundary(() => GL.GetInteger(GetPName.VertexArrayBinding));
        int vao = currentVao.Value;
        bindings.Add(() => { RequireBoundaryResource(EPipelineState.VertexArray, vao); if (currentVao != vao) OpenGLVertexSelection.Bind(this,vao); }, () => currentVao = null,
            () => currentVao != vao || boundaryRetiredResources.Contains((EPipelineState.VertexArray, vao)));
        }
        try
        {
            foreach (var slot in footprint.Textures) CaptureBoundaryTexture(slot.Unit, OpenGLImageValues.Target(slot.Target), bindings);
        }
        finally
        {
            // Texture queries select a unit temporarily. Failed entry must restore that selector too.
            try { if (textureSelection && activeTextureUnit != active) { OpenGLImageBindings.ActiveTexture(this, active); CheckBoundaryNativeError(); } }
            catch (Exception error)
            {
                activeTextureUnit = null;
                throw new EngineBoundaryRestoreException("Failed to restore texture selection during boundary entry.", error);
            }
        }
        foreach (int unit in footprint.Images) CaptureBoundaryImage(unit, bindings);
        var generic = new Dictionary<BufferTarget, int>();
        foreach (var slot in footprint.Buffers)
        {
            var target = (BufferTarget)slot.Target;
            generic.TryAdd(target, ResolveBoundaryBuffer(target));
            CaptureBoundaryIndexedBuffer(slot.Target, slot.Slot, bindings);
        }
        // Indexed buffer binds also alter the generic target, so restore that alias last.
        foreach (var value in generic)
            bindings.Add(() => { RequireBoundaryResource(EPipelineState.BufferBindings, value.Value); if (bufferBindingByTarget.GetValueOrDefault(value.Key) != value.Value) OpenGLBufferBindings.BindBuffer(this, value.Key, value.Value); },
                () => bufferBindingByTarget.Remove(value.Key),
                () => bufferBindingByTarget.GetValueOrDefault(value.Key) != value.Value || boundaryRetiredResources.Contains((EPipelineState.BufferBindings, value.Value)));
        if (textureSelection) bindings.Add(() => { if (activeTextureUnit != active) OpenGLImageBindings.ActiveTexture(this, active); }, () => activeTextureUnit = null, () => activeTextureUnit != active);
        scope.AddCleanup(EngineBoundaryCleanup.Bindings, bindings);

        if (footprint.Framebuffers)
        {
        currentReadFramebuffer ??= QueryBoundary(() => GL.GetInteger(GetPName.ReadFramebufferBinding));
        currentDrawFramebuffer ??= QueryBoundary(() => GL.GetInteger(GetPName.DrawFramebufferBinding));
        int read = currentReadFramebuffer.Value, draw = currentDrawFramebuffer.Value;
        var framebuffers = new BoundaryBindingRestoration(this);
        var readScope = new FramebufferScope(this, FramebufferTarget.ReadFramebuffer, read, draw);
        var drawScope = new FramebufferScope(this, FramebufferTarget.DrawFramebuffer, read, draw);
        framebuffers.Add(() => { RequireBoundaryResource(EPipelineState.FramebufferBindings, read); if (currentReadFramebuffer != read) readScope.Dispose(); },
            () => currentReadFramebuffer = null,
            () => currentReadFramebuffer != read || boundaryRetiredResources.Contains((EPipelineState.FramebufferBindings, read)));
        framebuffers.Add(() => { RequireBoundaryResource(EPipelineState.FramebufferBindings, draw); if (currentDrawFramebuffer != draw) drawScope.Dispose(); },
            () => { currentDrawFramebuffer = null; currentFramebuffer = null; },
            () => currentDrawFramebuffer != draw || boundaryRetiredResources.Contains((EPipelineState.FramebufferBindings, draw)));
        scope.AddCleanup(EngineBoundaryCleanup.Framebuffers, framebuffers);
        }
    }

    /// <summary>Captures a texture target and sampler through strict queries while keeping their authoritative cache entries.</summary>
    private void CaptureBoundaryTexture(int unit, TextureTarget target, BoundaryBindingRestoration restore)
    {
        if (!TryGetTextureBindingQuery(target, out var query)) throw new InvalidOperationException("Unsupported texture footprint target.");
        EnsureTextureUnitCapacity(unit);
        var slots = textureBindingsByUnit![unit] ??= new();
        if (!slots.TryGetValue(target, out int texture))
        {
            OpenGLImageBindings.ActiveTexture(this, unit); CheckBoundaryNativeError();
            texture = QueryBoundary(() => GL.GetInteger(query));
            slots[target] = texture;
        }
        if (!samplerBindingByUnit![unit].HasValue)
        {
            int samplerValue = QueryBoundaryIndexed(GetPName.SamplerBinding, unit);
            samplerBindingByUnit[unit] = samplerValue;
        }
        int sampler = samplerBindingByUnit[unit]!.Value;
        restore.Add(() => { RequireBoundaryResource(EPipelineState.TextureBindings, texture); if (!TryGetCachedBoundTexture(target, unit, out int current) || current != texture) OpenGLImageBindings.BindTexture(this, target, unit, texture); },
            // Texture binding also selects the active unit; neither result is trusted on failure.
            () => { textureBindingsByUnit?[unit]?.Remove(target); activeTextureUnit = null; },
            () => !TryGetCachedBoundTexture(target, unit, out int current) || current != texture || boundaryRetiredResources.Contains((EPipelineState.TextureBindings, texture)));
        restore.Add(() => { RequireBoundaryResource(EPipelineState.SamplerBindings, sampler); if (!TryGetCachedBoundSampler(unit, out int current) || current != sampler) OpenGLImageBindings.BindSampler(this, unit, sampler); },
            () => { if (samplerBindingByUnit is { } values && unit < values.Length) values[unit] = null; },
            () => !TryGetCachedBoundSampler(unit, out int current) || current != sampler || boundaryRetiredResources.Contains((EPipelineState.SamplerBindings, sampler)));
    }

    /// <summary>Captures all image-view parameters, including layered selection and format.</summary>
    private void CaptureBoundaryImage(int unit, BoundaryBindingRestoration restore)
    {
        if (!imageBindings.TryGetValue(unit, out var saved))
        {
            saved = new(QueryBoundaryIndexed((GetPName)All.ImageBindingName, unit),
                QueryBoundaryIndexed((GetPName)All.ImageBindingLevel, unit),
                QueryBoundaryIndexed((GetPName)All.ImageBindingLayered, unit) != 0,
                QueryBoundaryIndexed((GetPName)All.ImageBindingLayer, unit),
                (TextureAccess)QueryBoundaryIndexed((GetPName)All.ImageBindingAccess, unit),
                (SizedInternalFormat)QueryBoundaryIndexed((GetPName)All.ImageBindingFormat, unit));
            imageBindings[unit] = saved;
        }
        restore.Add(() => { RequireBoundaryResource(EPipelineState.TextureBindings, saved.Texture);
            OpenGLImageSlotBindings.BindImageTexture(this, unit, saved.Texture, saved.Level, saved.Layered, saved.Layer, saved.Access, saved.Format); },
            () => imageBindings.Remove(unit),
            () => !imageBindings.TryGetValue(unit, out var current) || current != saved || boundaryRetiredResources.Contains((EPipelineState.TextureBindings, saved.Texture)));
    }

    /// <summary>Captures indexed buffers with exact 64-bit offsets and sizes, retaining known base/range semantics.</summary>
    private void CaptureBoundaryIndexedBuffer(BufferRangeTarget target, int slot, BoundaryBindingRestoration restore)
    {
        if (!indexedBufferBindings.TryGetValue((target, slot), out var saved))
        {
            (GetPName Name, int Start, int Size) names = target switch
            {
                BufferRangeTarget.UniformBuffer => (GetPName.UniformBufferBinding, (int)All.UniformBufferStart, (int)All.UniformBufferSize),
                BufferRangeTarget.ShaderStorageBuffer => (GetPName.ShaderStorageBufferBinding, (int)All.ShaderStorageBufferStart, (int)All.ShaderStorageBufferSize),
                BufferRangeTarget.AtomicCounterBuffer => ((GetPName)All.AtomicCounterBufferBinding, (int)All.AtomicCounterBufferStart, (int)All.AtomicCounterBufferSize),
                _ => throw new InvalidOperationException("Unsupported indexed footprint.")
            };
            int buffer = QueryBoundaryIndexed(names.Name, slot);
            long offset = QueryBoundary(() => { GL.GetInteger64((GetIndexedPName)names.Start, slot, out long value); return value; });
            long size = QueryBoundary(() => { GL.GetInteger64((GetIndexedPName)names.Size, slot, out long value); return value; });
            saved = new(buffer, checked((nint)offset), checked((nint)size), buffer != 0 && size != 0);
            indexedBufferBindings[(target, slot)] = saved;
        }
        restore.Add(() =>
        {
            RequireBoundaryResource(EPipelineState.BufferBindings, saved.Buffer);
            if (indexedBufferBindings.TryGetValue((target, slot), out var current) && current == saved) return;
            if (saved.Range) OpenGLBufferBindings.BindBufferRange(this, target, slot, saved.Buffer, saved.Offset, saved.Size);
            else OpenGLBufferBindings.BindBufferBase(this, target, slot, saved.Buffer);
        }, () => { indexedBufferBindings.Remove((target, slot)); bufferBindingByTarget.Remove((BufferTarget)target); },
            () => !indexedBufferBindings.TryGetValue((target, slot), out var current) || current != saved || boundaryRetiredResources.Contains((EPipelineState.BufferBindings, saved.Buffer)));
    }

    /// <summary>Resolves generic buffer bindings without compatibility getters that substitute zero on failure.</summary>
    private int ResolveBoundaryBuffer(BufferTarget target)
    {
        if (bufferBindingByTarget.TryGetValue(target, out var value) && value.HasValue) return value.Value;
        if (!TryGetBufferBindingQuery(target, out var query)) throw new InvalidOperationException("Unsupported generic buffer footprint.");
        int result = QueryBoundary(() => GL.GetInteger(query));
        bufferBindingByTarget[target] = result;
        return result;
    }
    #endregion

    #region Retirement
    /// <summary>Records known retirement while a snapshot may still refer to the old numeric name.</summary>
    private void RecordBoundaryRetirement(EPipelineState kind, int name)
    {
        if (activeBoundary is not null && name != 0) boundaryRetiredResources.Add((kind, name));
    }

    /// <summary>Prevents binding a retired or reused borrowed name; snapshots never acquire resource ownership.</summary>
    internal void RequireBoundaryResource(EPipelineState kind, int name)
    {
        if (name != 0 && boundaryRetiredResources.Contains((kind, name)))
            throw new InvalidOperationException("A borrowed boundary resource was retired during the operation.");
    }
    #endregion
    #endregion

    /// <summary>Retains independent binding restoration actions without adding a second live binding cache.</summary>
    private sealed class BoundaryBindingRestoration(OpenGlStateCache cache) : IDisposable
    {
        private readonly List<(Action Restore, Action Forget, Func<bool> NeedsRestore)> fields = new();
        #region Internal API
        /// <summary>Appends one borrowed field in dependency order.</summary>
        internal void Add(Action restore, Action forget, Func<bool> needsRestore) => fields.Add((restore, forget, needsRestore));

        /// <summary>Attempts all independent borrowed fields and reports the complete failure set.</summary>
        public void Dispose()
        {
            var failures = new List<Exception>();
            // Predicates read live cache knowledge after earlier dependent restores.
            foreach (var field in fields)
                if (field.NeedsRestore()) cache.RestoreBoundaryField(field.Restore, field.Forget, failures);
            if (failures.Count != 0) throw new AggregateException(failures);
        }
        #endregion
    }
}
