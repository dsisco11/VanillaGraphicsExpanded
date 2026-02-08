using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class LumonSceneFeedbackParamsUbo
{
    public const int MarkParamsSizeBytes = 16;
    public const int CompactParamsSizeBytes = 16;

    public static void BindMark(ObjectParamsUbo paramsUbo, uint frameStamp)
    {
        Span<byte> bytes = stackalloc byte[MarkParamsSizeBytes];
        UboPacking.WriteUVec4(bytes, byteOffset: 0, x: frameStamp, y: 0u, z: 0u, w: 0u);
        paramsUbo.UploadAndBind(bytes);
    }

    public static void BindCompact(ObjectParamsUbo paramsUbo, uint maxRequests, uint frameStamp, uint scanOffset, uint compactMode)
    {
        Span<byte> bytes = stackalloc byte[CompactParamsSizeBytes];
        UboPacking.WriteUVec4(bytes, byteOffset: 0, x: maxRequests, y: frameStamp, z: scanOffset, w: compactMode);
        paramsUbo.UploadAndBind(bytes);
    }
}
