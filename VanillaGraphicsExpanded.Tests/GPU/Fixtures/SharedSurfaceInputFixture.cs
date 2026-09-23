using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes authored legacy voxels and face identities through the production geometry backend.</summary>
internal sealed class SharedSurfaceInputFixture : IDisposable
{
    public TraceGeometryGpuScene Scene { get; }

    #region Controlled scene publication
    /// <summary>Reads the fixture inputs and publishes complete cells without duplicating shader binding slots.</summary>
    public SharedSurfaceInputFixture(Texture3D legacy, Texture2D palette)
    {
        int size = legacy.Width, resolution = (size + 15) / 16 * 16;
        var words = new uint[size * size * size];
        // Integer texture readback is an observation seam; the ordinary shader inputs below
        // are published and subsequently bound by their actual production owners.
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, legacy.TextureId))
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, words);
        var entries = new uint[palette.Width * palette.Height * 4];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, palette.TextureId))
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, entries);
        var faces = new uint[16384 * 4];
        entries.CopyTo(faces, 0);
        for (int i = 0; i < faces.Length; i += 4)
            faces[i + 3] = (faces[i] | faces[i + 1] | faces[i + 2]) != 0 ? 1u : 0u;
        var tables = new TraceGeometryTables(1, faces, new byte[16384 * 48], new uint[65536 * 4], new float[256]);
        // Publish whole production cells while preserving the authored logical tracing extent.
        Scene = new(resolution);
        var window = new PartitionBounds(new(0, 0, 0), new(resolution, resolution, resolution));
        var domain = new PartitionBounds(new(0, 0, 0), new(size, size, size));
        Scene.SetWindow(new(domain, domain, window, resolution, int.MaxValue));
        Scene.UploadTableRange(tables, 0, (int)TraceGeometryTables.MaximumUploadBytes);
        // Native upload order is X/Y/Z; each cell keeps geometry and packed lighting coherent.
        for (int cz = 0; cz < resolution / 16; cz++)
        for (int cy = 0; cy < resolution / 16; cy++)
        for (int cx = 0; cx < resolution / 16; cx++)
        {
            var geometry = new uint[4096];
            var lighting = new uint[4096];
            for (int z = 0; z < 16; z++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                int wx = cx * 16 + x, wy = cy * 16 + y, wz = cz * 16 + z;
                int index = (z * 16 + y) * 16 + x;
                uint word = wx < size && wy < size && wz < size ? words[(wz * size + wy) * size + wx] : 0;
                lighting[index] = word;
                uint material = word >> 18;
                geometry[index] = material != 0 ? 2u | material << 2 : 1u;
            }
            var request = new PartitionRequest(1, new(1, "component", new(cx, cy, cz)), 1, 1, 1, default);
            Assert.True(Scene.Publish(request, new(geometry, lighting, new byte[16384], false), tables));
        }
    }

    /// <summary>Releases the production scene while preserving caller-owned authored inputs.</summary>
    public void Dispose() => Scene.Dispose();
    #endregion
}
