using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks independent cloud sequence storage, deterministic uploads and GPU retirement.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudPmjJitterTextureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The uploaded RG16 sequence matches CPU generation and survives stable allocation reuse.</summary>
    [Fact]
    public void UploadMatchesSharedGeneratorAndRetiresStorage()
    {
        EnsureContextValid();
        var config=new PmjConfig(CloudPmjJitterTexture.CycleLength,Seed:0x434C4F55u,
            Variant:PmjVariant.Pmj02,OutputKind:PmjOutputKind.Vector2F32,OwenScramble:true);
        ushort[] expected=PmjConversions.ToRg16UNormInterleaved(PmjGenerator.Generate(config));
        using var texture=CloudPmjJitterTexture.Create();int id=texture.TextureId;
        Assert.Equal(PixelInternalFormat.Rg16,texture.InternalFormat);
        Assert.Equal(128,texture.Width);Assert.Equal(1,texture.Height);
        ushort[] actual=new ushort[expected.Length];
        GL.GetInteger(GetPName.TextureBinding2D,out int previous);
        GL.BindTexture(TextureTarget.Texture2D,id);
        GL.GetTexImage(TextureTarget.Texture2D,0,PixelFormat.Rg,PixelType.UnsignedShort,actual);
        GL.BindTexture(TextureTarget.Texture2D,previous);
        Assert.Equal(expected,actual);
        Assert.Contains(Enumerable.Range(0,128),i=>actual[i*2]!=actual[i*2+1]);
        texture.EnsureCreated();Assert.Equal(id,texture.TextureId);
        texture.Dispose();Assert.False(GL.IsTexture(id));Assert.False(texture.IsValid);
        Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        using var replacement=CloudPmjJitterTexture.Create();Assert.True(replacement.IsValid);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Reconstruction reset retires its owned jitter allocation and permits a fresh owner afterward.</summary>
    [Fact]
    public void ReconstructionResetRetiresOwnedJitter()
    {
        EnsureContextValid();
        using var reconstruction=new CloudVolumeReconstruction(Moq.Mock.Of<Vintagestory.API.Client.ICoreClientAPI>());
        var field=typeof(CloudVolumeReconstruction).GetField("pmjJitter",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        using var texture=CloudPmjJitterTexture.Create();int id=texture.TextureId;
        field.SetValue(reconstruction,texture);
        reconstruction.Reset();Assert.Null(field.GetValue(reconstruction));Assert.False(GL.IsTexture(id));Assert.True(texture.IsDisposed);
        using var replacement=CloudPmjJitterTexture.Create();int replacementId=replacement.TextureId;
        field.SetValue(reconstruction,replacement);
        reconstruction.Dispose();Assert.False(GL.IsTexture(replacementId));Assert.True(replacement.IsDisposed);
    }
    #endregion
}
