using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed partial class GlStateCache
{
    private int? currentProgram;
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

    private void InvalidateBindings()
    {
        currentProgram = null;
        currentVao = null;
        currentProgramPipeline = null;
        currentFramebuffer = null;
        currentReadFramebuffer = null;
        currentDrawFramebuffer = null;
        currentRenderbuffer = null;
        currentTransformFeedback = null;

        activeTextureUnit = null;
        textureBindingsByUnit = null;
        samplerBindingByUnit = null;
        bufferBindingByTarget.Clear();
    }

    public int GetCurrentProgram()
    {
        if (currentProgram.HasValue) return currentProgram.Value;

        try { currentProgram = GL.GetInteger(GetPName.CurrentProgram); }
        catch { currentProgram = 0; }

        return currentProgram.Value;
    }

    public void UseProgram(int programId)
    {
        try
        {
            GL.UseProgram(programId);
            currentProgram = programId;
        }
        catch
        {
        }
    }

    public ProgramScope UseProgramScope(int programId)
    {
        int previous = 0;
        try { previous = GL.GetInteger(GetPName.CurrentProgram); } catch { previous = 0; }
        UseProgram(programId);
        return new ProgramScope(this, previous);
    }

    public readonly struct ProgramScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly int previous;

        public ProgramScope(GlStateCache cache, int previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.UseProgram(previous);
        }
    }

    public int GetCurrentVao()
    {
        if (currentVao.HasValue) return currentVao.Value;

        try
        {
            GL.GetInteger(GetPName.VertexArrayBinding, out int vao);
            currentVao = vao;
        }
        catch
        {
            currentVao = 0;
        }

        return currentVao.Value;
    }

    public void BindVertexArray(int vaoId)
    {
        try
        {
            GL.BindVertexArray(vaoId);
            currentVao = vaoId;
        }
        catch
        {
        }
    }

    public VaoScope BindVertexArrayScope(int vaoId)
    {
        int previous = 0;
        try { GL.GetInteger(GetPName.VertexArrayBinding, out previous); } catch { previous = 0; }
        BindVertexArray(vaoId);
        return new VaoScope(this, previous);
    }

    public readonly struct VaoScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly int previous;

        public VaoScope(GlStateCache cache, int previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindVertexArray(previous);
        }
    }

    public int GetCurrentFramebuffer(FramebufferTarget target)
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

            SetFramebufferCache(target, value);
        }
        catch
        {
            SetFramebufferCache(target, 0);
        }

        return GetCurrentFramebuffer(target);
    }

    public void BindFramebuffer(FramebufferTarget target, int fboId)
    {
        try
        {
            GL.BindFramebuffer(target, fboId);
            SetFramebufferCache(target, fboId);
        }
        catch
        {
        }
    }

    public FramebufferScope BindFramebufferScope(FramebufferTarget target, int fboId)
    {
        int previous = 0;
        try
        {
            previous = target switch
            {
                FramebufferTarget.ReadFramebuffer => GL.GetInteger(GetPName.ReadFramebufferBinding),
                FramebufferTarget.DrawFramebuffer => GL.GetInteger(GetPName.DrawFramebufferBinding),
                _ => GL.GetInteger(GetPName.FramebufferBinding)
            };
        }
        catch
        {
            previous = 0;
        }

        BindFramebuffer(target, fboId);
        return new FramebufferScope(this, target, previous);
    }

    public readonly struct FramebufferScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly FramebufferTarget target;
        private readonly int previous;

        public FramebufferScope(GlStateCache cache, FramebufferTarget target, int previous)
        {
            this.cache = cache;
            this.target = target;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindFramebuffer(target, previous);
        }
    }

    private void SetFramebufferCache(FramebufferTarget target, int value)
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
        }
    }

    public int GetCurrentProgramPipeline()
    {
        if (currentProgramPipeline.HasValue) return currentProgramPipeline.Value;

        try { currentProgramPipeline = GL.GetInteger(GetPName.ProgramPipelineBinding); }
        catch { currentProgramPipeline = 0; }

        return currentProgramPipeline.Value;
    }

    public void BindProgramPipeline(int pipelineId)
    {
        try
        {
            GL.BindProgramPipeline(pipelineId);
            currentProgramPipeline = pipelineId;
        }
        catch
        {
        }
    }

    public ProgramPipelineScope BindProgramPipelineScope(int pipelineId)
    {
        int previous = 0;
        try { previous = GL.GetInteger(GetPName.ProgramPipelineBinding); } catch { previous = 0; }
        BindProgramPipeline(pipelineId);
        return new ProgramPipelineScope(this, previous);
    }

    public readonly struct ProgramPipelineScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly int previous;

        public ProgramPipelineScope(GlStateCache cache, int previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindProgramPipeline(previous);
        }
    }

    public int GetCurrentRenderbuffer()
    {
        if (currentRenderbuffer.HasValue) return currentRenderbuffer.Value;

        try { currentRenderbuffer = GL.GetInteger(GetPName.RenderbufferBinding); }
        catch { currentRenderbuffer = 0; }

        return currentRenderbuffer.Value;
    }

    public void BindRenderbuffer(int renderbufferId)
    {
        try
        {
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbufferId);
            currentRenderbuffer = renderbufferId;
        }
        catch
        {
        }
    }

    public RenderbufferScope BindRenderbufferScope(int renderbufferId)
    {
        int previous = 0;
        try { previous = GL.GetInteger(GetPName.RenderbufferBinding); } catch { previous = 0; }
        BindRenderbuffer(renderbufferId);
        return new RenderbufferScope(this, previous);
    }

    public readonly struct RenderbufferScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly int previous;

        public RenderbufferScope(GlStateCache cache, int previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindRenderbuffer(previous);
        }
    }

    public int GetCurrentTransformFeedback()
    {
        if (currentTransformFeedback.HasValue) return currentTransformFeedback.Value;

        try { currentTransformFeedback = GL.GetInteger(GetPName.TransformFeedbackBinding); }
        catch { currentTransformFeedback = 0; }

        return currentTransformFeedback.Value;
    }

    public void BindTransformFeedback(int transformFeedbackId)
    {
        try
        {
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, transformFeedbackId);
            currentTransformFeedback = transformFeedbackId;
        }
        catch
        {
        }
    }

    public TransformFeedbackScope BindTransformFeedbackScope(int transformFeedbackId)
    {
        int previous = 0;
        try { previous = GL.GetInteger(GetPName.TransformFeedbackBinding); } catch { previous = 0; }
        BindTransformFeedback(transformFeedbackId);
        return new TransformFeedbackScope(this, previous);
    }

    public readonly struct TransformFeedbackScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly int previous;

        public TransformFeedbackScope(GlStateCache cache, int previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindTransformFeedback(previous);
        }
    }

    public int GetActiveTextureUnit()
    {
        if (activeTextureUnit.HasValue) return activeTextureUnit.Value;

        try
        {
            int enumValue = GL.GetInteger(GetPName.ActiveTexture);
            activeTextureUnit = enumValue - (int)TextureUnit.Texture0;
        }
        catch
        {
            activeTextureUnit = 0;
        }

        return activeTextureUnit.Value;
    }

    public void ActiveTexture(int unit)
    {
        if (unit < 0) throw new ArgumentOutOfRangeException(nameof(unit));

        try
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            activeTextureUnit = unit;
        }
        catch
        {
        }
    }

    public int GetBoundTexture(TextureTarget target, int unit)
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
            ActiveTexture(unit);
            if (TryGetTextureBindingQuery(target, out GetPName pname))
            {
                int id = GL.GetInteger(pname);
                dict[target] = id;
                return id;
            }
        }
        catch
        {
        }
        finally
        {
            ActiveTexture(prevActive);
        }

        dict[target] = 0;
        return 0;
    }

    public void BindTexture(TextureTarget target, int unit, int textureId)
    {
        EnsureTextureUnitCapacity(unit);

        var dict = textureBindingsByUnit![unit] ??= new Dictionary<TextureTarget, int>();
        try
        {
            ActiveTexture(unit);
            GL.BindTexture(target, textureId);
            dict[target] = textureId;
        }
        catch
        {
        }
    }

    public TextureScope BindTextureScope(TextureTarget target, int unit, int textureId)
    {
        int prevActiveEnum = 0;
        int prevActive = 0;
        try
        {
            prevActiveEnum = GL.GetInteger(GetPName.ActiveTexture);
            prevActive = prevActiveEnum - (int)TextureUnit.Texture0;
        }
        catch
        {
            prevActive = 0;
        }

        int prevTex = 0;
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            if (TryGetTextureBindingQuery(target, out GetPName pname))
            {
                prevTex = GL.GetInteger(pname);
            }
        }
        catch
        {
            prevTex = 0;
        }
        finally
        {
            try { GL.ActiveTexture((TextureUnit)prevActiveEnum); } catch { }
        }

        BindTexture(target, unit, textureId);
        return new TextureScope(this, target, unit, prevTex, prevActive);
    }

    public readonly struct TextureScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly TextureTarget target;
        private readonly int unit;
        private readonly int previousTexture;
        private readonly int previousActiveUnit;

        public TextureScope(GlStateCache cache, TextureTarget target, int unit, int previousTexture, int previousActiveUnit)
        {
            this.cache = cache;
            this.target = target;
            this.unit = unit;
            this.previousTexture = previousTexture;
            this.previousActiveUnit = previousActiveUnit;
        }

        public void Dispose()
        {
            cache.BindTexture(target, unit, previousTexture);
            cache.ActiveTexture(previousActiveUnit);
        }
    }

    public int GetBoundSampler(int unit)
    {
        EnsureTextureUnitCapacity(unit);

        int? cached = samplerBindingByUnit![unit];
        if (cached.HasValue) return cached.Value;

        int prevActive = GetActiveTextureUnit();
        try
        {
            ActiveTexture(unit);
            int sampler = GL.GetInteger(GetPName.SamplerBinding);
            samplerBindingByUnit[unit] = sampler;
            return sampler;
        }
        catch
        {
            samplerBindingByUnit[unit] = 0;
            return 0;
        }
        finally
        {
            ActiveTexture(prevActive);
        }
    }

    public void BindSampler(int unit, int samplerId)
    {
        EnsureTextureUnitCapacity(unit);

        try
        {
            GL.BindSampler(unit, samplerId);
            samplerBindingByUnit![unit] = samplerId;
        }
        catch
        {
        }
    }

    public SamplerScope BindSamplerScope(int unit, int samplerId)
    {
        int previous = 0;
        int prevActiveEnum = 0;
        try
        {
            prevActiveEnum = GL.GetInteger(GetPName.ActiveTexture);
        }
        catch
        {
        }

        try
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            previous = GL.GetInteger(GetPName.SamplerBinding);
        }
        catch
        {
            previous = 0;
        }
        finally
        {
            try { GL.ActiveTexture((TextureUnit)prevActiveEnum); } catch { }
        }

        BindSampler(unit, samplerId);
        return new SamplerScope(this, unit, previous);
    }

    public readonly struct SamplerScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly int unit;
        private readonly int previous;

        public SamplerScope(GlStateCache cache, int unit, int previous)
        {
            this.cache = cache;
            this.unit = unit;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindSampler(unit, previous);
        }
    }

    public int GetBoundBuffer(BufferTarget target)
    {
        if (target == BufferTarget.ElementArrayBuffer)
        {
            return 0;
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
        catch
        {
        }

        bufferBindingByTarget[target] = 0;
        return 0;
    }

    public void BindBuffer(BufferTarget target, int bufferId)
    {
        if (target == BufferTarget.ElementArrayBuffer)
        {
            GL.BindBuffer(target, bufferId);
            return;
        }

        try
        {
            GL.BindBuffer(target, bufferId);
            bufferBindingByTarget[target] = bufferId;
        }
        catch
        {
        }
    }

    public BufferScope BindBufferScope(BufferTarget target, int bufferId)
    {
        int previous = 0;
        try
        {
            if (TryGetBufferBindingQuery(target, out GetPName pname))
            {
                previous = GL.GetInteger(pname);
            }
        }
        catch
        {
            previous = 0;
        }

        BindBuffer(target, bufferId);
        return new BufferScope(this, target, previous);
    }

    public readonly struct BufferScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly BufferTarget target;
        private readonly int previous;

        public BufferScope(GlStateCache cache, BufferTarget target, int previous)
        {
            this.cache = cache;
            this.target = target;
            this.previous = previous;
        }

        public void Dispose()
        {
            cache.BindBuffer(target, previous);
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
            TextureTarget.TextureRectangle => GetPName.TextureBindingRectangle,
            TextureTarget.TextureCubeMap => GetPName.TextureBindingCubeMap,
            // Not present in some OpenTK builds; use raw GL enum value.
            TextureTarget.TextureCubeMapArray => (GetPName)0x900A, // GL_TEXTURE_BINDING_CUBE_MAP_ARRAY
            _ => default
        };

        return pname != default;
    }

    private static bool TryGetBufferBindingQuery(BufferTarget target, out GetPName pname)
    {
        pname = target switch
        {
            BufferTarget.ArrayBuffer => GetPName.ArrayBufferBinding,
            BufferTarget.CopyReadBuffer => (GetPName)0x8F36, // GL_COPY_READ_BUFFER_BINDING
            BufferTarget.CopyWriteBuffer => (GetPName)0x8F37, // GL_COPY_WRITE_BUFFER_BINDING
            BufferTarget.DrawIndirectBuffer => GetPName.DrawIndirectBufferBinding,
            BufferTarget.DispatchIndirectBuffer => GetPName.DispatchIndirectBufferBinding,
            BufferTarget.PixelPackBuffer => GetPName.PixelPackBufferBinding,
            BufferTarget.PixelUnpackBuffer => GetPName.PixelUnpackBufferBinding,
            BufferTarget.UniformBuffer => GetPName.UniformBufferBinding,
            BufferTarget.ShaderStorageBuffer => GetPName.ShaderStorageBufferBinding,
            BufferTarget.TextureBuffer => GetPName.TextureBuffer,
            BufferTarget.AtomicCounterBuffer => (GetPName)0x92C2, // GL_ATOMIC_COUNTER_BUFFER_BINDING
            BufferTarget.TransformFeedbackBuffer => GetPName.TransformFeedbackBufferBinding,
            _ => default
        };

        return pname != default;
    }
}
