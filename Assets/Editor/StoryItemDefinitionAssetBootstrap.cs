using HauntedHouse.Inventory;
using UnityEditor;
using UnityEngine;

namespace HauntedHouse.EditorTools
{
    /// <summary>
    /// 스토리 아이템 중 아직 존재하지 않는 정적 ItemDefinition 에셋만 보충한다.
    /// 기존 에셋은 절대로 수정하지 않는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoryItemDefinitionAssetBootstrap
    {
        private const string FuseAssetPath = "Assets/Data/Items/Fuse.asset";

        static StoryItemDefinitionAssetBootstrap()
        {
            EditorApplication.delayCall += EnsureFuseAsset;
        }

        private static void EnsureFuseAsset()
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
        }
    }
}
