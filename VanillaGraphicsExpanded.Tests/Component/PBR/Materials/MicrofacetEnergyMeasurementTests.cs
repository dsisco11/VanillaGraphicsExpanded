using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.ModSystems;
using System.Globalization;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;

using VanillaGraphicsExpanded.Tests.Support.Assets;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Runs opt-in warm synthetic microfacet kernel timings; results do not represent live renderer cost.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class MicrofacetEnergyMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper log) : RenderTestBase(fixture)
{
    private readonly VanillaGraphicsExpanded.Tests.Support.Cache.CountingDiskCacheFileSystem reports = new(DiagnosticReports.Publish);
    #region Public API
    /// <summary>Compares matched warm baseline and compensated Cook-Torrance kernels at typical and repeated workloads.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void WarmLookupCost()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_MICROFACET_MEASUREMENTS")=="1",
            "Explicit microfacet measurement run required.");
        EnsureContextValid();
        // Arrange: use one authored kernel with two fixed variants and prepare all executable/input resources first.
        using var owners=new ComponentShaderPrograms();
        var programs=new[]
        {
            owners.Create<MicrofacetEnergyMeasurementProgram>(program=>program.Compensated=false,
                identity:"tests/microfacet-measurement-single"),
            owners.Create<MicrofacetEnergyMeasurementProgram>(program=>program.Compensated=true,
                identity:"tests/microfacet-measurement-compensated")
        };
        var context=RHICommandContext.Current;
        var layout=new RHIVertexLayout([new(0,2,VertexComponentFormat.Float,VertexInterpretation.Floating,0,0,2*sizeof(float))]);
        using var vertices=context.CreateVertexBuffer(new RHIBufferDescription
        { Role=BufferRole.Vertex,Mode=ResourceStorageMode.Mutable,Capacity=0,Usage=BufferUsage.StaticDraw,Label="Tests.MicrofacetMeasurement.Quad" }).RequireResource();
        vertices.UploadData<float>([
            -1,-1, 1,-1, 1,1,
            -1,-1, 1,1, -1,1
        ]);
        using var geometry=new ArrayGraphicsGeometry(layout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>{{0,vertices}});
        using var target=CreateRenderTarget(256,256,TextureFormat.Rgba32f);
        var pass=FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex:0,Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        var renderTargets=context.DescribeRenderTargets(pass);
        var pipelines=programs.Select(program=>context.CreateGraphicsPipeline(new(program.Shader,layout,renderTargets,
            DynamicPipelineState.Viewport)).RequireResource()).ToArray();
        var records=new List<object>();
        try
        {
            using var boundary=context.EnterBoundary(new("Tests.MicrofacetMeasurement",true,
                graphics:pipelines.Select(pipeline=>(RHIGraphicsPipelineRef)pipeline),
                operations:BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                foreach(int evaluations in new[]{1,100})
                for(int programIndex=0;programIndex<programs.Length;programIndex++)
                {
                    var program=programs[programIndex];
                    bool compensated=program.Compensated;
                    program.Capture(evaluations,MicrofacetEnergyLookup.Prepare(),GpuSamplers.LinearClamp);
                    var captured=program.CaptureBindings();
                    context.BeginRenderPass(pass);
                    context.SetPipeline(pipelines[programIndex]);
                    context.SetDynamicState(new(){Viewport=context.PassViewport});
                    context.BindResources(captured);

                    // Act: warm and time only the same draw call; typed resource preparation stays outside each query.
                    for(int warmup=0;warmup<3;warmup++)context.Draw(geometry.Inputs,new(0,6));
                    var elapsed=new List<double>();
                    for(int sample=0;sample<8;sample++)
                    {
                        using var timer=GpuTimerQuery.Create();
                        timer.Begin();
                        context.Draw(geometry.Inputs,new(0,6));
                        timer.End();
                        elapsed.Add(timer.GetResultNanoseconds()/1e6);
                    }
                    context.EndRenderPass();
                    double median=elapsed.Order().Skip(3).Take(2).Average();
                    log.WriteLine($"Microfacet synthetic kernel via prepared RHI draw: compensated={compensated} pixels=65536 evaluations={evaluations} medianGPUms={median:F6}; GPU={GL.GetString(StringName.Renderer)}");
                    records.Add(new{compensated,pixels=65536,evaluations,medianGpuMilliseconds=median,samplesMilliseconds=elapsed});
                }
            });
            string? path=Environment.GetEnvironmentVariable("VGE_MICROFACET_MEASUREMENT_OUTPUT");
            if(!string.IsNullOrEmpty(path))reports.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{
                renderer=GL.GetString(StringName.Renderer),lookupBytes=VanillaGraphicsExpanded.PBR.Materials.Energy.MicrofacetEnergyLookup.Prepare().Width
                    *VanillaGraphicsExpanded.PBR.Materials.Energy.MicrofacetEnergyLookup.Prepare().Height*4,records,
                limitation="Warm synthetic shader cost with varying roughness per evaluation, timed through a prepared RHI draw. These timings are not directly comparable with older measurements that included program and VAO selection and deselection; they are not live frame cost or measured physical-memory bandwidth."
            },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            // Assert: the captured warm workload completed without OpenGL errors.
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { foreach(var pipeline in pipelines)pipeline.Dispose(); }
    }

    /// <summary>Qualifies one batched default-quality hardware lookup against retained independently integrated physical references.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void DefaultTextureMatchesRetainedIndependentReferences()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_MICROFACET_QUALIFICATION")=="1",
            "Explicit retained-reference qualification required.");
        EnsureContextValid();
        string root=Environment.GetEnvironmentVariable("VGE_MICROFACET_REFERENCE_ROOT")!;
        var references=new[]{"generator-warped-corners.csv","generator-offgrid.csv"}
            .SelectMany(name=>TestAssetSnapshot.CaptureDirectory(root).Lines(name).Skip(1))
            .Select(line=>line.Split(',').Select(value=>double.Parse(value,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        Assert.Equal(360,references.Length);
        int oldQuality=ConfigModSystem.Config.ShadingQuality;
        ConfigModSystem.Config.ShadingQuality=3;
        try
        {
            // Arrange: stage every authored coordinate through the existing typed microfacet probe.
            var context=RHICommandContext.Current;
            var inputs=references.SelectMany(row=>new[]{(float)row[0],(float)row[1]}).ToArray();
            using var coordinates=context.CreateTexture2D(new RHITextureDescription
            { Dimension=TextureDimension.Two,Width=references.Length,Height=1,Format=TextureFormat.Rg32f,
                MipLevels=1,Storage=ResourceStorageMode.Mutable,BaseLevel=0,MaxLevel=0,
                Filter=TextureFilterMode.Nearest,Label=null },
                [new RHITextureInitialData(new(new(),0,0,0,references.Length,1),
                    new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rg32f),PixelComponentType.Float),
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(inputs.AsSpan()).ToArray())]).RequireResource();
            using var owners=new ComponentShaderPrograms();
            var program=owners.Create<MicrofacetEnergyProbeProgram>();
            program.EnergyLookup=MicrofacetEnergyLookup.Prepare();
            program.EnergySampler=GpuSamplers.LinearClamp;
            program.CaptureLookupBatch(coordinates,GpuSamplers.NearestClamp);

            var layout=new RHIVertexLayout([
                new(0,2,VertexComponentFormat.Float,VertexInterpretation.Floating,0,0,4*sizeof(float)),
                new(1,2,VertexComponentFormat.Float,VertexInterpretation.Floating,0,2*sizeof(float),4*sizeof(float))]);
            using var vertices=context.CreateVertexBuffer(new RHIBufferDescription
            { Role=BufferRole.Vertex,Mode=ResourceStorageMode.Mutable,Capacity=0,
                Usage=BufferUsage.StaticDraw,Label="Tests.MicrofacetEnergy.QualificationQuad" }).RequireResource();
            vertices.UploadData<float>([
                -1,-1,0,0, 1,-1,1,0, 1,1,1,1,
                -1,-1,0,0, 1,1,1,1, -1,1,0,1
            ]);
            using var geometry=new ArrayGraphicsGeometry(layout,PrimitiveTopology.Triangles,
                new Dictionary<int,GpuVbo>{{0,vertices}});
            using var target=CreateRenderTarget(references.Length,1,TextureFormat.Rgba32f);
            var pass=FramebufferTestSetup.DescribeAttachments(target,
                [new(AttachmentIndex:0,Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
            using var pipeline=context.CreateGraphicsPipeline(new(program.Shader,layout,context.DescribeRenderTargets(pass),
                DynamicPipelineState.Viewport)).RequireResource();
            using var boundary=context.EnterBoundary(new("Tests.MicrofacetEnergyLookupBatch",true,
                graphics:[pipeline],operations:BoundaryOperations.RenderPasses));
            // Act: evaluate the full authored coordinate row through the prepared production RHI draw.
            boundary.Run(() =>
            {
                context.BeginRenderPass(pass);
                context.SetPipeline(pipeline);
                context.SetDynamicState(new(){Viewport=context.PassViewport});
                context.BindResources(program.CaptureBindings());
                context.Draw(geometry.Inputs,new(0,6));
                context.EndRenderPass();
            });

            // Assert: compare the 360 production lookup results against retained independent references.
            var pixels=ReadPixelsFloat(target);
            double maxE=0,maxEf=0,maxW=0,maxR=0,idealMaxE=0,idealMaxEf=0;
            var cases=new List<object>();
            for(int i=0;i<references.Length;i++)
            {
                double e=references[i][2],ef=references[i][3],actualE=pixels[i*4],actualEf=pixels[i*4+1];
                var model=global::VanillaGraphicsExpanded.Tests.Support.Lighting.MicrofacetTextureReference.Sample(128,(float)references[i][0],(float)references[i][1]);
                idealMaxE=Math.Max(idealMaxE,Math.Abs(model.Value.X/e-1));
                idealMaxEf=Math.Max(idealMaxEf,Math.Abs(model.Value.Y-ef));
                // Keep mathematical resampling and hardware-filter precision separate.
                // Actual physical error may add only this coordinate's derived filter envelope.
                Assert.InRange(Math.Abs(actualE-model.Value.X),0,model.Error.X);
                Assert.InRange(Math.Abs(actualEf-model.Value.Y),0,model.Error.Y);
                Assert.InRange(Math.Abs(actualE/e-1),0,.005+model.Error.X/e);
                Assert.InRange(Math.Abs(actualEf-ef),0,.0005+model.Error.Y);
                maxE=Math.Max(maxE,Math.Abs(actualE/e-1));maxEf=Math.Max(maxEf,Math.Abs(actualEf-ef));
                foreach(double f0 in new[]{0,.02037,.04,.5,1})
                {
                    double w=1+f0*(1-e)/e,actualW=1+f0*(1-actualE)/actualE;
                    double r=w*(e*f0+ef*(1-f0)),actualR=actualW*(actualE*f0+actualEf*(1-f0));
                    maxW=Math.Max(maxW,Math.Abs(actualW/w-1));maxR=Math.Max(maxR,Math.Abs(actualR-r));
                }
                cases.Add(new{roughness=references[i][0],viewCosine=references[i][1],expectedE=e,expectedEf=ef,actualE,actualEf});
            }
            string path=Environment.GetEnvironmentVariable("VGE_MICROFACET_QUALIFICATION_OUTPUT")??Path.Combine(root,"hardware-texture-quality.json");
            reports.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{renderer=GL.GetString(StringName.Renderer),size=128,maxRelativeE=maxE,maxAbsoluteEf=maxEf,maxRelativeW=maxW,maxAbsoluteR=maxR,idealMaxE,idealMaxEf,
                errorPartition="Ideal half-quantized bilinear table retains .5% relative E/.0005 absolute Ef; actual hardware adds only per-coordinate half-output rounding, local filter-weight slopes and Float32 epsilon.",cases},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            log.WriteLine($"Actual default texture: maxRelativeE={maxE:R}, maxAbsoluteEf={maxEf:R}, maxRelativeW={maxW:R}, maxAbsoluteR={maxR:R}");
            Assert.InRange(idealMaxE,0,.005);Assert.InRange(idealMaxEf,0,.0005);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally
        {
            ConfigModSystem.Config.ShadingQuality=oldQuality;MicrofacetEnergyLookup.Prepare();
        }
    }
    #endregion
}
