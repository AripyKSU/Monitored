using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Dialogue
{
    /// <summary>
    /// 플레이어가 도착하면 공용 DialogueService로 지정한 대화를 재생하고,
    /// 재생이 끝난 뒤 한 번만 완료되는 Target 행동을 제공한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public sealed class DialogueTarget : MonoBehaviour, ITargetAction, ICompletedState
    {
        [Header("대화 설정")]
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence sequence;
        [SerializeField] private string completionId;

        private Target target;

        /// <summary>체크포인트 복원 시 대화 완료 여부를 저장하는 고유 ID다.</summary>
        public string CompletionId => string.IsNullOrWhiteSpace(completionId) ? name : completionId;

        /// <summary>이 대화 Target이 현재 완료됐는지 반환한다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// 대화를 시작하고 DialogueService가 입력을 받아 재생을 끝낼 때까지 현재 Target 명령을 유지한다.
        /// DialogueService가 Pause 대화의 일시정지 수명과 진행 입력을 소유하므로 이 컴포넌트는 중복 처리하지 않는다.
        /// </summary>
        /// <param name="context">현재 Target과 플레이어를 담은 실행 문맥.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (!TryValidateConfiguration(out string error))
            {
                GameLog.LogError(error, this);
                yield break;
            }

            bool completed = false;

            // 여러 DialogueTarget이 공용 서비스를 사용해도 현재 Sequence의 종료만 받아야 한다.
            void HandleDialogueCompleted(DialogueSequence completedSequence)
            {
                if (completedSequence == sequence)
                {
                    completed = true;
                }
            }

            dialogueService.DialogueCompleted += HandleDialogueCompleted;

            try
            {
                if (!dialogueService.TryPlay(sequence))
                {
                    yield break;
                }

                if (!sequence.PauseGame)
                {
                    // Non-Pause 대화는 DialogueService가 별도로 재생하는 동안 Runner를 점유하지 않는다.
                    // 그래야 최종 추격 중에도 플레이어 이동과 다른 Target 선택을 계속 사용할 수 있다.
                    IsCompleted = true;
                    context?.Target?.SetSelectable(false);
                    context?.MarkCompleted();
                    yield break;
                }

                // Pause 대화도 서비스 코루틴이 입력을 모두 처리하므로 재생 종료 시점만 기다린다.
                while (dialogueService.IsPlaying && !completed)
                {
                    yield return null;
                }

                if (!completed)
                {
                    GameLog.LogWarning(
                        $"DialogueTarget '{name}'의 대화가 정상 완료 이벤트 없이 종료됐습니다.",
                        this);
                    yield break;
                }

                IsCompleted = true;
                context?.Target?.SetSelectable(false);
                context?.MarkCompleted();
            }
            finally
            {
                dialogueService.DialogueCompleted -= HandleDialogueCompleted;
            }
        }

        /// <summary>체크포인트에서 대화 완료 상태와 Target 선택 가능 여부를 함께 복원한다.</summary>
        /// <param name="completed">완료 상태로 복원할지 여부.</param>
        public void RestoreCompletedState(bool completed)
        {
            IsCompleted = completed;
            target ??= GetComponent<Target>();
            target?.SetSelectable(!completed);
        }

        /// <summary>대화 서비스, Sequence와 대상 컴포넌트가 실행 가능한지 검사한다.</summary>
        /// <param name="error">설정이 잘못됐을 때의 설명.</param>
        /// <returns>필수 설정이 모두 유효하면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (dialogueService == null || sequence == null)
            {
                error = $"DialogueTarget '{name}'의 DialogueService 또는 DialogueSequence가 누락됐습니다.";
                return false;
            }

            if (sequence.Lines == null || sequence.Lines.Count == 0)
            {
                error = $"DialogueTarget '{name}'에 연결된 DialogueSequence에 Line이 없습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>컴포넌트가 처음 추가될 때 같은 오브젝트의 Target과 씬 공용 DialogueService를 연결한다.</summary>
        private void Reset()
        {
            target = GetComponent<Target>();
            dialogueService = FindFirstObjectByType<DialogueService>();
            completionId = name;
        }
    }
}
