#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 Sector3 출구 문에 최초 1회 안내 이벤트를 연결하는 Editor 설정 도구다.
/// 기존 Doorway·DoorTarget을 보존하고 방향별 Target의 행동만 ExitDoorHintTarget으로 교체한다.
/// </summary>
public static class HospitalExitDoorHintSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Hospital Sector3 Exit Door Hint";
    private const string ValidateMenuPath = "Haunted House/Validate/Hospital Sector3 Exit Door Hint";
    private const string ReceptionRoomId = "S5_Reception";
    private const string DialogueAssetPath = "Assets/Data/Dialogue/S3_ExitDoorHintDialogue.asset";
    private const string LockedMessage = "잠겨있다.";
    private const string HintDialogueText = "[이 문이 출구이며 마스터키가 필요하다]";

    /// <summary>
    /// 접수처로 이어지는 Sector3 방향 Target을 찾아 출구 힌트 이벤트를 멱등적으로 연결한다.
    /// 이 메서드는 Play 모드가 아닌 CoreSandbox 씬에서 Unity Editor 메뉴로 실행해야 한다.
    /// </summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        GameSession gameSession = FindRequired<GameSession>("GameSession");
        MessagePanel messagePanel = FindRequired<MessagePanel>("MessagePanel");
        DialogueService dialogueService = FindRequired<DialogueService>("DialogueService");
        DoorTarget exitDoor = FindExitDoorTarget();
        Doorway doorway = exitDoor.GetComponentInParent<Doorway>();
        DialogueSequence hintDialogue = GetOrCreateHintDialogue();

        ExitDoorHintTarget wrapper = GetOrAdd<ExitDoorHintTarget>(exitDoor.gameObject);
        SetObject(wrapper, "normalExitDoor", exitDoor);
        SetObject(wrapper, "gameSession", gameSession);
        SetString(wrapper, "masterKeyFlagId", StoryProgressFlags.MasterKeyAcquired);
        SetString(wrapper, "hintShownFlagId", StoryProgressFlags.ExitDoorHintShown);
        SetObject(wrapper, "messagePanel", messagePanel);
        SetString(wrapper, "lockedMessage", LockedMessage);
        SetObject(wrapper, "dialogueService", dialogueService);
        SetObject(wrapper, "hintDialogue", hintDialogue);

        // 20번 이벤트 중에는 문이 잠긴 상태여야 하며, 마스터키 획득 뒤에는 기존 DoorTarget이 해제를 담당한다.
        SetBool(doorway, "locked", true);
        SetObject(exitDoor.GetComponent<Target>(), "actionComponent", wrapper);
        exitDoor.GetComponent<Target>().SetSelectable(true);

        EditorUtility.SetDirty(wrapper);
        EditorUtility.SetDirty(exitDoor);
        EditorUtility.SetDirty(doorway);
        EditorUtility.SetDirty(exitDoor.GetComponent<Target>());
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameLog.Log($"Sector3 출구 문 힌트 이벤트 구성을 완료했습니다: {exitDoor.name} / {doorway.name}.");
    }

    /// <summary>
    /// 출구 문과 전용 행동, 대화 에셋의 필수 참조를 읽기 전용으로 검사한다.
    /// 씬이나 에셋을 변경하지 않으며, 문제가 있으면 하나의 예외 메시지로 묶어 보고한다.
    /// </summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        List<string> errors = new();
        GameSession gameSession = FindFirst<GameSession>();
        MessagePanel messagePanel = FindFirst<MessagePanel>();
        DialogueService dialogueService = FindFirst<DialogueService>();
        DoorTarget exitDoor = FindExitDoorTargetOrNull(errors);
        DialogueSequence hintDialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);

        Check(errors, gameSession != null, "GameSession을 찾지 못했습니다.");
        Check(errors, messagePanel != null, "MessagePanel을 찾지 못했습니다.");
        Check(errors, dialogueService != null, "DialogueService를 찾지 못했습니다.");
        Check(errors, hintDialogue != null, $"출구 안내 Dialogue asset이 없습니다: {DialogueAssetPath}");

        if (hintDialogue != null)
        {
            Check(errors, hintDialogue.PauseGame, "출구 안내 Dialogue는 PauseGame이어야 합니다.");
            Check(errors, hintDialogue.Lines != null && hintDialogue.Lines.Count > 0,
                "출구 안내 Dialogue에 Line이 없습니다.");
        }

        if (exitDoor != null)
        {
            Target target = exitDoor.GetComponent<Target>();
            Doorway doorway = exitDoor.GetComponentInParent<Doorway>();
            ExitDoorHintTarget wrapper = exitDoor.GetComponent<ExitDoorHintTarget>();

            Check(errors, target != null, $"{exitDoor.name}에 Target이 없습니다.");
            Check(errors, doorway != null, $"{exitDoor.name}의 부모 Doorway를 찾지 못했습니다.");
            Check(errors, wrapper != null, $"{exitDoor.name}에 ExitDoorHintTarget이 없습니다.");
            Check(errors, doorway == null || doorway.IsLocked, $"{doorway?.name}이 잠금 상태가 아닙니다.");
            Check(errors, target == null || target.Action is ExitDoorHintTarget,
                $"{exitDoor.name}의 Target 행동이 ExitDoorHintTarget이 아닙니다.");

            if (wrapper != null)
            {
                Check(errors, wrapper.TryValidateConfiguration(out string configurationError), configurationError);
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Sector3 출구 문 힌트 이벤트 검증 실패:\n- " + string.Join("\n- ", errors));
        }

        GameLog.Log("Sector3 출구 문 힌트 이벤트 구성이 유효합니다.");
    }

    /// <summary>현재 씬과 Editor 실행 상태를 확인한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Sector3 출구 문 설정은 Play 모드에서 실행할 수 없습니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>접수처로 이어지는 Sector3·CenterHallway 쪽 DoorTarget을 하나 찾아 반환한다.</summary>
    private static DoorTarget FindExitDoorTarget()
    {
        List<DoorTarget> candidates = FindExitDoorTargets();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "S5_Reception으로 이어지는 Sector3 출구 DoorTarget을 찾지 못했습니다. " +
                "문 이름에 'To_Reception' 규칙이 있는지 확인하세요.");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                "출구 후보 DoorTarget이 여러 개입니다:\n- " +
                string.Join("\n- ", candidates.Select(candidate => candidate.name)));
        }

        return candidates[0];
    }

    /// <summary>검증용으로 출구 후보를 찾고, 실패 원인을 오류 목록에 추가한다.</summary>
    private static DoorTarget FindExitDoorTargetOrNull(List<string> errors)
    {
        List<DoorTarget> candidates = FindExitDoorTargets();
        if (candidates.Count == 0)
        {
            errors.Add("S5_Reception으로 이어지는 Sector3 출구 DoorTarget을 찾지 못했습니다.");
            return null;
        }

        if (candidates.Count > 1)
        {
            errors.Add("출구 후보 DoorTarget이 여러 개입니다: " +
                       string.Join(", ", candidates.Select(candidate => candidate.name)));
            return null;
        }

        return candidates[0];
    }

    /// <summary>
    /// Doorway의 방 연결과 이름 규칙으로 출구 후보를 제한한다.
    /// S5_DoorArea와 S5_Reception 사이의 정문 내부 연결은 20번 이벤트 대상에서 제외한다.
    /// </summary>
    private static List<DoorTarget> FindExitDoorTargets()
    {
        return UnityEngine.Object.FindObjectsByType<DoorTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(target =>
            {
                Doorway doorway = target.GetComponentInParent<Doorway>();
                Room ownerRoom = target.GetComponent<Target>()?.OwnerRoom;
                bool connectsReception = doorway != null &&
                                          (doorway.RoomA?.RoomId == ReceptionRoomId || doorway.RoomB?.RoomId == ReceptionRoomId);
                bool isSector3Side = ownerRoom != null &&
                                     (ownerRoom.RoomId.StartsWith("S3_", StringComparison.Ordinal) ||
                                      ownerRoom.RoomId.StartsWith("CenterHallway_", StringComparison.Ordinal));
                bool followsNamingRule = doorway != null && doorway.name.Contains("To_Reception", StringComparison.Ordinal);
                return connectsReception && isSector3Side && followsNamingRule;
            })
            .ToList();
    }

    /// <summary>출구 안내 Dialogue asset을 생성하거나 기존 asset을 재사용한다.</summary>
    private static DialogueSequence GetOrCreateHintDialogue()
    {
        DialogueSequence dialogue = AssetDatabase.LoadAssetAtPath<DialogueSequence>(DialogueAssetPath);
        if (dialogue == null)
        {
            dialogue = ScriptableObject.CreateInstance<DialogueSequence>();
            AssetDatabase.CreateAsset(dialogue, DialogueAssetPath);
        }

        SerializedObject serialized = new(dialogue);
        serialized.FindProperty("pauseGame").boolValue = true;
        serialized.FindProperty("autoAdvanceDelay").floatValue = 0f;
        SerializedProperty lines = serialized.FindProperty("lines");
        if (lines.arraySize == 0)
        {
            lines.InsertArrayElementAtIndex(0);
            SerializedProperty line = lines.GetArrayElementAtIndex(0);
            line.FindPropertyRelative("speaker").enumValueIndex = 0;
            line.FindPropertyRelative("text").stringValue = HintDialogueText;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(dialogue);
        return dialogue;
    }

    /// <summary>씬에서 필수 단일 참조를 찾는다.</summary>
    private static T FindRequired<T>(string label) where T : UnityEngine.Object
    {
        return FindFirst<T>() ?? throw new InvalidOperationException($"{label} 참조를 찾지 못했습니다.");
    }

    /// <summary>씬에서 지정한 타입의 첫 참조를 찾는다.</summary>
    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    }

    /// <summary>컴포넌트가 없을 때만 Undo 가능한 방식으로 추가한다.</summary>
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    /// <summary>private Unity 참조 필드를 직렬화해 저장한다.</summary>
    private static void SetObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>private 문자열 필드를 직렬화해 저장한다.</summary>
    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>private bool 필드를 직렬화해 저장한다.</summary>
    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{target.GetType().Name}의 SerializedProperty '{propertyName}'를 찾지 못했습니다.");
        }

        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>오류 목록에 조건 실패를 추가한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition && !string.IsNullOrWhiteSpace(message))
        {
            errors.Add(message);
        }
    }
}
#endif
