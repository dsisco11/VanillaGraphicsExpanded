using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class UniformBlockBindingUtil
{
    public static void EnsureBlockBound(int programId, string blockName, int bindingIndex)
    {
        int blockIndex = GL.GetUniformBlockIndex(programId, blockName);
        if (blockIndex < 0)
        {
            return;
        }

        GL.UniformBlockBinding(programId, blockIndex, bindingIndex);
    }
}
