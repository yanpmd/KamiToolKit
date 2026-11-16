using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KamiToolKit.Extensions;

/// <summary>
/// Extension methods for AtkStage.
/// </summary>
public static unsafe class AtkStageExtensions {
    extension(ref AtkStage atkStage) {

        /// <summary>
        /// Clears the target node from any focus entry in the AtkStage.FocusList.
        /// </summary>
        /// <param name="targetNode">The node address to search for.</param>
        public void ClearNodeFocus(AtkResNode* targetNode) {
            if (targetNode is null) return;

            var inputManager = atkStage.AtkInputManager;
            if (inputManager is null) return;

            foreach (ref var focusEntry in inputManager->FocusList) {

                // Only entries that actually reference the disposed node need cleanup.
                if (focusEntry.AtkEventTarget != targetNode) continue;

                // Clear the entry
                focusEntry.AtkEventTarget = null;
                focusEntry.FocusParam = 0;

                // Only drop the focused node if it still points at the node being disposed; a
                // later entry may already have replaced it, and unconditionally nulling it would
                // steal the focus of an unrelated addon.
                if (inputManager->FocusedNode == targetNode)
                    inputManager->FocusedNode = null;

                // Clear collision managers collision node
                if (atkStage.AtkCollisionManager is not null &&
                    atkStage.AtkCollisionManager->IntersectingCollisionNode == targetNode) {
                    atkStage.AtkCollisionManager->IntersectingCollisionNode = null;
                }

                // focusEntry.AtkEventListener is deliberately NOT dereferenced here. The listener
                // that registered this focus entry may already have been freed (e.g. an addon the
                // game closed while its entry is still queued); casting it to AtkUnitBase and
                // walking AdditionalFocusableNodes then terminates the process with an
                // AccessViolationException, which no managed handler can catch. The entry itself is
                // cleared above, so the game refreshes its focus state on the next input update.
            }
        }
    }
}
