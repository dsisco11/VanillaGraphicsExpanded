using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains bindings knowledge in the shared backend state owner.</summary>
internal sealed partial class OpenGlStateCache
{
    private int? currentProgram;
    // CURRENT_PROGRAM identifies the uniform destination. Desktop EXT selectors
    // can change that destination or individual stages without a full UseProgram.
    private bool currentProgramStagesKnown;
    private int? currentVao;
    private int? currentProgramPipeline;
    private int? currentFramebuffer;
    private int? currentReadFramebuffer;
    private int? currentDrawFramebuffer;
    private int? currentRenderbuffer;
    private int? currentTransformFeedback;

    private int? activeTextureUnit;
    private Dictionary<TextureTarget, int>[]? textureBindingsByUnit;
    private int?[]? samplerBindingByUnit;

    private readonly Dictionary<BufferTarget, int?> bufferBindingByTarget = new();
    private readonly Dictionary<int, int> elementArrayBufferByVao = new();

    internal int GetCurrentProgram()
    {
        if (currentProgram.HasValue) return currentProgram.Value;

        try { currentProgram = GL.GetInteger(GetPName.CurrentProgram); }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        { currentProgram = 0; }

        return currentProgram.Value;
    }

    internal bool TryGetCachedCurrentProgram(out int programId)
    {
        if (currentProgram.HasValue)
        {
            programId = currentProgram.Value;
            return true;
        }

        programId = 0;
        return false;
    }

    internal bool TryGetCachedCurrentVao(out int vaoId)
    {
        if (currentVao.HasValue)
        {
            vaoId = currentVao.Value;
            return true;
        }

        vaoId = 0;
        return false;
    }

    internal bool TryGetCachedCurrentFramebuffer(FramebufferTarget target, out int fboId)
    {
        int? cached = target switch
        {
            FramebufferTarget.ReadFramebuffer => currentReadFramebuffer,
            FramebufferTarget.DrawFramebuffer => currentDrawFramebuffer,
            _ => currentFramebuffer
        };

        if (cached.HasValue)
        {
            fboId = cached.Value;
            return true;
        }

        fboId = 0;
        return false;
    }

    internal bool TryGetCachedActiveTextureUnit(out int unit)
    {
        if (activeTextureUnit.HasValue)
        {
            unit = activeTextureUnit.Value;
            return true;
        }

        unit = 0;
        return false;
    }

    internal bool TryGetCachedBoundTexture(TextureTarget target, int unit, out int textureId)
    {
#if DEBUG
        TextureCacheChecks++;
#endif
        textureId = 0;

        if (unit < 0)
        {
            return false;
        }

        if (textureBindingsByUnit is null || unit >= textureBindingsByUnit.Length)
        {
            return false;
        }

        var dict = textureBindingsByUnit[unit];
        if (dict is null)
        {
            return false;
        }

        return dict.TryGetValue(target, out textureId);
    }

    internal bool TryGetCachedBoundSampler(int unit, out int samplerId)
    {
#if DEBUG
        SamplerCacheChecks++;
#endif
        samplerId = 0;

        if (unit < 0)
        {
            return false;
        }

        if (samplerBindingByUnit is null || unit >= samplerBindingByUnit.Length)
        {
            return false;
        }

        int? cached = samplerBindingByUnit[unit];
        if (!cached.HasValue)
        {
            return false;
        }

        samplerId = cached.Value;
        return true;
    }

    internal void UnbindProgram()
    {
        OpenGLProgramSelection.Select(this,0);
    }

    internal int GetCurrentVao()
    {
        if (currentVao.HasValue) return currentVao.Value;

        try
        {
            GL.GetInteger(GetPName.VertexArrayBinding, out int vao);
            currentVao = vao;
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
            currentVao = 0;
        }

        return currentVao.Value;
    }

    internal void UnbindVertexArray()
    {
        OpenGLVertexSelection.Bind(this,0);
    }

    internal VaoScope BindVertexArrayScope(int vaoId)
    {
        int previous = VaoScopeTracker.Begin(vaoId);
        return new VaoScope(previous);
    }

