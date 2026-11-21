using System;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Internal.Classes;

namespace KamiToolKit.BaseTypes;

public unsafe partial class NativeAddon {
    private static Hook<AtkUnitBase.Delegates.FireCallback>? fireCallbackHook;

    // Each plugin ships its own copy of KamiToolKit in its own AssemblyLoadContext, so every copy
    // installs a detour on the same global AtkUnitBase.FireCallback address. A Close() inside the
    // hook re-enters the game's Hide/Close path, which calls FireCallback again. Swallow nested
    // callbacks from this copy instead of recursing down that chain.
    [ThreadStatic] private static bool inFireCallback;

    internal static void InitializeCloseCallback() {
        // Idempotent: a plugin reload must not stack a second detour on the same address.
        if (fireCallbackHook is not null) return;

        fireCallbackHook = IGameInteropProvider.Get().HookFromAddress<AtkUnitBase.Delegates.FireCallback>(AtkUnitBase.Addresses.FireCallback.Value, OnFireCallback);
        fireCallbackHook.Enable();
    }

    private static bool OnFireCallback(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values, bool close) {
        if (inFireCallback)
            return true;

        inFireCallback = true;
        try {
            try {
                IPluginLog.Get().Excessive($"[{thisPtr->NameString}] OnFireCallback");
            }
            catch {
                // Logging must never escape into native code either.
            }

            foreach (var addon in CreatedAddons) {
                if (addon != thisPtr || !close || addon.IsOverlayAddon) continue;

                if (addon.RespectCloseAll) {
                    // The close was handled here, so stop the game from processing Esc any further.
                    addon.Close();

                    return true;
                }

                // This addon opted out of the escape handling. Reporting the callback as handled would
                // swallow Esc globally and stop the game from opening the system menu. Do not pass it on
                // to the original implementation either, because that would close the addon behind our
                // back and make its owner rebuild it on the next frame.
                return false;
            }
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
        finally {
            inFireCallback = false;
        }

        // The original call used to sit outside the try block and used a null-forgiving lookup.
        // After DisposeCloseCallback the hook is null; dereferencing it here killed the game.
        try {
            var hook = fireCallbackHook;
            return hook is not null && hook.Original(thisPtr, valueCount, values, close);
        }
        catch (Exception e) {
            ReportCallbackException(e);
            return false;
        }
    }

    private static long lastCallbackExceptionLogTick;

    /// <summary>
    /// Logs an exception from a game callback at most once per second. A per-frame failure would
    /// otherwise flood the log and make the stutter worse. Logging itself is guarded as well:
    /// during plugin unload the logger may no longer be usable.
    /// </summary>
    internal static void ReportCallbackException(Exception e) {
        try {
            var now = Environment.TickCount64;
            var previous = Interlocked.Read(ref lastCallbackExceptionLogTick);
            if (previous != 0 && now - previous < 1000) return;
            if (Interlocked.CompareExchange(ref lastCallbackExceptionLogTick, now, previous) != previous) return;

            IPluginLog.Get().Exception(e);
        }
        catch {
            // ignored
        }
    }

    internal static void DisposeCloseCallback() {
        fireCallbackHook?.Dispose();
        fireCallbackHook = null;
    }
}
