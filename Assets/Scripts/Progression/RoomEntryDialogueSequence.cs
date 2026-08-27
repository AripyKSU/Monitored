using System.Collections;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// RoomEntryEvent가 시작한 일회성 진입 대화를 공용 DialogueService로 재생한다.
    /// Dialogue가 정상 종료된 경우에만 RoomEntryEvent의 완료 flag를 기록한다.
    /// </summary>
    public sealed class RoomEntryDialogueSequence : MonoBehaviour
    {
        [Header("대화 설정")]
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence sequence;
        [SerializeField] private RoomEntryEvent completionEvent;

        private Coroutine playRoutine;

        /// <summary>현재 진입 대화가 재생 중인지 반환한다.</summary>
        public bool IsPlaying => playRoutine != null;

        /// <summary>
        /// 방 진입 대화를 시작한다. RoomEntryEvent의 UnityEvent에서 호출하며 중복 실행은 무시한다.
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

        /// <summary>DialogueService, Sequence와 완료 이벤트 참조가 실행 가능한지 검사한다.</summary>
        /// <param name="error">설정이 잘못됐을 때의 설명.</param>
        /// <returns>진입 대화를 재생할 수 있으면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (dialogueService == null || sequence == null || completionEvent == null)
            {
                error = $"RoomEntryDialogueSequence '{name}'의 DialogueService, Sequence 또는 RoomEntryEvent가 누락됐습니다.";
                return false;
            }

            if (sequence.Lines == null || sequence.Lines.Count == 0)
            {
                error = $"RoomEntryDialogueSequence '{name}'에 연결된 DialogueSequence에 Line이 없습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>공용 대화를 끝까지 기다린 뒤 완료 flag를 기록한다.</summary>
        private IEnumerator PlaySequence()
        {
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

                // Pause 대화의 입력과 일시정지는 DialogueService가 소유하므로
                // 이 컴포넌트는 지정한 Sequence의 완료 이벤트만 기다린다.
                while (dialogueService.IsPlaying && !dialogueCompleted)
                {
                    yield return null;
                }

                if (!dialogueCompleted)
                {
                    GameLog.LogWarning(
                        $"RoomEntryDialogueSequence '{name}'의 대화가 완료 이벤트 없이 종료됐습니다.",
                        this);
                    yield break;
                }

                // 진입 직후가 아니라 실제 대화 완료 뒤에만 일회성 flag를 기록한다.
                completionEvent.MarkCompleted();
            }
            finally
            {
                dialogueService.DialogueCompleted -= HandleDialogueCompleted;
                playRoutine = null;
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 공용 DialogueService와 같은 오브젝트의 이벤트를 연결한다.</summary>
        private void Reset()
        {
            dialogueService = FindFirstObjectByType<DialogueService>();
            completionEvent = GetComponent<RoomEntryEvent>();
        }

        /// <summary>오브젝트가 비활성화되면 이 컴포넌트가 가진 재생 코루틴을 정리한다.</summary>
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