    /// <summary>Restores its original selector only within the captured native namespace.</summary>
    public readonly struct VaoScope : IDisposable
    {
        private readonly int previous;
        private readonly OpenGlStateCache cache;
        private readonly (nint Handle, long Generation) origin;

        /// <summary>Retains the original selector, cache and physical context lifetime.</summary>
        internal VaoScope(int previous)
        {
            this.previous = previous;
            cache = Current;
            origin = Integration.RenderContextRegistry.CaptureResourceContext();
        }

        /// <summary>Leaves a foreign admitted namespace untouched instead of restoring an old numeric name.</summary>
        public void Dispose()
        {
            if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            {
                if (cache.OwnsKnowledgeFor(origin))
                {
                    VaoScopeTracker.ClearThreadStack();
                    cache.currentVao = null;
                }
                return;
            }
            // Re-admit the original namespace if another context used this cache meanwhile.
            _ = RHI.RHICommandContext.Current;
            VaoScopeTracker.End(previous);
        }
    }

    /// <summary>Returns the requested binding, treating the combined query as the driver's draw-binding alias.</summary>
    internal int GetCurrentFramebuffer(FramebufferTarget target)
    {
        int? cached = target switch
        {
            FramebufferTarget.ReadFramebuffer => currentReadFramebuffer,
            FramebufferTarget.DrawFramebuffer => currentDrawFramebuffer,
            _ => currentFramebuffer
        };

        if (cached.HasValue) return cached.Value;

        try
        {
            int value = target switch
            {
                FramebufferTarget.ReadFramebuffer => GL.GetInteger(GetPName.ReadFramebufferBinding),
                FramebufferTarget.DrawFramebuffer => GL.GetInteger(GetPName.DrawFramebufferBinding),
                _ => GL.GetInteger(GetPName.FramebufferBinding)
            };

            // GL_FRAMEBUFFER_BINDING queries the draw binding only. A query must not
            // overwrite a distinct read binding as a combined-target bind would.
            SetFramebufferCache(target == FramebufferTarget.Framebuffer ? FramebufferTarget.DrawFramebuffer : target, value);
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
            SetFramebufferCache(target == FramebufferTarget.Framebuffer ? FramebufferTarget.DrawFramebuffer : target, 0);
        }

        return GetCurrentFramebuffer(target);
    }

