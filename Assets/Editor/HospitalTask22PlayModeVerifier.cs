#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hospital gameplay Task 22를 사용자가 한 번의 메뉴 클릭으로 Play Mode 검증하기 위한 Editor 진입점이다.
/// 검증 중의 Story Flag 변경은 Play Mode 메모리에서만 수행하며 씬이나 에셋을 저장하지 않는다.
/// </summary>
[InitializeOnLoad]
public static class HospitalTask22PlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalTask22VerificationPending";

    static HospitalTask22PlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("Tools/Hospital/Task 22/Run Verification")]
    public static void RunVerification()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Task22] 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 검증 메뉴를 실행하세요.");
            return;
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            Debug.LogWarning($"[Task22] 검증은 '{ScenePath}' 씬에서 실행해야 합니다. 현재 씬: {SceneManager.GetActiveScene().path}");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        Debug.Log("[Task22] Play Mode 검증을 시작합니다. 완료 후 자동으로 Edit Mode로 돌아옵니다.");
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        new GameObject("HospitalTask22VerificationRunner")
            .AddComponent<HospitalTask22VerificationRunner>();
    }
}

/// <summary>
/// Task 22의 Kitchen 서쪽 선반 Hide Target과 FreezerHintRead 조건부 BasementKey Target을 검증한다.
/// </summary>
public sealed class HospitalTask22VerificationRunner : MonoBehaviour
{
    private const float StartupTimeout = 15f;
    private const string HideTargetName = "S3_Kitchen_WestShelfHideTarget";
    private const string KeyTargetName = "S3_BasementKeyFreezerTarget";
    private const string ShelfName = "Kitchen_Shelf_West_2";
    private const string FreezerName = "Story_FreezerUnit_2";
    private const string HintBoardName = "Story_FreezerHintBoard";
    private const string HintFlag = "FreezerHintRead";
    private const string AcquiredFlag = "BasementKeyAcquired";
    private const string BasementKeyAssetPath = "Assets/Data/Items/BasementKey.asset";

    private readonly List<string> failures = new();
    private int passCount;
    private GameSession gameSession;

    private IEnumerator Start()
    {
        yield return WaitForGameplayReady();

        if (gameSession != null)
        {
            VerifyHintBoard();
            VerifyHideTarget();
            VerifyKeyTargetStaticConfiguration();

            if (failures.Count == 0)
            {
                yield return VerifyFreezerHintGate();
            }
        }

        ReportResult();
        yield return null;
        EditorApplication.ExitPlaymode();
        Destroy(gameObject);
    }

    private IEnumerator WaitForGameplayReady()
    {
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            gameSession ??= FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
            if (gameSession != null && gameSession.Phase == GameSessionPhase.Playing)
            {
                Pass("GameSession이 Playing 상태로 초기화되었습니다.");
                yield break;
            }

            yield return null;
        }

