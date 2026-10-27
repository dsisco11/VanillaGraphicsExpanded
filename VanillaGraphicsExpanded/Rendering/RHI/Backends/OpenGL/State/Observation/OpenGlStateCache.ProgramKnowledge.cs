namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Provides the existing executed program-selection knowledge to backend-owned dispatch.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Withdraws changed executable selection knowledge without mutating desired RHI inputs.</summary>
    internal bool PrepareProgramSelection(int program)
    {
        if (currentProgramStagesKnown && currentProgram == program) return false;
        currentProgram = null;
        currentProgramStagesKnown = false;
        return true;
    }
    #endregion
}
