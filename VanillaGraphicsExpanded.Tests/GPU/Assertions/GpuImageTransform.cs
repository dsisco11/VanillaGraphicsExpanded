using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Authors test history inputs from completed GPU images without downloading and re-uploading their contents.</summary>
internal sealed class GpuImageTransform : IDisposable
{
    private readonly (nint Handle,long Generation) origin=RenderContextRegistry.Current();
    private readonly Dictionary<(int Kind,string Expression),GpuFixtureProgram> programs=[];
    private bool disposed;

    #region Public API
    /// <summary>Retires every fixture executable in its original rendering context.</summary>
    public void Dispose()
    {
        if(disposed)return;
        if(RenderContextRegistry.IsAlive(origin))RequireContext();
        foreach(var program in programs.Values)program.Dispose();
        programs.Clear();disposed=true;
    }
    #endregion

    #region Internal API
    /// <summary>Returns an independently owned RGBA32F projection of every original texel using a, coordinate, index and parameters.</summary>
    internal Texture3D Apply(GpuTexture image,string expression,Vector4 parameters=default,GpuTexture? second=null)
    {
        RequireContext();ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        using var source=new GpuImageSnapshot(image);
        using var reference=second is null?null:new GpuImageSnapshot(second);
        if(reference is not null && (reference.Kind!=source.Kind || reference.Texture.Width!=source.Texture.Width ||
            reference.Texture.Height!=source.Texture.Height || reference.Texture.Depth!=source.Texture.Depth))
            throw new ArgumentException("History projection controls must have corresponding floating domains.",nameof(second));
        using var state=new GpuAssertionState();
        var key=(source.Kind,expression);
        if(!programs.TryGetValue(key,out var program))
        {
            string sampler=source.Kind==1?"isampler2DArray":source.Kind==2?"usampler2DArray":"sampler2DArray";
            program=new GpuFixtureProgram(Source.Replace("/* SAMPLER */",sampler).Replace("/* EXPRESSION */",expression));
            programs.Add(key,program);
        }
        int width=source.Texture.Width,height=source.Texture.Height,depth=source.Texture.Depth;
        var output=RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        {
            Dimension=TextureDimension.TwoArray,Format=TextureFormat.Rgba32f,Width=width,Height=height,Depth=depth,
            MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest,Label="Tests.History.Projection"
        }).RequireResource();
        try
        {
            GL.UseProgram(program.Id);
            GL.ProgramUniform3(program.Id,0,width,height,depth);
            // A projection may ignore parameters, in which case the optimizer removes this uniform.
            int location=GL.GetUniformLocation(program.Id,"parameters");
            if(location>=0)GL.ProgramUniform4(program.Id,location,parameters.X,parameters.Y,parameters.Z,parameters.W);
            GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2DArray,source.Texture.TextureId);GL.BindSampler(0,0);
            GL.ActiveTexture(TextureUnit.Texture1);GL.BindTexture(TextureTarget.Texture2DArray,reference?.Texture.TextureId??source.Texture.TextureId);GL.BindSampler(1,0);
            GL.BindImageTexture(0,output.TextureId,0,true,0,TextureAccess.WriteOnly,SizedInternalFormat.Rgba32f);
            int count=checked(width*height*depth),groups=(count+63)/64,x=Math.Min(groups,65535);
            GL.DispatchCompute(x,(groups+x-1)/x,1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            return output;
        }
        catch { output.Dispose();throw; }
    }
    #endregion

    #region Private
    /// <summary>Rejects projections or cleanup through a disposed or foreign physical namespace.</summary>
    private void RequireContext()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!RenderContextRegistry.IsCurrentResourceContext(origin))throw new InvalidOperationException("History projection requires its original context.");
    }

    private const string Source="""
        #version 450 core
        layout(local_size_x=64)in;
        layout(binding=0)uniform /* SAMPLER */ sourceImage;
        layout(binding=1)uniform /* SAMPLER */ referenceImage;
        layout(rgba32f,binding=0)writeonly uniform image2DArray destinationImage;
        layout(location=0)uniform ivec3 extent;
        uniform vec4 parameters;
        /// Authors an explicit fixture input, independent of the shader algorithm under test.
        vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){ /* EXPRESSION */ }
        /// Copies the complete original domain, including the final partial workgroup.
        void main(){
            uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
            if(index>=uint(extent.x*extent.y*extent.z))return;
            ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
            imageStore(destinationImage,p,project(vec4(texelFetch(sourceImage,p,0)),vec4(texelFetch(referenceImage,p,0)),p,index));
        }
        """;
    #endregion
}
