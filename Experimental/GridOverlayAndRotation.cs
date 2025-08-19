using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace GridSnap
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class GridSnapPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> GridEnabled;
        internal static ConfigEntry<float> GridSize;
        internal static ConfigEntry<bool> SnapOnWalls;
        internal static ConfigEntry<float> GridSizeStep;

        internal static ConfigEntry<bool> RotationSnapEnabled;
        internal static ConfigEntry<float> RotationSnapAngle;
        internal static ConfigEntry<float> RotationSnapAngleStep;

        internal static ConfigEntry<float> GridDisplayExtent;
        internal static ConfigEntry<Color> GridLineColor;
        internal static ConfigEntry<int> GridMajorLineEvery;

        internal static GridSnapInputs Inputs;

        public static GridSnapPlugin Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger { get; private set; } = null!;
        internal static Harmony? Harmony { get; set; }

        internal static GridOverlay Overlay;

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            GridEnabled = Config.Bind("Grid", "Enabled", true, "Enable grid snapping while in ship build mode.");
            GridSize = Config.Bind("Grid", "GridSize", 0.5f, "Grid size in meters (XZ plane, relative to ship elevator).");
            GridSizeStep = Config.Bind("Grid", "GridSizeStep", 0.05f, "How much to change grid size when using the keybinds.");
            SnapOnWalls = Config.Bind("Grid", "SnapOnWalls", false, "If true, also snap when placing on walls.");

            RotationSnapEnabled = Config.Bind("Rotation", "Enabled", true, "Enable rotation snapping while in ship build mode.");
            RotationSnapAngle = Config.Bind("Rotation", "AngleStep", 45f, "Rotation snap angle in degrees.");
            RotationSnapAngleStep = Config.Bind("Rotation", "AngleStepIncrement", 15f, "How much to change rotation snap angle when using keybinds.");

            GridDisplayExtent = Config.Bind("Display", "Extent", 8f, "Half-extent in meters of the grid overlay (grid extends +-extent).");
            GridLineColor = Config.Bind("Display", "LineColor", new Color(0f, 1f, 1f, 0.3f), "Color of the grid lines (RGBA).");
            GridMajorLineEvery = Config.Bind("Display", "MajorLineEvery", 4, "Draw a major (thicker) line every N minor lines.");

            Inputs = new GridSnapInputs();
            Inputs.Enable();

            Inputs.ToggleGrid.performed += _ =>
            {
                GridEnabled.Value = !GridEnabled.Value;
                if (ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m)";
                }
                Logger.LogInfo($"Grid snapping toggled: {(GridEnabled.Value ? "ON" : "OFF")}");
            };

            Inputs.IncreaseGrid.performed += _ =>
            {
                GridSize.Value += GridSizeStep.Value;
                Logger.LogInfo($"Grid size increased: {GridSize.Value}");
                if (ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m)";
                    Overlay?.Rebuild();
                }
            };

            Inputs.DecreaseGrid.performed += _ =>
            {
                GridSize.Value = Mathf.Max(GridSizeStep.Value, GridSize.Value - GridSizeStep.Value);
                Logger.LogInfo($"Grid size decreased: {GridSize.Value}");
                if (ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m)";
                    Overlay?.Rebuild();
                }
            };

            Inputs.ToggleRotationSnap.performed += _ =>
            {
                RotationSnapEnabled.Value = !RotationSnapEnabled.Value;
                Logger.LogInfo($"Rotation snapping toggled: {(RotationSnapEnabled.Value ? "ON" : "OFF")}");
                if (ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m) | RotSnap: [F9] {(RotationSnapEnabled.Value ? "ON" : "OFF")} ({RotationSnapAngle.Value:0}°)";
                }
            };

            Inputs.IncreaseRotationSnapAngle.performed += _ =>
            {
                RotationSnapAngle.Value += RotationSnapAngleStep.Value;
                Logger.LogInfo($"Rotation snap angle increased: {RotationSnapAngle.Value}");
                if (ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m) | RotSnap: [F9] {(RotationSnapEnabled.Value ? "ON" : "OFF")} ({RotationSnapAngle.Value:0}°)";
                }
            };

            Inputs.DecreaseRotationSnapAngle.performed += _ =>
            {
                RotationSnapAngle.Value = Mathf.Max(1f, RotationSnapAngle.Value - RotationSnapAngleStep.Value);
                Logger.LogInfo($"Rotation snap angle decreased: {RotationSnapAngle.Value}");
                if (ShipBuildModeManager.Instance.InBuildMode)
                {
                    var baseText = HUDManager.Instance.buildModeControlTip.text;
                    int gridIdx = baseText.IndexOf(" | Grid:");
                    if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
                    HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridEnabled.Value ? "ON" : "OFF")} ({GridSize.Value:0.##}m) | RotSnap: [F9] {(RotationSnapEnabled.Value ? "ON" : "OFF")} ({RotationSnapAngle.Value:0}°)";
                }
            };

            Overlay = new GridOverlay();

            Patch();

            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        private void OnDestroy()
        {
            Inputs.Disable();
            Unpatch();
            Overlay.Destroy();
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

        [InputAction("<Keyboard>/f7", Name = "Toggle Grid Snap")]
        public InputAction ToggleGrid { get; set; }

        [InputAction("<Keyboard>/equals", Name = "Increase Grid Size")]
        [InputAction("<Keyboard>/numpadPlus", Name = "Increase Grid Size (numpad)")]
        public InputAction IncreaseGrid { get; set; }

        [InputAction("<Keyboard>/minus", Name = "Decrease Grid Size")]
        [InputAction("<Keyboard>/numpadMinus", Name = "Decrease Grid Size (numpad)")]
        public InputAction DecreaseGrid { get; set; }

        [InputAction("<Keyboard>/f9", Name = "Toggle Rotation Snap")]
        public InputAction ToggleRotationSnap { get; set; }

        [InputAction("<Keyboard>/bracketright", Name = "Increase Rotation Snap Angle")]
        public InputAction IncreaseRotationSnapAngle { get; set; }

        [InputAction("<Keyboard>/bracketleft", Name = "Decrease Rotation Snap Angle")]
        public InputAction DecreaseRotationSnapAngle { get; set; }
    }

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

            // 1) Position snapping
            if (GridSnapPlugin.GridEnabled.Value && (!placingObject.AllowPlacementOnWalls || GridSnapPlugin.SnapOnWalls.Value))
            {
                var gridSize = GridSnapPlugin.GridSize.Value;
                var elevator = StartOfRound.Instance.elevatorTransform;
                var local = elevator.InverseTransformPoint(ghost.position);
                local.x = Mathf.Round(local.x / gridSize) * gridSize;
                local.z = Mathf.Round(local.z / gridSize) * gridSize;
                ghost.position = elevator.TransformPoint(local);
            }

            // 1b) Rotation snapping (snap yaw only)
            if (GridSnapPlugin.RotationSnapEnabled.Value)
            {
                var angle = GridSnapPlugin.RotationSnapAngle.Value;
                var e = ghost.eulerAngles;
                e.y = Mathf.Round(e.y / angle) * angle;
                ghost.eulerAngles = e;
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

    [HarmonyPatch(typeof(ShipBuildModeManager), "CreateGhostObjectAndHighlight")]
    public static class ShipBuildModeManager_CreateGhostObjectAndHighlight_Patch
    {
        static void Postfix(ShipBuildModeManager __instance)
        {
            var baseText = HUDManager.Instance.buildModeControlTip.text;
            int gridIdx = baseText.IndexOf(" | Grid:");
            if (gridIdx >= 0) baseText = baseText.Substring(0, gridIdx);
            HUDManager.Instance.buildModeControlTip.text = $"{baseText} | Grid: [F7] {(GridSnapPlugin.GridEnabled.Value ? "ON" : "OFF")} ({GridSnapPlugin.GridSize.Value:0.##}m) | RotSnap: [F9] {(GridSnapPlugin.RotationSnapEnabled.Value ? "ON" : "OFF")} ({GridSnapPlugin.RotationSnapAngle.Value:0}°)";

            // Show overlay
            Overlay.Show(StartOfRound.Instance.elevatorTransform);
            Overlay.Rebuild();
        }
    }

    [HarmonyPatch(typeof(ShipBuildModeManager), "CancelBuildMode")]
    public static class ShipBuildModeManager_CancelBuildMode_Patch
    {
        static void Postfix(ShipBuildModeManager __instance, bool cancelBeforePlacement = true)
        {
            // Hide overlay when leaving build mode
            Overlay.Hide();
        }
    }

    // Simple grid overlay using LineRenderers
    public class GridOverlay
    {
        GameObject root;
        List<GameObject> lines = new List<GameObject>();

        public GridOverlay()
        {
            root = new GameObject("GridSnapOverlay");
            root.SetActive(false);
        }

        public void Show(Transform elevator)
        {
            root.transform.SetParent(elevator, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.SetActive(true);
            Rebuild();
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        public void Destroy()
        {
            Object.Destroy(root);
        }

        public void Rebuild()
        {
            // clear existing lines
            foreach (var go in lines) Object.Destroy(go);
            lines.Clear();

            float gridSize = GridSnapPlugin.GridSize.Value;
            float extent = GridSnapPlugin.GridDisplayExtent.Value;
            Color color = GridSnapPlugin.GridLineColor.Value;
            int majorEvery = GridSnapPlugin.GridMajorLineEvery.Value;

            int count = Mathf.CeilToInt((extent * 2f) / gridSize);
            float start = -extent;
            float end = extent;

            Material mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;

            // create lines along X (varying Z)
            for (int i = 0; i <= count; i++)
            {
                float z = start + i * gridSize;
                var go = new GameObject($"grid_x_{i}");
                go.transform.SetParent(root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = mat;
                bool major = (i % majorEvery) == 0;
                lr.startWidth = lr.endWidth = major ? 0.03f : 0.01f;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, new Vector3(start, 0.01f, z));
                lr.SetPosition(1, new Vector3(end, 0.01f, z));
                lr.numCapVertices = 0;
                lr.numCornerVertices = 0;
                lines.Add(go);
            }

            // create lines along Z (varying X)
            for (int i = 0; i <= count; i++)
            {
                float x = start + i * gridSize;
                var go = new GameObject($"grid_z_{i}");
                go.transform.SetParent(root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = mat;
                bool major = (i % majorEvery) == 0;
                lr.startWidth = lr.endWidth = major ? 0.03f : 0.01f;
                lr.positionCount = 2;
                lr.useWorldSpace = false;
                lr.SetPosition(0, new Vector3(x, 0.01f, start));
                lr.SetPosition(1, new Vector3(x, 0.01f, end));
                lr.numCapVertices = 0;
                lr.numCornerVertices = 0;
                lines.Add(go);
            }
        }
    }
}
