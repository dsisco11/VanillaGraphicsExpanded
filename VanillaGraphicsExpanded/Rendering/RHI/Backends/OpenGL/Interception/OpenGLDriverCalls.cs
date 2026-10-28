namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Provides generated backend-only calls to saved original driver entrypoints.</summary>
internal static unsafe partial class OpenGLDriverCalls
{
    private static readonly OpenGLBindingTable Core = OpenGLBindingTable.For(typeof(OpenTK.Graphics.OpenGL4.GL));
    private static readonly OpenGLBindingTable Legacy = OpenGLBindingTable.For(typeof(OpenTK.Graphics.OpenGL.GL));
}
