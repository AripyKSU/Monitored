using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Inventory;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// MorgueRoom2 시체안치함 B의 스토리 조건부 조사와 마스터키 획득 흐름을 제공한다.
    /// 일기 3을 읽기 전에는 기본 메시지만 표시하고, 읽은 뒤에는 조사 메시지·아이템 획득·무전기 대화를 순서대로 실행한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public sealed class MorgueBoxBTarget : MonoBehaviour, ITargetAction, ICompletedState
    {
        [Header("공용 진행 시스템")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField] private DialogueService dialogueService;

        [Header("마스터키 획득")]
        [SerializeField] private ItemDefinition masterKey;
        [SerializeField] private DialogueSequence masterKeyDialogue;
        [SerializeField] private string completionId;

        [Header("메시지")]
        [SerializeField, TextArea] private string unopenedMessage = "시체안치함 B";
        [SerializeField, TextArea] private string openedMessage = "시체안치함을 열어보았다.";

        private Target target;

        /// <summary>체크포인트에서 B의 마스터키 획득 완료 여부를 식별할 ID를 반환한다.</summary>
        public string CompletionId => string.IsNullOrWhiteSpace(completionId) ? name : completionId;

        /// <summary>마스터키 획득까지 완료된 상태인지 반환한다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// 일기 3 진행 상태에 따라 기본 조사 또는 마스터키 획득 순서를 실행한다.
        /// 마스터키가 이미 보유된 경우에는 중복 획득을 시도하지 않고 완료 상태로 종료한다.
        /// </summary>
        /// <param name="context">현재 Target과 플레이어를 담은 실행 문맥.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            // 씬 직렬화 전후 어느 경로로 실행되어도 동일한 Target을 사용하도록
            // 문맥의 대상과 컴포넌트 참조를 보완한다.
            target ??= context?.Target ?? GetComponent<Target>();

            if (!TryValidateConfiguration(out string error))
            {
                GameLog.LogError(error, this);
                yield break;
            }

            if (target == null)
            {
                GameLog.LogError($"MorgueBoxBTarget '{name}'에 Target 참조가 없습니다.", this);
                yield break;
            }

            // 체크포인트 복원 직후에도 완료 상태가 이미 적용된 경우에는
            // 중복 획득과 중복 대화 진입을 막는다. 아이템만 먼저 추가된 예외 상황은
            // 아래에서 대화만 재시도할 수 있도록 IsCompleted와 분리해 둔다.
            if (IsCompleted)
            {
                // 마스터키는 다시 지급하지 않되 다른 안치함과 동일하게 조사 메시지는 반복 확인할 수 있다.
                yield return messagePanel.Show(openedMessage);
                target.SetSelectable(true);
                yield break;
            }

            if (!gameSession.HasStoryFlag(StoryProgressFlags.KillerDiary3Read))
            {
                // 일기 3을 읽기 전에는 B의 위치를 조사했다는 메시지만 보여 주고,
                // 다음 조사에서 다시 같은 안내를 확인할 수 있도록 Target을 유지한다.
                yield return messagePanel.Show(unopenedMessage);
                context?.MarkCompleted();
                yield break;
            }

            bool alreadyAcquired = gameSession.HasStoryFlag(StoryProgressFlags.MasterKeyAcquired) ||
                                   inventory.Contains(masterKey);

            // 일기 3을 읽은 뒤 처음 조사하는 경우에만 계획서의 두 조사 메시지를 표시한다.
            // 아이템 획득 직후 Dialogue 시작이 실패한 예외 상황에서는 메시지를 반복하지 않고
            // 무전기 대화만 재시도할 수 있게 한다.
            if (!alreadyAcquired)
            {
                yield return messagePanel.Show(unopenedMessage);
                yield return messagePanel.Show(openedMessage);

                // Inventory가 ItemAcquired 사건을 발행하므로 아이템 목록·UI 갱신은
                // 기존 PlayerInventory 흐름을 그대로 재사용한다.
                if (!inventory.TryAdd(masterKey))
                {
                    GameLog.LogError($"MorgueBoxBTarget '{name}'이 마스터키를 추가하지 못했습니다.", this);
                    yield break;
                }
            }

            // 획득 직후 무전기 Pause Dialogue가 끝날 때까지 Target 명령을 유지한다.
            // 완료 사건은 대화가 실제로 끝난 뒤에만 발행해 Save5 생성 시점을 보장한다.
            bool dialogueCompleted = false;

            // TryPlay 직후에는 DialogueService의 재생 코루틴이 아직 첫 프레임을
            // 실행하지 않을 수 있으므로 IsPlaying 폴링만으로 완료를 판정하지 않는다.
            void HandleDialogueCompleted(DialogueSequence completedSequence)
            {
                if (completedSequence == masterKeyDialogue)
                {
                    dialogueCompleted = true;
                }
            }

            dialogueService.DialogueCompleted += HandleDialogueCompleted;
            try
            {
                if (!dialogueService.TryPlay(masterKeyDialogue))
                {
                    GameLog.LogError($"MorgueBoxBTarget '{name}'의 마스터키 Dialogue를 시작하지 못했습니다.", this);
                    yield break;
                }

                while (!dialogueCompleted)
                {
                    yield return null;
                }

                // DialogueCompleted 이벤트가 발생한 뒤에만 PuzzleCompleted를 발행한다.
                // CheckpointResolver가 이 사건을 받아 MasterKeyAcquired flag를 기록하고
                // Save5를 활성화하므로 B가 flag를 직접 중복 기록하지 않는다.
                IsCompleted = true;
                target.SetSelectable(true);
                gameSession.PublishPuzzleCompleted(StoryCheckpointEventIds.MasterKeyDialogueCompleted);

                // Save5 사건을 먼저 발행한 뒤 기존 추격을 정리하고 Storage 연출 위치를 고정한다.
                Sector5FinalChaseController finalChase =
                    FindFirstObjectByType<Sector5FinalChaseController>(FindObjectsInactive.Include);
                if (finalChase == null || !finalChase.PrepareEnemyForFinalChase())
                {
                    GameLog.LogError("마스터키 획득 후 살인마를 Storage 최종 추격 준비 위치로 옮기지 못했습니다.", this);
                }

                context?.MarkCompleted();
            }
            finally
            {
                dialogueService.DialogueCompleted -= HandleDialogueCompleted;
            }
        }

        /// <summary>
        /// 체크포인트가 제공한 완료 상태·Story Flag·Inventory를 기준으로 B의 선택 상태를 복원한다.
        /// </summary>
        /// <param name="completed">체크포인트가 이 Target을 완료 상태로 지정했는지 여부.</param>
        public void RestoreCompletedState(bool completed)
        {
            bool restoredByStory = gameSession != null && gameSession.HasStoryFlag(StoryProgressFlags.MasterKeyAcquired);
            bool restoredByInventory = inventory != null && masterKey != null && inventory.Contains(masterKey);
            IsCompleted = completed || restoredByStory || restoredByInventory;

            target ??= GetComponent<Target>();
            // 획득 완료 뒤에도 열린 안치함 메시지를 다시 확인할 수 있도록 선택 상태를 유지한다.
            target?.SetSelectable(true);
        }

        /// <summary>마스터키 Target의 필수 참조와 메시지·대화 설정이 유효한지 검사한다.</summary>
        /// <param name="error">검사 실패 원인.</param>
        /// <returns>모든 필수 설정이 유효하면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (gameSession == null || inventory == null || messagePanel == null || dialogueService == null ||
                masterKey == null || masterKeyDialogue == null)
            {
                error = $"MorgueBoxBTarget '{name}'의 공용 시스템, MasterKey 또는 Dialogue 참조가 누락됐습니다.";
                return false;
            }

            if (!masterKey.TryValidate(out error))
            {
                return false;
            }

            if (masterKeyDialogue.Lines == null || masterKeyDialogue.Lines.Count == 0)
            {
                error = $"MorgueBoxBTarget '{name}'에 연결된 MasterKey DialogueSequence에 Line이 없습니다.";
                return false;
            }

            if (!masterKeyDialogue.PauseGame)
            {
                error = $"MorgueBoxBTarget '{name}'의 MasterKey Dialogue는 Pause Dialogue여야 합니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>컴포넌트 추가 시 같은 씬의 공용 진행 시스템을 자동 연결한다.</summary>
        private void Reset()
        {
            target = GetComponent<Target>();
            gameSession = FindFirstObjectByType<GameSession>();
            inventory = FindFirstObjectByType<PlayerInventory>();
            messagePanel = FindFirstObjectByType<MessagePanel>();
            dialogueService = FindFirstObjectByType<DialogueService>();
            completionId = name;
        }
    }
}
