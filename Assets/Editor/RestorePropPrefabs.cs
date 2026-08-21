using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class RestorePropPrefabs : Editor
{
    [MenuItem("Tools/Restore All Prop Prefabs in Assets-Prefabs-Prefabs")]
    public static void RestoreAllProps()
    {
        string propsFolder = "Assets/Prefabs/Prefabs";
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { propsFolder });

        int count = 0;

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(prefabPath);

            // Clean name if needed
            int parenIdx = name.IndexOf(" ");
            string fbxName = (parenIdx > 0) ? name.Substring(0, parenIdx) : name;

            string fbxPath = $"Assets/Abandoned_Asylum/Models/{fbxName}.fbx";
            if (!File.Exists(fbxPath))
            {
                // Fallback to exact match search in Models
                string[] modelGuids = AssetDatabase.FindAssets($"{name} t:Model", new string[] { "Assets/Abandoned_Asylum/Models" });
                if (modelGuids.Length > 0)
                {
                    fbxPath = AssetDatabase.GUIDToAssetPath(modelGuids[0]);
                }
            }

            if (File.Exists(fbxPath))
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                Mesh origMesh = null;
                foreach (Object obj in subAssets)
                {
                    if (obj is Mesh m)
                    {
                        origMesh = m;
                        break;
                    }
                }

                if (origMesh != null)
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                    if (root != null)
                    {
                        bool modified = false;
                        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                        foreach (var mf in filters)
                        {
                            mf.sharedMesh = origMesh;
                            MeshCollider mc = mf.GetComponent<MeshCollider>();
                            if (mc != null) mc.sharedMesh = origMesh;
                            modified = true;
                        }

                        if (modified)
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                            count++;
                        }
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Props Restoration Complete!", 
            $"Successfully restored original FBX meshes for {count} / {prefabGuids.Length} furniture/prop prefabs in Assets/Prefabs/Prefabs!", "OK");
        Debug.Log($"[RestorePropPrefabs] Restored {count} prop prefabs.");
    }
}
