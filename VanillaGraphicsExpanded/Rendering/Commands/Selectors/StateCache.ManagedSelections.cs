using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes controlled program, VAO and pipeline selections without descriptor rediscovery.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Selects an executable only when its binding is unknown or different.</summary>
    public void UseProgram(int programId)
    {
        ProgramScopeTracker.ForgetCurrent();
        UseProgramBinding(programId);
    }

    /// <summary>Records a completed full-program installation, rather than a uniform-destination query.</summary>
    public void NotifyProgramBound(int programId)
    {
        currentProgram = programId;
        currentProgramStagesKnown = true;
    }

    /// <summary>Selects a VAO without discarding its retained element-buffer association.</summary>
    public void BindVertexArray(int vaoId)
    {
        if (currentVao == vaoId) return;
        currentVao = null;
        try
        {
            GL.BindVertexArray(vaoId);
            // Controlled selections retain their input; arbitrary external calls leave
            // the selector unknown through the exact-overload execution boundary.
            currentVao = vaoId;

            // A borrowed VAO may already own an EBO. Leave unknown associations uncached
            // until queried or observed through BindBuffer rather than inventing a zero binding.
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
        }
    }

    /// <summary>Retains a controlled pipeline selection and suppresses known-equal requests.</summary>
    public void BindProgramPipeline(int pipelineId)
    {
        if (currentProgramPipeline == pipelineId) return;
        currentProgramPipeline = null;
        try
        {
            GL.BindProgramPipeline(pipelineId);
            currentProgramPipeline = pipelineId;
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
        }
    }
    #endregion

    #region Private
    /// <summary>Binds through the single native cache without withdrawing an admitted owner.</summary>
    private void UseProgramBinding(int programId)
    {
        if (currentProgramStagesKnown && currentProgram == programId) return;
        currentProgram = null;
        currentProgramStagesKnown = false;
        try
        {
            GL.UseProgram(programId);
            NotifyProgramBound(programId);
        }
        catch when (!restoringBoundary && !resolvingBoundary && activeBoundary is null)
        {
        }
    }
    #endregion
}
