using System;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes sampling policy without inventing a canonical sampler-parameter mirror.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuSampler> CreateSampler(in RHISamplerDescription description)
    {
        var request = description;
        RequireContext();
        ValidateSampler(request);
        var owner = new GpuSampler();
        try
        {
                if (!RHIResourceManager.CreateSampler(new(), owner, out _).HasValue) return new(null, "Sampler creation did not complete.");
                ConfigureSampler(owner, request);
                owner.SetDebugName(request.Label);
                return new(owner, null);
            }
            catch { try { owner.Dispose(); } catch { } throw; }
        }
        /// <summary>Applies only assigned fields after validating the complete request before mutation.</summary>
        public void ConfigureSampler(GpuSampler owner, in RHISamplerDescription description)
        {
            var request = description;
            RequireContext();
            owner.RequireOriginalContext();
            RHIResourceManager.RequireWrapperMetadataAccess(owner);
            ValidateSampler(request);
            cache.RequireResourceOperationAvailable(GpuResourceKind.Sampler, owner.ResourceId);
            int id = owner.SamplerId;
            var guard = cache.EnterResourceOperation(new(GpuResourceKind.Sampler, id));
            try
            {
            if (request.Has(0)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureMinFilter, MinFilter(request.MinFilter)); CheckSamplerStep(owner, id, guard); }
            if (request.Has(1)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureMagFilter, MinFilter(request.MagFilter)); CheckSamplerStep(owner, id, guard); }
            if (request.Has(2)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureWrapS, Address(request.AddressS)); CheckSamplerStep(owner, id, guard); }
            if (request.Has(3)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureWrapT, Address(request.AddressT)); CheckSamplerStep(owner, id, guard); }
            if (request.Has(4)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureWrapR, Address(request.AddressR)); CheckSamplerStep(owner, id, guard); }
            if (request.Has(5)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureMinLod, request.MinLod); CheckSamplerStep(owner, id, guard); }
            if (request.Has(6)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureMaxLod, request.MaxLod); CheckSamplerStep(owner, id, guard); }
            if (request.Has(7)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureLodBias, request.LodBias); CheckSamplerStep(owner, id, guard); }
            if (request.Has(8)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureCompareMode, request.Compare ? (int)Native.TextureCompareMode.CompareRefToTexture : (int)Native.TextureCompareMode.None); CheckSamplerStep(owner, id, guard); }
            if (request.Has(9)) { OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureCompareFunc, Comparison(request.Comparison)); CheckSamplerStep(owner, id, guard); }
            if (request.Has(10))
            {
                var value = request.BorderColor;
                unsafe { float* values = stackalloc float[4] { value.X, value.Y, value.Z, value.W }; OpenGLDriverCalls.SamplerParameter(id, Native.SamplerParameterName.TextureBorderColor, values); CheckSamplerStep(owner, id, guard); }
            }
            if (request.Has(11)) { OpenGLDriverCalls.SamplerParameter(id, (Native.SamplerParameterName)Native.All.TextureMaxAnisotropyExt, request.Anisotropy); CheckSamplerStep(owner, id, guard); }
        }
        finally { cache.ExitResourceOperation(guard); }
    }
    #endregion

    #region Private
    /// <summary>Stops a setter sequence after original identity retirement or a context change.</summary>
    private void CheckSamplerStep(GpuSampler owner, int id, OpenGlStateCache.ResourceOperationGuard guard)
    {
        RequireContext();
        if (guard.Cancelled || !owner.IsValid || owner.SamplerId != id)
            throw new InvalidOperationException("The original sampler retired during configuration.");
    }

    /// <summary>Validates all legal policy fields before any setter, including optional extension admission.</summary>
    private void ValidateSampler(in RHISamplerDescription request)
    {
        if (request.Has(0)) _ = MinFilter(request.MinFilter);
        if (request.Has(1) && request.MagFilter is not (SamplerFilter.Nearest or SamplerFilter.Linear)) throw new ArgumentException("Magnification has no mip filter.");
        if (request.Has(2)) _ = Address(request.AddressS);
        if (request.Has(3)) _ = Address(request.AddressT);
        if (request.Has(4)) _ = Address(request.AddressR);
        if (request.Has(9)) _ = Comparison(request.Comparison);
        if (request.Has(5) && !float.IsFinite(request.MinLod) || request.Has(6) && !float.IsFinite(request.MaxLod) || request.Has(7) && !float.IsFinite(request.LodBias))
            throw new ArgumentException("LOD values must be finite.");
        if (request.Has(11))
        {
            if (!float.IsFinite(request.Anisotropy) || request.Anisotropy < 1) throw new ArgumentOutOfRangeException(nameof(request));
            if (!Capabilities.TextureAnisotropy) throw new NotSupportedException("Anisotropic sampling is unavailable.");
        }
    }
    /// <summary>Maps minification policy explicitly.</summary>
    private static int MinFilter(SamplerFilter value) => (int)(value switch
    {
        SamplerFilter.Nearest => Native.TextureMinFilter.Nearest, SamplerFilter.Linear => Native.TextureMinFilter.Linear,
        SamplerFilter.NearestMipNearest => Native.TextureMinFilter.NearestMipmapNearest, SamplerFilter.LinearMipNearest => Native.TextureMinFilter.LinearMipmapNearest,
        SamplerFilter.NearestMipLinear => Native.TextureMinFilter.NearestMipmapLinear, SamplerFilter.LinearMipLinear => Native.TextureMinFilter.LinearMipmapLinear,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });
    /// <summary>Maps addressing policy explicitly.</summary>
    private static int Address(SamplerAddressMode value) => (int)(value switch
    {
        SamplerAddressMode.Repeat => Native.TextureWrapMode.Repeat, SamplerAddressMode.MirroredRepeat => Native.TextureWrapMode.MirroredRepeat,
        SamplerAddressMode.ClampToEdge => Native.TextureWrapMode.ClampToEdge, SamplerAddressMode.ClampToBorder => Native.TextureWrapMode.ClampToBorder,
        SamplerAddressMode.MirrorClampToEdge => (Native.TextureWrapMode)Native.All.MirrorClampToEdge, _ => throw new ArgumentOutOfRangeException(nameof(value))
    });
    /// <summary>Maps comparison policy explicitly.</summary>
    private static int Comparison(ComparisonFunction value) => (int)(value switch
    {
        ComparisonFunction.Never => Native.DepthFunction.Never, ComparisonFunction.Less => Native.DepthFunction.Less, ComparisonFunction.Equal => Native.DepthFunction.Equal,
        ComparisonFunction.LessOrEqual => Native.DepthFunction.Lequal, ComparisonFunction.Greater => Native.DepthFunction.Greater, ComparisonFunction.NotEqual => Native.DepthFunction.Notequal,
        ComparisonFunction.GreaterOrEqual => Native.DepthFunction.Gequal, ComparisonFunction.Always => Native.DepthFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    });
    #endregion
}
