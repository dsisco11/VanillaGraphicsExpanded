using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using HarmonyLib;

namespace Automation;

/// <summary>Prevents automatic activation while retaining a visible, rendering game window.</summary>
internal static class WindowActivationPolicy
{
    private static PropertyInfo startFocused = null!;
    private static PropertyInfo startVisible = null!;
    private static PropertyInfo windowPointer = null!;
    private static FieldInfo visibleState = null!;
    private static MethodInfo getWin32Window = null!;
    private static PropertyInfo windowState = null!;
    private static MethodInfo windowHint = null!;
    private static object focusedHint = null!;
    private static object focusOnShowHint = null!;

    #region Hook installation

    /// <summary>Patches window creation before any OpenTK native window is constructed.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Install()
    {
        // Defer JIT resolution of Harmony until StartupHook has installed the resolver.
        Assembly desktop = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Lib", "OpenTK.Windowing.Desktop.dll"));
        Assembly framework = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Lib", "OpenTK.Windowing.GraphicsLibraryFramework.dll"));
        Type settingsType = desktop.GetType("OpenTK.Windowing.Desktop.NativeWindowSettings", throwOnError: true)!;
        Type windowType = desktop.GetType("OpenTK.Windowing.Desktop.NativeWindow", throwOnError: true)!;
        Type glfw = framework.GetType("OpenTK.Windowing.GraphicsLibraryFramework.GLFW", throwOnError: true)!;
        Type hintType = framework.GetType("OpenTK.Windowing.GraphicsLibraryFramework.WindowHintBool", throwOnError: true)!;
        startFocused = settingsType.GetProperty("StartFocused") ?? throw new MissingMemberException(settingsType.FullName, "StartFocused");
        startVisible = settingsType.GetProperty("StartVisible") ?? throw new MissingMemberException(settingsType.FullName, "StartVisible");
        windowPointer = windowType.GetProperty("WindowPtr") ?? throw new MissingMemberException(windowType.FullName, "WindowPtr");
        visibleState = windowType.GetField("_isVisible", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(windowType.FullName, "_isVisible");
        getWin32Window = glfw.GetMethods().Single(method => method.Name == "GetWin32Window");
        windowState = settingsType.GetProperty("WindowState") ?? throw new MissingMemberException(settingsType.FullName, "WindowState");
        windowHint = glfw.GetMethod("WindowHint", new[] { hintType, typeof(bool) }) ?? throw new MissingMethodException(glfw.FullName, "WindowHint");
        focusedHint = Enum.Parse(hintType, "Focused");
        focusOnShowHint = Enum.Parse(hintType, "FocusOnShow");

        var harmony = new Harmony("automation.window-activation");
        harmony.Patch(
            windowType.GetConstructor(new[] { settingsType }) ?? throw new MissingMethodException(windowType.FullName, ".ctor"),
            prefix: new HarmonyMethod(typeof(WindowActivationPolicy), nameof(ConfigureWindow)),
            postfix: new HarmonyMethod(typeof(WindowActivationPolicy), nameof(ShowBehindExistingWindows)));
        harmony.Patch(
            glfw.GetMethods().Single(method => method.Name == "CreateWindow" && method.GetParameters().Length == 5),
            prefix: new HarmonyMethod(typeof(WindowActivationPolicy), nameof(ConfigureCreationHints)));
        harmony.Patch(
            windowType.GetMethod("Focus", Type.EmptyTypes) ?? throw new MissingMethodException(windowType.FullName, "Focus"),
            prefix: new HarmonyMethod(typeof(WindowActivationPolicy), nameof(SuppressAutomaticFocus)));
    }

    #endregion

    #region Window policy

    /// <summary>Disables OpenTK's focus request and avoids activating fullscreen transitions.</summary>
    private static void ConfigureWindow(object settings)
    {
        startFocused.SetValue(settings, false);
        // Keep construction (including state restoration) hidden to avoid an initial flash above other apps.
        startVisible.SetValue(settings, false);
        windowState.SetValue(settings, Enum.Parse(windowState.PropertyType, "Normal"));
    }

    /// <summary>Reveals the completed native window behind existing windows without activating it.</summary>
    private static void ShowBehindExistingWindows(object __instance)
    {
        object pointer = windowPointer.GetValue(__instance)!;
        var handle = (IntPtr)getWin32Window.Invoke(null, new[] { pointer })!;
        BackgroundWindowPlacement.Show(handle);
        // OpenTK caches visibility. Synchronize it without calling its normal GLFW show path,
        // which can raise the window even when keyboard activation has been disabled.
        visibleState.SetValue(__instance, true);
    }

    /// <summary>Sets non-activating GLFW hints after initialization but before native creation.</summary>
    private static void ConfigureCreationHints()
    {
        // Native creation can activate a window independently of OpenTK's later Focus call.
        windowHint.Invoke(null, new[] { focusedHint, (object)false });
        windowHint.Invoke(null, new[] { focusOnShowHint, (object)false });
    }

    /// <summary>Suppresses programmatic activation; users can still select the window themselves.</summary>
    private static bool SuppressAutomaticFocus() => false;

    #endregion
}
