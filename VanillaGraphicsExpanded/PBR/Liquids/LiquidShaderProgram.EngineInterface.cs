using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Translates the mesh-pool interface into the owned draw block without standalone uniforms.</summary>
internal sealed partial class LiquidShaderProgram
{
    #region Engine parameter interface
    /// <summary>Advertises logical pool inputs even though their storage is a uniform block.</summary>
    bool IShaderProgram.HasUniform(string name)
        => name is "origin" or "modelViewMatrix" or "forcedTransparency" || HasUniform(name);

    /// <summary>Retains the next pool origin for pre-draw submission.</summary>
    void IShaderProgram.Uniform(string name, Vec3f value)
    {
        if (name != "origin") { Uniform(name, value); return; }
        Origin = new Vector3(value.X, value.Y, value.Z);
    }

    /// <summary>Retains mini-dimension transforms, including the engine's restoration write.</summary>
    void IShaderProgram.UniformMatrix(string name, float[] value)
    {
        if (name != "modelViewMatrix") { UniformMatrix(name, value); return; }
        ModelViewMatrix = value;
    }

    /// <summary>Retains preview transparency independently of origin and transform.</summary>
    void IShaderProgram.Uniform(string name, float value)
    {
        if (name != "forcedTransparency") { Uniform(name, value); return; }
        ForcedTransparency = value;
    }
    #endregion
}
