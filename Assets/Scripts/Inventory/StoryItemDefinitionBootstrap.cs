#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace HauntedHouse.Inventory
{
    /// <summary>
    /// 스토리 진행에 필요한 고유 ItemDefinition 중 아직 존재하지 않는 에셋만 생성한다.
    /// 이미 존재하는 에셋은 수정하지 않아 병렬 작업의 변경을 덮어쓰지 않는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoryItemDefinitionBootstrap
    {
        private const string FuseAssetPath = "Assets/Data/Items/Fuse.asset";

        static StoryItemDefinitionBootstrap()
        {
            EnsureAssets();
        }

        private static void EnsureAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<ItemDefinition>(FuseAssetPath) != null)
            {
                return;
            }

            ItemDefinition fuse = ScriptableObject.CreateInstance<ItemDefinition>();
            fuse.name = "Fuse";

            SerializedObject serializedFuse = new SerializedObject(fuse);
            serializedFuse.FindProperty("itemId").stringValue = "fuse";
            serializedFuse.FindProperty("displayName").stringValue = "퓨즈";
            serializedFuse.FindProperty("description").stringValue = "발전기에 사용할 수 있는 퓨즈.";
            serializedFuse.FindProperty("icon").objectReferenceValue = null;
            serializedFuse.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(fuse, FuseAssetPath);
            AssetDatabase.SaveAssets();
            GameLog.Log($"스토리 아이템 에셋을 생성했습니다: {FuseAssetPath}");
        }
    }
}
#endif
