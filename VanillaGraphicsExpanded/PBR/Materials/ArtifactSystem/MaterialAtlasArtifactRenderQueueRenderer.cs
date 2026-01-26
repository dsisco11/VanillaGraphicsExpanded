using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Artifacts;

internal sealed class MaterialAtlasArtifactRenderQueueRenderer : IRenderer
{
    private readonly MaterialAtlasArtifactRenderQueue queue;

    public MaterialAtlasArtifactRenderQueueRenderer(MaterialAtlasArtifactRenderQueue queue)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public double RenderOrder => 0;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        queue.Drain(maxItemsPerFrame: 16);
    }

    public void Dispose()
    {
        // Queue is owned elsewhere.
    }
}
