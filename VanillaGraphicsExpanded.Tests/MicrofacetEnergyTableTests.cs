using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Qualifies the packaged directional-energy data against separate hemisphere integration and physical endpoints.</summary>
public sealed class MicrofacetEnergyTableTests
{
    #region Public API
    /// <summary>Normal-view coefficients agree with direct projected-hemisphere integration, not another table sampler.</summary>
    [Theory]
    [InlineData(.25f)]
    [InlineData(.5f)]
    [InlineData(.75f)]
    [InlineData(1f)]
    public void NormalEnergyMatchesIndependentQuadrature(float roughness)
    {
        var expected = MicrofacetEnergyReference.NormalEnergy(roughness);
        var actual = MicrofacetEnergyTable.Shared.Sample(roughness, 1);
        Assert.InRange(actual.X, expected.E * .9993, expected.E * 1.0007);
        Assert.InRange(Math.Abs(actual.Y-expected.Ef), 0, .000003);
    }

    /// <summary>Perfect reflectors conserve unit furnace energy and black normal reflectors retain their Schlick grazing term.</summary>
    [Theory]
    [InlineData(.04f, 0f)]
    [InlineData(.04f, .001f)]
    [InlineData(.5f, .5f)]
    [InlineData(1f, 1f)]
    public void ReflectanceEndpointsRemainPhysical(float roughness, float viewCosine)
    {
        Vector2 energy = MicrofacetEnergyTable.Shared.Sample(roughness, viewCosine);
        Vector3 white = MicrofacetEnergyTable.Reflectance(energy, Vector3.One);
        Vector3 black = MicrofacetEnergyTable.Reflectance(energy, Vector3.Zero);
        Assert.InRange(white.X, .999999f, 1);
        Assert.Equal(new Vector3(energy.Y), black);
        Vector3 colored = MicrofacetEnergyTable.Reflectance(energy, new(.04f, .5f, 1));
        Assert.True(colored.X <= colored.Y && colored.Y <= colored.Z);
        Assert.InRange(colored.X, 0, 1);
    }

