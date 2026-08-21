using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class RestoreRoomsPerfectly : Editor
{
    [MenuItem("Tools/3. Perfectly Restore Rooms Prefabs")]
    public static void RestoreRooms()
    {
        // 1. Build dictionary of all original FBX meshes in Assets/Abandoned_Asylum/Models
        Dictionary<string, Mesh> meshDict = new Dictionary<string, Mesh>();

        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new string[] { "Assets/Abandoned_Asylum/Models" });
        foreach (string guid in fbxGuids)
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (Object obj in assets)
            {
                if (obj is Mesh mesh)
                {
                    if (!meshDict.ContainsKey(mesh.name))
                    {
                        meshDict.Add(mesh.name, mesh);
                    }
                }
            }
        }

        Debug.Log($"[RestoreRooms] Indexed {meshDict.Count} original FBX meshes.");

        // 2. Iterate through all Rooms prefabs
        string[] roomGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { "Assets/Prefabs/Rooms" });
        int restoredCount = 0;

        foreach (string guid in roomGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) continue;

            bool modified = false;

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in filters)
            {
                // Clean name (remove " (1)", " (2)", etc.)
                string cleanName = mf.gameObject.name;
                int parenIdx = cleanName.IndexOf(" (");
                if (parenIdx > 0)
                {
                    cleanName = cleanName.Substring(0, parenIdx);
                }

                // Try to find matching original mesh
                if (meshDict.TryGetValue(cleanName, out Mesh origMesh) || meshDict.TryGetValue(mf.gameObject.name, out origMesh))
                {
                    Vector3 origCenter = origMesh.bounds.center;

                    // If the current mesh is null or a generated centered mesh, revert position and mesh
                    if (mf.sharedMesh == null || mf.sharedMesh.name.EndsWith("_centered") || mf.sharedMesh != origMesh)
                    {
                        // Check if localPosition was previously shifted by center
                        // Revert position by subtracting center
                        mf.transform.localPosition -= origCenter;
                        mf.sharedMesh = origMesh;

                        MeshCollider mc = mf.GetComponent<MeshCollider>();
                        if (mc != null)
                        {
                            mc.sharedMesh = origMesh;
                        }

                        modified = true;
                    }
                }
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                restoredCount++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Rooms Restoration Complete", 
            $"Successfully restored original meshes and positions for {restoredCount} Rooms prefabs!", "OK");
        Debug.Log($"[RestoreRooms] Restored {restoredCount} Rooms prefabs.");
    }
}
