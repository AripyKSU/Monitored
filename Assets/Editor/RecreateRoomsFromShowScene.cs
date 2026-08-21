using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

public class RecreateRoomsFromShowScene : Editor
{
    [MenuItem("Tools/4. Recreate Rooms Prefabs from Original Show Scene")]
    public static void RecreateRooms()
    {
        string showScenePath = "Assets/Abandoned_Asylum/Show.unity";
        if (!File.Exists(showScenePath))
        {
            GameLog.LogError($"[RecreateRooms] Original scene file not found at {showScenePath}");
            return;
        }

        // Open original Show scene additively
        Scene showScene = EditorSceneManager.OpenScene(showScenePath, OpenSceneMode.Additive);
        GameObject[] rootObjects = showScene.GetRootGameObjects();

        string targetFolder = "Assets/Prefabs/Rooms";
        string[] existingRoomPrefabs = Directory.GetFiles(targetFolder, "*.prefab");

        int count = 0;

        foreach (string prefabPath in existingRoomPrefabs)
        {
            string roomName = Path.GetFileNameWithoutExtension(prefabPath);
            GameObject targetObject = FindGameObjectByName(rootObjects, roomName);

            if (targetObject != null)
            {
                // Duplicate intact original object from Show scene
                GameObject clone = Object.Instantiate(targetObject);
                clone.name = roomName;

                // Move clone to root of active scene for clean saving
                clone.transform.SetParent(null);
                clone.transform.position = Vector3.zero;
                clone.transform.rotation = Quaternion.identity;

                // Overwrite room prefab with clean original scene object
                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                Object.DestroyImmediate(clone);
                count++;
            }
            else
            {
                GameLog.LogWarning($"[RecreateRooms] Could not find original '{roomName}' in Show.unity.");
            }
        }

        EditorSceneManager.CloseScene(showScene, true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Rooms Fully Recreated!", 
            $"Successfully recreated {count} / {existingRoomPrefabs.Length} Rooms prefabs from original untouched Show.unity scene!", "OK");
        GameLog.Log($"[RecreateRooms] Recreated {count} Rooms prefabs.");
    }

    private static GameObject FindGameObjectByName(GameObject[] rootObjects, string name)
    {
        foreach (var root in rootObjects)
        {
            if (root.name == name) return root;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t.gameObject.name == name)
                {
                    return t.gameObject;
                }
            }
        }
        return null;
    }
}
