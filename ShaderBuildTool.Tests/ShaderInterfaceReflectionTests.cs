using ShaderBuildTool.Spirv;
using Silk.NET.SPIRV;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises portable reflection against optimized binaries from the pinned compiler.</summary>
public sealed class ShaderInterfaceReflectionTests
{
    #region Public API
    /// <summary>Numeric declarations retain signedness, matrix columns, arrays and sparse output locations.</summary>
    [Fact]
    public async Task NumericShapesSurviveOptimization()
    {
        var result = await Compile("""
            #version 450 core
            layout(location=0) in ivec3 integerInput;
            layout(location=1) in uvec2 unsignedInput;
            layout(location=2) in mat3 matrixInput;
            layout(location=5) in vec4 arrayInput[2];
            layout(location=0) out vec4 first;
            layout(location=4) out vec4 sparse;
            void main() { gl_Position=vec4(matrixInput*vec3(integerInput),1)+arrayInput[0];
              first=arrayInput[1]; sparse=vec4(unsignedInput,0,1); }
            """, ShaderStageKind.Vertex, "vertex");
        var signed = Assert.Single(result.Inputs, value => value.Location == 0);
        Assert.Equal(ShaderScalarType.Int, signed.Scalar);
        Assert.Equal(3u, signed.VectorSize);
        Assert.Equal(ShaderScalarType.UInt, Assert.Single(result.Inputs, value => value.Location == 1).Scalar);
        var matrix = Assert.Single(result.Inputs, value => value.Location == 2);
        Assert.Equal(3u, matrix.Columns);
        Assert.Equal(3u, matrix.VectorSize);
        Assert.Equal(32u, matrix.BitWidth);
        Assert.Equal(new uint[] { 2 }, Assert.Single(result.Inputs, value => value.Location == 5).ArrayDimensions);
        Assert.Equal(new uint[] { 0, 4 }, result.Outputs.Select(value => value.Location).Order());
    }

    /// <summary>Fragment output index remains distinct from its numeric location.</summary>
    [Fact]
    public async Task DualSourceOutputIndicesArePreserved()
    {
        var result = await Compile("""
            #version 450 core
            layout(location=0,index=0) out vec4 primary;
            layout(location=0,index=1) out vec4 secondary;
            void main(){primary=vec4(1);secondary=vec4(0.5);}
            """, ShaderStageKind.Fragment, "fragment");
        Assert.Equal(new uint[] { 0, 1 }, result.Outputs.Select(value => value.Index).Order());
    }

    /// <summary>Geometry and tessellation modes are compiler facts independent of debug names.</summary>
    [Theory]
    [InlineData(ShaderStageKind.Geometry, "geometry", "layout(triangles) in; layout(triangle_strip,max_vertices=3) out; void main(){for(int i=0;i<3;i++){gl_Position=gl_in[i].gl_Position;EmitVertex();}EndPrimitive();}", ExecutionMode.Triangles)]
    [InlineData(ShaderStageKind.TessellationEvaluation, "tesseval", "layout(quads,equal_spacing,ccw) in; void main(){gl_Position=gl_in[0].gl_Position;}", ExecutionMode.Quads)]
    public async Task ExecutionModesArePreserved(object kind, string compilerStage, string body, ExecutionMode expected)
    {
        var result = await Compile("#version 450 core\n" + body, (ShaderStageKind)kind, compilerStage);
        Assert.Contains(result.ExecutionModes, mode => mode.Mode == (uint)expected && !mode.ArgumentsAreIds);
        if ((ShaderStageKind)kind == ShaderStageKind.Geometry)
        {
            Assert.Equal(new uint[] { 3, 0, 0 }, Assert.Single(result.ExecutionModes, mode => mode.Mode == (uint)ExecutionMode.OutputVertices).Arguments);
            Assert.All(result.ExecutionModes.Where(mode => mode.Mode == (uint)ExecutionMode.Invocations),
                mode => Assert.Equal(new uint[] { 1, 0, 0 }, mode.Arguments));
        }
    }

    /// <summary>Ordinary unbounded numeric specialization does not require a finite selection table.</summary>
    [Fact]
    public async Task OrdinarySpecializationRemainsUnrestricted()
    {
        var result = await Compile("""
            #version 450 core
            layout(constant_id=7) const float scale=1;
            layout(location=0) in vec3 position;
            void main(){gl_Position=vec4(position*scale,1);}
            """, ShaderStageKind.Vertex, "vertex");
        Assert.Equal(3u, Assert.Single(result.Inputs).VectorSize);
    }

    /// <summary>ID execution modes preserve binary-local IDs rather than claiming resolved dimensions.</summary>
    [Fact]
    public async Task SpecializationWorkgroupDimensionsAreTaggedAsIds()
    {
        var result = await Compile("""
            #version 450 core
            layout(local_size_x_id=7,local_size_y_id=8,local_size_z_id=9) in;
            layout(set=0,binding=0,std430) buffer Values {uint value;};
            void main(){value=gl_GlobalInvocationID.x;}
            """, ShaderStageKind.Compute, "compute", "vulkan1.3");
        var mode = Assert.Single(result.ExecutionModes, value => value.Mode == (uint)ExecutionMode.LocalSizeId);
        Assert.True(mode.ArgumentsAreIds);
        Assert.All(mode.Arguments, value => Assert.NotEqual(0u, value));
    }

    /// <summary>Unsupported interface blocks and unresolved extents fail explicitly.</summary>
    [Theory]
    [InlineData("layout(location=0) out Block {vec4 value;} outputBlock; void main(){gl_Position=vec4(0);outputBlock.value=vec4(1);}")]
    [InlineData("layout(constant_id=7) const int count=2; layout(location=0) out vec4 values[count]; void main(){gl_Position=vec4(0);for(int i=0;i<count;i++)values[i]=vec4(i);}")]
    public async Task RejectsUnsupportedShapes(string body)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Compile("#version 450 core\n" + body, ShaderStageKind.Vertex, "vertex"));
    }
    #endregion

    #region Private
    /// <summary>Compiles through the existing process owner and reflects the exact resulting binary.</summary>
    private static async Task<PackagedStageInterface> Compile(string source, ShaderStageKind kind, string compilerStage, string target = "opengl4.5")
    {
        using var fixture = new ShaderBuildFixture();
        string input = Path.Combine(fixture.Shaders, "reflection.glsl");
        string output = Path.Combine(fixture.Root, "reflection.spv");
        File.WriteAllText(input, source);
        var result = await ShaderCompilerProcess.CompileAsync(fixture.Repository, input, output, compilerStage,
            target, false, "main", TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var reflection = new ShaderInterfaceReflection(File.ReadAllBytes(output),
            new ShaderStageContract("reflection", "reflection.glsl", kind, new()));
        return reflection.Read();
    }
    #endregion
}

