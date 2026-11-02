using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Restores only selectors touched by fixture compute, leaving SSBO and pixel-pack state untouched.</summary>
internal sealed class GpuAssertionState : IDisposable
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private readonly int program = GL.GetInteger(GetPName.CurrentProgram);
    private readonly int active = GL.GetInteger(GetPName.ActiveTexture);
    private readonly int uniformGeneric = GL.GetInteger(GetPName.UniformBufferBinding);
    private readonly int uniformName;
    private readonly long uniformStart, uniformSize;
    private readonly (int Array, int Sampler)[] textures = [];
    private readonly (int Name, int Level, int Layered, int Layer, int Access, int Format)[] images = [];

    private readonly int[] samplerUnits, imageUnits;

    #region Public API
    /// <summary>Restores exact selectors after successful dispatch or an exception without changing foreign-context state.</summary>
    public void Dispose()
    {
        if (!RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("Cannot restore checker state in a foreign context.");
        GL.UseProgram(program);
        if (uniformName != 0 && uniformSize > 0)
            GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 28, uniformName, (nint)uniformStart, (nint)uniformSize);
        else GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 28, uniformName);
        GL.BindBuffer(BufferTarget.UniformBuffer, uniformGeneric);
        for (int unit = 0; unit < textures.Length; unit++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + samplerUnits[unit]);
            GL.BindTexture(TextureTarget.Texture2DArray, textures[unit].Array);
            GL.BindSampler(samplerUnits[unit], textures[unit].Sampler);
        }
        GL.ActiveTexture((TextureUnit)active);
        for (int unit = 0; unit < images.Length; unit++)
        {
            var image = images[unit];
            GL.BindImageTexture(imageUnits[unit], image.Name, image.Level, image.Layered != 0, image.Layer,
                image.Access == 0 ? TextureAccess.ReadOnly : (TextureAccess)image.Access,
                image.Format == 0 ? SizedInternalFormat.R32ui : (SizedInternalFormat)image.Format);
        }
    }
    #endregion

    #region Internal API
    /// <summary>Captures the exact existing image subresources, array bindings and sampler objects before mutation.</summary>
    internal GpuAssertionState(IEnumerable<int> samplerUnits, IEnumerable<int> imageUnits)
    {
        this.samplerUnits = samplerUnits.Distinct().ToArray();
        this.imageUnits = imageUnits.Distinct().ToArray();
        textures = new (int, int)[this.samplerUnits.Length];
        images = new (int, int, int, int, int, int)[this.imageUnits.Length];
        // Querying texture units changes the active selector temporarily, including on partial construction failure.
        try
        {
            // Interception retains base versus range semantics, which native getters cannot distinguish.
            if (OpenGlStateCache.Current.TryGetCachedIndexedBuffer(OpenTK.Graphics.OpenGL.BufferRangeTarget.UniformBuffer,
                28, out uniformName, out nint start, out nint size))
            {
                uniformStart = start; uniformSize = size;
            }
            else
            {
                GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, 28, out uniformName);
                GL.GetInteger64((GetIndexedPName)All.UniformBufferStart, 28, out uniformStart);
                GL.GetInteger64((GetIndexedPName)All.UniformBufferSize, 28, out uniformSize);
            }
            for (int unit = 0; unit < textures.Length; unit++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + this.samplerUnits[unit]);
                GL.GetInteger((GetIndexedPName)All.SamplerBinding, this.samplerUnits[unit], out int sampler);
                textures[unit] = (GL.GetInteger(GetPName.TextureBinding2DArray), sampler);
            }
            for (int unit = 0; unit < images.Length; unit++)
                images[unit] = (Query((GetIndexedPName)All.ImageBindingName, this.imageUnits[unit]), Query((GetIndexedPName)All.ImageBindingLevel, this.imageUnits[unit]),
                    Query((GetIndexedPName)All.ImageBindingLayered, this.imageUnits[unit]), Query((GetIndexedPName)All.ImageBindingLayer, this.imageUnits[unit]),
                    Query((GetIndexedPName)All.ImageBindingAccess, this.imageUnits[unit]), Query((GetIndexedPName)All.ImageBindingFormat, this.imageUnits[unit]));
        }
        finally { GL.ActiveTexture((TextureUnit)active); }
    }
    #endregion



    #region Private
    /// <summary>Reads one exact indexed image selector.</summary>
    private static int Query(GetIndexedPName name, int unit) { GL.GetInteger(name, unit, out int value); return value; }
    #endregion
}
