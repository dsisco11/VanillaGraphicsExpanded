using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Exposes coherent executable dependencies to graphics preparation without replacing shader ownership.</summary>
public abstract partial class GpuProgram
{
    /// <summary>Monotonic successful-installation revision; failed and superseded candidates never advance it.</summary>
    internal ulong ExecutableRevision { get; private set; }
    /// <summary>Identifies the installed executable context, including native handle reuse.</summary>
    internal (nint Handle, long Generation) ExecutableContext { get; private set; }
    /// <summary>Supplies immutable installed generation identity to scoped restoration.</summary>
    ulong IGpuProgram.ExecutableRevision => ExecutableRevision;
    /// <summary>Supplies the originating context registration to scoped restoration.</summary>
    (nint Handle, long Generation) IGpuProgram.ExecutableContext => ExecutableContext;
    /// <summary>Retained interface belonging to the installed executable, never queried during pipeline validation.</summary>
    internal GraphicsExecutableInterface? GraphicsInterface => ProgramLayout.BinaryInterface?.Graphics;
    /// <summary>Installed structural selection, distinct from the live executable revision and pending settings.</summary>
    internal ShaderPipelineIdentity? GraphicsIdentity { get; private set; }
}
