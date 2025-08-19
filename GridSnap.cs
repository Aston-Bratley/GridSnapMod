using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSnap
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class GridSnapPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> GridEnabled;
        internal static ConfigEntry<float> GridSize;
        internal static ConfigEntry<bool> SnapOnWalls;
        internal static ConfigEntry<float> GridSizeStep;

        internal static GridSnapInputs Inputs;

        public static GridSnapPlugin Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger { get; private set; } = null!;
        internal static Harmony? Harmony { get; set; }

        private void Awake()
        {
            // initialize logger & instance early
            Logger = base.Logger;
            Instance = this;

            GridEnabled = Config.Bind("Grid", "Enabled", true, "Enable grid snapping while in ship build mode.");
            GridSize = Config.Bind("Grid", "GridSize", 0.5f, "Grid size in meters (XZ plane, relative to ship elevator).");
            GridSizeStep = Config.Bind("Grid", "GridSizeStep", 0.05f, "How much to change grid size when using the keybinds.");
            SnapOnWalls = Config.Bind("Grid", "SnapOnWalls", false, "If true, also snap when placing on walls.");

            Inputs = new GridSnapInputs();
            Inputs.Enable();

            // Toggle grid: do NOT display popup; instead update the build mode control tip when in build mode
            Inputs.ToggleGrid.performed += _ =>
            {
                GridEnabled.Value = !GridEnabled.Value;

                // If player is currently in build mode, update the buildModeControlTip text to show grid and state
                if (ShipBuildModeManager.Instance != null && ShipBuildModeManager.Instance.InBuildMode)
                {
                    // Keep same base text but append grid info. The ShipBuildModeManager.CreateGhostObjectAndHighlight patch also appends,
                    // but updating here ensures immediate feedback when toggling while already in build mode.
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    // remove any previous Grid suffix we appended earlier (simple heuristic)
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [G] {(GridEnabled.Value ? "ON" : "OFF")}";
                }

                Logger.LogInfo($"Grid snapping toggled: {(GridEnabled.Value ? "ON" : "OFF")}");
            };

            Inputs.IncreaseGrid.performed += _ =>
            {
                GridSize.Value += GridSizeStep.Value;
                Logger.LogInfo($"Grid size increased: {GridSize.Value}");
                if (ShipBuildModeManager.Instance != null && ShipBuildModeManager.Instance.InBuildMode)
                {
                    // Refresh build mode tip to show updated size
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [G] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m)";
                }
            };

            Inputs.DecreaseGrid.performed += _ =>
            {
                GridSize.Value = Mathf.Max(GridSizeStep.Value, GridSize.Value - GridSizeStep.Value);
                Logger.LogInfo($"Grid size decreased: {GridSize.Value}");
                if (ShipBuildModeManager.Instance != null && ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [G] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m)";
                }
            };

            Patch();

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        private void OnDestroy()
        {
            // best-effort cleanup
            Inputs.Disable();
            Unpatch();
        }

        internal static void Patch()
        {
            Harmony ??= new Harmony(MyPluginInfo.PLUGIN_GUID);
            Logger.LogDebug("Patching...");
            Harmony.PatchAll();
            Logger.LogDebug("Finished patching!");
        }

        internal static void Unpatch()
        {
            Logger.LogDebug("Unpatching...");
            Harmony?.UnpatchSelf();
            Logger.LogDebug("Finished unpatching!");
        }
    }

    public class GridSnapInputs : LcInputActions
    {
        public string Name => "Grid Snap";

        [InputAction("<Keyboard>/g", Name = "Toggle Grid Snap")]
        public InputAction ToggleGrid { get; set; }

        [InputAction("<Keyboard>/equals", Name = "Increase Grid Size")]
        [InputAction("<Keyboard>/numpadPlus", Name = "Increase Grid Size (numpad)")]
        public InputAction IncreaseGrid { get; set; }

        [InputAction("<Keyboard>/minus", Name = "Decrease Grid Size")]
        [InputAction("<Keyboard>/numpadMinus", Name = "Decrease Grid Size (numpad)")]
        public InputAction DecreaseGrid { get; set; }
    }

    // Patch that snaps positions (kept from earlier)
    [HarmonyPatch(typeof(ShipBuildModeManager), "Update")]
    public static class ShipBuildModeManager_Update_SnapPatch
    {
        static readonly FieldInfo placingObjectField = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject");
        static readonly FieldInfo currentColliderField = AccessTools.Field(typeof(ShipBuildModeManager), "currentCollider");
        static readonly FieldInfo placementMaskAndBlockersField = AccessTools.Field(typeof(ShipBuildModeManager), "placementMaskAndBlockers");
        static readonly FieldInfo canConfirmPositionField = AccessTools.Field(typeof(ShipBuildModeManager), "CanConfirmPosition");

        static void Postfix(ShipBuildModeManager __instance)
        {
            if (!__instance.InBuildMode) return;

            var placingObject = placingObjectField.GetValue(__instance) as PlaceableShipObject;
            if (placingObject == null) return;

            var ghost = __instance.ghostObject;
            var gridEnabled = GridSnapPlugin.GridEnabled.Value;
            if (gridEnabled && (!placingObject.AllowPlacementOnWalls || GridSnapPlugin.SnapOnWalls.Value))
            {
                var gridSize = GridSnapPlugin.GridSize.Value;
                var elevator = StartOfRound.Instance.elevatorTransform;
                var local = elevator.InverseTransformPoint(ghost.position);
                local.x = Mathf.Round(local.x / gridSize) * gridSize;
                local.z = Mathf.Round(local.z / gridSize) * gridSize;
                ghost.position = elevator.TransformPoint(local);
            }

            ReevaluatePlacement(__instance, placingObject);
        }

        static void ReevaluatePlacement(ShipBuildModeManager __instance, PlaceableShipObject placingObject)
        {
            var ghost = __instance.ghostObject;

            var currentCollider = currentColliderField.GetValue(__instance) as BoxCollider;
            if (currentCollider == null)
            {
                currentCollider = placingObject.placeObjectCollider as BoxCollider;
                currentColliderField.SetValue(__instance, currentCollider);
            }

            int placementMaskAndBlockers = (int)placementMaskAndBlockersField.GetValue(__instance);

            bool blocked = Physics.CheckBox(
                ghost.position,
                currentCollider.size * 0.5f * 0.57f,
                Quaternion.Euler(ghost.eulerAngles),
                placementMaskAndBlockers,
                QueryTriggerInteraction.Ignore
            );

            if (!blocked && placingObject.doCollisionPointCheck)
            {
                Vector3 p = ghost.position
                            + ghost.forward * placingObject.collisionPointCheck.z
                            + ghost.right * placingObject.collisionPointCheck.x
                            + ghost.up * placingObject.collisionPointCheck.y;

                if (Physics.CheckSphere(p, 1f, placementMaskAndBlockers, QueryTriggerInteraction.Ignore))
                    blocked = true;
            }

            bool inside = StartOfRound.Instance.shipInnerRoomBounds.bounds.Contains(ghost.position);
            bool canConfirm = !blocked && inside;

            canConfirmPositionField.SetValue(__instance, canConfirm);
            __instance.ghostObjectRenderer.sharedMaterial = blocked ? __instance.ghostObjectRed : __instance.ghostObjectGreen;
        }
    }

    // Postfix patch to append grid info to the buildModeControlTip text when ghost is created
    [HarmonyPatch(typeof(ShipBuildModeManager), "CreateGhostObjectAndHighlight")]
    public static class ShipBuildModeManager_CreateGhostObjectAndHighlight_Patch
    {
        static void Postfix(ShipBuildModeManager __instance)
        {
            // Append grid info to the existing control tip so players see the grid control while in build mode
            var baseText = HUDManager.Instance.buildModeControlTip.text;
            int gridIdx = baseText.IndexOf(" | Grid:");
            if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
            HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [G] {(GridSnapPlugin.GridEnabled.Value ? "ON" : "OFF")} ({GridSnapPlugin.GridSize.Value:0.##}m)";
        }
    }
}
