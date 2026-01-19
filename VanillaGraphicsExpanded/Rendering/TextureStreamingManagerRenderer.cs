using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class TextureStreamingManagerRenderer : IRenderer
{
    public double RenderOrder => 0;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        TextureStreamingSystem.TickOnRenderThread();
    }

    public void Dispose()
    {
        // Owned elsewhere.
    }
}

