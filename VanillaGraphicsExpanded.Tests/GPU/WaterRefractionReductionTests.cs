using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks geometry-aware reduction retains exact color, depth and original texel coordinates.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionReductionTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Rejects malformed candidates without poisoning selection, preserving first-valid ties and silhouettes.</summary>
    [Fact]
    public void CandidateValidityAndTieOrderPreserveExactReceiver()
    {
        EnsureShaderTestAvailable();
        var cases=new List<(float[] Depth,float[] Red,float[] Alpha,int Selected)>();
        foreach(float invalid in new[] {float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,0f,.999999f,1f})
            cases.Add(([.25f,.5f,.75f,invalid],[10,11,12,13],[1,1,1,1],2));
        foreach(float invalid in new[] {float.NaN,float.PositiveInfinity,float.NegativeInfinity,65520f})
            cases.Add(([.25f,.5f,.75f,.875f],[10,11,12,invalid],[1,1,1,1],2));
        foreach(float invalid in new[] {0f,.49f,float.NaN,float.PositiveInfinity})
            cases.Add(([.25f,.5f,.75f,.875f],[10,11,12,13],[1,1,1,invalid],2));
        cases.Add(([.5f,.75f,.75f,.75f],[10,11,12,13],[1,1,1,1],1));
        cases.Add(([.75f,.75f,.75f,.75f],[10,11,12,13],[1,1,1,1],0));
        cases.Add(([.9f,.7f,.5f,.2f],[10,11,12,13],[1,1,1,1],0));
        cases.Add(([.2f,.5f,.7f,.9f],[10,11,12,13],[1,1,1,1],3));
        cases.Add(([.2f,.5f,.7f,.9f],[10,11,12,13],[1,1,1,.5f],3));
        cases.Add(([.75f,.75f,.75f,.75f],[float.NaN,11,12,13],[1,1,1,1],1));
        cases.Add(([.25f,.5f,.9f,.8f],[10,11,float.NaN,13],[1,1,1,1],3));
        cases.Add(([.9f,.1f,.1f,.1f],[10,11,12,13],[1,1,1,1],0));
        cases.Add(([0,0,0,0],[10,11,12,13],[1,1,1,1],-1));
        int width=cases.Count*2;
        var colors=new float[width*2*4]; var depths=new float[width*2];
        for(int scenario=0;scenario<cases.Count;scenario++)
        for(int tap=0;tap<4;tap++)
        {
            int pixel=(tap/2)*width+scenario*2+tap%2;
            depths[pixel]=cases[scenario].Depth[tap];
            colors[pixel*4]=cases[scenario].Red[tap];
            colors[pixel*4+1]=2; colors[pixel*4+2]=1; colors[pixel*4+3]=cases[scenario].Alpha[tap];
        }
        using var color=DynamicTexture2D.Create(width,2,PixelInternalFormat.Rgba32f);
        using var depth=DynamicTexture2D.Create(width,2,PixelInternalFormat.R32f);
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        var program=Programs.Create<WaterRefractionReductionShaderProgram>();
        program.SourceColor=color; program.SourceDepth=depth;
        using var target=CreateMRTRenderTarget(cases.Count,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program,target);
        float[] actualColor=target[0].ReadPixels(),actualDepth=target[1].ReadPixels();
        for(int scenario=0;scenario<cases.Count;scenario++)
        {
            int selected=cases[scenario].Selected,offset=scenario*4;
            float[] expectedColor=[0,0,0,0],expectedDepth=[1,0,0,0];
            if(selected>=0)
            {
                expectedColor=[cases[scenario].Red[selected],2,1,cases[scenario].Alpha[selected]];
                expectedDepth=[cases[scenario].Depth[selected],(scenario*2+selected%2+.5f)/width,(selected/2+.5f)/2,1];
            }
            Assert.Equal(expectedColor,actualColor.AsSpan(offset,4).ToArray());
            Assert.Equal(expectedDepth[0],actualDepth[offset]);
            Assert.Equal(expectedDepth[3],actualDepth[offset+3]);
            for(int coordinate=1;coordinate<=2;coordinate++)
                Assert.InRange(MathF.Abs(expectedDepth[coordinate]-actualDepth[offset+coordinate]),0,1e-6f);
            output.WriteLine($"reduction-case={scenario} color={string.Join(',',actualColor.AsSpan(offset,4).ToArray())} depth={string.Join(',',actualDepth.AsSpan(offset,4).ToArray())}");
        }
        if(Environment.GetEnvironmentVariable("VGE_MEASURE_WATER_REDUCTION")=="1")
        {
            // Query only repeated draws after publication and activation, using
            // the same triangle, target formats and source footprint per binary.
            const int size=512;
            using var measuredColor=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba16f);
            using var measuredDepth=DynamicTexture2D.Create(size,size,PixelInternalFormat.R32f);
            using var measuredTarget=CreateMRTRenderTarget(size/2,size/2,PixelInternalFormat.Rgba16f,PixelInternalFormat.Rgba32f);
            measuredColor.UploadDataImmediate(Enumerable.Range(0,size*size).SelectMany(_=>new[] {8f,2f,1f,1f}).ToArray());
            program.SourceColor=measuredColor; program.SourceDepth=measuredDepth;
            using var vao=GpuVao.Create("Test.WaterReduction.Measurement");
            using var vertices=GpuVbo.Create(debugName:"Test.WaterReduction.Vertices");
            using var indices=GpuEbo.Create(debugName:"Test.WaterReduction.Indices");
            using var geometry=vao.BindScope();
            indices.UploadIndices(new uint[] {0,1,2});
            using(vertices.BindScope())
            {
                vertices.UploadData(new float[] {-1,-1,0,3,-1,0,-1,3,0});
                vao.AttribPointer(0,3,VertexAttribPointerType.Float,false,12,0);
            }
            output.WriteLine($"reduction-device renderer={GL.GetString(StringName.Renderer)} version={GL.GetString(StringName.Version)}");
            foreach(string workload in new[] {"invalid","descending","ascending","mixed"})
            {
                var measuredDepths=new float[size*size];
                for(int pixel=0;pixel<measuredDepths.Length;pixel++)
                {
                    int x=pixel%size,y=pixel/size,tap=(x&1)+2*(y&1);
                    measuredDepths[pixel]=workload=="invalid"?1:workload=="descending"?.8f-.2f*tap
                        :workload=="ascending"?.2f+.2f*tap:((x/2+y/2)&1)==0?1:tap==0?.9f:.1f;
                }
                measuredDepth.UploadDataImmediate(measuredDepths);
                TestFramework.RenderQuadTo(program,measuredTarget);
                measuredTarget.BindWithViewport();
                using var activation=program.UseScope();
                using var measuredGeometry=vao.BindScope();
                for(int sample=-5;sample<10;sample++)
                {
                    using var elapsed=GpuTimerQuery.Create();
                    elapsed.Begin();
                    for(int draw=0;draw<128;draw++) vao.DrawElements(PrimitiveType.Triangles,indices);
                    elapsed.End();
                    double milliseconds=elapsed.GetResultNanoseconds()/1e6;
                    if(sample>=0) output.WriteLine($"reduction-cost workload={workload} sample={sample} gpuMs={milliseconds:R} source=512x512 draws=128");
                }
                float[] validity=measuredTarget[1].ReadPixels();
                int valid=Enumerable.Range(0,size*size/4).Count(pixel=>validity[pixel*4+3]==1);
                Assert.Equal(workload=="invalid"?0:workload=="mixed"?size*size/8:size*size/4,valid);
            }
        }
    }

    /// <summary>Reduction rejects float32 radiance that cannot survive its RGBA16F destination.</summary>
    [Theory]
    [InlineData(65504f, true)]
    [InlineData(65520f, false)]
    public void ReductionPreservesHalfFloatRepresentability(float radiance, bool accepted)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(2, 2, PixelInternalFormat.R32f);
        color.UploadDataImmediate([8,2,1,1, 8,2,1,1, 8,2,1,1, radiance,2,1,1]);
        depth.UploadDataImmediate([.25f,.25f,.25f,.75f]);
        var program = Programs.Create<WaterRefractionReductionShaderProgram>();
        program.SourceColor = color; program.SourceDepth = depth;
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] selected = target[0].ReadPixels(), provenance = target[1].ReadPixels();
        Assert.Equal(accepted ? radiance : 8, selected[0]);
        Assert.Equal(accepted ? .75f : .25f, provenance[0]);
        Assert.Equal(1, selected[3]);
        Assert.Equal(1, provenance[3]);
        Assert.All(selected, value => Assert.True(float.IsFinite(value)));
    }

    /// <summary>Each reduced footprint selects the farthest eligible receiver, including incomplete odd edges.</summary>
    [Theory]
    [InlineData(2, 2, false)]
    [InlineData(5, 3, false)]
    [InlineData(5, 3, true)]
    public void ReductionPreservesAssociatedReceiver(int width, int height, bool invalid)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(width, height, PixelInternalFormat.R32f);
        float[] colors = new float[width * height * 4];
        float[] depths = new float[width * height];
        for (int i = 0; i < depths.Length; i++)
        {
            depths[i] = .1f + i * .04f;
            colors[i * 4] = 4 + i; colors[i * 4 + 1] = 2 + i; colors[i * 4 + 2] = .5f;
            colors[i * 4 + 3] = invalid ? 0 : 1;
        }
        // Bright unsupported taps must never contaminate a valid neighboring receiver.
        depths[0] = float.NaN;
        depths[1] = 1;
        if (width > 2) colors[(width + 1) * 4 + 3] = 0;
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        var program = Programs.Create<WaterRefractionReductionShaderProgram>();
        program.SourceColor = color; program.SourceDepth = depth;
        int reducedWidth = (width + 1) / 2, reducedHeight = (height + 1) / 2;
        using var target = CreateMRTRenderTarget(reducedWidth, reducedHeight, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] actualColor = target[0].ReadPixels(), actualDepth = target[1].ReadPixels();
        for (int y = 0; y < reducedHeight; y++)
        for (int x = 0; x < reducedWidth; x++)
        {
            int selected = -1;
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int sx = x * 2 + dx, sy = y * 2 + dy;
                if (sx >= width || sy >= height) continue;
                int candidate = sy * width + sx;
                if (!float.IsFinite(depths[candidate]) || depths[candidate] <= 0 || depths[candidate] >= .999999f
                    || colors[candidate * 4 + 3] < .5f) continue;
                if (selected < 0 || depths[candidate] > depths[selected]) selected = candidate;
            }
            int destination = (y * reducedWidth + x) * 4;
            if (selected < 0)
            {
                Assert.Equal(0, actualColor[destination + 3]);
                Assert.Equal(0, actualDepth[destination + 3]);
                continue;
            }
            for (int channel = 0; channel < 4; channel++) Assert.Equal(colors[selected * 4 + channel], actualColor[destination + channel]);
            Assert.Equal(depths[selected], actualDepth[destination]);
            Assert.InRange(MathF.Abs(actualDepth[destination + 1] - (selected % width + .5f) / width), 0, 1e-6f);
            Assert.InRange(MathF.Abs(actualDepth[destination + 2] - (selected / width + .5f) / height), 0, 1e-6f);
            Assert.Equal(1, actualDepth[destination + 3]);
        }
    }
    #endregion
}
