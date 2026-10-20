using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Retains immutable view-pass preparation without retaining an active attachment borrow.</summary>
internal sealed class CloudVolumeViewPass(GpuProgram program,GraphicsPipelineLifetime lifetime):IDisposable
{
    private ulong attachmentRevision;
    private int width,height;
    private bool blended;
    private (nint Handle,long Generation) context;
    internal RenderPassDesc? Description {get;private set;}
    internal GraphicsPipeline? Pipeline {get;private set;}
    /// <summary>Counts signature preparations, independently of per-draw resource validation.</summary>
    internal int Preparations {get;private set;}

    #region Public API
    /// <summary>Refreshes immutable descriptions only when target metadata or executable authority changes.</summary>
    internal void Prepare(GpuFramebuffer target,IEnumerable<RenderPassColor> outputs,bool blend)
    {
        ObjectDisposedException.ThrowIf(target.IsDisposed,target);
        if(Description is {} pass&&ReferenceEquals(pass.Target,target)&&attachmentRevision==target.AttachmentRevision&&
            width==target.Width&&height==target.Height&&blended==blend&&context==program.ExecutableContext&&
            Pipeline is {} existing&&existing.ExecutableRevision==program.ExecutableRevision&&
            existing.Description.Shader==program.GraphicsIdentity)return;

        // Metadata is borrowed only while deriving the signature. BeginPass still acquires and
        // validates current attachments on every draw, including the cached preparation path.
        var next=new RenderPassDesc(target,outputs);
        using var metadata=new RenderPassTargets(next);
        var description=new GraphicsPipelineDesc(program.GraphicsIdentity!,CloudVolumeViewRenderer.Layout,metadata.Signature,
            DynamicPipelineState.Viewport,blending:blend?LiquidPipelineStates.SurfaceBlending:null);
        if(Pipeline is null||Pipeline.Description!=description||Pipeline.ExecutableRevision!=program.ExecutableRevision||
            context!=program.ExecutableContext)
        {
            var replacement=new GraphicsPipeline(lifetime,description,program);
            Pipeline?.Dispose();Pipeline=replacement;
        }
        Description=next;attachmentRevision=target.AttachmentRevision;width=target.Width;height=target.Height;
        blended=blend;context=program.ExecutableContext;Preparations++;
    }

    /// <summary>Submits a fresh immediate pass and dynamic viewport using the retained immutable preparation.</summary>
    internal void Draw(GraphicsCommandContext commands,ArrayGraphicsGeometry geometry)
    {
        commands.BeginPass(Description??throw new InvalidOperationException("Stored cloud pass is not prepared."));
        commands.SetPipeline(Pipeline??throw new InvalidOperationException("Stored cloud pipeline is not prepared."));
        commands.SetDynamicState(new(){Viewport=commands.PassViewport});
        commands.Draw(geometry,new(0,3));commands.EndPass();
    }

    /// <summary>Releases cached target references and executable realizations without disposing their external owners.</summary>
    public void Dispose()
    {
        Pipeline?.Dispose();Pipeline=null;Description=null;
    }
    #endregion
}
