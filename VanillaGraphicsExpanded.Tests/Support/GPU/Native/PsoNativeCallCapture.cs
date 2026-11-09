using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Native;

/// <summary>Records native state effects separately from setup, draw application and engine restoration.</summary>
internal sealed partial class PsoNativeCallCapture : IDisposable
{
    private static readonly FieldInfo selectedGraphics = typeof(RHICommandContext).GetField("selectedGraphicsPipeline", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHICommandContext).FullName, "selectedGraphicsPipeline");
    private static readonly FieldInfo submitting = typeof(RHICommandContext).GetField("submitting", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHICommandContext).FullName, "submitting");
    private static readonly FieldInfo selectedBackend = typeof(RHICommandContext).GetField("backend", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHICommandContext).FullName, "backend");
    private static readonly FieldInfo activePass = typeof(OpenGLBackend).GetField("activeRenderPass", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(OpenGLBackend).FullName, "activeRenderPass");
    private static readonly FieldInfo referencedPipeline = typeof(RHIGraphicsPipelineRef).GetField("pipeline", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(RHIGraphicsPipelineRef).FullName, "pipeline");
    private readonly RHICommandContext context = RHICommandContext.Current;
    private readonly OpenGlStateCache cache = OpenGlStateCache.Current;
    private RHIGraphicsPipeline? markedPipeline;
    internal readonly List<string> Events = [];

    #region Public API
    /// <summary>Installs native observers without depending on JIT-sensitive managed method patching.</summary>
    internal PsoNativeCallCapture()
    {
        InstallNativeObservers();
    }

    /// <summary>Retires observers before any inspection or fixture cleanup.</summary>
    public void Dispose()
    {
        native.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Reads actual command/pass sequencing solely for trace classification, without adding a production hook.</summary>
    private RHIGraphicsPipeline? ExecutingPipeline()
    {
        // Pass loads run before the pass becomes active; idle selections and restoration must not
        // be classified as draw application. Observation never resolves resource metadata.
        if (!(bool)submitting.GetValue(context)! || activePass.GetValue(selectedBackend.GetValue(context)!) is null) return null;
        var pipeline = (RHIGraphicsPipeline?)referencedPipeline.GetValue(selectedGraphics.GetValue(context)!);
        return pipeline;
    }

    /// <summary>Associates subsequent native effects with the exact selected immutable blend contract.</summary>
    private void MarkApplication(RHIGraphicsPipeline pipeline)
    {
        if (ReferenceEquals(markedPipeline, pipeline)) return;
        markedPipeline = pipeline;
        var description = pipeline.Description;
        Events.Add("draw:" + string.Join(",", description.Blending.Select((blend, slot) =>
            description.Targets.Colors[slot].Format is null ? "hole" : blend.Enabled ? "blend" : "replace")));
    }

    /// <summary>Labels native effects from current execution facts while excluding pass loads and boundary restoration.</summary>
    private string Segment()
    {
        if (cache.RestoringBoundary) { markedPipeline = null; return "restoration"; }
        if (ExecutingPipeline() is { } pipeline) { MarkApplication(pipeline); return "application"; }
        markedPipeline = null;
        return "setup";
    }

    /// <summary>Checks real blend state at each native draw rather than relying on a managed postfix surviving warm-up.</summary>
    private void DrawCompleted()
    {
        var pipeline = ExecutingPipeline() ?? throw new InvalidOperationException("A captured RHI draw has no executing pipeline.");
        MarkApplication(pipeline);
        if (pipeline.Description.Blending.Any(blend=>blend.Enabled))
        {
            // Check native values independently of the emitted-call log.
            var state=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(PsoNativeStateFingerprint.Read())!;
            for(int slot=0;slot<pipeline.Description.Blending.Count;slot++)
            {
                var blend=pipeline.Description.Blending[slot];
                if(!blend.Enabled || pipeline.Description.Targets.Colors[slot].Format is null)continue;
                Assert.Equal(((int)OpenGLBlendValues.SourceFactor(blend.SourceRgb)).ToString(),state["BlendSrcRgb:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.DestinationFactor(blend.DestinationRgb)).ToString(),state["BlendDstRgb:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.SourceFactor(blend.SourceAlpha)).ToString(),state["BlendSrcAlpha:"+slot]);
                Assert.Equal(((int)OpenGLBlendValues.DestinationFactor(blend.DestinationAlpha)).ToString(),state["BlendDstAlpha:"+slot]);
            }
            Events.Add("validated:active-blend-factors");
        }
        markedPipeline = null;
    }
    #endregion
}
