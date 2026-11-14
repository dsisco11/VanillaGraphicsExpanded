using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Uniforms;

namespace VanillaGraphicsExpanded.Tests.Support.Rendering.Uniforms;

/// <summary>Provides arbitrary caller-sized packed input for test-owned shader programs.</summary>
/// <param name="size">Immutable byte size supplied by the test fixture.</param>
/// <param name="usage">Publication lifetime for this test-owned input block.</param>
internal sealed class PackedUniformBuffer(int size,
    UniformBufferUsage usage = UniformBufferUsage.SingleFrame) : CpuUniformBuffer(usage)
{
    #region Public API
    /// <summary>Declares the test input's fixed storage size before base allocation.</summary>
    public override int SizeBytes { get; } = size;

    /// <summary>Publishes a complete arbitrary test payload through the protected packed-buffer edit contract.</summary>
    internal void SetPayload(ReadOnlySpan<byte> bytes) => SetBytes(bytes);
    #endregion
}
