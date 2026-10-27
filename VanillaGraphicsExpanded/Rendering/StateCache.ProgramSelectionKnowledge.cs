namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Provides executed program and native pipeline-object selection knowledge without owning dispatch.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Records a completed full-program selection rather than a queried uniform destination.</summary>
    internal void NotifyProgramBound(int program){currentProgram=program;currentProgramStagesKnown=true;}
    /// <summary>Gets established native pipeline selection without driver discovery.</summary>
    internal int? ProgramPipelineSelectionKnowledge=>currentProgramPipeline;
    /// <summary>Publishes or withdraws native pipeline selection after backend execution.</summary>
    internal void SetProgramPipelineSelectionKnowledge(int? pipeline)=>currentProgramPipeline=pipeline;
    #endregion
}
