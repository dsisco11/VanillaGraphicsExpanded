using Newtonsoft.Json.Linq;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.PBR.Materials;

/// <summary>Checks displacement fields in the schema artifact consumed by the built test project.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialDisplacementSchemaTests
{
    #region Public API
    /// <summary>The built schema admits displacement on materials and excludes BRDF and mapping overrides.</summary>
    [Fact]
    public void BuiltSchemaKeepsDisplacementOnMaterialDefinitions()
    {
        // Arrange: select the schema artifact emitted for the test build.
        const string schemaPath = "schemas/pbr_material_definitions.schema.json";

        // Act: read it through the build manifest-backed test asset snapshot.
        JObject schema = JObject.Parse(TestAssetSnapshot.Built.Text(schemaPath));
        JToken definitions = schema["$defs"]!;

        // Assert: the emitted artifact keeps the geometric field on material definitions with bounded units.
        Assert.NotNull(definitions["material"]!["properties"]!["displacement"]);
        Assert.Null(definitions["BRDFProperties"]!["properties"]!["displacement"]);
        Assert.Null(definitions["mappingOverrides"]!["properties"]!["displacement"]);
        Assert.Equal(0f, (float)definitions["displacement"]!["properties"]!["amplitudeMetres"]!["minimum"]!);
        Assert.Equal(.05f, (float)definitions["displacement"]!["properties"]!["amplitudeMetres"]!["maximum"]!);
    }
    #endregion
}
