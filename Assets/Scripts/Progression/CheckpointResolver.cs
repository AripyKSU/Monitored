using System;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// GameSession이 발행한 사건을 스토리 flag와 체크포인트 활성화 조건으로 해석한다.
    /// 실제 월드 복원은 CheckpointService와 RespawnCoordinator에 위임한다.
    /// </summary>
    public sealed class CheckpointResolver : MonoBehaviour
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private CheckpointService checkpointService;
        [SerializeField] private StoryCheckpointRule[] rules = Array.Empty<StoryCheckpointRule>();

        /// <summary>기본 참조를 보완하고 GameSession 사건을 구독한다.</summary>
        private void Awake()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            checkpointService ??= FindFirstObjectByType<CheckpointService>();
        }

        /// <summary>스토리 사건을 Resolver에 연결한다.</summary>
        private void OnEnable()
        {
            if (gameSession != null)
            {
                gameSession.StoryEventPublished -= HandleStoryEvent;
                gameSession.StoryEventPublished += HandleStoryEvent;
            }
        }

        /// <summary>스토리 사건 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.StoryEventPublished -= HandleStoryEvent;
            }
        }

        /// <summary>일치하는 규칙을 순서대로 적용해 flag와 체크포인트를 갱신한다.</summary>
        private void HandleStoryEvent(StoryEvent storyEvent)
        {
            if (gameSession == null || gameSession.IsGameplayInputEnabled == false || rules == null)
            {
                return;
            }

            foreach (StoryCheckpointRule rule in rules)
            {
                // 이미 완료 flag가 기록된 규칙은 재진입·재상호작용으로 다시 실행하지 않는다.
                // Save3처럼 최초 방 진입 시에만 체크포인트를 갱신해야 하는 규칙의 일회성을 보장한다.
                if (rule.IsCompleted(gameSession.StoryFlags) ||
                    !rule.Matches(storyEvent, gameSession.StoryFlags))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.FlagId))
                {
                    gameSession.SetStoryFlag(rule.FlagId);
                }

                if (rule.Checkpoint != null && checkpointService != null)
                {
                    checkpointService.ActivateCheckpoint(rule.Checkpoint);
                }
            }
        }

        /// <summary>컴포넌트를 처음 추가했을 때 진행 Core를 자동 연결한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            checkpointService = FindFirstObjectByType<CheckpointService>();
        }
    }

    /// <summary>하나의 StoryEvent를 flag와 체크포인트로 변환하는 씬 설정 규칙이다.</summary>
    [Serializable]
    public sealed class StoryCheckpointRule
    {
        [SerializeField] private StoryEventType eventType;
        [SerializeField] private string subjectId;
        [SerializeField] private string requiredFlagId;
        [SerializeField] private string flagId;
        [SerializeField] private CheckpointDefinition checkpoint;

        /// <summary>규칙이 추가할 flag ID를 반환한다.</summary>
        public string FlagId => flagId;

        /// <summary>규칙이 활성화할 체크포인트를 반환한다.</summary>
        public CheckpointDefinition Checkpoint => checkpoint;

        /// <summary>이 규칙이 추가하는 완료 flag가 이미 기록됐는지 반환한다.</summary>
        public bool IsCompleted(StoryFlagSet flags)
        {
            return flags != null &&
                   !string.IsNullOrWhiteSpace(flagId) &&
                   flags.Contains(flagId);
        }

        /// <summary>사건 종류·대상·필수 flag가 규칙과 일치하는지 검사한다.</summary>
        public bool Matches(StoryEvent storyEvent, StoryFlagSet flags)
        {
            if (storyEvent.Type != eventType ||
                (!string.IsNullOrWhiteSpace(requiredFlagId) && !flags.Contains(requiredFlagId)))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(subjectId))
            {
                return true;
            }

            string actualSubjectId = storyEvent.Type switch
            {
                StoryEventType.RoomEntered => storyEvent.Room != null ? storyEvent.Room.RoomId : string.Empty,
                StoryEventType.ItemAcquired => storyEvent.Item != null ? storyEvent.Item.ItemId : string.Empty,
                StoryEventType.InteractionCompleted => storyEvent.Target != null ? storyEvent.Target.name : string.Empty,
                StoryEventType.PuzzleCompleted => storyEvent.SubjectId,
                _ => string.Empty
            };

            return string.Equals(actualSubjectId, subjectId, StringComparison.Ordinal);
        }
    }
}
