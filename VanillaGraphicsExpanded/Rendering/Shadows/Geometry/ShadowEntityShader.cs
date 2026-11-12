using System;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns packaged skinned native material executables and immutable animation snapshots.</summary>
[ShaderProgram("Contract", "shadows/entity", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "shadows/entity.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "shadows/entity.fsh")]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Waving))]
internal sealed partial class ShadowEntityShader : GpuProgram, IShadowEntityBindings
{
    /// <summary>Matches the qualified installed engine's animation uniform block capacity.</summary>
    internal const int JointCapacity = 230;
    private readonly PackedUniformBuffer parameters = new(208);
    private readonly PackedUniformBuffer animation = new(JointCapacity * 64);
    private readonly PackedUniformBuffer flags = new(16);

    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <summary>Uses the native surface material's deformation switch.</summary>
    [ShaderOption("VGE_VERTEX_DEFORMATION_ENABLED", 1, Domain = new object[] { 0, 1 })]
    public partial int Waving { get; set; }

    /// <summary>Copies a complete finite material and palette snapshot before generated resource capture.</summary>
    internal void CaptureMaterial(ReadOnlySpan<byte> material, ReadOnlySpan<float> joints, int renderFlags)
    {
        if (material.Length != 208 || joints.Length < 16 || joints.Length % 16 != 0 || joints.Length > JointCapacity * 16)
            throw new ArgumentException("Native entity material or animation palette has an unsupported extent.");
        for (int i = 0; i < material.Length; i += 4)
            if (i != 132 && !float.IsFinite(BitConverter.ToSingle(material.Slice(i, 4))))
                throw new ArgumentException("Native entity material is not finite.");
        foreach (float value in joints)
            if (!float.IsFinite(value)) throw new ArgumentException("Native entity animation is not finite.");
        // Unused joints cannot inherit another object's matrices. Native upload owns
        // joint indices; the complete fixed-size buffer is private shadow metadata.
        Span<byte> palette = stackalloc byte[JointCapacity * 64]; palette.Clear();
        MemoryMarshal.AsBytes(joints).CopyTo(palette);
        Span<byte> packedFlags = stackalloc byte[16]; packedFlags.Clear();
        UboPacking.WriteInt32(packedFlags, 0, renderFlags);
        RequireInputMutation(); parameters.SetBytes(material); animation.SetBytes(palette); flags.SetBytes(packedFlags);
    }

    /// <summary>Loads only a precompiled material variant, preserving the production SPIR-V loading requirement.</summary>
    internal static ShadowEntityShader Create(ICoreAPI api, bool waving)
    {
        var shader = new ShadowEntityShader { PassName = Contract.Identity, AssetDomain = Constants.ModId };
        try
        {
            shader.SetShaderOptions(options => options.Set(WavingOption, waving ? 1 : 0));
            shader.Initialize(EngineShaderAssets.Create(api.Assets, Constants.ModId));
            if (!shader.CompileAndLink()) throw new InvalidOperationException("Packaged native entity shadow variant could not be prepared.");
            return shader;
        }
        catch { shader.Dispose(); throw; }
    }
    #endregion

    #region Private
    /// <summary>Registers the generated resource interface and owns logical versions until their RHI consumers retire.</summary>
    private ShadowEntityShader()
    {
        foreach (var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
        OwnUniformBuffer(parameters); OwnUniformBuffer(animation); OwnUniformBuffer(flags);
    }

    /// <inheritdoc />
    CpuUniformBuffer IShadowTerrainBindings.Parameters => parameters;
    /// <inheritdoc />
    CpuUniformBuffer IShadowEntityBindings.Animation => animation;
    /// <inheritdoc />
    CpuUniformBuffer IShadowEntityBindings.Flags => flags;
    #endregion
}
