using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class ReimportOriginalModelsAndRestore : Editor
{
    [MenuItem("Tools/5. Force Reimport All Original FBX Models & Repair")]
    public static void ForceReimportAndRepair()
    {
        // 1. Force reimport all FBX models from disk to restore original vertex data
        string modelsFolder = "Assets/Abandoned_Asylum/Models";
        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new string[] { modelsFolder });

        GameLog.Log($"[Reimport] Re-importing {fbxGuids.Length} FBX files from disk...");
        foreach (string guid in fbxGuids)
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }

        // 2. Delete generated meshes folder if exists
        string genMeshFolder = "Assets/Prefabs/GeneratedMeshes";
        if (Directory.Exists(genMeshFolder))
        {
            AssetDatabase.DeleteAsset(genMeshFolder);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 3. Re-index freshly reimported FBX meshes
        Dictionary<string, Mesh> meshDict = new Dictionary<string, Mesh>();
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

        GameLog.Log($"[Reimport] Indexed {meshDict.Count} freshly imported FBX meshes.");

        // 4. Restore Rooms prefabs by re-linking original FBX meshes and resetting localPositions
        string[] roomGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { "Assets/Prefabs/Rooms" });
        int repairedCount = 0;

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

                if (meshDict.TryGetValue(cleanName, out Mesh origMesh) || meshDict.TryGetValue(mf.gameObject.name, out origMesh))
                {
                    mf.sharedMesh = origMesh;

                    MeshCollider mc = mf.GetComponent<MeshCollider>();
                    if (mc != null)
                    {
                        mc.sharedMesh = origMesh;
                    }

                    modified = true;
                }
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                repairedCount++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Complete Re-import & Repair Complete!", 
            $"Successfully re-imported all FBX files from disk and re-linked original meshes for {repairedCount} Rooms prefabs!", "OK");
    }
}
