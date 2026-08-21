using UnityEngine;
using UnityEditor;
using System.IO;

public class MakeModularPrefabsClean : Editor
{
    [MenuItem("Tools/Fix Walls and Floors Mesh Offset (Center Geometry)")]
    public static void CenterModularMeshes()
    {
        string modularMeshFolder = "Assets/Prefabs/ModularMeshes";
        if (!Directory.Exists(modularMeshFolder))
        {
            Directory.CreateDirectory(modularMeshFolder);
            AssetDatabase.Refresh();
        }

        string[] targetFolders = new string[] { "Assets/Prefabs/Walls", "Assets/Prefabs/Floors" };
        string[] guids = AssetDatabase.FindAssets("t:Prefab", targetFolders);

        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            bool modified = false;
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;

                Mesh origMesh = mf.sharedMesh;
                Vector3 center = origMesh.bounds.center;

                // Create a clean new mesh with vertices centered at (0,0,0)
                Mesh centeredMesh = Object.Instantiate(origMesh);
                centeredMesh.name = $"{mf.gameObject.name}_Modular";

                Vector3[] verts = centeredMesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] -= center;
                }
                centeredMesh.vertices = verts;
                centeredMesh.RecalculateBounds();
                centeredMesh.RecalculateNormals();

                // Save as a permanent .asset mesh file
                string prefabName = Path.GetFileNameWithoutExtension(path);
                string assetPath = $"{modularMeshFolder}/{prefabName}_{mf.gameObject.name}.asset";

                AssetDatabase.CreateAsset(centeredMesh, assetPath);
                Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

                mf.sharedMesh = savedMesh;
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc != null)
                {
                    mc.sharedMesh = savedMesh;
                }

                modified = true;
            }

            if (modified)
            {
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                count++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Modular Centering Complete!", 
            $"Successfully centered mesh geometry at (0,0,0) for {count} Walls and Floors prefabs!", "OK");
        Debug.Log($"[MakeModularPrefabsClean] Centered {count} prefabs.");
    }
}
