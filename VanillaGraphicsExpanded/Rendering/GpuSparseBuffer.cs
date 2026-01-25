using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Extension-gated helpers for sparse buffer workflows (ARB_sparse_buffer).
/// </summary>
internal static class GpuSparseBuffer
{
    /// <summary>
    /// Returns <c>true</c> when the current OpenGL context reports ARB sparse buffer support.
    /// </summary>
    public static bool IsSupported => GlExtensions.Supports("GL_ARB_sparse_buffer");

    /// <summary>
    /// Commits or decommits a range of pages for a sparse buffer via <c>glNamedBufferPageCommitmentARB</c>.
    /// </summary>
    public static void NamedPageCommitment(int bufferId, int offsetBytes, int sizeBytes, bool commit)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException("GL_ARB_sparse_buffer is not supported by the current context.");
        }

        if (bufferId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferId), bufferId, "Buffer id must be > 0.");
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "Size must be >= 0.");
        }

        GL.Arb.NamedBufferPageCommitment(bufferId, (IntPtr)offsetBytes, sizeBytes, commit);
    }

    /// <summary>
    /// Attempts to commit or decommit pages for a sparse buffer and returns <c>false</c> on failure.
    /// </summary>
    public static bool TryNamedPageCommitment(int bufferId, int offsetBytes, int sizeBytes, bool commit)
    {
        if (!IsSupported || bufferId <= 0 || offsetBytes < 0 || sizeBytes < 0)
        {
            return false;
        }

        try
        {
            NamedPageCommitment(bufferId, offsetBytes, sizeBytes, commit);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

