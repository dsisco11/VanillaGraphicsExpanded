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
    private bool disposed;

    #region Public API
    /// <summary>Closes this projection consumer while its fixture retains the production executable owner.</summary>
    public void Dispose()
    {
        if(disposed)return;
        if(RenderContextRegistry.IsAlive(origin))RequireContext();
        disposed=true;
    }
    #endregion

    #region Internal API
    /// <summary>Returns an independently owned RGBA32F projection of every original texel using a, coordinate, index and parameters.</summary>
    internal Texture3D Apply(GpuTexture image,GpuImageProjection expression,Vector4 parameters=default,GpuTexture? second=null)
    {
        RequireContext();
        if (expression == GpuImageProjection.None || !Enum.IsDefined(expression))
            throw new ArgumentOutOfRangeException(nameof(expression), "Select a declared independent projection.");
        using var source=new GpuImageSnapshot(image);
        using var reference=second is null?null:new GpuImageSnapshot(second);
        if(reference is not null && (reference.Kind!=source.Kind || reference.Texture.Width!=source.Texture.Width ||
            reference.Texture.Height!=source.Texture.Height || reference.Texture.Depth!=source.Texture.Depth))
            throw new ArgumentException("History projection controls must have corresponding floating domains.",nameof(second));
        using var state=new GpuAssertionState();
        var program=Fixtures.HeadlessGLFixture.Current.ProjectionProgram(source.Kind);
        int width=source.Texture.Width,height=source.Texture.Height,depth=source.Texture.Depth;
        var output=RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        {
            Dimension=TextureDimension.TwoArray,Format=TextureFormat.Rgba32f,Width=width,Height=height,Depth=depth,
            MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest,Label="Tests.History.Projection"
        }).RequireResource();
        try
        {
            byte[] bytes=new byte[160];
            GpuAssertionInputs.Integer(bytes,0,(uint)width);GpuAssertionInputs.Integer(bytes,4,(uint)height);
            GpuAssertionInputs.Integer(bytes,8,(uint)depth);GpuAssertionInputs.Integer(bytes,92,(uint)expression);
            GpuAssertionInputs.Vector(bytes,64,parameters);
            int count=checked(width*height*depth),groups=(count+63)/64,x=Math.Min(groups,65535);
            program.Execute(bytes,[source.Texture,reference?.Texture??source.Texture],[output],x,(groups+x-1)/x);
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image|ResourceAccess.TextureUpdate|ResourceAccess.TextureFetch));
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

    #endregion
}
