namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes the installed executable and layout shared by graphics and compute submission.</summary>
internal interface IShaderSubmissionTarget
{
    /// <summary>Gets the currently installed executable.</summary>
    int ProgramId { get; }
    /// <summary>Gets bindings resolved for that executable generation.</summary>
    GpuProgramLayout ProgramLayout { get; }
}
