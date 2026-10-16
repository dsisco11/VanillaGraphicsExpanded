using System;
using FluffyClouds;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Certifies the joint raw-data upload without reading or copying worker tiles.</summary>
[HarmonyPatch(typeof(CloudRendererMap), "WriteTexture")]
internal static class CloudMapUploadHook
{
    #region Private
    /// <summary>Revokes previous content before an in-place update can fail.</summary>
    private static void Prefix(CloudRendererMap __instance, out CloudNativeMapAdapter? __state)
    {
        __state = CloudNativeRuntime.Active?.ForMap(__instance); __state?.BeginUpload();
    }
    /// <summary>Only normal completion and an error-free pair of uploads receives a certificate.</summary>
    private static void Postfix(CloudNativeMapAdapter? __state, bool __runOriginal) => __state?.CompleteUpload(__runOriginal);
    /// <summary>A failed in-place upload cannot retain the old certificate.</summary>
    private static void Finalizer(Exception? __exception, CloudNativeMapAdapter? __state)
    {
        if (__exception is not null) __state?.Publication.BeginUpload();
    }
    #endregion
}
