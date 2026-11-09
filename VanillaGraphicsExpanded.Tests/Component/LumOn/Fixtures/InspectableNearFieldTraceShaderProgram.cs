using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

/// <summary>Exposes retained frame references to the test fixture without adding production reset APIs.</summary>
internal sealed class InspectableNearFieldTraceShaderProgram : LumOnScreenProbeAtlasTraceShaderProgram
{
    #region Public API
    /// <summary>Creates a production trace executable for class-owned numerical reuse.</summary>
    public InspectableNearFieldTraceShaderProgram() { }
    #endregion

    #region Internal API
    /// <summary>Reports whether case camera and uniform-buffer objects are no longer retained.</summary>
    internal bool HasNoCaseUniformReferences => FrameInputs is null && RetainedFrame is null && RetainedWorldProbe is null;
    #endregion
}
