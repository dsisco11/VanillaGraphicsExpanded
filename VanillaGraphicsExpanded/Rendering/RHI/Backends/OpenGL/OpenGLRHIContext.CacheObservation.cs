using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Connects existing native cache observation and deletion effects to its admitted implementation.</summary>
internal static partial class OpenGLRHIContext
{
    #region Internal API
    /// <summary>Reads unknown native selector knowledge for the existing cache collaborator.</summary>
    internal static int ReadImageSelector(GetPName parameter)
    { _ = Current; return backend!.ReadImageSelector(parameter); }
    #endregion
}
