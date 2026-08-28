using System;
using System.Collections;
using HauntedHouse.Stealth;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 현재 활성 체크포인트를 메모리에 보관하고 초기 시작·Restart 복원을 요청한다.
    /// 상태별 세부 적용 순서는 RespawnCoordinator가 담당한다.
    /// </summary>
    public sealed class CheckpointService : MonoBehaviour
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private CheckpointDefinition initialCheckpoint;
        [SerializeField] private RespawnCoordinator respawnCoordinator;

#if UNITY_EDITOR
        [Header("개발용 시작 지점")]
        [Tooltip("실험할 때 시작할 세이브 지점이다. Scene Default는 Initial Checkpoint를 그대로 사용하며, 빌드에는 적용되지 않는다.")]
        [SerializeField] private DevelopmentStartCheckpoint developmentStartCheckpoint;
#endif

        private bool isRestoring;

        /// <summary>현재 활성화된 체크포인트 정의다.</summary>
        public CheckpointDefinition ActiveCheckpoint { get; private set; }

        /// <summary>활성 체크포인트가 바뀔 때 새 정의를 전달한다.</summary>
        public event Action<CheckpointDefinition> CheckpointChanged;

        /// <summary>기본 참조를 보완한다.</summary>
        private void Awake()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            respawnCoordinator ??= FindFirstObjectByType<RespawnCoordinator>();
        }

        /// <summary>초기 체크포인트를 활성화하고 게임 시작 복원을 수행한다.</summary>
        private void Start()
        {
            StartCoroutine(InitializeAndRestore());
        }

        /// <summary>
        /// 씬의 모든 컴포넌트가 Start 초기화를 마친 다음 최초 체크포인트를 적용한다.
        /// 적과 플레이어가 복원 전에 기존 런타임 상태로 움직이는 것을 막는다.
        /// </summary>
        private IEnumerator InitializeAndRestore()
        {
            yield return null;

            CheckpointDefinition startCheckpoint = ResolveStartCheckpoint();
            if (!ActivateCheckpoint(startCheckpoint))
            {
                GameLog.LogError("초기 CheckpointDefinition을 활성화하지 못했습니다.", this);
                yield break;
            }

            RestoreActiveCheckpoint();
        }

        /// <summary>
        /// Editor 실험 설정이 있으면 해당 세이브 지점을 찾고, 그 외에는 씬의 기본 시작 체크포인트를 반환한다.
        /// 개발용 선택값이 빌드의 정상 시작 흐름에 영향을 주지 않도록 Editor에서만 덮어쓴다.
        /// </summary>
        private CheckpointDefinition ResolveStartCheckpoint()
        {
#if UNITY_EDITOR
            if (developmentStartCheckpoint != DevelopmentStartCheckpoint.SceneDefault)
            {
                string checkpointId = developmentStartCheckpoint.ToString();
                CheckpointDefinition[] checkpoints = FindObjectsByType<CheckpointDefinition>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                // 씬에 배치된 체크포인트 중 Inspector에서 선택한 고유 ID와 일치하는 정의를 사용한다.
                foreach (CheckpointDefinition checkpoint in checkpoints)
                {
                    if (checkpoint != null &&
                        string.Equals(checkpoint.CheckpointId, checkpointId, StringComparison.Ordinal))
                    {
                        return checkpoint;
                    }
                }

                GameLog.LogWarning(
                    $"개발용 시작 지점 '{checkpointId}'을 씬에서 찾지 못해 Initial Checkpoint를 사용합니다.",
                    this);
            }
#endif

            return initialCheckpoint;
        }

        /// <summary>
        /// 새로운 체크포인트 정의를 현재 활성 정의로 교체한다.
        /// 이 호출은 현재 월드를 즉시 되감지 않고 이후 사망 복원의 기준만 변경한다.
        /// </summary>
        public bool ActivateCheckpoint(CheckpointDefinition checkpoint)
        {
            string error = string.Empty;
            if (checkpoint == null || !checkpoint.TryValidateConfiguration(out error))
            {
                GameLog.LogError(error ?? "활성화할 CheckpointDefinition이 없습니다.", checkpoint != null ? checkpoint : this);
                return false;
            }

            ActiveCheckpoint = checkpoint;
            if (checkpoint.StoryStage != StoryStage.None)
            {
                gameSession?.SetStoryStage(checkpoint.StoryStage);
            }

            CheckpointChanged?.Invoke(ActiveCheckpoint);
            return true;
        }

        /// <summary>활성 체크포인트를 적용해 초기 시작과 Restart를 같은 흐름으로 처리한다.</summary>
        public bool RestoreActiveCheckpoint()
        {
            if (isRestoring || ActiveCheckpoint == null || respawnCoordinator == null || gameSession == null)
            {
                return false;
            }

            isRestoring = true;
            gameSession.BeginRestore();
            StartCoroutine(RestoreRoutine(ActiveCheckpoint));
            return true;
        }

        /// <summary>복원 코루틴이 끝난 뒤 다시 복원 요청을 받을 수 있도록 정리한다.</summary>
        private IEnumerator RestoreRoutine(CheckpointDefinition checkpoint)
        {
            yield return respawnCoordinator.Restore(checkpoint);
            isRestoring = false;
            gameSession.CompleteRestore();
        }

        /// <summary>컴포넌트를 처음 추가했을 때 초기 체크포인트와 복원기를 연결한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            respawnCoordinator = FindFirstObjectByType<RespawnCoordinator>();
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Unity Editor에서 특정 세이브 상태부터 플레이하기 위한 개발 전용 시작 지점 선택값이다.
    /// </summary>
    public enum DevelopmentStartCheckpoint
    {
        SceneDefault,
        Save1,
        Save2,
        Save3,
        Save4,
        Save5
    }
#endif
}
