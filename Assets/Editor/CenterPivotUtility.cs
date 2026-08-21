using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using System.IO;

public class CenterPivotUtility : Editor
{
    [MenuItem("Tools/Center Pivot of Modular Prefabs (Walls, Floors Only)")]
    public static void CenterAllPrefabsPivot()
    {
        string[] searchFolders = new string[] { "Assets/Prefabs/Walls", "Assets/Prefabs/Floors" };
        string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);

        if (guids == null || guids.Length == 0)
        {
            GameLog.LogWarning("[CenterPivotUtility] No prefabs found in Assets/Prefabs!");
            return;
        }

        string genMeshFolder = "Assets/Prefabs/GeneratedMeshes";
        if (!Directory.Exists(genMeshFolder))
        {
            Directory.CreateDirectory(genMeshFolder);
            AssetDatabase.Refresh();
        }

        int processedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);

            if (prefabRoot == null) continue;

            bool isModified = CenterPivotForGameObject(prefabRoot, path, genMeshFolder);

            if (isModified)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                processedCount++;
            }

            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Pivot Centering Complete", 
            $"Successfully fixed and centered pivots for {processedCount} prefab(s) under Assets/Prefabs!", "OK");
        GameLog.Log($"[CenterPivotUtility] Processed {processedCount} prefabs.");
    }

    private static bool CenterPivotForGameObject(GameObject root, string prefabPath, string genMeshFolder)
    {
        bool modified = false;

        // 1. Process ProBuilderMesh on root or children
        ProBuilderMesh[] pbMeshes = root.GetComponentsInChildren<ProBuilderMesh>(true);
        foreach (var pb in pbMeshes)
        {
            pb.CenterPivot(null);
            pb.ToMesh();
            pb.Refresh();
            modified = true;
        }

        // 2. Process standard MeshFilters (that don't have ProBuilderMesh)
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf.GetComponent<ProBuilderMesh>() != null) continue;

            // Repair missing MeshFilter mesh from MeshCollider if present
            MeshCollider mc = mf.GetComponent<MeshCollider>();
            if (mf.sharedMesh == null && mc != null && mc.sharedMesh != null)
            {
                mf.sharedMesh = mc.sharedMesh;
                modified = true;
            }

            if (mf.sharedMesh == null) continue;

            Mesh mesh = mf.sharedMesh;
            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center;

            if (center.sqrMagnitude < 0.0001f) continue;

            Mesh newMesh = Object.Instantiate(mesh);
            Vector3[] vertices = newMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] -= center;
            }
            newMesh.vertices = vertices;
            newMesh.RecalculateBounds();
            newMesh.RecalculateNormals();

            // Save modified mesh into AssetDatabase so it persists properly
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            string meshAssetPath = $"{genMeshFolder}/{prefabName}_{mf.gameObject.name}_centered.asset";
            
            AssetDatabase.CreateAsset(newMesh, meshAssetPath);
            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);

            mf.sharedMesh = savedMesh;
            if (mc != null)
            {
                mc.sharedMesh = savedMesh;
            }

            mf.transform.localPosition += center;
            modified = true;
        }

        return modified;
    }
}
