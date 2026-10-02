using System.Security.Cryptography;
using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Protects the approved retained-submission resource layout with exact stage fingerprints.</summary>
public sealed class ShaderBindingMigrationTests
{
    #region Public API
    /// <summary>Opaque resources have fixed units and no authored uniform-location metadata.</summary>
    [Fact]
    public void ProductionTextureBindingsDoNotCarryUniformLocations()
    {
        var redundant = GeneratedShaderCatalog.Programs("production")
            .SelectMany(program => program.Stages).DistinctBy(stage => stage.Identity)
            .SelectMany(stage => stage.Bindings.Samplers.Keys.Concat(stage.Bindings.Images.Keys)
                .Where(name => stage.Bindings.UniformLocations.ContainsKey(name))
                .Select(name => $"{stage.Identity}: {name}"))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Empty(redundant);
    }

    /// <summary>Indices, explicit locations, optional-resource policy and shared-stage layouts match the approved contract.</summary>
    [Fact]
    public void GeneratedBindingsMatchApprovedSubmissionLayouts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "project.todo"))) directory = directory.Parent;
        Assert.NotNull(directory);
        string path = Path.Combine(directory.FullName, "VanillaGraphicsExpanded.Tests", "Unit", "Rendering", "Contracts", "Fixtures", "ShaderBindingMigrationBaseline.txt");
        string[] expected = File.ReadAllLines(path);
        string[] actual = new[] { "production", "build-validation", "generator-fixture" }.SelectMany(scope =>
            GeneratedShaderCatalog.Programs(scope).SelectMany(p => p.Stages).DistinctBy(s => s.Identity)
                .Select(stage => scope + "|" + stage.Identity + "|" + Fingerprint(stage.Bindings)))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    /// <summary>All production binding metadata and imports are owned by interfaces after migration.</summary>
    [Fact]
    public void BindingDeclarationsAndImportsHaveOnlyInterfaceOwners()
    {
        var types = typeof(GpuBindingContract).Assembly.GetTypes();
        int declarations = 0;
        foreach (var type in types)
        {
            // Inspect declared properties only, so inherited API does not obscure the actual
            // attribute owner. Reflection is test evidence, never a runtime discovery path.
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (!property.CustomAttributes.Any(a => a.AttributeType == typeof(ShaderBindingAttribute))) continue;
                Assert.True(type.IsInterface, $"Binding metadata remains on class property {type.FullName}.{property.Name}.");
                declarations++;
            }
            foreach (var import in type.CustomAttributes.Where(a => a.AttributeType == typeof(ShaderBindingSetAttribute)))
            {
                var owner = Assert.IsAssignableFrom<Type>(import.ConstructorArguments[0].Value);
                Assert.True(owner.IsInterface, $"Binding import on {type.FullName} still references class {owner.FullName}.");
            }
        }
        Assert.True(declarations > 0);
    }
    #endregion

    #region Private
    /// <summary>Canonicalizes each independent namespace before hashing; declaration order cannot affect the baseline.</summary>
    private static string Fingerprint(GpuBindingContract bindings)
    {
        var rows = new List<string>();
        AddBindings("AtomicCounters", bindings.AtomicCounters);
        AddBindings("UniformBlocks", bindings.UniformBlocks);
        AddBindings("StorageBlocks", bindings.StorageBlocks);
        AddBindings("Samplers", bindings.Samplers);
        AddBindings("Images", bindings.Images);
        AddLocations("UniformLocations", bindings.UniformLocations);
        AddLocations("VaryingLocations", bindings.VaryingLocations);
        AddLocations("FragmentOutputLocations", bindings.FragmentOutputLocations);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Order(StringComparer.Ordinal)))));

        // Slot presence and Required are both observable runtime contracts, including resources
        // optimized out of a particular binary. Preserve them independently of linked reflection.
        /// <summary>Adds resource slots and required-input policy to the existing canonical fingerprint.</summary>
        void AddBindings(string kind, IDictionary<string, GpuBindingContract.Binding> values)
        {
            rows.AddRange(values.Select(p => kind + "|" + p.Key + "|" + p.Value.Slot + "|" + (p.Value.Required ? "true" : "false")));
        }
        /// <summary>Adds stable explicit interface indices to canonical fingerprint rows.</summary>
        void AddLocations(string kind, IDictionary<string, int> values)
        {
            rows.AddRange(values.Select(p => kind + "|" + p.Key + "|" + p.Value));
        }
    }
    #endregion
}
