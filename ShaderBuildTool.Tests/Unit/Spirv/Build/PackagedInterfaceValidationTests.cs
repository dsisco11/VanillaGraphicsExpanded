using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests.Unit.Spirv.Build;

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
        Assert.Equal(new(false, 0), result.ClipInput);
        Assert.Equal(new(false, 0), result.ClipOutput);
    }

    /// <summary>Older or incomplete serialized payloads cannot omit required compiler activity facts.</summary>
    [Theory]
    [InlineData("ClipInput", "Active")]
    [InlineData("ClipInput", "DeclaredExtent")]
    [InlineData("ClipOutput", "Active")]
    [InlineData("ClipOutput", "DeclaredExtent")]
    public void RejectsSerializedClipMetadataMissingRequiredFields(string direction, string property)
    {
        // Arrange: serialize a valid manifest, then remove a required member from its clip facts.
        var fixture = Fixture(1);
        var json = JsonNode.Parse(ShaderBinaryDigest.Encode(fixture.Manifest.Binaries))!;
        json["Binaries"]![fixture.Stage.BinaryPath]!["Interface"]!["Interface"]![direction]!.AsObject().Remove(property);

        // Act: parse the incomplete package metadata.
        var parsed = ShaderBinaryDigest.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));

        // Assert: the versioned metadata parser rejects missing required clip facts.
        Assert.Null(parsed);
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
    [InlineData("extractor")]
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
            "extractor" => metadata with { Extractor = "unknown-reflector" },
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
            ? [new((uint)Silk.NET.SPIRV.ExecutionMode.LocalSizeId, [1, 0, 3], true)] : [], new(false, 0), new(false, 0));
        fixture.Manifest.Binaries[fixture.Stage.BinaryPath] = entry with { Interface = entry.Interface! with { Interface = shape } };
        Assert.Throws<InvalidDataException>(() => PackagedInterfaceValidation.Resolve(fixture.Manifest, fixture.Stage, fixture.Binary));
    }

    /// <summary>Missing or impossible compiler clip facts cannot be mistaken for proven inactivity.</summary>
    [Theory]
    [InlineData("missing-input")]
    [InlineData("missing-output")]
    [InlineData("active-empty")]
    [InlineData("oversized")]
    public void RejectsMalformedClipDistanceFacts(string fault)
    {
        // Arrange: start with valid facts associated with the exact fixture binary.
        var fixture = Fixture(1);
        var entry = fixture.Manifest.Binaries[fixture.Stage.BinaryPath];
        var stage = entry.Interface!.Interface;
        var malformed = fault switch
        {
            "missing-input" => stage with { ClipInput = null! },
            "missing-output" => stage with { ClipOutput = null! },
            "active-empty" => stage with { ClipOutput = new(true, 0) },
            _ => stage with { ClipOutput = new(false, uint.MaxValue) }
        };
        fixture.Manifest.Binaries[fixture.Stage.BinaryPath] = entry with
        {
            Interface = entry.Interface with { Interface = malformed }
        };

        // Act: validate the metadata before executable preparation.
        // Assert: incomplete or unsupported compiler facts fail closed.
        Assert.Throws<InvalidDataException>(() => PackagedInterfaceValidation.Resolve(
            fixture.Manifest, fixture.Stage, fixture.Binary));
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
        var metadata = new PackagedShaderInterface(PackagedShaderInterface.CurrentVersion, PackagedShaderInterface.CurrentExtractor, ShaderStageKind.Vertex,
            "main", selection.Key, "Release", new([], [], [], new(false, 0), new(false, 0)));
        var manifest = new ShaderBinaryDigest.Manifest(2, new()
        {
            [selection.BinaryPath] = new(binary.Length, Convert.ToHexString(SHA256.HashData(binary)), metadata)
        });
        return (selection, binary, manifest);
    }
    #endregion
}
