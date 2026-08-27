using UnityEngine;
using UnityEngine.Events;
using HauntedHouse.World;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 지정한 Room에 플레이어가 실제로 진입했을 때 한 번만 UnityEvent를 실행한다.
    /// 체크포인트 복원 시 완료 flag가 없으면 다시 실행할 수 있도록 실행 여부를 복원 주기마다 계산한다.
    /// </summary>
    public sealed class RoomEntryEvent : MonoBehaviour
    {
        [Header("진입 조건")]
        [SerializeField] private Room targetRoom;
        [Tooltip("비워 두면 복원 주기마다 다시 실행할 수 있습니다. 입력하면 해당 flag가 복원된 경우 실행을 건너뜁니다.")]
        [SerializeField] private string completionFlagId;

        [Header("공용 진행 상태")]
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private GameSession gameSession;

        [Header("진입 시 실행")]
        [SerializeField] private UnityEvent onEntered;

        private bool hasRun;

        /// <summary>현재 복원 주기에서 이 이벤트가 이미 실행됐는지 반환한다.</summary>
        public bool HasRun => hasRun;

        /// <summary>완료 flag가 현재 활성화되어 이벤트를 건너뛰는 상태인지 반환한다.</summary>
        public bool IsCompletedByFlag => gameSession != null &&
                                         !string.IsNullOrWhiteSpace(completionFlagId) &&
                                         gameSession.HasStoryFlag(completionFlagId);

        /// <summary>플레이어 방 변경과 체크포인트 복원 완료 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered -= HandleRoomEntered;
                playerRoomTracker.RoomEntered += HandleRoomEntered;
            }

            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.StoryFlags.Changed += HandleStoryFlagsChanged;
            }

            RefreshCompletionState();
        }

        /// <summary>컴포넌트가 비활성화될 때 모든 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered -= HandleRoomEntered;
            }

            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
            }
        }

        /// <summary>
        /// 플레이어가 새로운 Room에 진입했을 때 대상 Room인지와 현재 실행 가능 상태를 검사한다.
        /// 실행 여부를 먼저 기록해 콜백 내부에서 방을 다시 통과해도 중복 이벤트가 발생하지 않게 한다.
        /// </summary>
        private void HandleRoomEntered(Room previousRoom, Room currentRoom)
        {
            if (gameSession != null && !gameSession.IsGameplayInputEnabled)
            {
                return;
            }

            if (targetRoom == null || currentRoom != targetRoom || hasRun || IsCompletedByFlag)
            {
                return;
            }

            hasRun = true;
            if (gameSession != null && !string.IsNullOrWhiteSpace(completionFlagId))
            {
                // Room 진입 자체가 완료 조건인 이벤트는 같은 flag를 직접 기록해
                // 별도의 전체 순회 없이 다음 진입부터 즉시 건너뛸 수 있게 한다.
                gameSession.SetStoryFlag(completionFlagId);
            }

            onEntered?.Invoke();
        }

        /// <summary>체크포인트 복원이 끝나면 저장된 flag 기준으로 실행 여부를 다시 설정한다.</summary>
        private void HandleGameplayStarted()
        {
            RefreshCompletionState();
        }

        /// <summary>완료 flag가 복원되거나 새로 추가된 경우 이벤트를 완료 상태로 고정한다.</summary>
        private void HandleStoryFlagsChanged()
        {
            if (IsCompletedByFlag)
            {
                hasRun = true;
            }
        }

        /// <summary>
        /// 복원 주기의 시작 기준을 적용한다.
        /// 완료 flag가 있으면 실행 완료로 유지하고, flag가 없으면 다음 실제 Room 진입을 허용한다.
        /// </summary>
        private void RefreshCompletionState()
        {
            hasRun = IsCompletedByFlag;
        }

        /// <summary>컴포넌트를 처음 추가할 때 현재 방 추적기와 부모 Room을 연결한다.</summary>
        private void Reset()
        {
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            gameSession = FindFirstObjectByType<GameSession>();
            targetRoom = GetComponentInParent<Room>();
        }
    }
}
