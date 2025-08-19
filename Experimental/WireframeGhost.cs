using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GhostWireframe
{
    [BepInPlugin("com.AstonBratley.GhostWireframe", "Ghost Wireframe", "1.0.0")]
    public class GhostWireframePlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<Color> WireColor;
        internal static ConfigEntry<bool> HideSolidGhost;
        internal static ConfigEntry<string> WireShader; // shader name

        internal static GameObject WireRoot;
        internal static Material WireMaterial;

        public static GhostWireframePlugin Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger { get; private set; } = null!;

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            WireColor = Config.Bind("Visual", "WireColor", new Color(0f, 1f, 1f, 1f), "Color of the wireframe lines");
            HideSolidGhost = Config.Bind("Visual", "HideSolidGhost", true, "Hide the original solid ghost material while wireframe is shown");
            WireShader = Config.Bind("Visual", "Shader", "Unlit/Color", "Shader to use for wire material (Unlit/Color recommended)");

            WireRoot = new GameObject("GhostWireframeRoot");
            WireRoot.SetActive(false);

            var shader = Shader.Find(WireShader.Value);
            WireMaterial = new Material(shader);
            WireMaterial.color = WireColor.Value;

            var harmony = new Harmony("com.AstonBratley.GhostWireframe");
            harmony.PatchAll();

            Logger.LogInfo("Ghost Wireframe loaded");
        }

        private void OnDestroy()
        {
            Harmony.UnpatchAll("com.AstonBratley.GhostWireframe");
            Destroy(WireRoot);
            Destroy(WireMaterial);
        }
    }

    [HarmonyPatch(typeof(ShipBuildModeManager), "CreateGhostObjectAndHighlight")]
    public static class ShipBuildModeManager_CreateGhostWire_Patch
    {
        static void Postfix(ShipBuildModeManager __instance)
        {
            // create a wireframe child under the ghostObjectMesh to mirror the visible ghost mesh
            var meshFilter = __instance.ghostObjectMesh;
            var mesh = meshFilter.mesh;

            // create holder
            var holder = new GameObject("GhostWireframe");
            holder.transform.SetParent(meshFilter.transform, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;

            // build line mesh from original mesh edges
            var wireMesh = BuildLineMeshFromMesh(mesh);

            var mf = holder.AddComponent<MeshFilter>();
            mf.mesh = wireMesh;

            var mr = holder.AddComponent<MeshRenderer>();
            mr.material = GhostWireframePlugin.WireMaterial;

            // show root
            GhostWireframePlugin.WireRoot.SetActive(true);
            holder.transform.SetParent(GhostWireframePlugin.WireRoot.transform, true);

            if (GhostWireframePlugin.HideSolidGhost.Value)
            {
                __instance.ghostObjectRenderer.enabled = false;
            }
        }

        // convert mesh triangles into unique edges and emit a line-only mesh
        static Mesh BuildLineMeshFromMesh(Mesh source)
        {
            var verts = source.vertices;
            var tris = source.triangles;

            var edgeSet = new HashSet<long>();
            var edges = new List<int>();

            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];
                int c = tris[i + 2];

                AddEdge(edgeSet, edges, a, b);
                AddEdge(edgeSet, edges, b, c);
                AddEdge(edgeSet, edges, c, a);
            }

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.SetIndices(edges.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddEdge(HashSet<long> set, List<int> edges, int i1, int i2)
        {
            int a = Mathf.Min(i1, i2);
            int b = Mathf.Max(i1, i2);
            long key = ((long)a << 32) | (long)b;
            if (!set.Contains(key))
            {
                set.Add(key);
                edges.Add(a);
                edges.Add(b);
            }
        }
    }

    [HarmonyPatch(typeof(ShipBuildModeManager), "CancelBuildMode")]
    public static class ShipBuildModeManager_CancelBuildWire_Patch
    {
        static void Postfix(ShipBuildModeManager __instance, bool cancelBeforePlacement = true)
        {
            // hide and clear wireframe objects
            var root = GhostWireframePlugin.WireRoot;
            foreach (Transform t in root.transform)
            {
                Object.Destroy(t.gameObject);
            }
            root.SetActive(false);

            if (GhostWireframePlugin.HideSolidGhost.Value)
            {
                __instance.ghostObjectRenderer.enabled = true;
            }
        }
    }
}
