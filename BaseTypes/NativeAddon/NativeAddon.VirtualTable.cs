using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Enums;
using KamiToolKit.Internal.Classes;
using KamiToolKit.Timelines;

namespace KamiToolKit.BaseTypes;

public unsafe partial class NativeAddon {

    private const int VirtualTableEntryCount = 200;

    private AtkUnitBase.Delegates.Dtor destructorFunction = null!;
    private AtkUnitBase.Delegates.Draw drawFunction = null!;
    private AtkUnitBase.Delegates.Finalizer finalizerFunction = null!;
    private AtkUnitBase.Delegates.Hide hideFunction = null!;
    private AtkUnitBase.Delegates.Initialize initializeFunction = null!;
    private AtkUnitBase.Delegates.OnSetup onSetupFunction = null!;
    private AtkUnitBase.Delegates.Show showFunction = null!;
    private AtkUnitBase.Delegates.Hide2 softHideFunction = null!;
    private AtkUnitBase.Delegates.Update updateFunction = null!;
    private AtkUnitBase.Delegates.OnRequestedUpdate onRequestedUpdateFunction = null!;
    private AtkUnitBase.Delegates.OnRefresh onRefreshFunction = null!;
    private AtkUnitBase.Delegates.OnScreenSizeChange onScreenSizeChangedFunction = null!;

    private AtkUnitBase.AtkUnitBaseVirtualTable* modifiedVirtualTable;
    private AtkUnitBase.AtkUnitBaseVirtualTable* originalVirtualTable;

    private void RegisterVirtualTable() {

        originalVirtualTable = InternalAddon->VirtualTable;

        // Overwrite virtual table with a custom copy,
        // Note: currently there are 73 vfuncs, but there's no harm in copying more for when they add new vfuncs to the game
        modifiedVirtualTable = (AtkUnitBase.AtkUnitBaseVirtualTable*) IMemorySpace.GetUISpace()->AllocateZeroedArray<nint>(VirtualTableEntryCount);
        NativeMemory.Copy(InternalAddon->VirtualTable, modifiedVirtualTable, 0x8 * VirtualTableEntryCount);
        InternalAddon->VirtualTable = modifiedVirtualTable;

        initializeFunction = Initialize;
        onSetupFunction = Setup;
        showFunction = Show;
        updateFunction = Update;
        drawFunction = Draw;
        hideFunction = Hide;
        softHideFunction = Hide2;
        finalizerFunction = Finalizer;
        destructorFunction = Destructor;
        onRequestedUpdateFunction = RequestedUpdate;
        onRefreshFunction = Refresh;
        onScreenSizeChangedFunction = ScreenSizeChange;

        modifiedVirtualTable->Initialize = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(initializeFunction);
        modifiedVirtualTable->OnSetup = (delegate* unmanaged<AtkUnitBase*, uint, AtkValue*, void>)Marshal.GetFunctionPointerForDelegate(onSetupFunction);
        modifiedVirtualTable->Show = (delegate* unmanaged<AtkUnitBase*, bool, uint, void>)Marshal.GetFunctionPointerForDelegate(showFunction);
        modifiedVirtualTable->Update = (delegate* unmanaged<AtkUnitBase*, float, void>)Marshal.GetFunctionPointerForDelegate(updateFunction);
        modifiedVirtualTable->Draw = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(drawFunction);
        modifiedVirtualTable->Hide = (delegate* unmanaged<AtkUnitBase*, bool, bool, uint, void>)Marshal.GetFunctionPointerForDelegate(hideFunction);
        modifiedVirtualTable->Hide2 = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(softHideFunction);
        modifiedVirtualTable->Finalizer = (delegate* unmanaged<AtkUnitBase*, void>)Marshal.GetFunctionPointerForDelegate(finalizerFunction);
        modifiedVirtualTable->Dtor = (delegate* unmanaged<AtkUnitBase*, byte, AtkEventListener*>)Marshal.GetFunctionPointerForDelegate(destructorFunction);
        modifiedVirtualTable->OnRequestedUpdate = (delegate* unmanaged<AtkUnitBase*, NumberArrayData**, StringArrayData**, void>)Marshal.GetFunctionPointerForDelegate(onRequestedUpdateFunction);
        modifiedVirtualTable->OnRefresh = (delegate* unmanaged<AtkUnitBase*, uint, AtkValue*, bool>)Marshal.GetFunctionPointerForDelegate(onRefreshFunction);
        modifiedVirtualTable->OnScreenSizeChange = (delegate* unmanaged<AtkUnitBase*, int, int, void>)Marshal.GetFunctionPointerForDelegate(onScreenSizeChangedFunction);
    }