    /// <summary>Preserves actual bindings, optionally binding a temporary target; combined scopes preserve read and draw independently.</summary>
    internal FramebufferScope BindFramebufferScope(FramebufferTarget target = FramebufferTarget.Framebuffer, int? fboId = null)
    {
        // A one-direction scope neither changes nor needs to discover the other binding.
        int previousRead = target == FramebufferTarget.DrawFramebuffer ? 0
            : GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer);
        int previousDraw = target == FramebufferTarget.ReadFramebuffer ? 0
            : GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        if (fboId.HasValue) OpenGLFramebufferSelection.Bind(this, target, fboId.Value);
        return new FramebufferScope(this, target, previousRead, previousDraw);
    }

    /// <summary>Restores framebuffer bindings only; attachment routing and viewport are unchanged by binding.</summary>
    public readonly struct FramebufferScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly FramebufferTarget target;
        private readonly int previousRead;
        private readonly int previousDraw;
        private readonly (nint Handle, long Generation) origin;

        #region Internal API
        /// <summary>Retains independent binding names for the requested target.</summary>
        internal FramebufferScope(OpenGlStateCache cache, FramebufferTarget target, int previousRead, int previousDraw)
        {
            this.cache = cache;
            this.target = target;
            this.previousRead = previousRead;
            this.previousDraw = previousDraw;
            origin = Integration.RenderContextRegistry.CaptureResourceContext();
        }

        /// <summary>Restores only the selected binding, or both independent bindings for a combined scope.</summary>
        public void Dispose()
        {
            // A stale scope cannot restore selectors or erase knowledge in another context.
            if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            {
                if (cache.OwnsKnowledgeFor(origin))
                {
                    if (target != FramebufferTarget.DrawFramebuffer) cache.currentReadFramebuffer = null;
                    if (target != FramebufferTarget.ReadFramebuffer)
                    { cache.currentDrawFramebuffer = null; cache.currentFramebuffer = null; }
                }
                return;
            }
            // Native identity may be original again while the thread-local cache still belongs elsewhere.
            _ = RHI.RHICommandContext.Current;
            if (target != FramebufferTarget.DrawFramebuffer)
                OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, previousRead);
            if (target != FramebufferTarget.ReadFramebuffer)
                OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.DrawFramebuffer, previousDraw);
        }
        #endregion
    }
    /// <summary>Records a completed VGE or engine bind without issuing GL calls; the combined query aliases the draw target.</summary>
    internal void SetFramebufferCache(FramebufferTarget target, int value)
    {
        if (target == FramebufferTarget.Framebuffer)
        {
            currentFramebuffer = value;
            currentReadFramebuffer = value;
            currentDrawFramebuffer = value;
            return;
        }

        if (target == FramebufferTarget.ReadFramebuffer)
        {
            currentReadFramebuffer = value;
            return;
        }

        if (target == FramebufferTarget.DrawFramebuffer)
        {
            currentDrawFramebuffer = value;
            currentFramebuffer = value;
        }
    }

    internal int GetCurrentProgramPipeline()
    {
        if (currentProgramPipeline.HasValue) return currentProgramPipeline.Value;

        try { currentProgramPipeline = GL.GetInteger(GetPName.ProgramPipelineBinding); }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        { currentProgramPipeline = 0; }

        return currentProgramPipeline.Value;
    }

    internal void UnbindProgramPipeline()
    {
        OpenGLProgramPipelineSelection.Bind(this,0);
    }

    internal ProgramPipelineScope BindProgramPipelineScope(int pipelineId)
    {
        int previous = ProgramPipelineScopeTracker.Begin(pipelineId);
        return new ProgramPipelineScope(previous);
    }

    /// <summary>Restores its original selector only within the captured native namespace.</summary>
    public readonly struct ProgramPipelineScope : IDisposable
    {
        private readonly int previous;
        private readonly OpenGlStateCache cache;
        private readonly (nint Handle, long Generation) origin;

        /// <summary>Retains the original selector, cache and physical context lifetime.</summary>
        internal ProgramPipelineScope(int previous)
        {
            this.previous = previous;
            cache = Current;
            origin = Integration.RenderContextRegistry.CaptureResourceContext();
        }

        /// <summary>Leaves a foreign admitted namespace untouched instead of restoring an old numeric name.</summary>
        public void Dispose()
        {
            if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            {
                if (cache.OwnsKnowledgeFor(origin))
                {
                    ProgramPipelineScopeTracker.ClearThreadStack();
                    cache.currentProgramPipeline = null;
                }
                return;
            }
            // Re-admit the original namespace if another context used this cache meanwhile.
            _ = RHI.RHICommandContext.Current;
            ProgramPipelineScopeTracker.End(previous);
        }
    }

    /// <summary>Resolves unknown renderbuffer state without consuming the caller's native error queue.</summary>
    internal int GetCurrentRenderbuffer()
    {
        if (currentRenderbuffer.HasValue) return currentRenderbuffer.Value;
        // Metadata observation can recover bindings after a rejected native command.
        // Boundary restoration owns any required diagnostics; this query preserves errors.
        currentRenderbuffer = RHI.Backends.OpenGL.OpenGLRHIContext.ReadImageSelector(GetPName.RenderbufferBinding);
        return currentRenderbuffer.Value;
    }





    internal RenderbufferScope BindRenderbufferScope(int renderbufferId)
    {
        int previous = RenderbufferScopeTracker.Begin(renderbufferId);
        return new RenderbufferScope(previous);
    }

    /// <summary>Retains an original renderbuffer selector for context-affine restoration.</summary>
    public readonly struct RenderbufferScope : IDisposable
    {
        private readonly int previous;
        private readonly OpenGlStateCache cache;
        private readonly RHI.RHICommandContext context;
        private readonly (nint Handle, long Generation) origin;

        /// <summary>Captures the original renderbuffer selector and its native context lifetime.</summary>
        internal RenderbufferScope(int previous)
        {
            this.previous = previous;
            cache = OpenGlStateCache.Current;
            context = RHI.RHICommandContext.Current;
            origin = Integration.RenderContextRegistry.CaptureResourceContext();
        }

        /// <summary>Restores the original renderbuffer selector without modifying foreign context knowledge.</summary>
        public void Dispose()
        {
            // Foreign disposal may withdraw only facts still owned by the original namespace.
            if (!context.IsCurrentContext)
            {
                if (cache.OwnsKnowledgeFor(origin)) { RenderbufferScopeTracker.ClearThreadStack(); cache.RenderbufferKnowledge = null; }
                return;
            }
            context.RequireContext();
            RenderbufferScopeTracker.End(previous);
        }
    }

    private sealed class VaoScopeTracker : BindScopeTracker<VaoScopeTracker>
    {
        protected override int GetCurrent() => OpenGlStateCache.Current.GetCurrentVao();

        protected override void Bind(int id) => OpenGLVertexSelection.Bind(OpenGlStateCache.Current,id);
    }

    private sealed class ProgramPipelineScopeTracker : BindScopeTracker<ProgramPipelineScopeTracker>
    {
        protected override int GetCurrent() => OpenGlStateCache.Current.GetCurrentProgramPipeline();

        protected override void Bind(int id) => OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current,id);
    }

    private sealed class RenderbufferScopeTracker : BindScopeTracker<RenderbufferScopeTracker>
    {
        protected override int GetCurrent() => OpenGlStateCache.Current.GetCurrentRenderbuffer();

        protected override void Bind(int id) => OpenGLImageBindings.BindRenderbuffer(OpenGlStateCache.Current, id);
    }



    internal int GetActiveTextureUnit()
    {
        if (activeTextureUnit.HasValue) return activeTextureUnit.Value;

        try
        {
            int enumValue = RHI.Backends.OpenGL.OpenGLRHIContext.ReadImageSelector(GetPName.ActiveTexture);
            activeTextureUnit = enumValue - (int)TextureUnit.Texture0;
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
            activeTextureUnit = 0;
        }

        return activeTextureUnit.Value;
    }



    internal int GetBoundTexture(TextureTarget target, int unit)
    {
        EnsureTextureUnitCapacity(unit);

        var dict = textureBindingsByUnit![unit] ??= new Dictionary<TextureTarget, int>();
        if (dict.TryGetValue(target, out int cached))
        {
            return cached;
        }

        int prevActive = GetActiveTextureUnit();
        try
        {
            OpenGLImageBindings.ActiveTexture(this, unit);
            if (TryGetTextureBindingQuery(target, out GetPName pname))
            {
                int id = RHI.Backends.OpenGL.OpenGLRHIContext.ReadImageSelector(pname);
                dict[target] = id;
                return id;
            }
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
        }
        finally
        {
            OpenGLImageBindings.ActiveTexture(this, prevActive);
        }

        dict[target] = 0;
        return 0;
    }









    internal TextureScope BindTextureScope(TextureTarget target, int unit, int textureId)
    {
        int prevActive = GetActiveTextureUnit();
        int prevTex = GetBoundTexture(target, unit);

        OpenGLImageBindings.BindTexture(this, target, unit, textureId);
        return new TextureScope(this, target, unit, prevTex, prevActive);
    }

    public readonly struct TextureScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly RHI.RHICommandContext context;
        private readonly (nint Handle, long Generation) origin;
        private readonly TextureTarget target;
        private readonly int unit;
        private readonly int previousTexture;
        private readonly int previousActiveUnit;

        /// <summary>Captures the original texture selector and active unit and its native context lifetime.</summary>
        internal TextureScope(OpenGlStateCache cache, TextureTarget target, int unit, int previousTexture, int previousActiveUnit)
        {
            this.cache = cache;
            context = RHI.RHICommandContext.Current;
            origin = Integration.RenderContextRegistry.CaptureResourceContext();
            this.target = target;
            this.unit = unit;
            this.previousTexture = previousTexture;
            this.previousActiveUnit = previousActiveUnit;
        }

        /// <summary>Restores the original texture selector and active unit without modifying foreign context knowledge.</summary>
        public void Dispose()
        {
            // Foreign disposal may withdraw only facts still owned by the original namespace.
            if (!context.IsCurrentContext)
            {
                if (cache.OwnsKnowledgeFor(origin)) { cache.ForgetImageBinding(target, unit); cache.ActiveTextureKnowledge = null; }
                return;
            }
            context.RequireContext();
            OpenGLImageBindings.BindTexture(cache, target, unit, previousTexture);
            OpenGLImageBindings.ActiveTexture(cache, previousActiveUnit);
        }
    }

    internal int GetBoundSampler(int unit)
    {
        EnsureTextureUnitCapacity(unit);

        int? cached = samplerBindingByUnit![unit];
        if (cached.HasValue) return cached.Value;

        int prevActive = GetActiveTextureUnit();
        try
        {
            OpenGLImageBindings.ActiveTexture(this, unit);
            int sampler = RHI.Backends.OpenGL.OpenGLRHIContext.ReadImageSelector(GetPName.SamplerBinding);
            samplerBindingByUnit[unit] = sampler;
            return sampler;
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
            samplerBindingByUnit[unit] = 0;
            return 0;
        }
        finally
        {
            OpenGLImageBindings.ActiveTexture(this, prevActive);
        }
    }







    internal SamplerScope BindSamplerScope(int unit, int samplerId)
    {
        int previous = GetBoundSampler(unit);

        OpenGLImageBindings.BindSampler(this, unit, samplerId);
        return new SamplerScope(this, unit, previous);
    }

    /// <summary>Retains an original sampler slot for context-affine restoration.</summary>
    public readonly struct SamplerScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly RHI.RHICommandContext context;
        private readonly (nint Handle, long Generation) origin;
        private readonly int unit;
        private readonly int previous;

        /// <summary>Captures the original sampler slot and its native context lifetime.</summary>
        internal SamplerScope(OpenGlStateCache cache, int unit, int previous)
        {
            this.cache = cache;
            context = RHI.RHICommandContext.Current;
            origin = Integration.RenderContextRegistry.CaptureResourceContext();
            this.unit = unit;
            this.previous = previous;
        }

        /// <summary>Restores the original sampler slot without modifying foreign context knowledge.</summary>
        public void Dispose()
        {
            // Foreign disposal may withdraw only facts still owned by the original namespace.
            if (!context.IsCurrentContext)
            {
                if (cache.OwnsKnowledgeFor(origin)) { cache.SetSamplerKnowledge(unit, null); }
                return;
            }
            context.RequireContext();
            OpenGLImageBindings.BindSampler(cache, unit, previous);
        }
    }

    internal int GetBoundBuffer(BufferTarget target)
    {
        if (target == BufferTarget.ElementArrayBuffer)
        {
            int vaoId = GetCurrentVao();

            if (elementArrayBufferByVao.TryGetValue(vaoId, out int cachedEbo))
            {
                return cachedEbo;
            }

            try
            {
                int value = GL.GetInteger(GetPName.ElementArrayBufferBinding);
                elementArrayBufferByVao[vaoId] = value;
                return value;
            }
            catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
                elementArrayBufferByVao[vaoId] = 0;
                return 0;
            }
        }

        if (bufferBindingByTarget.TryGetValue(target, out int? cached) && cached.HasValue)
        {
            return cached.Value;
        }

        try
        {
            if (TryGetBufferBindingQuery(target, out GetPName pname))
            {
                int value = GL.GetInteger(pname);
                bufferBindingByTarget[target] = value;
                return value;
            }
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
        }

        bufferBindingByTarget[target] = 0;
        return 0;
    }

    internal bool TryGetCachedBoundBuffer(BufferTarget target, out int bufferId)
    {
        if (target == BufferTarget.ElementArrayBuffer)
        {
            if (currentVao.HasValue && elementArrayBufferByVao.TryGetValue(currentVao.Value, out bufferId))
            {
                return true;
            }

            bufferId = 0;
            return false;
        }

        if (bufferBindingByTarget.TryGetValue(target, out int? cached) && cached.HasValue)
        {
            bufferId = cached.Value;
            return true;
        }

        bufferId = 0;
        return false;
    }

    /// <summary>Captures known generic or VAO-owned binding facts before a temporary selection.</summary>
    internal BufferScope BindBufferScope(BufferTarget target, int bufferId)
    {
        int? vao = target == BufferTarget.ElementArrayBuffer ? GetCurrentVao() : null;
        int previous = GetBoundBuffer(target);

        OpenGLBufferBindings.BindBuffer(this, target, bufferId);
        return new BufferScope(this, target, previous, vao);
    }

    /// <summary>Restores the original buffer selector and, for element buffers, its owning VAO association.</summary>
    public readonly struct BufferScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly BufferTarget target;
        private readonly int previous;
        private readonly int? vao;
        private readonly RHI.RHICommandContext context;

        /// <summary>Captures the generic selector or the original VAO-owned element selector.</summary>
        internal BufferScope(OpenGlStateCache cache, BufferTarget target, int previous, int? vao = null)
        {
            this.cache = cache;
            this.target = target;
            this.previous = previous;
            this.vao = vao;
            context = RHI.RHICommandContext.Current;
        }

        /// <summary>Restores an element association on its original VAO without changing a later selection.</summary>
        public void Dispose()
        {
            context.RequireContext();
            if (vao.HasValue && cache.GetCurrentVao() != vao.Value)
            {
                using var selection = cache.BindVertexArrayScope(vao.Value);
                OpenGLBufferBindings.BindBuffer(cache, target, previous);
                return;
            }
            OpenGLBufferBindings.BindBuffer(cache, target, previous);
        }
    }

    private void EnsureTextureUnitCapacity(int unit)
    {
        if (unit < 0) throw new ArgumentOutOfRangeException(nameof(unit));
        int size = unit + 1;

        if (textureBindingsByUnit is null || textureBindingsByUnit.Length < size)
        {
            int newSize = Math.Max(size, textureBindingsByUnit?.Length ?? 0);
            newSize = Math.Max(newSize, 8);
            newSize = Math.Max(newSize, (textureBindingsByUnit?.Length ?? 0) * 2);

            var newArr = new Dictionary<TextureTarget, int>[newSize];
            if (textureBindingsByUnit is not null)
            {
                Array.Copy(textureBindingsByUnit, newArr, textureBindingsByUnit.Length);
            }
            textureBindingsByUnit = newArr;
        }

        if (samplerBindingByUnit is null || samplerBindingByUnit.Length < size)
        {
            int newSize = Math.Max(size, samplerBindingByUnit?.Length ?? 0);
            newSize = Math.Max(newSize, 8);
            newSize = Math.Max(newSize, (samplerBindingByUnit?.Length ?? 0) * 2);

            var newArr = new int?[newSize];
            if (samplerBindingByUnit is not null)
            {
                Array.Copy(samplerBindingByUnit, newArr, samplerBindingByUnit.Length);
            }
            samplerBindingByUnit = newArr;
        }
    }

    private static bool TryGetTextureBindingQuery(TextureTarget target, out GetPName pname)
    {
        pname = target switch
        {
            TextureTarget.Texture1D => GetPName.TextureBinding1D,
            TextureTarget.Texture1DArray => GetPName.TextureBinding1DArray,
            TextureTarget.Texture2D => GetPName.TextureBinding2D,
            TextureTarget.Texture2DArray => GetPName.TextureBinding2DArray,
            TextureTarget.Texture3D => GetPName.TextureBinding3D,
            TextureTarget.TextureBuffer => GetPName.TextureBindingBuffer, // GL_TEXTURE_BINDING_BUFFER
            (TextureTarget)All.TextureRenderbufferNv => (GetPName)All.TextureBindingRenderbufferNv, // GL_TEXTURE_BINDING_RENDERBUFFER_NV
            TextureTarget.TextureRectangle => GetPName.TextureBindingRectangle,
            TextureTarget.TextureCubeMap => GetPName.TextureBindingCubeMap,
            TextureTarget.TextureCubeMapArray => (GetPName)All.TextureBindingCubeMapArray, // GL_TEXTURE_BINDING_CUBE_MAP_ARRAY
            TextureTarget.Texture2DMultisample => GetPName.TextureBinding2DMultisample,
            TextureTarget.Texture2DMultisampleArray => GetPName.TextureBinding2DMultisampleArray,
            _ => default
        };

        return pname != default;
    }

    private static bool TryGetBufferBindingQuery(BufferTarget target, out GetPName pname)
    {
        pname = target switch
        {
            BufferTarget.ArrayBuffer => GetPName.ArrayBufferBinding,
            BufferTarget.CopyReadBuffer => (GetPName)All.CopyReadBuffer, // GL_COPY_READ_BUFFER_BINDING
            BufferTarget.CopyWriteBuffer => (GetPName)All.CopyWriteBuffer, // GL_COPY_WRITE_BUFFER_BINDING
            BufferTarget.DrawIndirectBuffer => GetPName.DrawIndirectBufferBinding,
            BufferTarget.DispatchIndirectBuffer => GetPName.DispatchIndirectBufferBinding,
            BufferTarget.ParameterBuffer => GetPName.ParameterBufferBinding,
            BufferTarget.PixelPackBuffer => GetPName.PixelPackBufferBinding,
            BufferTarget.PixelUnpackBuffer => GetPName.PixelUnpackBufferBinding,
            BufferTarget.UniformBuffer => GetPName.UniformBufferBinding,
            BufferTarget.ShaderStorageBuffer => GetPName.ShaderStorageBufferBinding,
            BufferTarget.TextureBuffer => GetPName.TextureBuffer, // GL_TEXTURE_BUFFER_BINDING
            BufferTarget.AtomicCounterBuffer => (GetPName)All.AtomicCounterBufferBinding, // GL_ATOMIC_COUNTER_BUFFER_BINDING
            BufferTarget.TransformFeedbackBuffer => GetPName.TransformFeedbackBufferBinding,
            _ => default
        };

        return pname != default;
    }
}
