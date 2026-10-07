using System.Collections.Immutable;
using System.Security.Cryptography;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Checks exact artifact association and deterministic selection before any native executable is published.</summary>
public sealed class PackagedInterfaceValidationTests
{
    #region Public API
    /// <summary>Metadata preserves compiler declarations and survives the existing digest manifest serialization.</summary>
    [Fact]
    public void ResolvesDeclarationsFromDigestManifest()
    {
        var fixture = Fixture(5);
        var serialized = ShaderBinaryDigest.Encode(fixture.Manifest.Binaries);
        var result = PackagedInterfaceValidation.Resolve(ShaderBinaryDigest.Parse(serialized), fixture.Stage, fixture.Binary);
        Assert.Empty(result.Inputs);
    }

    /// <summary>A same-length binary substitution cannot reuse compiler facts from different code.</summary>
    [Fact]
    public void RejectsSameLengthStaleBinary()
    {
        var fixture = Fixture(1);
        fixture.Binary[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => PackagedInterfaceValidation.Resolve(fixture.Manifest, fixture.Stage, fixture.Binary));
    }

    /// <summary>Missing or mismatched declarations reject preparation.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("stage")]
    [InlineData("entry")]
    [InlineData("structural")]
    [InlineData("version")]
    [InlineData("interface")]
    public void RejectsInvalidInterface(string fault)
    {
        var fixture = Fixture(5);
        var entry = fixture.Manifest.Binaries[fixture.Stage.BinaryPath];
        var metadata = entry.Interface!;
        metadata = fault switch
        {
            "missing" => null,
            "stage" => metadata with { Stage = ShaderStageKind.Fragment },
            "entry" => metadata with { EntryPoint = "other" },
            "structural" => metadata with { StructuralKey = "other" },
            "version" => metadata with { Version = 99 },
            _ => metadata with { Interface = null! }
        };
        fixture.Manifest.Binaries[fixture.Stage.BinaryPath] = entry with { Interface = metadata };
        Assert.Throws<InvalidDataException>(() => PackagedInterfaceValidation.Resolve(fixture.Manifest, fixture.Stage, fixture.Binary));
    }
    #endregion

    #region Malformed shapes
    /// <summary>Corrupt numeric declarations cannot manufacture valid interface coverage.</summary>
    [Theory]
    [InlineData("scalar")]
    [InlineData("width")]
    [InlineData("matrix")]
    [InlineData("index")]
    [InlineData("component")]
    [InlineData("extent")]
    [InlineData("overflow")]
    [InlineData("ids")]
    public void RejectsMalformedShape(string fault)
    {
        var fixture = Fixture(1);
        var entry = fixture.Manifest.Binaries[fixture.Stage.BinaryPath];
        var value = new PackagedInterfaceVariable(0, 0, 0, ShaderScalarType.Float, 32, 4, 1, []);
        value = fault switch
        {
            "scalar" => value with { Scalar = (ShaderScalarType)999 },
            "width" => value with { BitWidth = 16 },
            "matrix" => value with { Scalar = ShaderScalarType.Int, Columns = 2 },
            "index" => value with { Index = 2 },
            "component" => value with { Component = 4 },
            "extent" => value with { ArrayDimensions = [0] },
            "overflow" => value with { ArrayDimensions = [uint.MaxValue, uint.MaxValue] },
            _ => value
        };
        var shape = new PackagedStageInterface([value], [], fault == "ids"
            ? [new((uint)Silk.NET.SPIRV.ExecutionMode.LocalSizeId, [1, 0, 3], true)] : []);
        fixture.Manifest.Binaries[fixture.Stage.BinaryPath] = entry with { Interface = entry.Interface! with { Interface = shape } };
        Assert.Throws<InvalidDataException>(() => PackagedInterfaceValidation.Resolve(fixture.Manifest, fixture.Stage, fixture.Binary));
    }
    #endregion

    #region Private
    /// <summary>Constructs an unrestricted numeric contract and compiler-shaped metadata for association tests.</summary>
    private static (ShaderStageSelection Stage, byte[] Binary, ShaderBinaryDigest.Manifest Manifest) Fixture(int selected)
    {
        var count = new ShaderOption<int>("count", 1);
        var contract = new ShaderStageContract("fixture", "fixture.vsh", ShaderStageKind.Vertex, new(),
            specializations: [new ShaderSpecialization(7, count)]);
        var selection = new ShaderStageSelection(contract, new Dictionary<string, ShaderScalar> { ["count"] = ShaderScalar.From(selected) });
        byte[] binary = [1, 2, 3, 4];
        var metadata = new PackagedShaderInterface(PackagedShaderInterface.CurrentVersion, "test-compiler", ShaderStageKind.Vertex,
            "main", selection.Key, "Release", new([], [], []));
        var manifest = new ShaderBinaryDigest.Manifest(2, new()
        {
            [selection.BinaryPath] = new(binary.Length, Convert.ToHexString(SHA256.HashData(binary)), metadata)
        });
        return (selection, binary, manifest);
    }
    #endregion
}
