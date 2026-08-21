using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class FixAllRoomsClean : Editor
{
    [MenuItem("Tools/6. Fix All Rooms Alignment & Revert Overwrites")]
    public static void FixRoomsAlignment()
    {
        // 1. Force re-import all FBX models from disk
        string modelsFolder = "Assets/Abandoned_Asylum/Models";
        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new string[] { modelsFolder });

        foreach (string guid in fbxGuids)
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }

        // 2. Delete generated meshes folder
        string genMeshFolder = "Assets/Prefabs/GeneratedMeshes";
        if (Directory.Exists(genMeshFolder))
        {
            AssetDatabase.DeleteAsset(genMeshFolder);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 3. Load Asylum.fbx sub-meshes
        string asylumFbxPath = "Assets/Abandoned_Asylum/Models/Asylum.fbx";
        Object[] asylumSubAssets = AssetDatabase.LoadAllAssetsAtPath(asylumFbxPath);
        Dictionary<string, Mesh> asylumMeshDict = new Dictionary<string, Mesh>();
        foreach (Object obj in asylumSubAssets)
        {
            if (obj is Mesh m)
            {
                asylumMeshDict[m.name] = m;
            }
        }

        // Load all individual prop meshes
        Dictionary<string, Mesh> propMeshDict = new Dictionary<string, Mesh>();
        foreach (string guid in fbxGuids)
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            if (fbxPath.EndsWith("Asylum.fbx")) continue;

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            foreach (Object obj in subAssets)
            {
                if (obj is Mesh m)
                {
                    propMeshDict[m.name] = m;
                }
            }
        }

        // 4. Process all Rooms prefabs
        string[] roomGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { "Assets/Prefabs/Rooms" });
        int fixedCount = 0;

        foreach (string guid in roomGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) continue;

            bool modified = false;
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);

            foreach (var mf in filters)
            {
                string cleanName = mf.gameObject.name;
                int parenIdx = cleanName.IndexOf(" (");
                if (parenIdx > 0)
                {
                    cleanName = cleanName.Substring(0, parenIdx);
                }

                // A. Check if it's an Asylum.fbx structural mesh (Wall, Floor, Roof, etc.)
                if (asylumMeshDict.TryGetValue(cleanName, out Mesh asylumMesh) || asylumMeshDict.TryGetValue(mf.gameObject.name, out asylumMesh))
                {
                    mf.sharedMesh = asylumMesh;
                    MeshCollider mc = mf.GetComponent<MeshCollider>();
                    if (mc != null) mc.sharedMesh = asylumMesh;

                    // Structural meshes in Asylum.fbx belong at localPosition (0,0,0) relative to room root
                    mf.transform.localPosition = Vector3.zero;
                    mf.transform.localRotation = Quaternion.identity;
                    mf.transform.localScale = Vector3.one;

                    modified = true;
                }
                // B. Check if it's an individual prop mesh (Furniture, props)
                else if (propMeshDict.TryGetValue(cleanName, out Mesh propMesh) || propMeshDict.TryGetValue(mf.gameObject.name, out propMesh))
                {
                    mf.sharedMesh = propMesh;
                    MeshCollider mc = mf.GetComponent<MeshCollider>();
                    if (mc != null) mc.sharedMesh = propMesh;

                    modified = true;
                }
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                fixedCount++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("All Rooms Repaired!", 
            $"Successfully re-aligned structural walls/floors and restored original meshes for {fixedCount} Rooms prefabs!", "OK");
        GameLog.Log($"[FixAllRoomsClean] Repaired {fixedCount} Rooms prefabs.");
    }
}
