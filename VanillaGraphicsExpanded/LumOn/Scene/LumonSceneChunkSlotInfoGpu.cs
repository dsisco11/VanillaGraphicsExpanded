using System.Runtime.InteropServices;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>One std430 ivec4: integer chunk origin and generation, with a 16-byte array stride.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LumonSceneChunkSlotInfoGpu(
    VectorInt4 ChunkOriginBlocksAndGeneration);
