namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares the query target before native realization.</summary>
public enum QueryKind
{
    TimeElapsed, Timestamp, SamplesPassed, AnySamplesPassed, AnySamplesPassedConservative,
    PrimitivesGenerated, TransformFeedbackPrimitivesWritten,
    VerticesSubmitted, PrimitivesSubmitted, VertexShaderInvocations, TessellationControlPatches,
    TessellationEvaluationInvocations, GeometryShaderInvocations, GeometryPrimitivesEmitted,
    FragmentShaderInvocations, ComputeShaderInvocations, ClippingInputPrimitives, ClippingOutputPrimitives
}
