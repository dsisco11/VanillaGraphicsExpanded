using System;
using System.Linq;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Generates contents immediately and derives newly allocated mutable mip facts.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void GenerateMipmaps(GpuTextureRef texture)
    {
        RequireContext();
        var previous = texture.RequireMetadata();
        var owner = texture.Resource!;
        owner.RequireOriginalContext();
        if (previous.Target is TextureTarget.TextureRectangle or TextureTarget.TextureBuffer or TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)
            throw new ArgumentException("This target does not support mip generation.");
        var bases = previous.Levels.Where(image => image.Level == previous.BaseLevel && image.Allocated).ToArray();
        if (bases.Length != (previous.Target == TextureTarget.TextureCubeMap ? 6 : 1)) throw new ArgumentException("Mip generation requires complete base images.");
        var first = bases[0];
        foreach (var image in bases)
            if (image.Width != first.Width || image.Height != first.Height || image.Format != first.Format)
                throw new ArgumentException("Cube base images must have matching representation and extent.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, owner.ResourceId);
        if (previous.ImmutableStorage)
        {
            GL.GenerateTextureMipmap((int)owner.ResourceId);
            RequireContext();
            return;
        }
        int extent = Math.Max(first.Width, previous.Target == TextureTarget.Texture1DArray ? 1 : first.Height);
        if (previous.Target == TextureTarget.Texture3D) extent = Math.Max(extent, first.Depth);
        int last = previous.BaseLevel;
        while ((extent >>= 1) > 0) last++;
        last = Math.Min(last, previous.MaxLevel);
        var result = previous.Levels.Where(image => image.Level <= previous.BaseLevel || image.Level > last).ToImmutableArray().ToBuilder();
        foreach (var image in bases)
            for (int level = previous.BaseLevel + 1; level <= last; level++)
            {
                int shift = level - previous.BaseLevel;
                result.Add(image with { Level = level, Width = Math.Max(1, image.Width >> shift),
                    Height = previous.Target == TextureTarget.Texture1DArray ? image.Height : Math.Max(1, image.Height >> shift),
                    Depth = previous.Target == TextureTarget.Texture3D ? Math.Max(1, image.Depth >> shift) : image.Depth });
            }
        var images = result.ToImmutable();
        bool changed = images.Length != previous.Levels.Length || images.Any(image => !previous.Levels.Contains(image));
        if (!changed)
        {
            GL.GenerateTextureMipmap((int)owner.ResourceId);
            RequireContext();
            return;
        }
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(owner, GpuMetadataChange.Storage);
        GL.GenerateTextureMipmap((int)owner.ResourceId);
        RequireContext();
        operation.Commit(previous with { Levels = images });
    }
    #endregion
}
