using System.Collections;
using HauntedHouse.Dialogue;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>현재 플레이 세션의 최초 Gameplay 시작 직후 지정한 도입 대화를 한 번 재생한다.</summary>
    public sealed class GameStartDialogueController : MonoBehaviour
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence sequence;

        private bool played;

        private void OnEnable()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
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
            if (!played)
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
