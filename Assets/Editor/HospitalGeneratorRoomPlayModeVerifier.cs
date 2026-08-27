#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Sector2 GeneratorRoom 작업 15의 Item Use UI 분기와 Save3 Restart 복원을
/// 실제 Play Mode 런타임에서 자동 검증하는 Editor 전용 진입점이다.
/// </summary>
[InitializeOnLoad]
public static class HospitalGeneratorRoomPlayModeVerifier
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string PendingKey = "HauntedHouse.HospitalGeneratorRoomPlayModeVerificationPending";

    static HospitalGeneratorRoomPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("Haunted House/Verify/Hospital Sector2 GeneratorRoom Task 15")]
    public static void VerifyInPlayMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Task15 GeneratorRoom] 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != ScenePath)
        {
            Debug.LogWarning($"[Task15 GeneratorRoom] CoreSandbox 씬에서만 실행할 수 있습니다. 현재 씬: {activeScene.path}");
            return;
        }

        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false) ||
            false)
        {
            return;
        }

        SessionState.SetBool(PendingKey, false);
        GameObject runnerObject = new("HospitalGeneratorRoomPlayModeVerificationRunner");
        runnerObject.AddComponent<HospitalGeneratorRoomPlayModeVerificationRunner>();
    }
}

/// <summary>
/// 현재 Play 세션 안에서만 상태를 조작하고 검증한다.
/// Play Mode 종료 시 모든 테스트용 Inventory/StoryFlag 변경은 폐기된다.
/// </summary>
public sealed class HospitalGeneratorRoomPlayModeVerificationRunner : MonoBehaviour
{
    private const string GeneratorName = "Generator_Boiler";
    private const string Save3Name = "Save3";
    private const string FuseAssetPath = "Assets/Data/Items/Fuse.asset";
    private const string ItemFolder = "Assets/Data/Items";
    private const float StartupTimeout = 12f;
    private const float UiTimeout = 6f;
    private const float ActionTimeout = 10f;
    private const float RestoreTimeout = 12f;

    private readonly List<string> failures = new();
    private int passCount;

    private GameSession gameSession;
    private CheckpointService checkpointService;
    private PlayerInventory inventory;
    private ItemSelectionPanel selectionPanel;
    private MessagePanel messagePanel;
    private GameObject messagePanelRoot;
    private Button messageConfirmButton;
    private ItemUseTarget itemUseTarget;
    private Target generatorTarget;
    private Player player;
    private CheckpointDefinition save3;
    private ItemDefinition fuseItem;
    private ItemDefinition wrongItem;

    private bool actionFinished;
    private string actionException;

    private IEnumerator Start()
    {
        try
        {
            CollectReferences();
        }
        catch (Exception exception)
        {
            RecordFailure($"참조 수집 예외: {exception.GetType().Name}: {exception.Message}");
        }

        yield return RunSafely(WaitForGameplayReady());

        if (gameSession != null && gameSession.IsGameplayInputEnabled)
        {
            PrepareUiTestState();
            yield return null;

            yield return RunSafely(VerifyCancelBranch());
            yield return RunSafely(VerifyWrongItemBranch());
            yield return RunSafely(VerifyFuseBranch());
            yield return RunSafely(VerifySave3RestartRestore());
        }

        ReportResult();
        yield return null;

        if (EditorApplication.isPlaying)
        {
            EditorApplication.ExitPlaymode();
        }

        Destroy(gameObject);
    }

    private IEnumerator RunSafely(IEnumerator routine)
    {
        while (true)
        {
            bool hasNext;
            object yieldedValue;
            try
            {
                hasNext = routine != null && routine.MoveNext();
                yieldedValue = hasNext ? routine.Current : null;
            }
            catch (Exception exception)
            {
                RecordFailure($"검증 코루틴 예외: {exception.GetType().Name}: {exception.Message}");
                yield break;
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return yieldedValue;
        }
    }

    private void CollectReferences()
    {
        gameSession = FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
        checkpointService = FindFirstObjectByType<CheckpointService>(FindObjectsInactive.Include);
        inventory = FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
        selectionPanel = FindFirstObjectByType<ItemSelectionPanel>(FindObjectsInactive.Include);
        messagePanel = FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        player = FindFirstObjectByType<Player>(FindObjectsInactive.Include);

        itemUseTarget = FindObjectsByType<ItemUseTarget>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == GeneratorName);

        generatorTarget = itemUseTarget != null ? itemUseTarget.GetComponent<Target>() : null;

        // 검증기는 씬에서 임의의 UI를 찾지 않고 실제 Generator ItemUseTarget이 참조하는 객체를 사용한다.
        if (itemUseTarget != null)
        {
            SerializedObject serializedItemUseTarget = new(itemUseTarget);
            inventory = serializedItemUseTarget.FindProperty("inventory")?.objectReferenceValue as PlayerInventory;
            selectionPanel = serializedItemUseTarget.FindProperty("selectionPanel")?.objectReferenceValue as ItemSelectionPanel;
            messagePanel = serializedItemUseTarget.FindProperty("messagePanel")?.objectReferenceValue as MessagePanel;
        }

        if (messagePanel != null)
        {
            SerializedObject serializedMessagePanel = new(messagePanel);
            messagePanelRoot = serializedMessagePanel.FindProperty("panelRoot")?.objectReferenceValue as GameObject;
            messageConfirmButton = serializedMessagePanel.FindProperty("confirmButton")?.objectReferenceValue as Button;
        }

        save3 = FindObjectsByType<CheckpointDefinition>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == Save3Name);

        fuseItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(FuseAssetPath);
        wrongItem = FindWrongItem();

        Require(gameSession != null, "GameSession 참조");
        Require(checkpointService != null, "CheckpointService 참조");
        Require(inventory != null, "PlayerInventory 참조");
        Require(selectionPanel != null, "ItemSelectionPanel 참조");
        Require(messagePanel != null, "MessagePanel 참조");
        Require(player != null, "Player 참조");
        Require(itemUseTarget != null, "Generator_Boiler ItemUseTarget 참조");
        Require(generatorTarget != null, "Generator_Boiler Target 참조");
        Require(save3 != null, "Save3 CheckpointDefinition 참조");
        Require(fuseItem != null, "Fuse.asset 참조");
        Require(wrongItem != null, "Fuse 외 WrongItem용 ItemDefinition 참조");
    }

    private ItemDefinition FindWrongItem()
    {
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ItemDefinition candidate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (candidate == null || fuseItem == null)
            {
                continue;
            }

            if (!string.Equals(candidate.ItemId, fuseItem.ItemId, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerator WaitForGameplayReady()
    {
        float deadline = Time.realtimeSinceStartup + StartupTimeout;
        while (gameSession != null &&
               !gameSession.IsGameplayInputEnabled &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (gameSession == null)
        {
            yield break;
        }

        if (!gameSession.IsGameplayInputEnabled)
        {
            RecordFailure("초기 Checkpoint 복원이 끝나기 전에 StartupTimeout이 만료됨");
        }
        else
        {
            RecordPass("Play Mode 초기화 및 Gameplay 시작");
        }
    }

    private void PrepareUiTestState()
    {
        if (inventory == null || fuseItem == null || wrongItem == null || gameSession == null)
        {
            return;
        }

        gameSession.ClearStoryFlag(StoryProgressFlags.FuseInstalled);
        gameSession.ClearStoryFlag(StoryProgressFlags.GeneratorPowered);
        inventory.RestoreItems(new[] { wrongItem, fuseItem });
    }

    private IEnumerator VerifyCancelBranch()
    {
        TargetActionContext context = BeginGeneratorAction();
        if (context == null)
        {
            yield break;
        }

        yield return WaitForSelectionPanel();
        if (!selectionPanel.IsOpen)
        {
            AbortCurrentAction();
            yield break;
        }

        selectionPanel.Cancel();
        yield return WaitForActionCompletion(false);

        Require(itemUseTarget.LastResult == ItemUseResult.Cancel, "Cancel 결과가 ItemUseResult.Cancel");
        Require(!context.IsCompleted, "Cancel 시 TargetActionContext 미완료");
        Require(inventory.Contains(wrongItem) && inventory.Contains(fuseItem), "Cancel 시 Inventory 무변경");
        Require(!HasGeneratorFlags(), "Cancel 시 FuseInstalled/GeneratorPowered 미설정");
    }

    private IEnumerator VerifyWrongItemBranch()
    {
        TargetActionContext context = BeginGeneratorAction();
        if (context == null)
        {
            yield break;
        }

        yield return WaitForSelectionPanel();
        if (!selectionPanel.IsOpen)
        {
            AbortCurrentAction();
            yield break;
        }

        Button button = FindItemButton(wrongItem);
        if (button == null)
        {
            RecordFailure($"WrongItem '{wrongItem.DisplayName}'의 실제 InventoryItemView Button을 찾지 못함");
            selectionPanel.Cancel();
            yield return WaitForActionCompletion(false);
            yield break;
        }

        button.onClick.Invoke();
        yield return WaitForActionCompletion(true);

        Require(itemUseTarget.LastResult == ItemUseResult.WrongItem, "WrongItem 결과가 ItemUseResult.WrongItem");
        Require(!context.IsCompleted, "WrongItem 시 TargetActionContext 미완료");
        Require(inventory.Contains(wrongItem) && inventory.Contains(fuseItem), "WrongItem 시 Inventory 무변경");
        Require(!HasGeneratorFlags(), "WrongItem 시 FuseInstalled/GeneratorPowered 미설정");
    }

    private IEnumerator VerifyFuseBranch()
    {
        TargetActionContext context = BeginGeneratorAction();
        if (context == null)
        {
            yield break;
        }

        yield return WaitForSelectionPanel();
        if (!selectionPanel.IsOpen)
        {
            AbortCurrentAction();
            yield break;
        }

        Button button = FindItemButton(fuseItem);
        if (button == null)
        {
            RecordFailure("Fuse의 실제 InventoryItemView Button을 찾지 못함");
            selectionPanel.Cancel();
            yield return WaitForActionCompletion(false);
            yield break;
        }

        button.onClick.Invoke();
        yield return WaitForActionCompletion(true);

        Require(itemUseTarget.LastResult == ItemUseResult.CorrectItem, "Fuse 결과가 ItemUseResult.CorrectItem");
        Require(context.IsCompleted, "Fuse 사용 성공 시 TargetActionContext 완료");
        Require(!inventory.Contains(fuseItem), "Fuse 성공 사용 시 Inventory에서 Fuse 소비");
        Require(inventory.Contains(wrongItem), "Fuse 성공 사용 시 다른 Inventory Item 유지");
        Require(HasGeneratorFlags(), "Fuse 성공 사용 시 FuseInstalled + GeneratorPowered 설정");
        Require(!ReadTargetSelectable(), "GeneratorPowered 이후 Generator Target 재사용 차단");
    }

    private IEnumerator VerifySave3RestartRestore()
    {
        if (checkpointService == null || save3 == null || gameSession == null)
        {
            yield break;
        }

        if (!checkpointService.ActivateCheckpoint(save3))
        {
            RecordFailure("Save3 ActivateCheckpoint 실패");
            yield break;
        }

        gameSession.ClearStoryFlag(StoryProgressFlags.FuseInstalled);
        gameSession.ClearStoryFlag(StoryProgressFlags.GeneratorPowered);
        yield return null;

        Require(!HasGeneratorFlags(), "Save3 복원 전 두 Generator Story Flag를 테스트용으로 제거");
        Require(ReadTargetSelectable(), "GeneratorPowered 제거 후 Generator Target이 다시 선택 가능");

        if (!checkpointService.RestoreActiveCheckpoint())
        {
            RecordFailure("Save3 RestoreActiveCheckpoint 시작 실패");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + RestoreTimeout;
        bool sawRestoring = gameSession.Phase == GameSessionPhase.Restoring;

        while (Time.realtimeSinceStartup < deadline)
        {
            sawRestoring |= gameSession.Phase == GameSessionPhase.Restoring;
            if (sawRestoring && gameSession.Phase == GameSessionPhase.Playing)
            {
                break;
            }

            yield return null;
        }

        Require(sawRestoring, "Save3 Restart 복원에서 Restoring 단계 진입");
        Require(gameSession.Phase == GameSessionPhase.Playing, "Save3 Restart 복원 완료 후 Playing 복귀");
        Require(HasGeneratorFlags(), "Save3 Restart 후 FuseInstalled + GeneratorPowered 복원");
        Require(!ReadTargetSelectable(), "Save3 Restart 후 Generator Target 재사용 차단 유지");
    }

    private TargetActionContext BeginGeneratorAction()
    {
        if (itemUseTarget == null || generatorTarget == null || player == null)
        {
            RecordFailure("Generator ItemUseTarget 실행에 필요한 참조가 없음");
            return null;
        }

        actionFinished = false;
        actionException = null;

        TargetActionContext context = new(generatorTarget, player);
        StartCoroutine(ExecuteTracked(context));
        return context;
    }

    private IEnumerator ExecuteTracked(TargetActionContext context)
    {
        // 실제 게임과 동일하게 Unity coroutine runner가 ItemUseTarget의 중첩 IEnumerator를 직접 처리하게 한다.
        yield return itemUseTarget.Execute(context);
        actionFinished = true;
    }

    private IEnumerator WaitForSelectionPanel()
    {
        float deadline = Time.realtimeSinceStartup + UiTimeout;
        while (!actionFinished &&
               selectionPanel != null &&
               !selectionPanel.IsOpen &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (selectionPanel == null || !selectionPanel.IsOpen)
        {
            RecordFailure("ItemSelectionPanel이 제한 시간 안에 열리지 않음");
        }
        else
        {
            RecordPass("실제 ItemSelectionPanel Open 경로 진입");
        }
    }

    private IEnumerator WaitForActionCompletion(bool dismissMessages)
    {
        bool sawMessagePanel = false;
        bool invokedConfirm = false;
        float deadline = Time.realtimeSinceStartup + ActionTimeout;
        while (!actionFinished && Time.realtimeSinceStartup < deadline)
        {
            if (dismissMessages && messagePanelRoot != null && messagePanelRoot.activeInHierarchy)
            {
                sawMessagePanel = true;
                if (messageConfirmButton != null)
                {
                    messageConfirmButton.onClick.Invoke();
                    invokedConfirm = true;
                }
            }

            yield return null;
        }

        if (!actionFinished)
        {
            string panelState = messagePanelRoot == null
                ? "null"
                : $"activeSelf={messagePanelRoot.activeSelf}, activeInHierarchy={messagePanelRoot.activeInHierarchy}";
            RecordFailure(
                $"ItemUseTarget ActionTimeout [sawMessagePanel={sawMessagePanel}, invokedConfirm={invokedConfirm}, panel={panelState}, confirmNull={messageConfirmButton == null}, selectionOpen={selectionPanel != null && selectionPanel.IsOpen}, lastResult={itemUseTarget?.LastResult}]");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(actionException))
        {
            RecordFailure($"ItemUseTarget 실행 예외: {actionException}");
        }
    }

    private void AbortCurrentAction()
    {
        if (selectionPanel != null && selectionPanel.IsOpen)
        {
            selectionPanel.Cancel();
        }
    }

    private Button FindItemButton(ItemDefinition item)
    {
        if (selectionPanel == null || item == null)
        {
            return null;
        }

        SerializedObject serializedSelectionPanel = new(selectionPanel);
 Transform itemContainer = serializedSelectionPanel.FindProperty("itemContainer")?.objectReferenceValue as Transform;
 if (itemContainer == null)
 {
 return null;
 }

 InventoryItemView[] views = itemContainer.GetComponentsInChildren<InventoryItemView>(true);
        foreach (InventoryItemView view in views)
        {
            SerializedObject serializedView = new(view);
            Text label = serializedView.FindProperty("nameLabel")?.objectReferenceValue as Text;
            if (label == null ||
                !string.Equals(label.text, item.DisplayName, StringComparison.Ordinal))
            {
                continue;
            }

            return serializedView.FindProperty("button")?.objectReferenceValue as Button;
        }

        return null;
    }

    private bool HasGeneratorFlags()
    {
        return gameSession != null &&
               gameSession.HasStoryFlag(StoryProgressFlags.FuseInstalled) &&
               gameSession.HasStoryFlag(StoryProgressFlags.GeneratorPowered);
    }

    private bool ReadTargetSelectable()
    {
        if (generatorTarget == null)
        {
            return false;
        }

        SerializedObject serializedTarget = new(generatorTarget);
        SerializedProperty selectableProperty = serializedTarget.FindProperty("selectable");
        if (selectableProperty == null)
        {
            RecordFailure("Target.selectable SerializedProperty를 찾지 못함");
            return false;
        }

        serializedTarget.Update();
        return selectableProperty.boolValue;
    }

    private void Require(bool condition, string description)
    {
        if (condition)
        {
            RecordPass(description);
        }
        else
        {
            RecordFailure(description);
        }
    }

    private void RecordPass(string description)
    {
        passCount++;
        Debug.Log($"[Task15 GeneratorRoom][PASS] {description}");
    }

    private void RecordFailure(string description)
    {
        failures.Add(description);
        Debug.LogError($"[Task15 GeneratorRoom][FAIL] {description}");
    }

    private void ReportResult()
    {
        string summary = failures.Count == 0
            ? $"Task 15 GeneratorRoom 검증 PASS ({passCount} checks)."
            : $"Task 15 GeneratorRoom 검증 FAIL: {failures.Count} failure(s), {passCount} pass(es).\n- {string.Join("\n- ", failures)}";

        if (failures.Count == 0)
        {
            Debug.Log($"[Task15 GeneratorRoom] {summary}");
        }
        else
        {
            Debug.LogError($"[Task15 GeneratorRoom] {summary}");
        }

        EditorUtility.DisplayDialog("GeneratorRoom Task 15 검증", summary, "확인");
    }
}
#endif
