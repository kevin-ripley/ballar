// ARMeshCaptureExporter.cs
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;

public class ARMeshCaptureExporter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARMeshManager meshManager;          // Drag your ARMeshManager here
    [SerializeField] private Transform reviewRoot;               // Empty GameObject to hold loaded mesh in Review Mode
    [SerializeField] private Material reviewMaterial;            // Unlit material for review mesh (assign)
    [SerializeField] private Transform cameraTransform;          // Usually AR Camera (assign)

    [Header("Saving")]
    [SerializeField] private string filePrefix = "Green";
    [SerializeField] private bool centerOnSave = true;           // Recenter OBJ around origin when writing

    [Header("Post-Save (optional)")]
    [SerializeField] private bool analyzeAfterSave = false;
    [SerializeField] private GreenAnalyzer analyzer;             // Drag if you want auto-analyze
    [SerializeField] private float analyzePaddingMeters = 0.5f;  // Expand analyzed bounds (XZ) by this

    [Header("Review Mode")]
    [SerializeField] private float reviewSpawnDistance = 1.5f;   // Meters in front of camera
    [SerializeField] private bool disableMeshingInReview = true;

    public UnityEvent<string> OnSaved; // Invoked with full path of the saved OBJ

    private GameObject _loadedReviewGO;
    private bool _inReviewMode;

    void Awake()
    {
        if (!meshManager)
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
            meshManager = FindFirstObjectByType<ARMeshManager>(FindObjectsInactive.Exclude);
#else
            meshManager = FindObjectOfType<ARMeshManager>();
#endif
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!reviewRoot)
        {
            var go = new GameObject("ReviewRoot");
            reviewRoot = go.transform;
        }
    }

    // ================= PUBLIC API =================

    public string SaveCurrentMesh()
    {
        var meshFilters = CollectMeshFilters();
        if (meshFilters.Count == 0)
        {
            Debug.LogWarning("No AR meshes to export.");
            return null;
        }

        // Merge world-space verts/triangles and compute world bounds
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        Bounds? worldBounds = null;

        foreach (var mf in meshFilters)
        {
            var mesh = mf.sharedMesh;
            if (!mesh) continue;

            int vStart = verts.Count;
            var l2w = mf.transform.localToWorldMatrix;

            var mverts = mesh.vertices;
            for (int i = 0; i < mverts.Length; i++)
            {
                var wv = l2w.MultiplyPoint3x4(mverts[i]);
                verts.Add(wv);
                worldBounds = worldBounds == null ? new Bounds(wv, Vector3.zero) : Enc(worldBounds.Value, wv);
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetIndices(s);
                for (int i = 0; i < indices.Length; i++) tris.Add(vStart + indices[i]);
            }
        }

        // Optional: recenter OBJ about its bounds center (write-time only)
        Vector3 offset = Vector3.zero;
        if (centerOnSave && worldBounds.HasValue)
        {
            offset = worldBounds.Value.center;
            for (int i = 0; i < verts.Count; i++) verts[i] -= offset;
        }

        // Write OBJ
        string dir = Application.persistentDataPath;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(dir, $"{filePrefix}_{timestamp}.obj");
        WriteObj(path, verts, tris);
        Debug.Log($"Saved OBJ: {path}");

        OnSaved?.Invoke(path);

        // Optional: auto-analyze area in-place using the CURRENT world bounds (pre-center)
        if (analyzeAfterSave && analyzer != null && worldBounds.HasValue)
        {
            var b = worldBounds.Value;
            // Expand only XZ by padding
            var size = b.size;
            size.x += analyzePaddingMeters * 2f;
            size.z += analyzePaddingMeters * 2f;
            var expanded = new Bounds(b.center, new Vector3(size.x, size.y, size.z));
            analyzer.AnalyzeBounds(expanded);
        }

        return path;
    }

    public void EnterReviewModeLatest()
    {
        string dir = Application.persistentDataPath;
        var files = Directory.GetFiles(dir, $"{filePrefix}_*.obj");
        if (files.Length == 0) { Debug.LogWarning("No saved OBJ files found."); return; }
        Array.Sort(files, StringComparer.Ordinal);
        EnterReviewModeWithPath(files[^1]);
    }

    public void EnterReviewModeWithPath(string objPath)
    {
        if (!File.Exists(objPath)) { Debug.LogWarning($"OBJ not found: {objPath}"); return; }
        if (_inReviewMode) ExitReviewMode();

        if (disableMeshingInReview && meshManager) meshManager.enabled = false;

        var mesh = ReadObj(objPath);
        if (!mesh) { Debug.LogWarning("Failed to load OBJ."); return; }

        _loadedReviewGO = new GameObject($"ReviewMesh_{Path.GetFileNameWithoutExtension(objPath)}");
        var mf = _loadedReviewGO.AddComponent<MeshFilter>();
        var mr = _loadedReviewGO.AddComponent<MeshRenderer>();
        mf.sharedMesh = mesh;
        mr.sharedMaterial = reviewMaterial ? reviewMaterial : new Material(Shader.Find("Unlit/Color"));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // Let physics/laser/analyzer work in Review
        var col = _loadedReviewGO.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;

        // Place in front of camera
        if (cameraTransform)
        {
            reviewRoot.position = cameraTransform.position + cameraTransform.forward * reviewSpawnDistance;
            reviewRoot.rotation = Quaternion.LookRotation(cameraTransform.forward, Vector3.up);
        }
        _loadedReviewGO.transform.SetParent(reviewRoot, worldPositionStays: true);

        _inReviewMode = true;
        Debug.Log("Entered Review Mode.");
    }

    public void ExitReviewMode()
    {
        if (_loadedReviewGO) Destroy(_loadedReviewGO);
        _loadedReviewGO = null;

        if (disableMeshingInReview && meshManager) meshManager.enabled = true;
        _inReviewMode = false;
        Debug.Log("Exited Review Mode.");
    }

    // ================= INTERNALS =================

    private static Bounds Enc(Bounds b, Vector3 p) { b.Encapsulate(p); return b; }

    private List<MeshFilter> CollectMeshFilters()
    {
        var list = new List<MeshFilter>();
        if (!meshManager) return list;

        // ARFoundation 6: use 'meshes' (IList<MeshFilter>)
        foreach (var mf in meshManager.meshes)
        {
            if (mf && mf.sharedMesh) list.Add(mf);
        }
        return list;
    }

    private static void WriteObj(string path, List<Vector3> verts, List<int> tris)
    {
        var sb = new StringBuilder(verts.Count * 32);
        sb.AppendLine("# Exported by ARMeshCaptureExporter");
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            sb.Append("v ").Append(v.x.ToString("F6")).Append(' ')
                         .Append(v.y.ToString("F6")).Append(' ')
                         .Append(v.z.ToString("F6")).Append('\n');
        }
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i] + 1, b = tris[i + 1] + 1, c = tris[i + 2] + 1;
            sb.Append("f ").Append(a).Append(' ').Append(b).Append(' ').Append(c).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static Mesh ReadObj(string path)
    {
        var verts = new List<Vector3>(8192);
        var tris  = new List<int>(16384);

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("v "))
                {
                    var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    float x = float.Parse(tok[1]);
                    float y = float.Parse(tok[2]);
                    float z = float.Parse(tok[3]);
                    verts.Add(new Vector3(x, y, z));
                }
                else if (line.StartsWith("f "))
                {
                    var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i + 2 < tok.Length; i++)
                    {
                        int a = ParseFaceIndex(tok[1]) - 1;
                        int b = ParseFaceIndex(tok[i + 1]) - 1;
                        int c = ParseFaceIndex(tok[i + 2]) - 1;
                        tris.Add(a); tris.Add(b); tris.Add(c);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = (verts.Count > 65535)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        catch (Exception e)
        {
            Debug.LogError($"OBJ load error: {e.Message}");
            return null;
        }

        static int ParseFaceIndex(string s)
        {
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            return int.Parse(s);
        }
    }
}
