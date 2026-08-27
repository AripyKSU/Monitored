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

            if (!ActivateCheckpoint(initialCheckpoint))
            {
                GameLog.LogError("초기 CheckpointDefinition을 활성화하지 못했습니다.", this);
                yield break;
            }

            RestoreActiveCheckpoint();
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
}
