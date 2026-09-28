using System.Text.Json.Serialization;

namespace ShaderBuildTool.Spirv;

/// <summary>Generates JSON read metadata and write code for the complete file-hash index at build time.</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ShaderFileHashIndex.Index), TypeInfoPropertyName = "FileIndex")]
internal partial class ShaderFileHashJsonContext : JsonSerializerContext
{
}
