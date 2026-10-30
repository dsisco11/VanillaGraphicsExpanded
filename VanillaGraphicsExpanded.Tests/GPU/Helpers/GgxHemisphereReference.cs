using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Integrates the documented GGX model over light directions without production samples, tables or GPU observations.</summary>
internal static class GgxHemisphereReference
{
    private static readonly (double[] Nodes,double[] Weights) DirectionRule=GaussLegendre(256);
    private static readonly (double[] Nodes,double[] Weights) AverageRule=GaussLegendre(64);
    private static readonly double[] AzimuthCosines=DirectionRule.Nodes.Select(node=>Math.Cos(Math.PI*(node+1)/2)).ToArray();
    private static readonly double DefaultDiffuseFraction=AverageDiffuseFraction();

    #region Public API
    /// <summary>Returns compensated directional reflectance for perceptual roughness, view cosine and normal-incidence reflectance.</summary>
    internal static double Reflection(double roughness,double viewCosine,double f0)
    {
        // The normal-view azimuth integral is analytic; its log-slope reference also resolves minimum roughness.
        if(viewCosine>=1)return MicrofacetEnergyReference.NormalResponse(roughness,f0).Reflection;
        var (energy,fresnel)=Energy(Math.Clamp(roughness,.04,1),Math.Clamp(viewCosine,1e-6,1));
        double gain=1+f0*(1-energy)/energy;
        return Math.Clamp(gain*(energy*f0+fresnel*(1-f0)),0,1);
    }

    /// <summary>Predicts the RGBA8 diffuse material channel for the fixture's roughness-.5, dielectric-.04 surface.</summary>
    internal static float CapturedDiffuse(float baseColor)=>
        (float)(Math.Round(Math.Clamp(baseColor,0,1)*DefaultDiffuseFraction*255)/255);

    /// <summary>Predicts each captured diffuse channel independently from the authored base color.</summary>
    internal static Vector3 CapturedDiffuse(Vector3 baseColor)=>
        new(CapturedDiffuse(baseColor.X),CapturedDiffuse(baseColor.Y),CapturedDiffuse(baseColor.Z));
    #endregion

    #region Private
    /// <summary>Integrates D times correlated Smith visibility and light cosine, plus the Schlick grazing coefficient.</summary>
    private static (double Energy,double Fresnel) Energy(double roughness,double mu)
    {
        double a2=Math.Pow(roughness,4),viewX=Math.Sqrt(1-mu*mu),energy=0,fresnel=0;
        // Split the light-cosine interval at the mirror direction. Gaussian endpoint clustering
        // resolves the narrow specular lobe without sharing the production visible-normal sampler.
        Integrate(0,mu);Integrate(mu,1);
        return (energy,fresnel);

        /// <summary>Accumulates one light-cosine interval over the symmetric half-azimuth domain.</summary>
        void Integrate(double lower,double upper)
        {
            if(lower==upper)return;
            for(int i=0;i<DirectionRule.Nodes.Length;i++)
            {
                double nl=lower+(upper-lower)*(DirectionRule.Nodes[i]+1)/2;
                double lightX=Math.Sqrt(1-nl*nl);
                double visibility=.5/(nl*Math.Sqrt(a2+(1-a2)*mu*mu)+mu*Math.Sqrt(a2+(1-a2)*nl*nl));
                double weight=DirectionRule.Weights[i]*(upper-lower)/2;
                for(int j=0;j<DirectionRule.Nodes.Length;j++)
                {
                    double vl=viewX*lightX*AzimuthCosines[j]+mu*nl;
                    double length=Math.Sqrt(2+2*vl),nh=(mu+nl)/length,vh=(1+vl)/length;
                    double denominator=1+(a2-1)*nh*nh;
                    double distribution=a2/(Math.PI*denominator*denominator);
                    double contribution=distribution*visibility*nl*weight*DirectionRule.Weights[j]*Math.PI;
                    energy+=contribution;
                    fresnel+=contribution*Math.Pow(1-vh,5);
                }
            }
        }
    }

    /// <summary>Reserves the projected-view-average reflection budget before any diffuse color is quantized.</summary>
    private static double AverageDiffuseFraction()
    {
        double reflection=0;
        for(int i=0;i<AverageRule.Nodes.Length;i++)
        {
            double mu=(AverageRule.Nodes[i]+1)/2;
            // The normalized projected measure is 2*mu*dmu; mapping [-1,1] cancels the factor two.
            reflection+=AverageRule.Weights[i]*mu*Reflection(.5,mu,.04);
        }
        return 1-reflection;
    }

    /// <summary>Constructs a deterministic Gauss-Legendre rule by refining each Legendre root in double precision.</summary>
    private static (double[] Nodes,double[] Weights) GaussLegendre(int count)
    {
        var nodes=new double[count];var weights=new double[count];
        for(int i=0;i<count;i++)
        {
            double root=Math.Cos(Math.PI*(i+.75)/(count+.5)),derivative=0;
            for(int iteration=0;iteration<32;iteration++)
            {
                double current=1,previous=0;
                for(int degree=1;degree<=count;degree++)
                {
                    double older=previous;previous=current;
                    current=((2*degree-1)*root*previous-(degree-1)*older)/degree;
                }
                derivative=count*(root*current-previous)/(root*root-1);
                double change=current/derivative;root-=change;
                if(Math.Abs(change)<1e-15)break;
            }
            nodes[i]=root;weights[i]=2/((1-root*root)*derivative*derivative);
        }
        return (nodes,weights);
    }
    #endregion
}
