using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains observed per-object capture activity without adding a canonical configuration schema.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, (bool Active, bool Paused)> feedbackActivity = [];

    #region Internal API
    /// <summary>Reads established activity only when the selected object's identity is known.</summary>
    internal bool TryGetFeedbackActivity(out (bool Active, bool Paused) activity)
    {
        activity = default;
        return currentTransformFeedback is { } id && feedbackActivity.TryGetValue(id, out activity);
    }

    /// <summary>Records controlled or observed activity against the selected object, never a foreign namespace.</summary>
    internal void RecordFeedbackActivity(bool active, bool paused)
    {
        if (currentTransformFeedback is { } id) feedbackActivity[id] = (active, paused);
    }

    /// <summary>Records guaranteed initial activity of newly created names without probing native object validity.</summary>
    internal void RecordFeedbackCreated(int id)
    {
        if (id != 0) feedbackActivity[id] = (false, false);
    }

    /// <summary>Withdraws only the selected capture object's uncertain activity.</summary>
    internal void ForgetFeedbackActivity()
    {
        if (currentTransformFeedback is { } id) feedbackActivity.Remove(id);
    }

    /// <summary>Reconciles the completed native transition using local acceptance and original-context facts.</summary>
    internal static void ObserveFeedbackActivity(FeedbackActivityCommand command,
        TransformFeedbackPrimitiveType primitive = 0)
    {
        var cache = OpenGLObservationContext.CurrentCache;
        int id = OpenGLFeedbackBindings.GetExternalSelection(cache);
        bool known = cache.feedbackActivity.TryGetValue(id, out var previous);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();

        if (!cache.OwnsKnowledgeFor(origin) || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) return;
        bool invalidPrimitive = command == FeedbackActivityCommand.Begin
            && primitive is not (TransformFeedbackPrimitiveType.Points or TransformFeedbackPrimitiveType.Lines or TransformFeedbackPrimitiveType.Triangles);
        if (known)
        {
            if (invalidPrimitive || command == FeedbackActivityCommand.Begin && previous.Active
                || command is FeedbackActivityCommand.End or FeedbackActivityCommand.Pause or FeedbackActivityCommand.Resume && !previous.Active
                || command == FeedbackActivityCommand.Pause && previous.Paused
                || command == FeedbackActivityCommand.Resume && !previous.Paused)
            { return; }
            if (command == FeedbackActivityCommand.End) { cache.feedbackActivity[id] = (false, false); return; }
            if (command == FeedbackActivityCommand.Pause) { cache.feedbackActivity[id] = (true, true); return; }
        }
        if (invalidPrimitive) return;
        // Begin depends on an intentionally unrepresented native output configuration;
        // Resume also depends on the executable retained by the paused object. Read this
        // outcome once, then reuse activity history at subsequent managed boundaries.
        cache.feedbackActivity.Remove(id);
        _ = OpenGLFeedbackBindings.GetExternalActivity(cache);
    }
    #endregion

    /// <summary>Identifies activity transitions independently of the managed capture scope's ownership.</summary>
    internal enum FeedbackActivityCommand { Begin, End, Pause, Resume }

}
