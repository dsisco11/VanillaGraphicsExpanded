using System;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// A VGE shader program that can load vertex/fragment stages from different asset base-names.
///
/// Intended for multi-pass pipelines that share a common fullscreen vertex stage.
/// </summary>
internal sealed class VgeStageNamedShaderProgram : VgeShaderProgram
{
    private readonly string vertexStageShaderName;
    private readonly string fragmentStageShaderName;
    private readonly string geometryStageShaderName;

    private readonly Dictionary<string, int> uniformLocationCache = new(StringComparer.Ordinal);

    protected override string VertexStageShaderName => vertexStageShaderName;

    protected override string FragmentStageShaderName => fragmentStageShaderName;

    protected override string GeometryStageShaderName => geometryStageShaderName;

    public VgeStageNamedShaderProgram(
        string passName,
        string vertexStageShaderName,
        string fragmentStageShaderName,
        string assetDomain,
        string? geometryStageShaderName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexStageShaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentStageShaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetDomain);

        PassName = passName;
        AssetDomain = assetDomain;

        this.vertexStageShaderName = vertexStageShaderName;
        this.fragmentStageShaderName = fragmentStageShaderName;
        this.geometryStageShaderName = geometryStageShaderName ?? passName;
    }

    private int GetUniformLocationCached(string uniformName)
    {
        if (uniformLocationCache.TryGetValue(uniformName, out int loc))
        {
            return loc;
        }

        loc = GL.GetUniformLocation(ProgramId, uniformName);
        uniformLocationCache[uniformName] = loc;
        return loc;
    }

    public void Uniform2i(string uniformName, int x, int y)
    {
        int loc = GetUniformLocationCached(uniformName);
        if (loc >= 0)
        {
            GL.Uniform2(loc, x, y);
        }
    }

    public void Uniform4i(string uniformName, int x, int y, int z, int w)
    {
        int loc = GetUniformLocationCached(uniformName);
        if (loc >= 0)
        {
            GL.Uniform4(loc, x, y, z, w);
        }
    }

    public void Uniform2f(string uniformName, float x, float y)
    {
        int loc = GetUniformLocationCached(uniformName);
        if (loc >= 0)
        {
            GL.Uniform2(loc, x, y);
        }
    }

    public void Uniform3f(string uniformName, float x, float y, float z)
    {
        int loc = GetUniformLocationCached(uniformName);
        if (loc >= 0)
        {
            GL.Uniform3(loc, x, y, z);
        }
    }

    public void Uniform1fv(string uniformName, float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        int loc = GetUniformLocationCached(uniformName);
        if (loc >= 0)
        {
            GL.Uniform1(loc, values.Length, values);
        }
    }
}
