using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MultiSelect
{
    [BepInPlugin("com.AstonBratley.MultiSelect", "MultiSelect Furniture", "1.0.0")]
    public class MultiSelectPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<Key> ToggleSelectionKey;
        internal static ConfigEntry<Key> GroupPickKey;
        internal static ConfigEntry<Key> ConfirmGroupKey;
        internal static ConfigEntry<Key> ClearSelectionKey;
        internal static ConfigEntry<Color> SelectionColor;
        internal static ConfigEntry<Color> GroupGhostColor;
        internal static ConfigEntry<float> HighlightScale;

        internal static ManualLogSource L;

        InputAction leftClick;
        InputAction toggleSelection;
        InputAction pickGroup;
        InputAction confirmGroup;
        InputAction clearSelection;

        readonly List<PlaceableShipObject> selected = new List<PlaceableShipObject>();
        readonly Dictionary<PlaceableShipObject, GameObject> highlights = new Dictionary<PlaceableShipObject, GameObject>();

        bool selectionMode = false;
        bool groupMoving = false;

        GameObject groupRoot;
        List<Vector3> localOffsets = new List<Vector3>();
        List<Quaternion> originalRotations = new List<Quaternion>();
        List<Mesh> groupMeshes = new List<Mesh>();
        Material highlightMat;
        Material ghostMat;

        void Awake()
        {
            L = Logger;

            ToggleSelectionKey = Config.Bind("Keys", "ToggleSelection", Key.F3, "Toggle multi-select mode");
            GroupPickKey = Config.Bind("Keys", "PickUpGroup", Key.G, "Pick up selected group to move");
            ConfirmGroupKey = Config.Bind("Keys", "ConfirmGroup", Key.F, "Confirm group placement");
            ClearSelectionKey = Config.Bind("Keys", "ClearSelection", Key.Escape, "Clear current selection");

            SelectionColor = Config.Bind("Visual", "SelectionColor", new Color(0f, 1f, 1f, 0.35f), "Color used to highlight selected objects");
            GroupGhostColor = Config.Bind("Visual", "GroupGhostColor", new Color(1f, 0.85f, 0.2f, 0.45f), "Color used for group ghost previews");
            HighlightScale = Config.Bind("Visual", "HighlightScale", 1.02f, "Scale multiplier for selection highlight mesh");

            // input actions
            leftClick = new InputAction(binding: "<Mouse>/leftButton");
            toggleSelection = new InputAction(binding: $"<Keyboard>/{ToggleSelectionKey.Value.ToString().ToLower()}");
            pickGroup = new InputAction(binding: $"<Keyboard>/{GroupPickKey.Value.ToString().ToLower()}");
            confirmGroup = new InputAction(binding: $"<Keyboard>/{ConfirmGroupKey.Value.ToString().ToLower()}");
            clearSelection = new InputAction(binding: $"<Keyboard>/{ClearSelectionKey.Value.ToString().ToLower()}");

            leftClick.performed += _ => OnLeftClick();
            toggleSelection.performed += _ => ToggleSelectionMode();
            pickGroup.performed += _ => ToggleGroupMove();
            confirmGroup.performed += _ => ConfirmGroupPlace();
            clearSelection.performed += _ => ClearSelection();

            leftClick.Enable();
            toggleSelection.Enable();
            pickGroup.Enable();
            confirmGroup.Enable();
            clearSelection.Enable();

            // materials
            highlightMat = new Material(Shader.Find("Unlit/Color")) { color = SelectionColor.Value };
            ghostMat = new Material(Shader.Find("Unlit/Color")) { color = GroupGhostColor.Value };

            groupRoot = new GameObject("MultiSelect_GroupRoot");
            groupRoot.SetActive(false);

            var h = new Harmony("com.AstonBratley.MultiSelect");
            h.PatchAll();

            L.LogInfo("MultiSelect loaded");
        }

        void OnDestroy()
        {
            leftClick.Disable();
            toggleSelection.Disable();
            pickGroup.Disable();
            confirmGroup.Disable();
            clearSelection.Disable();

            Destroy(highlightMat);
            Destroy(ghostMat);
            Destroy(groupRoot);

            Harmony.UnpatchAll("com.AstonBratley.MultiSelect");
        }

        void ToggleSelectionMode()
        {
            selectionMode = !selectionMode;
            HUDManager.Instance.DisplayTip("MultiSelect", $"Selection mode {(selectionMode ? "ON" : "OFF")}", false, false, "LC_MultiSelect");
            L.LogInfo($"Selection mode {(selectionMode ? "ON" : "OFF")}");
            if (!selectionMode) ClearSelection();
        }

        void OnLeftClick()
        {
            if (!selectionMode || GameNetworkManager.Instance == null) return;

            var cam = GameNetworkManager.Instance.localPlayerController.gameplayCamera;
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit hit;
            int mask = 67108864; // same placeableShipObjectsMask used in ShipBuildModeManager
            if (Physics.Raycast(ray, out hit, 6f, mask, QueryTriggerInteraction.Ignore))
            {
                var go = hit.collider.gameObject;
                if (go.CompareTag("PlaceableObject"))
                {
                    var p = go.GetComponent<PlaceableShipObject>();
                    if (p != null)
                    {
                        if (selected.Contains(p))
                        {
                            RemoveHighlight(p);
                            selected.Remove(p);
                        }
                        else
                        {
                            selected.Add(p);
                            CreateHighlight(p);
                        }
                        UpdateHudTip();
                    }
                }
            }
        }

        void CreateHighlight(PlaceableShipObject p)
        {
            var mf = p.mainMesh;
            var holder = new GameObject("MS_Highlight");
            holder.transform.SetParent(mf.transform, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one * HighlightScale.Value;

            var mf2 = holder.AddComponent<MeshFilter>();
            mf2.mesh = mf.mesh;
            var mr2 = holder.AddComponent<MeshRenderer>();
            mr2.material = highlightMat;
            highlights[p] = holder;
        }

        void RemoveHighlight(PlaceableShipObject p)
        {
            var go = highlights[p];
            Object.Destroy(go);
            highlights.Remove(p);
        }

        void ClearSelection()
        {
            foreach (var kv in new List<KeyValuePair<PlaceableShipObject, GameObject>>(highlights))
            {
                Object.Destroy(kv.Value);
            }
            highlights.Clear();
            selected.Clear();
            HUDManager.Instance.DisplayTip("MultiSelect", "Selection cleared", false, false, "LC_MultiSelect");
        }

        void UpdateHudTip()
        {
            HUDManager.Instance.DisplayTip("MultiSelect", $"Selected: {selected.Count} (G to pick up, Esc to clear)", false, false, "LC_MultiSelect");
        }

        void ToggleGroupMove()
        {
            if (groupMoving)
            {
                CancelGroupMove();
                return;
            }

            if (selected.Count == 0)
            {
                HUDManager.Instance.DisplayTip("MultiSelect", "No objects selected", false, false, "LC_MultiSelect");
                return;
            }

            StartGroupMove();
        }

        void StartGroupMove()
        {
            groupMoving = true;
            groupRoot.SetActive(true);
            groupRoot.transform.SetParent(StartOfRound.Instance.elevatorTransform, false);

            // compute group center in elevator local space
            Vector3 centerWorld = Vector3.zero;
            foreach (var p in selected) centerWorld += p.parentObject.transform.position;
            centerWorld /= selected.Count;
            Vector3 centerLocal = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(centerWorld);

            localOffsets.Clear();
            originalRotations.Clear();
            groupMeshes.Clear();

            foreach (var p in selected)
            {
                var worldPos = p.parentObject.transform.position;
                var local = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(worldPos) - centerLocal;
                localOffsets.Add(local);
                originalRotations.Add(p.parentObject.transform.rotation);
                groupMeshes.Add(p.mainMesh.mesh);

                // remove highlight while moving
                if (highlights.ContainsKey(p))
                {
                    Object.Destroy(highlights[p]);
                    highlights.Remove(p);
                }
            }

            // build ghost children
            for (int i = 0; i < selected.Count; i++)
            {
                var go = new GameObject($"MS_Ghost_{i}");
                go.transform.SetParent(groupRoot.transform, false);
                go.transform.localPosition = localOffsets[i];
                go.transform.localRotation = Quaternion.identity;
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = groupMeshes[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.material = ghostMat;
            }

            HUDManager.Instance.DisplayTip("MultiSelect", "Group picked up. Move and press F to confirm or Esc to cancel.", false, false, "LC_MultiSelect");
        }

        void CancelGroupMove()
        {
            groupMoving = false;
            foreach (Transform t in groupRoot.transform)
                Object.Destroy(t.gameObject);
            groupRoot.SetActive(false);
            HUDManager.Instance.DisplayTip("MultiSelect", "Group move cancelled", false, false, "LC_MultiSelect");

            // restore highlights for selected
            foreach (var p in selected) CreateHighlight(p);
        }

        void ConfirmGroupPlace()
        {
            if (!groupMoving) return;

            // place each object at group's current position + offset
            var manager = ShipBuildModeManager.Instance;
            var localCenter = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(groupRoot.transform.position);

            for (int i = 0; i < selected.Count; i++)
            {
                var p = selected[i];
                var newLocal = localCenter + localOffsets[i];
                var newWorld = StartOfRound.Instance.elevatorTransform.TransformPoint(newLocal);

                // preserve original rotation (could be extended to rotate group)
                var newRot = originalRotations[i].eulerAngles;

                // pass the NetworkObject directly; implicit conversion to NetworkObjectReference happens in method signature
                var netObj = p.parentObject.GetComponent<NetworkObject>();
                manager.PlaceShipObjectServerRpc(newWorld, newRot, netObj, (int)GameNetworkManager.Instance.localPlayerController.playerClientId);
            }

            // cleanup
            groupMoving = false;
            foreach (Transform t in groupRoot.transform) Object.Destroy(t.gameObject);
            groupRoot.SetActive(false);
            HUDManager.Instance.DisplayTip("MultiSelect", "Group placed", false, false, "LC_MultiSelect");

            // clear selection (optional); keep selected but re-highlight
            foreach (var p in selected) CreateHighlight(p);
        }

        void Update()
        {
            if (!groupMoving) return;

            // move groupRoot with camera raycast similar to game ghost behavior (simple ground snap)
            var cam = GameNetworkManager.Instance.localPlayerController.gameplayCamera;
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit hit;
            int placementMask = 2305; // same as in manager
            if (Physics.Raycast(ray, out hit, 6f, placementMask, QueryTriggerInteraction.Ignore))
            {
                groupRoot.transform.position = hit.point + Vector3.up * 0.01f;
            }
            else if (Physics.Raycast(ray.GetPoint(6f), Vector3.down, out hit, 20f, placementMask, QueryTriggerInteraction.Ignore))
            {
                groupRoot.transform.position = hit.point + Vector3.up * 0.01f;
            }
        }
    }
}
