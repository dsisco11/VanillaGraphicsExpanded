using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies explicit geometry/readiness for existing surface-lighting fixtures with packed lighting inputs.</summary>
internal sealed class SharedSurfaceInputFixture : IDisposable
{
    private readonly Texture3D geometry, readiness;
    private readonly Texture2D faces;
    private readonly GpuUniformBuffer parameters = GpuUniformBuffer.Create(debugName: "Tests.SharedSurface.Parameters");

    #region Fixture binding
    /// <summary>Declares every fixture voxel loaded, classifies its authored material words, and preserves their lighting.</summary>
    public SharedSurfaceInputFixture(int program, Texture3D legacy, Texture2D palette)
    {
        GlStateCache.Current.InvalidateAll();
        int size = legacy.Width;
        uint[] words = new uint[size * size * size];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, legacy.TextureId))
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, words);
        for (int i = 0; i < words.Length; i++)
        {
            uint material = words[i] >> 18;
            words[i] = material != 0 ? 2u | material << 2 : 1u;
        }
        geometry = Texture3D.Create(size, size, size, PixelInternalFormat.R32ui);
        geometry.UploadDataImmediate(words, 0, 0, 0, size, size, size);
        int cellSize = Math.Min(16, size), slots = size / cellSize;
        readiness = Texture3D.Create(slots, slots, slots, PixelInternalFormat.R8ui);
        readiness.UploadDataImmediate(Enumerable.Repeat((byte)1, slots * slots * slots).ToArray(), 0, 0, 0, slots, slots, slots);
        uint[] entries = new uint[palette.Width * palette.Height * 4];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, palette.TextureId))
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, entries);
        for (int i = 0; i < entries.Length; i += 4)
            entries[i + 3] = (entries[i] | entries[i + 1] | entries[i + 2]) != 0 ? 1u : 0u;
        faces = Texture2D.Create(palette.Width, palette.Height, PixelInternalFormat.Rgba32ui);
        faces.UploadDataImmediate(entries);
        var mapping = new LumOnNearFieldParamsUbo(); mapping.Set(default, size, cellSize: cellSize);
        parameters.UploadOrResize(mapping.Bytes, growExponentially: false); parameters.BindBase(LumOnNearFieldParamsUbo.Binding);
        UniformBlockBindingUtil.EnsureBlockBound(program, LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding);
        geometry.Bind(8); readiness.Bind(9); legacy.Bind(10); faces.Bind(11);
        for (int i = 8; i <= 11; i++) GpuSamplers.NearestClamp.Bind(i);
    }

    /// <summary>Releases only companion fixture resources, preserving caller-owned light and material inputs.</summary>
    public void Dispose() { geometry.Dispose(); readiness.Dispose(); faces.Dispose(); parameters.Dispose(); }
    #endregion
}
