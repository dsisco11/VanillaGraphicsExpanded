using System;

using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Reports singleplayer readiness without loading rendering systems on the server.</summary>
public sealed class AutomationReadinessModSystem : ModSystem
{
    private ICoreServerAPI? serverApi;
    private string runId = "manual";
    private IServerPlayer? pendingPlayer;
    private long? readinessListenerId;

    #region Public lifecycle

    /// <summary>Loads the readiness observer only on the server.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    /// <summary>Observes readiness after the integrated server completes character selection.</summary>
    public override void StartServerSide(ICoreServerAPI api)
    {
        if (api.Server.IsDedicated) return;

        // The launcher supplies a unique token so another session cannot satisfy its wait.
        string? requestedRunId = Environment.GetEnvironmentVariable("AUTOMATION_ID");
        runId = Guid.TryParseExact(requestedRunId, "N", out Guid parsedRunId)
            ? parsedRunId.ToString("N")
            : "manual";
        serverApi = api;
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
    }

    /// <summary>Removes the observer when the world closes.</summary>
    public override void Dispose()
    {
        if (serverApi is not null)
        {
            serverApi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            if (readinessListenerId is long listenerId)
            {
                serverApi.Event.UnregisterGameTickListener(listenerId);
                readinessListenerId = null;
            }
            pendingPlayer = null;
            serverApi = null;
        }
        base.Dispose();
    }

    #endregion

    #region Readiness logging

    /// <summary>Starts a bounded-lifetime observer for the first joining player.</summary>
    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        if (serverApi is null || pendingPlayer is not null) return;

        // In 1.22.7 the public PlayerReady event is not forwarded to ModEventManager.
        // Observe the server's acknowledged state instead of treating level loading as readiness.
        pendingPlayer = player;
        readinessListenerId = serverApi.Event.RegisterGameTickListener(OnReadinessTick, 100);
    }

    /// <summary>Logs readiness after the client acknowledges character selection, then stops polling.</summary>
    private void OnReadinessTick(float deltaTime)
    {
        if (serverApi is null || pendingPlayer is null) return;
        EnumClientState state = pendingPlayer.ConnectionState;
        if (state != EnumClientState.Playing && state != EnumClientState.Offline) return;

        // Stop the temporary listener on both successful readiness and a cancelled join.
        if (readinessListenerId is long listenerId)
        {
            serverApi.Event.UnregisterGameTickListener(listenerId);
            readinessListenerId = null;
        }
        pendingPlayer = null;
        if (state == EnumClientState.Playing)
        {
            serverApi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            serverApi.Logger.Notification("[VGE.Automation] PlayerReady RunId={0}", runId);
        }
    }

    #endregion
}
