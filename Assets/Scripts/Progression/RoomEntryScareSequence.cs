using System.Collections;
using HauntedHouse.Dialogue;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 방 진입 이벤트에서 특정 문을 닫은 다음 공용 Pause 대화를 재생하는 일회성 연출이다.
    /// 문 이동과 대화 재생을 RoomEntryEvent의 UnityEvent에서 조합할 수 있도록 분리한다.
    /// </summary>
    public sealed class RoomEntryScareSequence : MonoBehaviour
    {
        [SerializeField] private Doorway scareDoor;
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence sequence;
        [SerializeField] private RoomEntryEvent completionEvent;

        private Coroutine playRoutine;

        /// <summary>현재 문 닫힘·대화 연출이 진행 중인지 반환한다.</summary>
        public bool IsPlaying => playRoutine != null;

        /// <summary>
        /// 문을 닫은 뒤 지정한 대화를 재생한다. 이미 실행 중이면 중복 실행을 무시한다.
        /// </summary>
        public void Play()
        {
            if (IsPlaying)
            {
                return;
            }

            if (!TryValidateConfiguration(out string error))
            {
                GameLog.LogError(error, this);
                return;
            }

            playRoutine = StartCoroutine(PlaySequence());
        }

        /// <summary>연출에 필요한 Doorway, DialogueService와 DialogueSequence를 검사한다.</summary>
        /// <param name="error">설정이 잘못됐을 때의 설명.</param>
        /// <returns>연출을 실행할 수 있으면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (scareDoor == null || dialogueService == null || sequence == null)
            {
                error = $"RoomEntryScareSequence '{name}'의 문 또는 대화 참조가 누락됐습니다.";
                return false;
            }

            if (sequence.Lines == null || sequence.Lines.Count == 0)
            {
                error = $"RoomEntryScareSequence '{name}'에 연결된 DialogueSequence에 Line이 없습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>문 닫힘과 대화 재생을 순서대로 실행하고 코루틴 참조를 정리한다.</summary>
        private IEnumerator PlaySequence()
        {
            try
            {
                // 체크포인트 복원은 모든 문을 닫힌 포즈로 되돌리므로,
                // 닫힘 연출이 실제로 재생되도록 먼저 문을 열어 연출의 시작 상태를 만든다.
                if (!scareDoor.IsOpen)
                {
                    yield return scareDoor.Open();
                }

                // 문 닫힘은 대화보다 먼저 실행해 플레이어가 문이 닫히는 장면을 볼 수 있게 한다.
                yield return scareDoor.Close();

                bool dialogueCompleted = false;
                void HandleDialogueCompleted(DialogueSequence completedSequence)
                {
                    if (completedSequence == sequence)
                    {
                        dialogueCompleted = true;
                    }
                }

                dialogueService.DialogueCompleted += HandleDialogueCompleted;

                try
                {
                    if (!dialogueService.TryPlay(sequence))
                    {
                        yield break;
                    }

                    while (dialogueService.IsPlaying && !dialogueCompleted)
                    {
                        yield return null;
                    }

                    if (!dialogueCompleted)
                    {
                        GameLog.LogWarning(
                            $"RoomEntryScareSequence '{name}'의 대화가 완료 이벤트 없이 종료됐습니다.",
                            this);
                        yield break;
                    }

                    // 문 닫힘과 대화가 모두 끝난 뒤에만 RoomEntryEvent의 completion flag를 기록한다.
                    completionEvent?.MarkCompleted();
                }
                finally
                {
                    dialogueService.DialogueCompleted -= HandleDialogueCompleted;
                }
            }
            finally
            {
                playRoutine = null;
            }
        }

        /// <summary>컴포넌트가 처음 추가될 때 현재 씬의 공용 참조를 자동 연결한다.</summary>
        private void Reset()
        {
            dialogueService = FindFirstObjectByType<DialogueService>();
        }

        /// <summary>오브젝트가 비활성화되면 진행 중인 연출을 정리한다.</summary>
        private void OnDisable()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }
        }
    }
}
