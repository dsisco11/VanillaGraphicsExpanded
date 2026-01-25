using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Extension-gated helpers for sparse texture workflows (ARB_sparse_texture).
/// </summary>
internal static class GpuSparseTexture
{
    /// <summary>
    /// Returns <c>true</c> when the current OpenGL context reports ARB sparse texture support.
    /// </summary>
    public static bool IsSupported => GlExtensions.Supports("GL_ARB_sparse_texture");

    /// <summary>
    /// Commits or decommits a region of pages for a sparse texture via <c>glTexPageCommitmentARB</c>.
    /// </summary>
    public static void TexPageCommitment(
        TextureTarget target,
        int level,
        int xOffset,
        int yOffset,
        int zOffset,
        int width,
        int height,
        int depth,
        bool commit)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException("GL_ARB_sparse_texture is not supported by the current context.");
        }

        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be >= 0.");
        }

        if (width < 0 || height < 0 || depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Sizes must be >= 0.");
        }

        GL.Arb.TexPageCommitment(
            (ArbSparseTexture)(int)target,
            level,
            xOffset,
            yOffset,
            zOffset,
            width,
            height,
            depth,
            commit);
    }

    /// <summary>
    /// Attempts to commit/decommit pages for a sparse texture and returns <c>false</c> on failure.
    /// </summary>
    public static bool TryTexPageCommitment(
        TextureTarget target,
        int level,
        int xOffset,
        int yOffset,
        int zOffset,
        int width,
        int height,
        int depth,
        bool commit)
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            TexPageCommitment(target, level, xOffset, yOffset, zOffset, width, height, depth, commit);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