        Fail("GameSession이 제한 시간 안에 Playing 상태가 되지 않았습니다.");
    }

    private void VerifyHintBoard()
    {
        GameObject hintBoard = GameObject.Find("Map/Sector3/DiningRoom/Sector3DiningTargets/Story_FreezerHintBoard");
        if (hintBoard == null)
        {
            Fail($"힌트 보드 '{HintBoardName}'를 찾지 못했습니다.");
            return;
        }

        MessageTarget messageTarget = hintBoard.GetComponent<MessageTarget>();
        if (messageTarget == null)
        {
            Fail($"'{HintBoardName}'에 MessageTarget이 없습니다.");
            return;
        }

        string completionFlagId = ReadString(messageTarget, "completionFlagId");
        Check(completionFlagId == HintFlag, $"힌트 보드 completionFlagId가 '{HintFlag}'입니다.",
            $"힌트 보드 completionFlagId가 '{completionFlagId}'입니다. 기대값: '{HintFlag}'.");
    }

    private void VerifyHideTarget()
    {
        GameObject hideObject = FindSceneObject(HideTargetName);
        if (hideObject == null)
        {
            Fail($"Hide Target '{HideTargetName}'를 찾지 못했습니다.");
            return;
        }

        Target target = hideObject.GetComponent<Target>();
        TargetPresenter presenter = hideObject.GetComponent<TargetPresenter>();
        StealthHideTarget hideAction = hideObject.GetComponent<StealthHideTarget>();
        Collider clickArea = hideObject.GetComponentInChildren<Collider>(true);

        Check(target != null, "Kitchen 서쪽 선반 Hide Target에 Target이 있습니다.", "Kitchen Hide Target에 Target이 없습니다.");
        Check(presenter != null, "Kitchen 서쪽 선반 Hide Target에 TargetPresenter가 있습니다.", "Kitchen Hide Target에 TargetPresenter가 없습니다.");
        Check(hideAction != null, "Kitchen 서쪽 선반 Hide Target에 StealthHideTarget이 있습니다.", "Kitchen Hide Target에 StealthHideTarget이 없습니다.");
        Check(clickArea != null, "Kitchen 서쪽 선반 Hide Target에 클릭 Collider가 있습니다.", "Kitchen Hide Target에 Collider가 없습니다.");

        if (target != null)
        {
            Check(target.Action is StealthHideTarget, "Hide Target의 actionComponent가 StealthHideTarget에 연결되어 있습니다.",
                "Hide Target의 actionComponent가 StealthHideTarget에 연결되지 않았습니다.");

            if (target.TryValidateConfiguration(out string error))
            {
                Pass("Hide Target의 Target 필수 참조가 유효합니다.");
            }
            else
            {
                Fail($"Hide Target Target 설정 오류: {error}");
            }
        }

        if (presenter != null)
        {
            VerifyPresenterReferences(presenter, hideObject, "Hide Target");
        }

        if (hideAction != null)
        {
            Object stealthSystem = ReadObject(hideAction, "stealthHideSystem");
            Check(stealthSystem != null, "Hide Target의 StealthHideSystem 참조가 연결되어 있습니다.",
                "Hide Target의 StealthHideSystem 참조가 비어 있습니다.");
        }

        GameObject shelf = FindSceneObject(ShelfName);
        if (shelf != null)
        {
            float distance = Vector3.Distance(hideObject.transform.position, shelf.transform.position);
            Check(distance <= 12f, $"Hide Target이 서쪽 선반 2번 근처에 배치되어 있습니다. 거리={distance:F2}m",
                $"Hide Target이 서쪽 선반 2번에서 너무 멉니다. 거리={distance:F2}m");
        }
        else
        {
            Fail($"배치 기준 선반 '{ShelfName}'를 찾지 못했습니다.");
        }
    }

    private void VerifyKeyTargetStaticConfiguration()
    {
        GameObject keyObject = FindSceneObject(KeyTargetName);
        if (keyObject == null)
        {
            Fail($"Basement Key Target '{KeyTargetName}'를 찾지 못했습니다.");
            return;
        }

        Target target = keyObject.GetComponent<Target>();
        TargetPresenter presenter = keyObject.GetComponent<TargetPresenter>();
        ItemPickupTarget pickup = keyObject.GetComponent<ItemPickupTarget>();
        StoryFlagTarget gate = keyObject.GetComponent<StoryFlagTarget>();
        StoryFlagOnItemAcquired acquiredFlag = keyObject.GetComponent<StoryFlagOnItemAcquired>();
        Collider clickArea = keyObject.GetComponentInChildren<Collider>(true);

        Check(target != null, "Basement Key Target에 Target이 있습니다.", "Basement Key Target에 Target이 없습니다.");
        Check(presenter != null, "Basement Key Target에 TargetPresenter가 있습니다.", "Basement Key Target에 TargetPresenter가 없습니다.");
        Check(pickup != null, "Basement Key Target에 ItemPickupTarget이 있습니다.", "Basement Key Target에 ItemPickupTarget이 없습니다.");
        Check(gate != null, "Basement Key Target에 StoryFlagTarget이 있습니다.", "Basement Key Target에 StoryFlagTarget이 없습니다.");
        Check(acquiredFlag != null, "Basement Key Target에 StoryFlagOnItemAcquired가 있습니다.", "Basement Key Target에 StoryFlagOnItemAcquired가 없습니다.");
        Check(clickArea != null, "Basement Key Target에 클릭 Collider가 있습니다.", "Basement Key Target에 Collider가 없습니다.");

        if (target != null)
        {
            Check(target.Action is ItemPickupTarget, "Basement Key Target의 actionComponent가 ItemPickupTarget에 연결되어 있습니다.",
                "Basement Key Target의 actionComponent가 ItemPickupTarget에 연결되지 않았습니다.");

            if (target.TryValidateConfiguration(out string error))
            {
                Pass("Basement Key Target의 Target 필수 참조가 유효합니다.");
            }
            else
            {
                Fail($"Basement Key Target Target 설정 오류: {error}");
            }
        }

        if (presenter != null)
        {
            VerifyPresenterReferences(presenter, keyObject, "Basement Key Target");
        }

        Object basementKey = AssetDatabase.LoadAssetAtPath<Object>(BasementKeyAssetPath);
        Check(basementKey != null, $"BasementKey 에셋이 존재합니다: {BasementKeyAssetPath}",
            $"BasementKey 에셋을 찾지 못했습니다: {BasementKeyAssetPath}");

        if (pickup != null)
        {
            Object item = ReadObject(pickup, "item");
            string itemPath = item != null ? AssetDatabase.GetAssetPath(item) : string.Empty;
            Check(itemPath == BasementKeyAssetPath, "ItemPickupTarget이 BasementKey.asset을 획득하도록 연결되어 있습니다.",
                $"ItemPickupTarget item 경로가 다릅니다: '{itemPath}'.");
            Check(ReadString(pickup, "completionId") == KeyTargetName, "ItemPickupTarget completionId가 Target 이름과 일치합니다.",
                $"ItemPickupTarget completionId가 '{ReadString(pickup, "completionId")}'입니다.");
        }

        if (gate != null)
        {
            Check(ReadString(gate, "requiredFlagId") == HintFlag, $"StoryFlagTarget이 '{HintFlag}'를 요구합니다.",
                $"StoryFlagTarget requiredFlagId가 '{ReadString(gate, "requiredFlagId")}'입니다.");
        }

        if (acquiredFlag != null)
        {
            Object watchedItem = ReadObject(acquiredFlag, "watchedItem");
            string watchedPath = watchedItem != null ? AssetDatabase.GetAssetPath(watchedItem) : string.Empty;
            Check(watchedPath == BasementKeyAssetPath, "StoryFlagOnItemAcquired가 BasementKey.asset을 감시합니다.",
                $"StoryFlagOnItemAcquired watchedItem 경로가 다릅니다: '{watchedPath}'.");
            Check(ReadString(acquiredFlag, "flagId") == AcquiredFlag, $"열쇠 획득 시 '{AcquiredFlag}' flag를 기록하도록 구성되어 있습니다.",
                $"StoryFlagOnItemAcquired flagId가 '{ReadString(acquiredFlag, "flagId")}'입니다.");
        }

        GameObject freezer = FindSceneObject(FreezerName);
        if (freezer != null)
        {
            float distance = Vector3.Distance(keyObject.transform.position, freezer.transform.position);
            Check(distance <= 12f, $"Basement Key Target이 냉장고 2번 근처에 배치되어 있습니다. 거리={distance:F2}m",
                $"Basement Key Target이 냉장고 2번에서 너무 멉니다. 거리={distance:F2}m");
        }
        else
        {
            Fail($"배치 기준 냉장고 '{FreezerName}'를 찾지 못했습니다.");
        }
    }

    private IEnumerator VerifyFreezerHintGate()
    {
        GameObject keyObject = FindSceneObject(KeyTargetName);
        if (keyObject == null)
        {
            yield break;
        }

        Target target = keyObject.GetComponent<Target>();
        StoryFlagTarget gate = keyObject.GetComponent<StoryFlagTarget>();
        ItemPickupTarget pickup = keyObject.GetComponent<ItemPickupTarget>();
        if (target == null || gate == null || pickup == null)
        {
            yield break;
        }

        if (pickup.IsCompleted)
        {
            Debug.LogWarning("[Task22][SKIP] 현재 런타임 복원 상태에서 Basement Key가 이미 획득 완료라 FreezerHintRead 잠금 전환 검증은 건너뜁니다. 정적 배선 검사는 이미 수행했습니다.");
            yield break;
        }

        bool hadHintFlag = gameSession.HasStoryFlag(HintFlag);
        yield return null;

        if (!hadHintFlag)
        {
            Check(!gate.IsConditionMet, "FreezerHintRead 전에는 StoryFlagTarget 조건이 충족되지 않습니다.",
                "FreezerHintRead가 없는데 StoryFlagTarget 조건이 충족된 상태입니다.");
            Check(!target.IsSelectable, "FreezerHintRead 전에는 Basement Key Target이 선택 불가입니다.",
                "FreezerHintRead가 없는데 Basement Key Target이 선택 가능합니다.");

            gameSession.SetStoryFlag(HintFlag);
            yield return null;
            yield return null;

            Check(gate.IsConditionMet, "FreezerHintRead 기록 후 StoryFlagTarget 조건이 충족됩니다.",
                "FreezerHintRead 기록 후에도 StoryFlagTarget 조건이 충족되지 않습니다.");
            Check(target.IsSelectable, "FreezerHintRead 기록 후 Basement Key Target이 선택 가능해집니다.",
                "FreezerHintRead 기록 후에도 Basement Key Target이 선택 불가입니다.");
            Debug.Log("[Task22][INFO] FreezerHintRead는 이 Play Mode 세션에서만 임시 설정했으며 씬/에셋 저장은 수행하지 않습니다.");
        }
        else
        {
            Check(gate.IsConditionMet, "기존 FreezerHintRead 복원 상태와 StoryFlagTarget 조건이 일치합니다.",
                "FreezerHintRead가 복원되어 있지만 StoryFlagTarget 조건이 충족되지 않습니다.");
            Check(target.IsSelectable, "기존 FreezerHintRead 복원 상태에서 Basement Key Target이 선택 가능합니다.",
                "FreezerHintRead가 복원되어 있지만 Basement Key Target이 선택 불가입니다.");
            Debug.LogWarning("[Task22][SKIP] 시작 시점에 FreezerHintRead가 이미 존재해 잠금 전 상태 검사는 생략했습니다.");
        }
    }

    private void VerifyPresenterReferences(TargetPresenter presenter, GameObject owner, string label)
    {
        Object targetRef = ReadObject(presenter, "target");
        Object clickAreaRef = ReadObject(presenter, "clickArea");
        Transform uiAnchorRef = ReadObject(presenter, "uiAnchor") as Transform ?? owner.transform.Find("UIAnchor");

        Check(targetRef == owner.GetComponent<Target>(), $"{label} TargetPresenter.target이 올바릅니다.",
            $"{label} TargetPresenter.target 참조가 올바르지 않습니다.");
        Check(clickAreaRef != null, $"{label} TargetPresenter.clickArea가 연결되어 있습니다.",
            $"{label} TargetPresenter.clickArea가 비어 있습니다.");
        Check(uiAnchorRef != null, $"{label} TargetPresenter.uiAnchor가 연결되어 있습니다.",
            $"{label} TargetPresenter.uiAnchor가 비어 있습니다.");
    }

    private static GameObject FindSceneObject(string objectName)
    {
        Transform transform = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate != null && candidate.name == objectName);
        return transform != null ? transform.gameObject : null;
    }

    private static Object ReadObject(Object source, string propertyName)
    {
        if (source == null)
        {
            return null;
        }

        SerializedProperty property = new SerializedObject(source).FindProperty(propertyName);
        return property != null ? property.objectReferenceValue : null;
    }

    private static string ReadString(Object source, string propertyName)
    {
        if (source == null)
        {
            return string.Empty;
        }

        SerializedProperty property = new SerializedObject(source).FindProperty(propertyName);
        return property != null ? property.stringValue : string.Empty;
    }

    private void Check(bool condition, string success, string failure)
    {
        if (condition)
        {
            Pass(success);
        }
        else
        {
            Fail(failure);
        }
    }

    private void Pass(string message)
    {
        passCount++;
        Debug.Log($"[Task22][PASS] {message}");
    }

    private void Fail(string message)
    {
        failures.Add(message);
        Debug.LogError($"[Task22][FAIL] {message}");
    }

    private void ReportResult()
    {
        if (failures.Count == 0)
        {
            Debug.Log($"[Task22][RESULT] PASS - {passCount} checks passed.");
            return;
        }

        Debug.LogError($"[Task22][RESULT] FAIL - {passCount} passed, {failures.Count} failed.\n - {string.Join("\n - ", failures)}");
    }
}
#endif
