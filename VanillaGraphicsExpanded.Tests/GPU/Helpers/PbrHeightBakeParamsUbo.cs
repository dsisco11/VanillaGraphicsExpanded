using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class PbrHeightBakeParamsUbo
{
    private const string BlockName = "VgePbrHeightBakeParamsUBO";

    // std140 size of VgePbrHeightBakeParamsUBO through uWeightsPacked[17].
    public const int ParamsSizeBytes = 528;

    // Offsets (bytes) for the ivec4 fields in the UBO.
    private const int OffsetUFineSize = 0;
    private const int OffsetUCoarseSize = 16;
    private const int OffsetUSize = 32;

    public static void EnsureHeightBakeBlockBound(int programId)
    {
        // Under GLSL < 420, layout(binding=...) is omitted; we must bind the block in C#.
        int blockIndex = GL.GetUniformBlockIndex(programId, BlockName);
        if (blockIndex < 0)
        {
            return;
        }

        GL.UniformBlockBinding(programId, blockIndex, GpuBindingRegistry.Ubo.Object);
    }

    public static void BindSize(ObjectParamsUbo paramsUbo, int width, int height)
    {
        BindSizes(paramsUbo,
            fineW: 0,
            fineH: 0,
            coarseW: 0,
            coarseH: 0,
            sizeW: width,
            sizeH: height);
    }

    public static void BindFineCoarse(ObjectParamsUbo paramsUbo, int fineW, int fineH, int coarseW, int coarseH)
    {
        BindSizes(paramsUbo,
            fineW: fineW,
            fineH: fineH,
            coarseW: coarseW,
            coarseH: coarseH,
            sizeW: 0,
            sizeH: 0);
    }

    public static void BindSizes(ObjectParamsUbo paramsUbo, int fineW, int fineH, int coarseW, int coarseH, int sizeW, int sizeH)
    {
        Span<byte> bytes = stackalloc byte[ParamsSizeBytes];

        // ivec4 uFineSize/uCoarseSize/uSize only; leave everything else zero.
        UboPacking.WriteIVec4(bytes, OffsetUFineSize, x: fineW, y: fineH, z: 0, w: 0);
        UboPacking.WriteIVec4(bytes, OffsetUCoarseSize, x: coarseW, y: coarseH, z: 0, w: 0);
        UboPacking.WriteIVec4(bytes, OffsetUSize, x: sizeW, y: sizeH, z: 0, w: 0);

        paramsUbo.UploadAndBind(bytes);
    }
}
