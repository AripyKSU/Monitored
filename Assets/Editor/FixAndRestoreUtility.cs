using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;
using System.IO;

public class FixAndRestoreUtility : Editor
{
    [MenuItem("Tools/1. Reset Walls & Floors Transform to (0,0,0)")]
    public static void ResetWallsAndFloorsTransform()
    {
        string[] folders = new string[] { "Assets/Prefabs/Walls", "Assets/Prefabs/Floors" };
        string[] guids = AssetDatabase.FindAssets("t:Prefab", folders);

        if (guids == null || guids.Length == 0)
        {
            GameLog.LogWarning("[FixAndRestoreUtility] No prefabs found in Walls or Floors.");
            return;
        }

        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root != null)
            {
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Transform Reset Complete", 
            $"Successfully set Position to (0,0,0) and Rotation to Identity for {count} Walls and Floors prefabs!", "OK");
        GameLog.Log($"[FixAndRestoreUtility] Reset transform to 0,0,0 for {count} prefabs.");
    }

    [MenuItem("Tools/2. Restore Rooms Prefabs (Revert Child Positions & Meshes)")]
    public static void RestoreRoomsPrefabs()
    {
        string[] folders = new string[] { "Assets/Prefabs/Rooms" };
        string[] guids = AssetDatabase.FindAssets("t:Prefab", folders);

        if (guids == null || guids.Length == 0)
        {
            GameLog.LogWarning("[FixAndRestoreUtility] No prefabs found in Assets/Prefabs/Rooms.");
            return;
        }

        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            bool modified = false;

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.GetComponent<ProBuilderMesh>() != null) continue;

                MeshCollider mc = mf.GetComponent<MeshCollider>();
                Mesh currentMesh = mf.sharedMesh;

                // Check if mesh was modified (_centered asset or null)
                bool isModifiedMesh = (currentMesh == null) || (currentMesh.name != null && currentMesh.name.EndsWith("_centered"));

                if (isModifiedMesh)
                {
                    // Try to get original mesh from MeshCollider
                    Mesh origMesh = (mc != null && mc.sharedMesh != null && !mc.sharedMesh.name.EndsWith("_centered")) ? mc.sharedMesh : null;

                    if (origMesh != null)
                    {
                        Vector3 center = origMesh.bounds.center;

                        // Subtract center offset to restore child's original localPosition
                        mf.transform.localPosition -= center;
                        mf.sharedMesh = origMesh;
                        if (mc != null) mc.sharedMesh = origMesh;

                        modified = true;
                    }
                }
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                count++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Rooms Restore Complete", 
            $"Successfully restored child positions and original meshes for {count} Rooms prefabs!", "OK");
        GameLog.Log($"[FixAndRestoreUtility] Restored {count} Rooms prefabs.");
    }
}
