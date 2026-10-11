using System;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
namespace VanillaGraphicsExpanded.Rendering.Integration;
/// <summary>Translates only the engine pool's logical inputs and owns no executable.</summary>
internal sealed class LiquidPoolInputAdapter(ILiquidPoolInputs inputs) : IShaderProgram
{
    #region Public API
    #region Pool inputs
    /// <summary>Preserves the model-view traversal branch and explicitly rejects unknown input queries.</summary>
    public bool HasUniform(string uniformName) => uniformName is "origin" or "modelViewMatrix" or "forcedTransparency";
    /// <summary>Translates the engine's camera-relative pool origin.</summary>
    public void Uniform(string uniformName, Vec3f value)
    {
        if (uniformName != "origin") throw Unsupported(uniformName);
        inputs.Origin = new(value.X, value.Y, value.Z);
    }
    /// <summary>Translates mini-dimension transforms and the engine's finally restoration.</summary>
    public void UniformMatrix(string uniformName, float[] matrix)
    {
        if (uniformName != "modelViewMatrix") throw Unsupported(uniformName);
        inputs.ModelViewMatrix = matrix;
    }
    /// <summary>Translates preview transparency and its restoration.</summary>
    public void Uniform(string uniformName, float value)
    {
        if (uniformName != "forcedTransparency") throw Unsupported(uniformName);
        inputs.ForcedTransparency = value;
    }
    #endregion
    #region Unsupported engine contract
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public int ProgramId { get => throw Unsupported("ProgramId"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public string AssetDomain { get => throw Unsupported("AssetDomain"); set => throw Unsupported("AssetDomain"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public int PassId { get => throw Unsupported("PassId"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public string PassName { get => throw Unsupported("PassName"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public bool ClampTexturesToEdge { get => throw Unsupported("ClampTexturesToEdge"); set => throw Unsupported("ClampTexturesToEdge"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public IShader VertexShader { get => throw Unsupported("VertexShader"); set => throw Unsupported("VertexShader"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public IShader FragmentShader { get => throw Unsupported("FragmentShader"); set => throw Unsupported("FragmentShader"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public IShader GeometryShader { get => throw Unsupported("GeometryShader"); set => throw Unsupported("GeometryShader"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public bool Oit { get => throw Unsupported("Oit"); set => throw Unsupported("Oit"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public bool Disposed { get => throw Unsupported("Disposed"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public bool LoadError { get => throw Unsupported("LoadError"); }
    /// <summary>Rejects executable metadata on an input-only translator.</summary>
    public OrderedDictionary<string, UBORef> UBOs { get => throw Unsupported("UBOs"); }
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Use() => throw Unsupported("Use");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Stop() => throw Unsupported("Stop");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public bool Compile() => throw Unsupported("Compile");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Dispose() => throw Unsupported("Dispose");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, int value) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, Vec2f value) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, Vec2i value) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, float valueX, float valueY) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, float valueX, float valueY, float valueZ) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, float valueX, float valueY, float valueZ, float valueW) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniform(string uniformName, Vec4f value) => throw Unsupported("Uniform");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void Uniforms4(string uniformName, int count, float[] values) => throw Unsupported("Uniforms4");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void BindTexture2D(string samplerName, int textureId, int textureNumber) => throw Unsupported("BindTexture2D");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void BindTextureCube(string samplerName, int textureId, int textureNumber) => throw Unsupported("BindTextureCube");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void UniformMatrices(string uniformName, int count, float[] matrix) => throw Unsupported("UniformMatrices");
    /// <summary>Rejects operations outside the verified pool-input contract.</summary>
    public void UniformMatrices4x3(string uniformName, int count, float[] matrix) => throw Unsupported("UniformMatrices4x3");
    #endregion
    #endregion
    #region Private
    /// <summary>Reports the operation that exceeded this adapter's bounded contract.</summary>
    private static NotSupportedException Unsupported(string operation) => new($"Liquid pool input adapter does not support '{operation}'.");
    #endregion
}
