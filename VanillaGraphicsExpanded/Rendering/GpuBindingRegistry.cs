using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Central registry of binding indices / units used by VGE shaders.
///
/// Notes:
/// - UBO binding points, SSBO binding points, image units, and texture units are distinct namespaces in OpenGL.
/// - Texture units and image units are global GL state; treat them as part of the program contract and always apply them
///   deterministically (explicit bindings when available; reflection-based fallback otherwise).
/// </summary>
internal static partial class GpuBindingRegistry
{
    /// <summary>
    /// UBO (GL_UNIFORM_BUFFER) binding points.
    /// </summary>
    internal static class Ubo
    {
        // Keep these stable: many shaders/wrappers assume these values.
        public const int Frame = 12;
        public const int WorldProbe = 13;

        // Reserved for the upcoming UBO-only refactor.
        public const int Object = 14;
        public const int Material = 15;
        public const int Lights = 16;

        // Small, dedicated bridge UBO for vanilla-terrain world-space reconstruction.
        public const int TerrainBridge = 27;
    }

    /// <summary>
    /// SSBO (GL_SHADER_STORAGE_BUFFER) binding points.
    /// </summary>
    internal static class Ssbo
    {
        internal static class LumOnScene
        {
            public const int Work = 0;
            public const int Metadata = 1;
            public const int Payload = 0;
            public const int Updates = 1;
            public const int Triangles = 2;
            public const int ChunkSlotInfo = 2;
        }
    }

    /// <summary>
    /// Image units for GLSL image load/store.
    /// </summary>
    internal static class Image
    {
        internal static class LumOnScene
        {
            public const int DepthAtlas = 0;
            public const int MaterialAtlas = 1;
            public const int IrradianceAtlas = 0;
            public const int PageUsageStamp = 0;
            public const int OccLevelsBase = 0;
        }
    }

    /// <summary>
    /// Texture units used by samplers (sampler2D/3D/Array/etc).
    /// </summary>
    internal static class Sampler
    {
        internal static class LumOnScene
        {
            public const int PatchIdGBuffer = 0;
            public const int ChunkSlotGenerationTex = 1;
            public const int OccL0 = 2;
            public const int MaterialPalette = 3;

            public const int LightColorLut = 3;
            public const int BlockLevelScalarLut = 4;
            public const int SunLevelScalarLut = 5;
            public const int SurfaceLut = 7;

            public const int PageUsageStamp = 0;
            public const int PageTableMip0 = 1;
        }

        internal static class Common
        {
            // Matches engine conventions for fullscreen passes in many shaders.
            public const int PrimaryDepth = 0;
            public const int GBufferNormal = 1;
        }
    }

}
