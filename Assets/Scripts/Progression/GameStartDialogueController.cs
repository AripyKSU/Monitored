using System.Collections;
using HauntedHouse.Dialogue;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>현재 플레이 세션의 최초 Gameplay 시작 직후 지정한 도입 대화를 한 번 재생한다.</summary>
    public sealed class GameStartDialogueController : MonoBehaviour
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private CheckpointService checkpointService;
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence sequence;

        private bool played;

        private void OnEnable()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            checkpointService ??= FindFirstObjectByType<CheckpointService>();
            dialogueService ??= FindFirstObjectByType<DialogueService>();
            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }
        }

        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }
        }

        private void HandleGameplayStarted()
        {
            // 개발용 Save2~5 시작은 새 게임이 아니므로 Save1 전용 도입 대화를 재생하지 않는다.
            bool startsFromSave1 = checkpointService?.ActiveCheckpoint != null &&
                                   checkpointService.ActiveCheckpoint.CheckpointId == "Save1";
            if (!played && startsFromSave1)
            {
                StartCoroutine(PlayAfterRestore());
            }
        }

        /// <summary>복원 완료 이벤트의 다른 구독자 처리가 끝난 다음 프레임에 도입 대화를 재생한다.</summary>
        private IEnumerator PlayAfterRestore()
        {
            played = true;
            yield return null;
            if (dialogueService == null || sequence == null || !dialogueService.TryPlay(sequence))
            {
                GameLog.LogError($"GameStartDialogueController '{name}'가 시작 대화를 재생하지 못했습니다.", this);
            }
        }
    }
}
