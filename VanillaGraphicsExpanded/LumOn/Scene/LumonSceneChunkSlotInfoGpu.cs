using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LumonSceneChunkSlotInfoGpu(
    VectorInt4 ChunkOriginBlocksAndGeneration,
    VectorInt4 Reserved0);

