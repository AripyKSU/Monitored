using System;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Stealth;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CoreSandbox에 은신·추격·포획 시스템을 연결하는 Editor 설정 도구다.
/// 런타임에 씬을 임의로 검색해 참조를 만들지 않고, 현재 씬에 직렬화된 참조를 저장한다.
/// </summary>
public static class StealthCaptureSystemSetup
{
    private const string ScenePath = "Assets/_3DStealthGame/Scenes/CoreSandbox.unity";

    /// <summary>
    /// CoreSandbox의 GameSession, Target UI, Room_A/HideSpot에 은신 시스템을 연결하고 씬을 저장한다.
    /// </summary>
    [MenuItem("Haunted House/Setup Stealth Capture System")]
    public static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            GameLog.LogError($"활성 씬이 '{ScenePath}'가 아닙니다.");
            return;
        }

        GameObject gameSession = RequireObject("GameSession");
        GameObject playerSystems = GameObject.Find("PlayerSystems") ?? gameSession;
        GameObject enemySystems = GameObject.Find("EnemySystems") ?? gameSession;
        GameObject targetUi = RequireObject("Target UI");
        GameObject hideSpotObject = RequireObject("Room_A/HideSpot");
        Player player = RequireComponent<Player>("Player");
        PlayerRoomTracker playerRoomTracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>();
        TargetCommandRunner commandRunner = UnityEngine.Object.FindFirstObjectByType<TargetCommandRunner>();
        EnemyController[] enemies = UnityEngine.Object.FindObjectsByType<EnemyController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        TargetPresenter[] presenters = UnityEngine.Object.FindObjectsByType<TargetPresenter>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (playerRoomTracker == null)
        {
            throw new InvalidOperationException("씬에 PlayerRoomTracker가 없습니다.");
        }

        StealthHideSystem hideSystem = GetOrAdd<StealthHideSystem>(playerSystems);
        EnemyChaseSystem chaseSystem = GetOrAdd<EnemyChaseSystem>(enemySystems);
        PlayerCaptureDeathSystem captureSystem = GetOrAdd<PlayerCaptureDeathSystem>(playerSystems);
        GetOrAdd<StealthExitButtonView>(targetUi);

        SetObjectReference(hideSystem, "player", player);
        SetObjectReference(hideSystem, "playerRoomTracker", playerRoomTracker);
        SetObjectReference(hideSystem, "enemyChaseSystem", chaseSystem);
        SetObjectArray(hideSystem, "targetPresenters", presenters);

        SetObjectReference(chaseSystem, "player", player);
        SetObjectReference(chaseSystem, "playerRoomTracker", playerRoomTracker);
        SetObjectReference(chaseSystem, "stealthHideSystem", hideSystem);
        SetObjectReference(chaseSystem, "captureDeathSystem", captureSystem);
        SetObjectArray(chaseSystem, "enemies", enemies);
        SetFloat(chaseSystem, "hiddenRoomWaitDuration", 3f);

        SetObjectReference(captureSystem, "stealthHideSystem", hideSystem);
        SetObjectReference(captureSystem, "enemyChaseSystem", chaseSystem);

        if (commandRunner != null)
        {
            SetObjectReference(commandRunner, "stealthHideSystem", hideSystem);
        }

        ConfigureHideSpot(hideSpotObject, hideSystem);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        GameLog.Log("CoreSandbox 은신·추격·포획 시스템 연결이 완료됐습니다.");
    }

    /// <summary>HideSpot의 기존 임시 행동을 은신 Target 행동으로 교체한다.</summary>
    /// <param name="hideSpotObject">Room_A/HideSpot 오브젝트.</param>
    /// <param name="hideSystem">은신 상태를 소유하는 시스템.</param>
    private static void ConfigureHideSpot(GameObject hideSpotObject, StealthHideSystem hideSystem)
    {
        Target target = hideSpotObject.GetComponent<Target>();
        if (target == null)
        {
            throw new InvalidOperationException("Room_A/HideSpot에 Target이 없습니다.");
        }

        StealthHideTarget hideTarget = GetOrAdd<StealthHideTarget>(hideSpotObject);
        SetObjectReference(hideTarget, "stealthHideSystem", hideSystem);
        target.SetAction(hideTarget);
        target.SetSelectable(true);
        EditorUtility.SetDirty(target);
    }

    /// <summary>씬 계층의 필수 오브젝트를 찾아 반환한다.</summary>
    /// <param name="path">GameObject.Find에서 사용하는 경로.</param>
    /// <returns>찾은 오브젝트.</returns>
    private static GameObject RequireObject(string path)
    {
        GameObject gameObject = GameObject.Find(path);
        if (gameObject == null)
        {
            throw new InvalidOperationException($"씬 오브젝트 '{path}'를 찾을 수 없습니다.");
        }

        return gameObject;
    }

    /// <summary>지정한 경로의 컴포넌트를 찾아 반환한다.</summary>
    /// <typeparam name="T">찾을 컴포넌트 유형.</typeparam>
    /// <param name="path">GameObject.Find에서 사용하는 경로.</param>
    /// <returns>찾은 컴포넌트.</returns>
    private static T RequireComponent<T>(string path) where T : Component
    {
        T component = RequireObject(path).GetComponent<T>();
        if (component == null)
        {
            throw new InvalidOperationException($"'{path}'에 {typeof(T).Name} 컴포넌트가 없습니다.");
        }

        return component;
    }

    /// <summary>컴포넌트가 없을 때만 추가해 설정 도구를 여러 번 실행해도 중복을 만들지 않는다.</summary>
    /// <typeparam name="T">추가할 컴포넌트 유형.</typeparam>
    /// <param name="gameObject">컴포넌트를 소유할 오브젝트.</param>
    /// <returns>기존 또는 새 컴포넌트.</returns>
    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        return gameObject.GetComponent<T>() ?? Undo.AddComponent<T>(gameObject);
    }

    /// <summary>SerializedObject를 통해 private Unity 참조 필드를 저장한다.</summary>
    /// <param name="target">설정할 Unity 오브젝트.</param>
    /// <param name="propertyName">SerializedField 이름.</param>
    /// <param name="value">저장할 참조.</param>
    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없습니다.");
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>SerializedObject를 통해 private Unity 배열 참조 필드를 저장한다.</summary>
    /// <param name="target">설정할 Unity 오브젝트.</param>
    /// <param name="propertyName">SerializedField 이름.</param>
    /// <param name="values">저장할 참조 배열.</param>
    private static void SetObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 배열 필드를 찾을 수 없습니다.");
        }

        property.arraySize = values != null ? values.Length : 0;
        for (int index = 0; index < property.arraySize; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>SerializedObject를 통해 private float 설정을 저장한다.</summary>
    /// <param name="target">설정할 Unity 오브젝트.</param>
    /// <param name="propertyName">SerializedField 이름.</param>
    /// <param name="value">저장할 값.</param>
    private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없습니다.");
        }

        property.floatValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
