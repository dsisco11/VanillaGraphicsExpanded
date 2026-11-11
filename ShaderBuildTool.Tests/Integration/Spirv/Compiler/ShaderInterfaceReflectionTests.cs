using ShaderBuildTool.Spirv;
using Silk.NET.SPIRV;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Integration.Spirv.Compiler;

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

    /// <summary>Selected-entry-point clip activity stays distinct from a declared array extent.</summary>
    [Fact]
    public async Task ClipDistanceActivityAndExtentAreReflectedSeparately()
    {
        // Arrange: one output is written, while the input declaration remains implicit and inactive.
        var active = await Compile("""
            #version 450 core
            layout(location=0) in vec3 position;
            out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[4]; };
            void main(){gl_Position=vec4(position,1);gl_ClipDistance[0]=1.0;gl_ClipDistance[1]=1.0;gl_ClipDistance[2]=1.0;gl_ClipDistance[3]=1.0;}
            """, ShaderStageKind.Vertex, "vertex");
        var unused = await Compile("""
            #version 450 core
            layout(location=0) in vec3 position;
            out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[4]; };
            void main(){gl_Position=vec4(position,1);}
            """, ShaderStageKind.Vertex, "vertex");

        // Act: reflection was performed on each optimized compiler output by Compile.

        // Assert: declaration shape alone never upgrades an inactive built-in to active.
        Assert.True(active.ClipOutput.Active);
        Assert.Equal(4u, active.ClipOutput.DeclaredExtent);
        Assert.False(active.ClipInput.Active);
        Assert.Equal(0u, active.ClipInput.DeclaredExtent);
        Assert.False(unused.ClipOutput.Active);
        Assert.Equal(4u, unused.ClipOutput.DeclaredExtent);
    }

    /// <summary>Compiler reflection reports active clip inputs separately from outputs in tessellation-control stages.</summary>
    [Fact]
    public async Task ActiveTessellationControlClipInputRetainsPlaneExtent()
    {
        // Arrange: a tessellation-control stage reads and forwards the per-vertex clip array.
        var result = await Compile("""
            #version 450 core
            layout(vertices=3) out;
            in gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[4]; } gl_in[];
            out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[4]; } gl_out[];
            void main() {
              gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
              for (int plane=0; plane<4; ++plane) gl_out[gl_InvocationID].gl_ClipDistance[plane] = gl_in[gl_InvocationID].gl_ClipDistance[plane];
              if (gl_InvocationID == 0) { gl_TessLevelInner[0]=1.0; gl_TessLevelInner[1]=1.0; gl_TessLevelOuter[0]=1.0; gl_TessLevelOuter[1]=1.0; gl_TessLevelOuter[2]=1.0; gl_TessLevelOuter[3]=1.0; }
            }
        """, ShaderStageKind.TessellationControl, "tesscontrol");

        // Act: reflect the optimized binary selected for this stage.

        // Assert: active per-vertex input and output each retain the declared clipping extent.
        Assert.True(result.ClipInput.Active);
        Assert.Equal(4u, result.ClipInput.DeclaredExtent);
        Assert.True(result.ClipOutput.Active);
        Assert.Equal(4u, result.ClipOutput.DeclaredExtent);
    }

    /// <summary>The compiler reflects the selected specialization-backed clip declaration as its concrete literal extent.</summary>
    [Fact]
    public async Task SpecializedClipDistanceExtentIsCompilerResolved()
    {
        // Arrange: compile a clip array written with a specialization constant in its source declaration.
        var result = await Compile("""
            #version 450 core
            layout(constant_id=7) const int ClipCount=4;
            out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[ClipCount]; };
            void main(){gl_Position=vec4(0,0,0,1);for(int plane=0;plane<ClipCount;++plane)gl_ClipDistance[plane]=1.0;}
            """, ShaderStageKind.Vertex, "vertex", "vulkan1.3");

        // Assert: compiler-owned reflection resolves the selected built-in interface to its concrete four-plane shape.
        Assert.True(result.ClipOutput.Active);
        Assert.Equal(4u, result.ClipOutput.DeclaredExtent);
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

