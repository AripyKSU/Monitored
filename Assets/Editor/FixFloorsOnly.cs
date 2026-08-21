using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class FixFloorsOnly : Editor
{
    [MenuItem("Tools/Fix Invisible Floors (Restore Original Meshes)")]
    public static void RepairFloors()
    {
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

        string floorsFolder = "Assets/Prefabs/Floors";
        string[] floorGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { floorsFolder });
        int fixedCount = 0;

        foreach (string guid in floorGuids)
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

                Mesh origMesh = null;
                if (asylumMeshDict.TryGetValue(cleanName, out origMesh) || asylumMeshDict.TryGetValue(mf.gameObject.name, out origMesh))
                {
                    mf.sharedMesh = origMesh;

                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null) mr.enabled = true;

                    MeshCollider mc = mf.GetComponent<MeshCollider>();
                    if (mc != null) mc.sharedMesh = origMesh;

                    modified = true;
                }
                else
                {
                    Debug.LogWarning($"[FixFloorsOnly] Could not find mesh for floor '{mf.gameObject.name}' in Asylum.fbx");
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

        EditorUtility.DisplayDialog("Floors Fixed!", 
            $"Successfully restored original meshes for {fixedCount} / {floorGuids.Length} Floor prefabs!", "OK");
        Debug.Log($"[FixFloorsOnly] Fixed {fixedCount} floor prefabs.");
    }
}