    /// <summary>
    /// OnSetup Callback for an addon, this is called to attach and save references to created nodes.
    /// </summary>
    protected virtual void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan) { }

    /// <summary>
    /// OnShow Callback for an addon, this is called when the window is opened.
    /// </summary>
    /// <remarks>
    /// KamiToolKit intentionally does not allow hiding addons, so this is only called when it's opened.
    /// </remarks>
    protected virtual void OnShow(AtkUnitBase* addon) { }

    /// <summary>
    /// OnHide Callback for an addon, this is called when the window is opened.
    /// </summary>
    /// <remarks>
    /// KamiToolKit intentionally does not allow hiding addons, so this will then trigger close and then subsequently <see cref="OnFinalize"/>.
    /// </remarks>
    protected virtual void OnHide(AtkUnitBase* addon) { }

    /// <summary>
    /// OnDraw Callback for an addon, this is called every frame the addon is visible.
    /// </summary>
    protected virtual void OnDraw(AtkUnitBase* addon) { }

    /// <summary>
    /// OnUpdate Callback for an addon, this is called every frame the addon exists before its opened, and after it's closed but not finalized yet.
    /// </summary>
    protected virtual void OnUpdate(AtkUnitBase* addon) { }

    /// <summary>
    /// OnFinalize Callback for the addon, this is called immediately before it is deallocated/closed fully.
    /// </summary>
    protected virtual void OnFinalize(AtkUnitBase* addon) { }

    /// <summary>
    /// OnRequestedUpdate Callback for the addon, this is only called if you subscribe to string/number array data entries.
    /// </summary>
    protected virtual void OnRequestedUpdate(AtkUnitBase* addon, NumberArrayData** numberArrayData, StringArrayData** stringArrayData) { }

    /// <summary>
    /// OnRefresh Callback for the addon, the game calls this once on open, and may trigger it under other unknown conditions.
    /// </summary>
    protected virtual void OnRefresh(AtkUnitBase* addon, Span<AtkValue> atkValues) { }

    // Every method below is a ReversePInvoke entry point installed in the addon's virtual table.
    // A managed exception leaving one of them is NOT caught by Dalamud: it travels back into the
    // game and terminates the whole process (CLR error 0x12345679). Both the managed half and the
    // call into the original virtual table are guarded; the original must still run whenever
    // possible, because skipping it leaves the game's UI state machine stuck.

    private void Initialize(AtkUnitBase* thisPtr) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Initialize");
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        // The native initializer has to run before we build on top of the UldManager.
        try {
            originalVirtualTable->Initialize(thisPtr);
        }
        catch (Exception e) {
            ReportCallbackException(e);
            return;
        }

        try {
            var widgetInfo = IMemorySpace.GetUISpace()->MallocZeroed<AtkUldWidgetInfo>();
            widgetInfo->Id = 1;
            widgetInfo->NodeCount = 0;
            widgetInfo->NodeList = null;
            widgetInfo->WidgetAlignment = new AtkWidgetAlignment {
                AlignmentType = AlignmentType.Center,
                X = 50.0f,
                Y = 50.0f,
            };

            thisPtr->UldManager.InitializeResourceRendererManager();
            InternalAddon->UldManager.ResourceFlags |= AtkUldManagerResourceFlag.Initialized;

            InternalAddon->UldManager.Objects = (AtkUldObjectInfo*)widgetInfo;
            InternalAddon->UldManager.ObjectCount = 1;
            InternalAddon->UldManager.ResourceFlags |= AtkUldManagerResourceFlag.ArraysAllocated;

            RootNode.AddTimeline(new TimelineBuilder()
                .BeginFrameSet(1, 89)
                .AddLabel(1, 101, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(10, 102, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(20, 103, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(30, 104, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(40, 105, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(50, 106, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(60, 107, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(70, 108, AtkTimelineJumpBehavior.PlayOnce, 0)
                .AddLabel(80, 109, AtkTimelineJumpBehavior.PlayOnce, 0)
                .EndFrameSet()
                .Build());

            InternalAddon->RootNode = RootNode;
            InternalAddon->UldManager.AddNodeToObjectList(RootNode);

            if (!IsOverlayAddon && WindowNode is not null) {
                WindowNode.AttachNode(this, NodePosition.AsFirstChild);
                InternalAddon->WindowNode = WindowNode;
                InternalAddon->UldManager.AddNodeToObjectList(WindowNode);
            }

            WindowNode?.WindowHeaderFocusNode.AddNodeFlags(NodeFlags.Focusable);
            InternalAddon->FocusNode = WindowNode is not null ? WindowNode.WindowHeaderFocusNode : RootNode;

            InternalAddon->UldManager.UpdateDrawNodeList();
            InternalAddon->UldManager.LoadedState = AtkLoadState.Loaded;

            InternalAddon->LoadState = AtkUnitBaseLoadState.LoadingUldResource;
            InternalAddon->WasLoadUldByNameCalled = true;
            InternalAddon->UpdateCollisionNodeList(false);

            // Now that we have constructed this instance, track it for auto-dispose
            CreatedAddons.Add(this);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Setup(AtkUnitBase* addon, uint valueCount, AtkValue* values) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Setup");

            if (!IsOverlayAddon) {
                SetInitialState();
            }
            else {
                ref var screenSize = ref AtkStage.Instance()->ScreenSize;

                addon->SetScale(1.0f / AtkUnitBase.GetGlobalUIScale(), true);
                addon->SetSize((ushort)screenSize.Width, (ushort)screenSize.Height);
                addon->SetPosition(0, 0);
            }
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->OnSetup(addon, valueCount, values);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            OnSetup(addon, new Span<AtkValue>(values, (int)valueCount));

            // Initialize all TextId fields that were attached in OnSetup
            addon->UldManager.SetupTextRecursive();

            isSetup = true;
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Show(AtkUnitBase* addon, bool silenceOpenSoundEffect, uint unsetShowHideFlags) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Show");
            OnShow(addon);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->Show(addon, silenceOpenSoundEffect, unsetShowHideFlags);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Update(AtkUnitBase* addon, float delta) {
        try {
            IPluginLog.Get().Excessive($"[{InternalName}] Update");
            OnUpdate(addon);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->Update(addon, delta);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Draw(AtkUnitBase* addon) {
        try {
            IPluginLog.Get().Excessive($"[{InternalName}] Draw");
            OnDraw(addon);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->Draw(addon);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Hide(AtkUnitBase* addon, bool unkBool, bool callHideCallback, uint setShowHideFlags) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Hide");
            OnHide(addon);
            SaveAddonConfig();
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        // Keep the original Hide + Close sequence: KTK intentionally treats a hide as a close,
        // and dropping the Close call would leave InternalAddon non-null while the addon is gone.
        try {
            originalVirtualTable->Hide(addon, unkBool, callHideCallback, setShowHideFlags);
            originalVirtualTable->Close(addon, false);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Hide2(AtkUnitBase* addon) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Hide2");
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->Hide2(addon);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private void Finalizer(AtkUnitBase* addon) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Finalize");
            OnFinalize(addon);

            if (RememberClosePosition) {
                LastClosePosition = new Vector2(InternalAddon->X, InternalAddon->Y);
            }

            isSetup = false;
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->Finalizer(addon);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private AtkEventListener* Destructor(AtkUnitBase* addon, byte flags) {
        AtkEventListener* result = null;

        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Destructor");
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        // Restore the game's virtual table BEFORE the native destructor frees the addon. The old
        // code freed our copy without restoring the pointer, leaving a dangling vtable that the
        // game could still call during teardown (the pure-native C0000005 crash).
        if ((flags & 1) == 1) {
            try {
                if (originalVirtualTable != null)
                    addon->VirtualTable = originalVirtualTable;
            }
            catch (Exception e) {
                ReportCallbackException(e);
            }
        }

        try {
            result = originalVirtualTable->Dtor(addon, flags);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        if ((flags & 1) == 1) {
            InternalAddon = null;
            disposeHandle?.Dispose();
            disposeHandle = null;
            CreatedAddons.Remove(this);

            try {
                // Free our custom virtual table, the game doesn't know this exists and won't clear it on its own.
                if (modifiedVirtualTable != null)
                    IMemorySpace.Free(modifiedVirtualTable);
            }
            catch (Exception e) {
                ReportCallbackException(e);
            }
            modifiedVirtualTable = null;
        }

        return result;
    }

    private void RequestedUpdate(AtkUnitBase* thisPtr, NumberArrayData** numberArrayData, StringArrayData** stringArrayData) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] RequestedUpdate");

            // Prevent calls to OnRequestedUpdate before Setup is completed. The game will try to call this after Show but before Setup
            if (isSetup)
                OnRequestedUpdate(thisPtr, numberArrayData, stringArrayData);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->OnRequestedUpdate(thisPtr, numberArrayData, stringArrayData);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private bool Refresh(AtkUnitBase* thisPtr, uint valueCount, AtkValue* values) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] Refresh");
            OnRefresh(thisPtr, new Span<AtkValue>(values, (int)valueCount));
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            return originalVirtualTable->OnRefresh(thisPtr, valueCount, values);
        }
        catch (Exception e) {
            ReportCallbackException(e);
            return false;
        }
    }

    private void ScreenSizeChange(AtkUnitBase* thisPtr, int width, int height) {
        try {
            IPluginLog.Get().Verbose($"[{InternalName}] ScreenSizeChange");
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            originalVirtualTable->OnScreenSizeChange(thisPtr, width, height);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }

        try {
            if (IsOverlayAddon || IgnoreGlobalScale)
                thisPtr->SetScale(1.0f / AtkUnitBase.GetGlobalUIScale(), true);
        }
        catch (Exception e) {
            ReportCallbackException(e);
        }
    }

    private bool isSetup;
}
