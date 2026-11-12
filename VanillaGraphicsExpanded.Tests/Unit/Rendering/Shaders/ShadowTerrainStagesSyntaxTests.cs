using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Protects syntax-aware translation of owned terrain varyings around the shared tessellation generator.</summary>
public sealed class ShadowTerrainStagesSyntaxTests
{
    #region Public API
    /// <summary>Renames stage identifiers while retaining comments and structure member selectors.</summary>
    [Fact]
    public void SubdivisionPreservesCommentsAndStructureMembers()
    {
        // Arrange: owned interface identifiers overlap a structure field and appear as comments.
        const string vertex = """
            #version 450 core
            out vec4 vge_materialPosition;
            out vec3 vge_shadowNormal;
            out vec2 vge_shadowUv;
            flat out vec2 vge_uvBase;
            flat out vec2 vge_uvExtent;
            flat out int vge_shadowVertexFlags;
            void main() {
                vge_materialPosition = vec4(0.0, 0.0, 0.0, 1.0);
                vge_shadowNormal = vec3(0.0);
                vge_shadowUv = vec2(0.0);
                vge_shadowVertexFlags = 0;
            }
            """;
        const string template = """
            #version 450 core
            // vge_materialPosition vge_shadowNormal vge_shadowUv vge_shadowVertexFlags
            struct TerrainPayload { vec2 uv; vec3 normal; float renderFlags; };
            void main() {
                TerrainPayload payload;
                float preserved = payload.uv.x + payload.normal.x + payload.renderFlags;
            }
            """;
        var templates = new TerrainTessellationStages.Sources(
            template, template);

        // Act: translate through the shared generator and back to the owned shader interface.
        TerrainTessellationStages.Sources translated = ShadowTerrainStages.Subdivision(vertex, templates);

        // Assert: output interface symbols are owned again, while member selectors and comments are untouched.
        Assert.Contains("out vec2 tc_vge_shadowUv[]", translated.Control);
        Assert.Contains("payload.uv", translated.Control + translated.Evaluation);
        Assert.Contains("payload.normal", translated.Control + translated.Evaluation);
        Assert.Contains("payload.renderFlags", translated.Control + translated.Evaluation);
        foreach (string stage in new[] { translated.Control, translated.Evaluation })
        {
            Assert.Contains("vec2 uv;", stage);
            Assert.Contains("vec3 normal;", stage);
            Assert.Contains("float renderFlags;", stage);
        }
        Assert.Contains("// vge_materialPosition vge_shadowNormal vge_shadowUv vge_shadowVertexFlags", translated.Control);
        Assert.DoesNotContain("out vec2 uv[]", translated.Control);
        Assert.DoesNotContain("payload.vge_shadowUv", translated.Control + translated.Evaluation);
    }
    #endregion
}
