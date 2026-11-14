using System;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Vintagestory.API.Client;
using ICoreAPI=Vintagestory.API.Common.ICoreAPI;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Owns early controlled frame execution and complete configuration generations through nonblocking retirement.</summary>
internal sealed class ShadowFrameRenderer:IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly Func<ShadowSettings,ShadowFrameInstance> create;
    private readonly Func<double> clock;
    private readonly Func<ShadowLightSnapshot?> lights;
    private readonly IDisposable? reloadRegistration;
    private readonly (nint Handle,long Generation) context;
    private ShadowSettings requested;
    private string identity;
    private ShadowSettings? pending;
    private ShadowFrameInstance? active;
    private GpuFence? retirement;
    private bool disposed,eligible;
    private long frame;
    private string? previousFailure;

    #region Public API
    /// <summary>Follows current light capture at -10000 and precedes participating world shading.</summary>
    public double RenderOrder=>-9999;
    /// <summary>Shadows follow the represented loaded domain rather than camera draw distance.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Exposes only a published configuration generation; lifecycle withdrawal fails closed synchronously.</summary>
    internal NativeShadowFrameGate? Current=>eligible?active?.Gate:null;

    /// <summary>Registers early frame execution and lifecycle withdrawal for the supplied configuration-generation factory.</summary>
    internal ShadowFrameRenderer(ICoreClientAPI api,Func<ShadowSettings,ShadowFrameInstance> create,ShadowSettings settings,
        Func<double> clock,Func<ShadowLightSnapshot?>? lights=null)
    {
        this.api=api;this.create=create;this.clock=clock;this.lights=lights??(()=>VgeLightsRenderer.ShadowInputs);
        requested=settings.Freeze();identity=JsonConvert.SerializeObject(requested);pending=requested;
        if(requested.Scheduling.ValidationDiagnostic is { } diagnostic)Report(diagnostic);
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"vge_shadow_readiness");
        api.Event.RegisterRenderer(this,EnumRenderStage.Done,"vge_shadow_surface_end");
        api.Event.LeaveWorld+=Dispose;LiveConfigReload.Applied+=OnConfigurationApplied;
        context=RenderContextRegistry.Current();RenderContextRegistry.Retiring+=OnContextRetiring;
        reloadRegistration=ClientRenderingLifetime.Find(api)?.RegisterPublication(RequestRecreation);
    }

    /// <summary>Queues one immutable requested policy; no settings are polled during ordinary frame execution.</summary>
    internal void ApplyPolicy(ShadowSettings settings)
    {
        var next=settings.Freeze();string key=JsonConvert.SerializeObject(next);
        if(next.Scheduling.ValidationDiagnostic is { } diagnostic)Report(diagnostic);
        if(key==identity)return;
        requested=next;identity=key;pending=next;eligible=false;
        active?.Gate.WithdrawPublication();
    }

    /// <summary>Drains changed generations, then runs demand and readiness before any participating consumer.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        if(disposed)return;
        if(stage==EnumRenderStage.Done){active?.Gate.EndFrame();return;}
        if(stage!=EnumRenderStage.Opaque)return;
        if(pending is not null && !AdvancePolicy())return;
        if(active is null)return;
        var snapshot=lights();
        if(snapshot is null){RequestRecreation();eligible=false;return;}
        if(snapshot.Frame==frame)return;
        if(snapshot.Frame<frame){RequestRecreation();return;}
        frame=snapshot.Frame;
        try
        {
            var budget=active.Gate.BeginFrame(active.Source.LiveAndRetiringBytes,snapshot.Frame);
            var input=active.Source.Capture(snapshot,deltaTime,budget);
            if(!ReferenceEquals(input.Lights,snapshot))
            {input.Scene?.Dispose();throw new InvalidOperationException("Shadow demand captured another current-light interval.");}
            active.Gate.Update(input,clock(),budget);eligible=true;previousFailure=null;
        }
        catch(Exception error)
        {
            active.Gate.RejectFrame(error.Message,snapshot,clock());eligible=false;
            Report(error.Message);
        }
    }

    /// <summary>Withdraws callbacks before terminal resource retirement.</summary>
    public void Dispose()
    {
        if(disposed)return;disposed=true;eligible=false;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);api.Event.LeaveWorld-=Dispose;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Done);
        LiveConfigReload.Applied-=OnConfigurationApplied;reloadRegistration?.Dispose();
        RenderContextRegistry.Retiring-=OnContextRetiring;
        active?.Dispose();active=null;retirement?.Dispose();retirement=null;pending=null;
    }
    #endregion

    #region Private
    /// <summary>Creates no replacement allocation until every earlier generation's GPU user has completed.</summary>
    private bool AdvancePolicy()
    {
        eligible=false;
        if(active is not null)
        {
            if(retirement is null){active.Gate.Withdraw();retirement=RHICommandContext.Current.InsertFence();}
            active.Gate.Poll();
            if(!retirement.TryConsumeIfSignaled())return false;
            retirement=null;active.Dispose();active=null;
        }
        var next=pending!;pending=null;frame=0;
        if(!next.Scheduling.Enabled)return false;
        try{_=ShadowResourceLayout.Resolve(next);active=create(next);return true;}
        catch(Exception error){Report(error.Message);return false;}
    }

    /// <summary>Queues shader/resource recreation without extending any temporal eligibility.</summary>
    private void RequestRecreation(){if(disposed)return;eligible=false;active?.Gate.WithdrawPublication();pending=requested;}

    /// <summary>Receives the existing sanitized configuration event and withdraws CPU publication before the next gate.</summary>
    private void OnConfigurationApplied(ICoreAPI changed)
    {if(ReferenceEquals(api,changed))ApplyPolicy(ConfigModSystem.Config.VirtualShadows);}

    /// <summary>Ends callbacks before they can execute into a replacement native context.</summary>
    private void OnContextRetiring((nint Handle,long Generation) retiring){if(retiring==context)Dispose();}

    /// <summary>Reports state changes once instead of logging the same unavailable frame continuously.</summary>
    private void Report(string reason)
    {
        if(previousFailure!=reason)api.Logger.Warning("[VGE Shadows] Current frame unavailable: {0}",reason);
        previousFailure=reason;
    }
    #endregion
}
