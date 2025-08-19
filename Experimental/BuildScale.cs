using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlacementScaler
{
    [BepInPlugin("com.AstonBratley.PlacementScaler", "Placement Scaler", "1.0.0")]
    public class PlacementScalerPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> ScaleStepPercent;
        internal static ConfigEntry<float> MinScale;
        internal static ConfigEntry<float> MaxScale;
        internal static ConfigEntry<Key> IncreaseKey;
        internal static ConfigEntry<Key> DecreaseKey;
        internal static ConfigEntry<Key> ResetKey;
        internal static ConfigEntry<bool> ShowHudTip;

        internal static ManualLogSource L;

        // Current relative scale factor while placing (1 == original)
        internal static float CurrentScale = 1f;

        // Keep original parent scales per placing object so we can compute ghost scale and apply on confirm
        static readonly Dictionary<PlaceableShipObject, Vector3> originalParentScales = new Dictionary<PlaceableShipObject, Vector3>();

        InputAction increaseAction;
        InputAction decreaseAction;
        InputAction resetAction;

        Harmony harmony;

        void Awake()
        {
            L = Logger;

            ScaleStepPercent = Config.Bind("Scale", "StepPercent", 0.05f, "Scale step as percentage (0.05 = 5%) per key press.");
            MinScale = Config.Bind("Scale", "Min", 0.1f, "Minimum allowed scale multiplier.");
            MaxScale = Config.Bind("Scale", "Max", 3f, "Maximum allowed scale multiplier.");
            IncreaseKey = Config.Bind("Keys", "Increase", Key.RightBracket, "Key to increase scale");
            DecreaseKey = Config.Bind("Keys", "Decrease", Key.LeftBracket, "Key to decrease scale");
            ResetKey = Config.Bind("Keys", "Reset", Key.Backspace, "Key to reset scale to 1");
            ShowHudTip = Config.Bind("UI", "ShowHudTip", true, "Show HUD tip when scale changes");

            increaseAction = new InputAction(binding: $"<Keyboard>/{IncreaseKey.Value.ToString().ToLower()}");
            decreaseAction = new InputAction(binding: $"<Keyboard>/{DecreaseKey.Value.ToString().ToLower()}");
            resetAction = new InputAction(binding: $"<Keyboard>/{ResetKey.Value.ToString().ToLower()}");

            increaseAction.performed += _ => ChangeScale(true);
            decreaseAction.performed += _ => ChangeScale(false);
            resetAction.performed += _ => ResetScale();

            increaseAction.Enable();
            decreaseAction.Enable();
            resetAction.Enable();

            harmony = new Harmony("com.AstonBratley.PlacementScaler");
            harmony.PatchAll();

            L.LogInfo("Placement Scaler loaded");
        }

        void OnDestroy()
        {
            increaseAction.Disable();
            decreaseAction.Disable();
            resetAction.Disable();
            harmony.UnpatchAll("com.AstonBratley.PlacementScaler");
        }

        static void ChangeScale(bool increase)
        {
            float step = ScaleStepPercent.Value;
            if (increase)
                CurrentScale *= (1f + step);
            else
                CurrentScale /= (1f + step);

            CurrentScale = Mathf.Clamp(CurrentScale, MinScale.Value, MaxScale.Value);

            if (ShowHudTip.Value && HUDManager.Instance != null)
                HUDManager.Instance.DisplayTip("Scale", $"Scale: {CurrentScale:0.###}x", false, false, "LC_ScaleTip");

            L.LogInfo($"Placement scale changed to {CurrentScale:0.###}x");

            // If currently in build mode and ghost exists, update ghost scale visually
            if (ShipBuildModeManager.Instance != null && ShipBuildModeManager.Instance.InBuildMode)
            {
                var placing = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject").GetValue(ShipBuildModeManager.Instance) as PlaceableShipObject;
                if (placing != null)
                {
                    ApplyGhostScale(ShipBuildModeManager.Instance, placing);
                }
            }
        }

        static void ResetScale()
        {
            CurrentScale = 1f;
            if (ShowHudTip.Value && HUDManager.Instance != null)
                HUDManager.Instance.DisplayTip("Scale", $"Scale reset to {CurrentScale:0.###}x", false, false, "LC_ScaleTip");
            L.LogInfo("Placement scale reset to 1x");

            if (ShipBuildModeManager.Instance != null && ShipBuildModeManager.Instance.InBuildMode)
            {
                var placing = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject").GetValue(ShipBuildModeManager.Instance) as PlaceableShipObject;
                if (placing != null)
                {
                    ApplyGhostScale(ShipBuildModeManager.Instance, placing);
                }
            }
        }

        // Utility: recompute and apply ghostObjectMesh.localScale from stored original parent scale and CurrentScale
        internal static void ApplyGhostScale(ShipBuildModeManager manager, PlaceableShipObject placing)
        {
            // original mainMesh.localScale * originalParentScale * CurrentScale
            Vector3 originalParent = placing.parentObject.transform.localScale;
            if (originalParentScales.ContainsKey(placing))
                originalParent = originalParentScales[placing];

            Vector3 baseScale = Vector3.Scale(placing.mainMesh.transform.localScale, originalParent);
            manager.ghostObjectMesh.transform.localScale = baseScale * CurrentScale;
        }
    }

    // Hook into CreateGhostObjectAndHighlight to record original scale and set initial ghost scale
    [HarmonyPatch(typeof(ShipBuildModeManager), "CreateGhostObjectAndHighlight")]
    public static class CreateGhostObjectAndHighlight_ScalePatch
    {
        static void Postfix(ShipBuildModeManager __instance)
        {
            var placing = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject").GetValue(__instance) as PlaceableShipObject;
            if (placing == null) return;

            // store original parent scale for this placing object
            if (!PlacementScalerPlugin.originalParentScales.ContainsKey(placing))
                PlacementScalerPlugin.originalParentScales[placing] = placing.parentObject.transform.localScale;

            // reset current scale to 1 for a fresh placement session
            PlacementScalerPlugin.CurrentScale = 1f;

            // compute and apply ghost scale
            Vector3 originalParent = PlacementScalerPlugin.originalParentScales[placing];
            Vector3 baseScale = Vector3.Scale(placing.mainMesh.transform.localScale, originalParent);
            __instance.ghostObjectMesh.transform.localScale = baseScale * PlacementScalerPlugin.CurrentScale;
        }
    }

    // Keep ghost scale in sync each Update while in build mode (so rotation/grid changes don't overwrite scale)
    [HarmonyPatch(typeof(ShipBuildModeManager), "Update")]
    public static class ShipBuildModeManager_Update_ScalePatch
    {
        static void Postfix(ShipBuildModeManager __instance)
        {
            if (!__instance.InBuildMode) return;

            var placing = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject").GetValue(__instance) as PlaceableShipObject;
            if (placing == null) return;

            // ensure we have stored original parent scale
            if (!PlacementScalerPlugin.originalParentScales.ContainsKey(placing))
                PlacementScalerPlugin.originalParentScales[placing] = placing.parentObject.transform.localScale;

            // apply the ghost scale each frame so other code won't override it
            PlacementScalerPlugin.ApplyGhostScale(__instance, placing);
        }
    }

    // Apply scale to actual object before placement is finalized so offsets/rotations use the scaled transform
    [HarmonyPatch(typeof(ShipBuildModeManager), "PlaceShipObject")]
    public static class ShipBuildModeManager_PlaceShipObject_ScalePatch
    {
        static void Prefix(PlaceableShipObject placeableObject)
        {
            if (placeableObject == null) return;

            if (PlacementScalerPlugin.originalParentScales.ContainsKey(placeableObject))
            {
                var original = PlacementScalerPlugin.originalParentScales[placeableObject];
                // apply scaled parent object scale (this mutates the object so the game's subsequent logic uses the new scale)
                placeableObject.parentObject.transform.localScale = original * PlacementScalerPlugin.CurrentScale;

                // if a secondary parent exists, scale it as well to keep consistent visuals
                if (placeableObject.parentObjectSecondary != null)
                {
                    placeableObject.parentObjectSecondary.transform.localScale = original * PlacementScalerPlugin.CurrentScale;
                }
            }
        }
    }

    // When leaving build mode (cancel), clear stored original scales and reset CurrentScale
    [HarmonyPatch(typeof(ShipBuildModeManager), "CancelBuildMode")]
    public static class ShipBuildModeManager_CancelBuildMode_ScalePatch
    {
        static void Postfix(ShipBuildModeManager __instance, bool cancelBeforePlacement = true)
        {
            // clear any stored originals for the current placingObject
            var placing = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject").GetValue(__instance) as PlaceableShipObject;
            if (placing != null && PlacementScalerPlugin.originalParentScales.ContainsKey(placing))
                PlacementScalerPlugin.originalParentScales.Remove(placing);

            PlacementScalerPlugin.CurrentScale = 1f;
        }
    }
}
