using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns ready, resource-free executable generations for native state-policy tests.</summary>
internal sealed class GraphicsStateShaders : IDisposable
{
    private readonly Dictionary<(nint Handle, long Generation, string Stages), RHIShader> shaders = new();

    #region Internal API
    /// <summary>Reuses an exact stage set within its originating context while keeping ownership in the test.</summary>
    internal RHIShaderRef Get(IEnumerable<ShaderStageKind>? extraStages = null)
    {
        var kinds = new[] { ShaderStageKind.Vertex, ShaderStageKind.Fragment }.Concat(extraStages ?? []).Distinct().Order().ToArray();
        var origin = RenderContextRegistry.Current();
        var key = (origin.Handle, origin.Generation, string.Join(",", kinds));
        if (shaders.TryGetValue(key, out var existing)) return existing;
        // Stage bodies deliberately have no resource bindings: state tests apply policy without executing these shaders.
        var stages = kinds.Select(kind => kind switch
        {
            ShaderStageKind.Vertex => new RHIShaderStageDescription(ShaderStage.Vertex, "#version 450\nvoid main(){gl_Position=vec4(0,0,0,1);}"),
            ShaderStageKind.Fragment => new RHIShaderStageDescription(ShaderStage.Fragment, "#version 450\nlayout(location=0)out vec4 color;void main(){color=vec4(1);}"),
            ShaderStageKind.Geometry => new RHIShaderStageDescription(ShaderStage.Geometry, "#version 450\nlayout(triangles)in;layout(triangle_strip,max_vertices=3)out;void main(){for(int i=0;i<3;i++){gl_Position=gl_in[i].gl_Position;EmitVertex();}EndPrimitive();}"),
            ShaderStageKind.TessellationControl => new RHIShaderStageDescription(ShaderStage.TessellationControl, "#version 450\nlayout(vertices=3)out;void main(){gl_out[gl_InvocationID].gl_Position=gl_in[gl_InvocationID].gl_Position;if(gl_InvocationID==0){gl_TessLevelOuter[0]=1;gl_TessLevelOuter[1]=1;gl_TessLevelOuter[2]=1;gl_TessLevelInner[0]=1;}}"),
            ShaderStageKind.TessellationEvaluation => new RHIShaderStageDescription(ShaderStage.TessellationEvaluation, "#version 450\nlayout(triangles,equal_spacing,ccw)in;void main(){gl_Position=gl_TessCoord.x*gl_in[0].gl_Position+gl_TessCoord.y*gl_in[1].gl_Position+gl_TessCoord.z*gl_in[2].gl_Position;}"),
            _ => throw new ArgumentException("A graphics stage is required.", nameof(extraStages))
        }).ToArray();
        var created = RHICommandContext.Current.CreateShader(new(ShaderKind.Graphics, stages, new())).RequireResource();
        shaders.Add(key, created);
        return created;
    }
    #endregion

    #region Public API
    /// <summary>Retires each executable through its original native resource owner.</summary>
    public void Dispose()
    {
        foreach (var shader in shaders.Values.Reverse()) shader.Dispose();
        shaders.Clear();
    }
    #endregion
}
