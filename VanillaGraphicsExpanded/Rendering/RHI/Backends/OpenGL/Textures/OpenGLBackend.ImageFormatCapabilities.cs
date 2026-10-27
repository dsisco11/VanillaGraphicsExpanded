using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes demand-driven format discovery for the existing shared capability cache.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <summary>Queries supported representation and sample choices once per cached target/format selection.</summary>
    public InternalFormatCapabilities ReadFormatCapabilities(ImageTarget target, SizedInternalFormat format)
    {
        RequireContext();
        // This is explicit capability discovery, retaining its targeted cold-query error policy.
        GlDebug.ThrowIfErrors("Before internal format capability query");
        int supported = ReadFormatField(target, format, InternalFormatParameter.InternalformatSupported);
        GlDebug.ThrowIfErrors("Internal format support");
        int renderable = 0, imageLoad = 0, imageStore = 0, filter = 0;
        int[] samples = [];
        if (supported != 0)
        {
            if (target is ImageTarget.Texture2D or ImageTarget.Texture3D)
            {
                imageLoad = ReadFormatField(target, format, InternalFormatParameter.ShaderImageLoad);
                imageStore = ReadFormatField(target, format, InternalFormatParameter.ShaderImageStore);
                filter = ReadFormatField(target, format, InternalFormatParameter.Filter);
            }
            renderable = ReadFormatField(target, format, InternalFormatParameter.FramebufferRenderable);
            if (target is ImageTarget.Renderbuffer or ImageTarget.Texture2DMultisample or ImageTarget.Texture2DMultisampleArray)
            {
                int count = ReadFormatField(target, format, InternalFormatParameter.NumSampleCounts);
                GlDebug.ThrowIfErrors("Internal format sample count");
                samples = new int[count];
                if (count > 0) GL.GetInternalformat((Native.ImageTarget)target, (Native.SizedInternalFormat)format, Native.InternalFormatParameter.Samples, count, samples);
                RequireContext();
            }
        }
        GlDebug.ThrowIfErrors("Internal format capabilities");
        return new() { Supported = supported != 0, ImageLoadSupport = imageLoad, ImageStoreSupport = imageStore,
            FilterSupport = filter, FramebufferRenderableSupport = renderable, SampleCounts = [.. samples] };
    }
    #endregion

    #region Private
    /// <summary>Retains original-context affinity between every native capability query.</summary>
    private int ReadFormatField(ImageTarget target, SizedInternalFormat format, InternalFormatParameter parameter)
    {
        GL.GetInternalformat((Native.ImageTarget)target, (Native.SizedInternalFormat)format, (Native.InternalFormatParameter)parameter, 1, out int value);
        RequireContext();
        return value;
    }
    #endregion
}
