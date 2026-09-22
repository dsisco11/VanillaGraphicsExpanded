using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks contract-driven source emission without inspecting compiler binaries.</summary>
public sealed class ShaderVariantSourceTests
{
    #region Typed emission
    /// <summary>Preprocessor Boolean values remain numeric while typed constants retain GLSL spelling and explicit IDs.</summary>
    [Fact]
    public void EmitsTypedMacrosAndActiveSpecializationDefaults()
    {
        var enabled = new ShaderOption<bool>("ENABLED", false, aliases: ["LEGACY_ENABLED"]);
        var mode = new ShaderOption<uint>("MODE", 1u, domain: [1u, 3u]);
        var steps = new ShaderOption<int>("STEPS", 10);
        var gain = new ShaderOption<float>("GAIN", 2f);
        var mask = new ShaderOption<uint>("MASK", uint.MaxValue);
        var toggle = new ShaderOption<bool>("TOGGLE", true);
        var stage = new ShaderStageContract("typed.csh", "typed.csh", ShaderStageKind.Compute, new(),
            [enabled, mode], [new(8, steps, ShaderCondition.Equal(enabled, true)), new(2, gain), new(9, mask), new(5, toggle)],
            new Dictionary<string, ShaderScalar> { ["FIXED_BOOL"] = ShaderScalar.From(true), ["FIXED_UINT"] = ShaderScalar.From(7u), ["FIXED_FLOAT"] = ShaderScalar.From(1.25f), ["VGE_SPIRV_BUILD"] = ShaderScalar.From(1) });
        var program = new GpuShaderContract("typed", [stage], 4, [enabled, mode, steps, gain, mask, toggle]);
        var resolver = new ShaderVariantResolver([program]);
        var active = resolver.Resolve(new ShaderSettings(program).With(enabled, true).With(mode, 3u).With(steps, 24))[0];
        string emitted = new ShaderVariantSource(".", "fixture").Emit("#version 330 core\nvoid main() {}", active);
        Assert.Contains("#version 450 core", emitted);
        Assert.Contains("#define ENABLED 1", emitted);
        Assert.Contains("#define MODE 3u", emitted);
        Assert.Contains("#define FIXED_BOOL 1", emitted);
        Assert.Contains("#define FIXED_UINT 7u", emitted);
        Assert.Contains("#define FIXED_FLOAT 1.25", emitted);
        Assert.Contains("layout(constant_id = 8) const int vgeSpecialization8 = 10;", emitted);
        Assert.Contains("layout(constant_id = 2) const float vgeSpecialization2 = 2.0;", emitted);
        Assert.Contains("layout(constant_id = 9) const uint vgeSpecialization9 = 4294967295u;", emitted);
        Assert.Contains("layout(constant_id = 5) const bool vgeSpecialization5 = true;", emitted);
        Assert.Contains("#define STEPS vgeSpecialization8", emitted);
        Assert.DoesNotContain("LEGACY_ENABLED", emitted);
        string inactive = new ShaderVariantSource(".", "fixture").Emit("#version 450 core\nvoid main() {}", resolver.Resolve(new ShaderSettings(program))[0]);
        Assert.Contains("#define ENABLED 0", inactive);
        Assert.DoesNotContain("vgeSpecialization8", inactive);
        Assert.DoesNotContain("#define STEPS", inactive);
    }

    /// <summary>Imported guarded defaults stay behind generated declarations and imports preserve dependency content.</summary>
    [Fact]
    public void ConfigurationPrecedesImportedGuardedDefaults()
    {
        using var assets = new SourceAssets();
        assets.Write("entry.csh", "#version 330 core\n@import \"guard.inc\"\nvoid main() { float value = FACTOR; }");
        assets.Write("guard.inc", "@import \"nested.inc\"\n#ifndef FACTOR\n#define FACTOR 0.25\n#endif\n");
        assets.Write("nested.inc", "// nested import retained\n#define IMPORTED 1\n");
        var factor = new ShaderOption<float>("FACTOR", 0.75f);
        var stage = new ShaderStageContract("entry.csh", "entry.csh", ShaderStageKind.Compute, new(), specializations: [new(3, factor)]);
        var program = new GpuShaderContract("entry", [stage], 1, [factor]);
        var emitter = new ShaderVariantSource(assets.Root, "fixture");
        string source = emitter.Emit(emitter.Expand("entry.csh"), new ShaderVariantResolver([program]).Binaries[0]);
        Assert.DoesNotContain("@import", source);
        Assert.Contains("nested import retained", source);
        Assert.Contains("#define FACTOR 0.25", source);
        Assert.True(source.IndexOf("#version", StringComparison.Ordinal) < source.IndexOf("#extension", StringComparison.Ordinal));
        Assert.True(source.IndexOf("#define FACTOR vgeSpecialization3", StringComparison.Ordinal) < source.IndexOf("#ifndef FACTOR", StringComparison.Ordinal));
        Assert.True(source.IndexOf("vgeSpecialization3 = 0.75", StringComparison.Ordinal) < source.IndexOf("float value", StringComparison.Ordinal));
    }

    /// <summary>Ambiguous or absent language versions fail before invoking the compiler.</summary>
    [Theory]
    [InlineData("void main() {}")]
    [InlineData("#version 450 core\n#version 450 core\nvoid main() {}")]
    public void InvalidVersionDirectivesFail(string source)
    {
        var stage = new ShaderStageContract("empty.csh", "empty.csh", ShaderStageKind.Compute, new());
        var selected = new ShaderVariantResolver([new GpuShaderContract("empty", [stage], 1)]).Binaries[0];
        Assert.Throws<InvalidOperationException>(() => new ShaderVariantSource(".", "fixture").Emit(source, selected));
    }
    #endregion

    #region Isolated source assets
    /// <summary>Owns a bounded source tree for production import expansion.</summary>
    private sealed class SourceAssets : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vge-source-emission-" + Guid.NewGuid().ToString("N"));
        /// <summary>Creates the owned domain source directory.</summary>
        public SourceAssets() => Directory.CreateDirectory(Path.Combine(Root, "fixture", "shaders"));
        /// <summary>Writes only test-specified files within the owned source directory.</summary>
        public void Write(string name, string text) => File.WriteAllText(Path.Combine(Root, "fixture", "shaders", name), text);
        /// <summary>Removes this fixture's uniquely allocated tree.</summary>
        public void Dispose() => Directory.Delete(Root, true);
    }
    #endregion
}
