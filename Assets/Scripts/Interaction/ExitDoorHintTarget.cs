using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Sector3 출구 문을 조사할 때 잠금 메시지와 최초 1회 출구 안내 대화를 재생한다.
    /// 마스터키를 얻은 뒤에는 기존 DoorTarget에 실행을 위임해 실제 문 통과 흐름을 보존한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public sealed class ExitDoorHintTarget : MonoBehaviour, ITargetAction
    {
        [Header("기존 문 행동")]
        [SerializeField] private DoorTarget normalExitDoor;

        [Header("진행 상태")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private string masterKeyFlagId = StoryProgressFlags.MasterKeyAcquired;
        [SerializeField] private string hintShownFlagId = StoryProgressFlags.ExitDoorHintShown;

        [Header("잠금 안내")]
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField, TextArea] private string lockedMessage = "잠겨있다.";

        [Header("최초 출구 안내 대화")]
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence hintDialogue;

        /// <summary>
        /// 마스터키 획득 전에는 출구 힌트 이벤트를 실행하고, 획득 후에는 기존 문 행동을 실행한다.
        /// </summary>
        /// <param name="context">현재 Target과 플레이어를 담은 실행 문맥.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (!TryValidateConfiguration(out string error))
            {
                GameLog.LogError(error, this);
                yield break;
            }

            // 마스터키 획득 이후에는 문 열기·아이템 선택·방 이동을 기존 구현에 맡긴다.
            if (gameSession.HasStoryFlag(masterKeyFlagId))
            {
                yield return normalExitDoor.Execute(context);
                yield break;
            }

            // 출구가 잠겨 있다는 즉시 피드백을 먼저 표시한다.
            yield return messagePanel.Show(lockedMessage);

            // ExitDoorHintShown이 없을 때만 Pause Dialogue를 한 번 재생한다.
            if (!gameSession.HasStoryFlag(hintShownFlagId))
            {
                bool dialogueCompleted = false;

                void HandleDialogueCompleted(DialogueSequence completedSequence)
                {
                    if (completedSequence == hintDialogue)
                    {
                        dialogueCompleted = true;
                    }
                }

                dialogueService.DialogueCompleted += HandleDialogueCompleted;
                try
                {
                    if (!dialogueService.TryPlay(hintDialogue))
                    {
                        yield break;
                    }

                    // DialogueService가 Pause 수명과 입력을 소유하므로 완료 이벤트만 기다린다.
                    while (dialogueService.IsPlaying && !dialogueCompleted)
                    {
                        yield return null;
                    }

                    // 비정상 종료 시에는 진행 flag를 기록하지 않아 다음 선택에서 재시도할 수 있게 한다.
                    if (!dialogueCompleted)
                    {
                        GameLog.LogWarning(
                            $"ExitDoorHintTarget '{name}'의 출구 안내 대화가 정상 완료되지 않았습니다.",
                            this);
                        yield break;
                    }

                    gameSession.SetStoryFlag(hintShownFlagId);
                }
                finally
                {
                    dialogueService.DialogueCompleted -= HandleDialogueCompleted;
                }
            }

            // 메시지·대화가 모두 끝난 상호작용만 진행 Core에 완료로 전달한다.
            context?.MarkCompleted();
        }

        /// <summary>
        /// 출구 이벤트와 이후 문 통과에 필요한 참조·문구·대화 설정을 검사한다.
        /// </summary>
        /// <param name="error">설정이 잘못됐을 때의 설명.</param>
        /// <returns>모든 필수 설정이 유효하면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (normalExitDoor == null || gameSession == null || messagePanel == null ||
                dialogueService == null || hintDialogue == null)
            {
                error = $"ExitDoorHintTarget '{name}'의 기존 DoorTarget, 진행 상태, UI 또는 대화 참조가 누락됐습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(masterKeyFlagId) || string.IsNullOrWhiteSpace(hintShownFlagId))
            {
                error = $"ExitDoorHintTarget '{name}'의 Story flag ID가 비어 있습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(lockedMessage))
            {
                error = $"ExitDoorHintTarget '{name}'의 잠금 메시지가 비어 있습니다.";
                return false;
            }

            if (!hintDialogue.PauseGame || hintDialogue.Lines == null || hintDialogue.Lines.Count == 0)
            {
                error = $"ExitDoorHintTarget '{name}'의 출구 안내 대화는 Pause 대화와 하나 이상의 Line이 필요합니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>컴포넌트가 처음 추가될 때 같은 오브젝트의 기존 문과 공용 시스템을 연결한다.</summary>
        private void Reset()
        {
            normalExitDoor = GetComponent<DoorTarget>();
            gameSession = FindFirstObjectByType<GameSession>();
            messagePanel = FindFirstObjectByType<MessagePanel>();
            dialogueService = FindFirstObjectByType<DialogueService>();
        }
    }
}
