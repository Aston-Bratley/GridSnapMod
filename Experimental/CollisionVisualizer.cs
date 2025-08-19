using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CollisionVisualizer
{
    [BepInPlugin("com.AstonBratley.CollisionVisualizer", "Collision Visualizer", "1.0.0")]
    public class CollisionVisualizerPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> VisualizerEnabled;
        internal static ConfigEntry<bool> ShowBlockedColliders;
        internal static ConfigEntry<bool> ShowCollisionPointSphere;
        internal static ConfigEntry<Color> BoxColor;
        internal static ConfigEntry<Color> SphereColor;
        internal static ConfigEntry<Color> OverlapColor;
        internal static ConfigEntry<float> LineWidth;
        internal static ConfigEntry<int> MaxOverlapHighlights;

        public static CollisionVisualizerPlugin Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger { get; private set; } = null!;

        GameObject root = null!;
        GameObject boxGO = null!;
        GameObject sphereGO = null!;
        readonly List<GameObject> overlapGOs = new List<GameObject>();

        InputAction toggleAction = null!;

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            VisualizerEnabled = Config.Bind("Visualizer", "Enabled", true, "Enable collision visualization while in build mode.");
            ShowBlockedColliders = Config.Bind("Visualizer", "ShowBlockedColliders", true, "Highlight colliders that block placement.");
            ShowCollisionPointSphere = Config.Bind("Visualizer", "ShowCollisionPointSphere", true, "Show the collision point sphere used for placement checks.");
            BoxColor = Config.Bind("Visualizer", "BoxColor", new Color(0f, 1f, 0f, 0.25f), "Color for the placement check box (when not blocked).");
            SphereColor = Config.Bind("Visualizer", "SphereColor", new Color(1f, 0.5f, 0f, 0.5f), "Color for the collision point sphere.");
            OverlapColor = Config.Bind("Visualizer", "OverlapColor", new Color(1f, 0f, 0f, 0.3f), "Color for overlapping/blocked colliders.");
            LineWidth = Config.Bind("Visualizer", "LineWidth", 0.02f, "Line renderer width for box outlines.");
            MaxOverlapHighlights = Config.Bind("Visualizer", "MaxOverlapHighlights", 16, "Maximum number of overlap highlights to create.");

            // root and primitives
            root = new GameObject("CollisionVisualizerRoot");
            root.SetActive(false);

            boxGO = CreateWireCube("PlacementBox", BoxColor.Value, LineWidth.Value);
            boxGO.transform.SetParent(root.transform, false);

            sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(sphereGO.GetComponent<SphereCollider>()); // no collider
            var sphereMat = new Material(Shader.Find("Standard"));
            sphereMat.color = SphereColor.Value;
            sphereMat.SetFloat("_Mode", 3); // transparent
            sphereMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sphereMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sphereMat.SetInt("_ZWrite", 0);
            sphereMat.DisableKeyword("_ALPHATEST_ON");
            sphereMat.EnableKeyword("_ALPHABLEND_ON");
            sphereMat.renderQueue = 3000;
            sphereGO.GetComponent<MeshRenderer>().material = sphereMat;
            sphereGO.transform.SetParent(root.transform, false);

            for (int i = 0; i < MaxOverlapHighlights.Value; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<BoxCollider>());
                var mat = new Material(Shader.Find("Standard"));
                mat.color = OverlapColor.Value;
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.renderQueue = 3000;
                go.GetComponent<MeshRenderer>().material = mat;
                go.transform.SetParent(root.transform, false);
                go.SetActive(false);
                overlapGOs.Add(go);
            }

            // input toggle (F8)
            toggleAction = new InputAction(binding: "<Keyboard>/f8");
            toggleAction.performed += _ =>
            {
                VisualizerEnabled.Value = !VisualizerEnabled.Value;
                if (VisualizerEnabled.Value)
                {
                    HUDManager.Instance.DisplayTip("Collision Visualizer", "Visualizer enabled (F8 to toggle)", false, false, "LC_CollisionVis");
                    Logger.LogInfo("Collision Visualizer enabled");
                }
                else
                {
                    HUDManager.Instance.DisplayTip("Collision Visualizer", "Visualizer disabled (F8 to toggle)", false, false, "LC_CollisionVis");
                    Logger.LogInfo("Collision Visualizer disabled");
                }
                UpdateRootState();
            };
            toggleAction.Enable();

            Harmony harmony = new Harmony("com.AstonBratley.CollisionVisualizer");
            harmony.PatchAll();
            Logger.LogInfo("Collision Visualizer loaded");
        }

        private void OnDestroy()
        {
            toggleAction.Disable();
            Object.Destroy(root);
            Harmony.UnpatchAll("com.AstonBratley.CollisionVisualizer");
        }

        static GameObject CreateWireCube(string name, Color color, float width)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            lr.material = mat;
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = 16;
            lr.useWorldSpace = false;
            // positions will be set later
            return go;
        }

        void UpdateRootState()
        {
            if (root == null) return;
            root.SetActive(VisualizerEnabled.Value && ShipBuildModeManager.Instance != null && ShipBuildModeManager.Instance.InBuildMode);
        }

        // Called from patch to update visuals each frame
        internal void UpdateVisuals(Vector3 boxCenter, Vector3 halfExtents, Quaternion rotation, bool blocked, Vector3? collisionPoint, Collider[] overlaps)
        {
            if (!VisualizerEnabled.Value) return;

            UpdateRootState();
            if (!root.activeSelf) return;

            // Update boxGO wireframe
            UpdateWireCube(boxGO, boxCenter, halfExtents, rotation, blocked ? OverlapColor.Value : BoxColor.Value);

            // Update sphere
            if (collisionPoint.HasValue && ShowCollisionPointSphere.Value)
            {
                sphereGO.SetActive(true);
                sphereGO.transform.position = collisionPoint.Value;
                sphereGO.transform.localScale = Vector3.one * 2f; // matches original CheckSphere radius 1f
            }
            else
            {
                sphereGO.SetActive(false);
            }

            // Update overlaps highlights
            int i = 0;
            if (ShowBlockedColliders.Value && overlaps != null)
            {
                for (; i < overlaps.Length && i < overlapGOs.Count; i++)
                {
                    var c = overlaps[i];
                    var go = overlapGOs[i];
                    go.SetActive(true);
                    var b = c.bounds;
                    go.transform.position = b.center;
                    go.transform.rotation = Quaternion.identity;
                    go.transform.localScale = b.size;
                }
            }

            for (; i < overlapGOs.Count; i++)
            {
                overlapGOs[i].SetActive(false);
            }
        }

        static void UpdateWireCube(GameObject go, Vector3 center, Vector3 halfExtents, Quaternion rotation, Color color)
        {
            var lr = go.GetComponent<LineRenderer>();
            lr.material.color = color;

            // 8 corners in local space
            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z);
            corners[1] = new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z);
            corners[2] = new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z);
            corners[3] = new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z);

            corners[4] = new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z);
            corners[5] = new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z);
            corners[6] = new Vector3(halfExtents.x, halfExtents.y, halfExtents.z);
            corners[7] = new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z);

            // wireframe order (16 points connecting edges)
            Vector3[] pts = new Vector3[16];
            pts[0] = corners[0];
            pts[1] = corners[1];
            pts[2] = corners[2];
            pts[3] = corners[3];
            pts[4] = corners[0];
            pts[5] = corners[4];
            pts[6] = corners[5];
            pts[7] = corners[1];
            pts[8] = corners[5];
            pts[9] = corners[6];
            pts[10] = corners[2];
            pts[11] = corners[6];
            pts[12] = corners[7];
            pts[13] = corners[3];
            pts[14] = corners[7];
            pts[15] = corners[4];

            for (int i = 0; i < pts.Length; i++)
            {
                // transform local corner to world
                pts[i] = rotation * pts[i] + center;
            }

            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
        }
    }

    // Harmony patches that call the visualizer
    [HarmonyLib.HarmonyPatch(typeof(ShipBuildModeManager), "Update")]
    public static class ShipBuildModeManager_Update_CollisionVisPatch
    {
        static readonly System.Reflection.FieldInfo currentColliderField = AccessTools.Field(typeof(ShipBuildModeManager), "currentCollider");
        static readonly System.Reflection.FieldInfo placingObjectField = AccessTools.Field(typeof(ShipBuildModeManager), "placingObject");
        static readonly System.Reflection.FieldInfo placementMaskAndBlockersField = AccessTools.Field(typeof(ShipBuildModeManager), "placementMaskAndBlockers");

        static void Postfix(ShipBuildModeManager __instance)
        {
            var vis = CollisionVisualizerPlugin.Instance;
            if (vis == null) return;
            if (!CollisionVisualizerPlugin.VisualizerEnabled.Value) 
            {
                vis.UpdateVisuals(Vector3.zero, Vector3.zero, Quaternion.identity, false, null, null);
                return;
            }

            if (!__instance.InBuildMode)
            {
                // hide visuals
                vis.UpdateVisuals(Vector3.zero, Vector3.zero, Quaternion.identity, false, null, null);
                return;
            }

            var placingObject = placingObjectField.GetValue(__instance) as PlaceableShipObject;
            if (placingObject == null) return;

            var ghost = __instance.ghostObject;
            if (ghost == null) return;

            var currentCollider = currentColliderField.GetValue(__instance) as BoxCollider;
            if (currentCollider == null)
            {
                currentCollider = placingObject.placeObjectCollider as BoxCollider;
                currentColliderField.SetValue(__instance, currentCollider);
            }

            Vector3 halfExtents = currentCollider.size * 0.5f * 0.57f; // matches game check
            Quaternion rot = Quaternion.Euler(ghost.eulerAngles);
            Vector3 center = ghost.position;

            int placementMaskAndBlockers = (int)placementMaskAndBlockersField.GetValue(__instance);

            // CheckBox result
            bool blocked = Physics.CheckBox(center, halfExtents, rot, placementMaskAndBlockers, QueryTriggerInteraction.Ignore);

            // collision point sphere check
            Vector3? collPoint = null;
            Collider[] overlaps = null;
            if (!blocked && placingObject.doCollisionPointCheck)
            {
                Vector3 p = center + ghost.forward * placingObject.collisionPointCheck.z + ghost.right * placingObject.collisionPointCheck.x + ghost.up * placingObject.collisionPointCheck.y;
                collPoint = p;
                if (Physics.CheckSphere(p, 1f, placementMaskAndBlockers, QueryTriggerInteraction.Ignore))
                    blocked = true;
            }

            // find overlapping colliders (only if showing blocked colliders)
            if (CollisionVisualizerPlugin.ShowBlockedColliders.Value)
            {
                var hits = Physics.OverlapBox(center, halfExtents, rot, placementMaskAndBlockers, QueryTriggerInteraction.Ignore);
                overlaps = hits;
            }

            vis.UpdateVisuals(center, halfExtents, rot, blocked, collPoint, overlaps);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ShipBuildModeManager), "CreateGhostObjectAndHighlight")]
    public static class ShipBuildModeManager_CreateGhostObjectAndHighlight_CollisionVisPatch
    {
        static void Postfix(ShipBuildModeManager __instance)
        {
            if (CollisionVisualizerPlugin.Instance == null) return;
            if (CollisionVisualizerPlugin.VisualizerEnabled.Value && __instance.InBuildMode)
            {
                CollisionVisualizerPlugin.Instance.UpdateVisuals(Vector3.zero, Vector3.zero, Quaternion.identity, false, null, null);
                CollisionVisualizerPlugin.Instance.root.SetActive(true);
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ShipBuildModeManager), "CancelBuildMode")]
    public static class ShipBuildModeManager_CancelBuildMode_CollisionVisPatch
    {
        static void Postfix(ShipBuildModeManager __instance, bool cancelBeforePlacement = true)
        {
            if (CollisionVisualizerPlugin.Instance == null) return;
            CollisionVisualizerPlugin.Instance.root.SetActive(false);
        }
    }
}