    /// <summary>Out-of-domain finite materials clamp to declared endpoints; nonfinite coordinates are rejected.</summary>
    [Fact]
    public void SamplingAdmitsOnlyFiniteCoordinates()
    {
        var table = MicrofacetEnergyTable.Shared;
        Assert.Equal(table.Sample(.04f, 0), table.Sample(-1, -1));
        Assert.Equal(table.Sample(1, 1), table.Sample(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Sample(float.NaN, .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Sample(.5f, float.PositiveInfinity));
        Assert.Equal(128*128*2, table.Samples.Length);
    }

    /// <summary>The data owner rejects incomplete headers before exposing a partial lookup.</summary>
    [Fact]
    public void TruncatedAssetIsRejected()
    {
        using var input = new MemoryStream(new byte[4]);
        Assert.Throws<InvalidDataException>(() => new MicrofacetEnergyTable(input));
    }

    /// <summary>Invalid coefficients cannot be published to either CPU material adapters or GPU receivers.</summary>
    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(.24f, 0f)]
    [InlineData(1.01f, 0f)]
    [InlineData(.5f, -.001f)]
    [InlineData(.5f, .51f)]
    public void CorruptedCoefficientIsRejected(float e, float ef)
    {
        using var embedded = typeof(MicrofacetEnergyTable).Assembly.GetManifestResourceStream("VGE.MicrofacetEnergy")!;
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);
        byte[] bytes = copy.ToArray();
        BitConverter.GetBytes(e).CopyTo(bytes, 32);
        BitConverter.GetBytes(ef).CopyTo(bytes, 36);
        using var input = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => new MicrofacetEnergyTable(input));
    }

    /// <summary>Diffuse cache coefficients reserve projected-view-average reflection, without embedding a particular receiver view.</summary>
    [Theory]
    [InlineData(.04f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void CacheDiffuseUsesProjectedViewAverage(float roughness)
    {
        var table=MicrofacetEnergyTable.Shared;
        Vector3 albedo=new(.5f,.25f,.125f),f0=new(.04f,.5f,1f);
        var material=new PbrMaterialSurface(roughness,0,0,albedo,f0);
        Vector3 actual=table.DiffuseAlbedo(material);
        const int intervals=4096;
        Vector3 sum=Vector3.Zero;
        // Integrate a uniform cosine grid with Simpson weights, independently of the
        // runtime adapter's warped-node trapezoids and their nonuniform spacing.
        for(int index=0;index<=intervals;index++)
        {
            float mu=(float)index/intervals;
            float weight=index is 0 or intervals ? 1 : index%2==0 ? 2 : 4;
            sum+=MicrofacetEnergyTable.Reflectance(table.Sample(roughness,mu),f0)*(2*mu*weight);
        }
        Vector3 expected=albedo*(Vector3.One-sum/(3*intervals));
        for(int channel=0;channel<3;channel++) Assert.InRange(actual[channel],expected[channel]-.0001f,expected[channel]+.0001f);
        Assert.InRange(actual.Z,0,.000001f);
    }

    /// <summary>Texture generation preserves the physical CPU asset and produces admitted exactly representable binary16 nodes.</summary>
    [Theory]
    [InlineData(16)] [InlineData(32)] [InlineData(64)] [InlineData(128)]
    public void TexturePayloadIsHalfRoundedWithoutChangingPhysicalSource(int size)
    {
        var table=MicrofacetEnergyTable.Shared;
        var before=table.Samples.ToArray();
        var pixels=table.CreateTextureData(size);
        Assert.Equal(size*size*2,pixels.Length);
        for(int i=0;i<pixels.Length;i+=2)
        {
            Assert.InRange(pixels[i],.25f,1);
            Assert.InRange(pixels[i+1],0,pixels[i]);
            Assert.Equal(pixels[i],(float)(Half)pixels[i]);
            Assert.Equal(pixels[i+1],(float)(Half)pixels[i+1]);
        }
        Assert.Equal(before,table.Samples.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(()=>table.CreateTextureData(96));
    }

    /// <summary>Records lower-tier interpolation error independently of GPU filtering and retains the high-tier physical error contract.</summary>
    [Fact]
    public void TextureTiersConvergeTowardPhysicalSource()
    {
        double previousE=double.PositiveInfinity,previousEf=double.PositiveInfinity;
        foreach(int size in new[]{16,32,64,128})
        {
            double maximumE=0,maximumEf=0;
            // Offset coordinates avoid qualifying only the stored nodes or symmetric axes.
            for(int y=0;y<193;y++)for(int x=0;x<193;x++)
            {
                float u=(x+.37f)/193,v=(y+.61f)/193;
                float roughness=.04f+.96f*u*u,view=v*v*v*v;
                var physical=MicrofacetEnergyTable.Shared.Sample(roughness,view);
                var reduced=MicrofacetTextureReference.Sample(size,roughness,view).Value;
                maximumE=Math.Max(maximumE,Math.Abs(reduced.X/physical.X-1));
                maximumEf=Math.Max(maximumEf,Math.Abs(reduced.Y-physical.Y));
            }
            TestContext.Current.TestOutputHelper!.WriteLine($"tier={size} maximumRelativeE={maximumE:R} maximumAbsoluteEf={maximumEf:R}");
            Assert.True(maximumE<=previousE&&maximumEf<=previousEf,"Increasing the texture resolution must reduce worst-case source error.");
            // Lower tiers use explicit quality budgets qualified independently against
            // the retained high-sample integration corpus; the default keeps its original target.
            var budget=size switch{16=>(.03,.02),32=>(.01,.006),64=>(.003,.002),_=>(.005,.0005)};
            Assert.InRange(maximumE,0,budget.Item1);Assert.InRange(maximumEf,0,budget.Item2);
            previousE=maximumE;previousEf=maximumEf;
        }
    }
    #endregion
}
