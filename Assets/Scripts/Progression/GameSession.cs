using System;
using HauntedHouse.Commands;
using HauntedHouse.Inventory;
using HauntedHouse.Stealth;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 현재 실행 세션의 게임 진행 상태와 스토리 사건 발행을 소유한다.
    /// 플레이어·적의 세부 시스템을 소유하지 않고 생명주기 게이트와 진행 Core만 제공한다.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        [Header("이벤트 소스")]
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private TargetCommandRunner targetCommandRunner;
        [SerializeField] private PlayerCaptureDeathSystem captureDeathSystem;

        private readonly StoryFlagSet storyFlags = new();

        /// <summary>현재 실행 세션의 유일한 StoryFlagSet이다.</summary>
        public StoryFlagSet StoryFlags => storyFlags;

        /// <summary>현재 세션 생명주기 단계를 반환한다.</summary>
        public GameSessionPhase Phase { get; private set; } = GameSessionPhase.Restoring;

        /// <summary>현재 플레이어 입력을 받아도 되는지 반환한다.</summary>
        public bool IsGameplayInputEnabled => Phase == GameSessionPhase.Playing;

        /// <summary>현재 적 AI와 월드 진행을 실행해도 되는지 반환한다.</summary>
        public bool IsGameplaySimulationEnabled => Phase == GameSessionPhase.Playing;

        /// <summary>세션 단계가 실제로 변경될 때 새 단계를 전달한다.</summary>
        public event Action<GameSessionPhase> PhaseChanged;

        /// <summary>스토리 진행 Resolver가 해석할 일회성 사건을 전달한다.</summary>
        public event Action<StoryEvent> StoryEventPublished;

        /// <summary>플레이어 입력과 적 시스템이 다시 시작되는 순간 발생한다.</summary>
        public event Action GameplayStarted;

        /// <summary>기본 참조를 보완하고 복원 중 입력 차단 상태로 시작한다.</summary>
        private void Awake()
        {
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            playerInventory ??= FindFirstObjectByType<PlayerInventory>();
            targetCommandRunner ??= FindFirstObjectByType<TargetCommandRunner>();
            captureDeathSystem ??= FindFirstObjectByType<PlayerCaptureDeathSystem>();
            SetPhase(GameSessionPhase.Restoring);
        }

        /// <summary>런타임 이벤트 소스를 구독한다.</summary>
        private void OnEnable()
        {
            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered -= HandleRoomEntered;
                playerRoomTracker.RoomEntered += HandleRoomEntered;
            }

            if (playerInventory != null)
            {
                playerInventory.ItemAcquired -= HandleItemAcquired;
                playerInventory.ItemAcquired += HandleItemAcquired;
            }

            if (targetCommandRunner != null)
            {
                targetCommandRunner.InteractionCompleted -= HandleInteractionCompleted;
                targetCommandRunner.InteractionCompleted += HandleInteractionCompleted;
            }

            if (captureDeathSystem != null)
            {
                captureDeathSystem.PlayerDied -= HandlePlayerDied;
                captureDeathSystem.PlayerDied += HandlePlayerDied;
            }
        }

        /// <summary>런타임 이벤트 소스 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (playerRoomTracker != null) playerRoomTracker.RoomEntered -= HandleRoomEntered;
            if (playerInventory != null) playerInventory.ItemAcquired -= HandleItemAcquired;
            if (targetCommandRunner != null) targetCommandRunner.InteractionCompleted -= HandleInteractionCompleted;
            if (captureDeathSystem != null) captureDeathSystem.PlayerDied -= HandlePlayerDied;
        }

        /// <summary>방 진입 사건을 발행한다.</summary>
        public void PublishRoomEntered(Room room)
        {
            Publish(new StoryEvent(StoryEventType.RoomEntered, room: room));
        }

        /// <summary>아이템 획득 사건을 발행한다.</summary>
        public void PublishItemAcquired(ItemDefinition item)
        {
            Publish(new StoryEvent(StoryEventType.ItemAcquired, item: item));
        }

        /// <summary>성공한 대상 행동 완료 사건을 발행한다.</summary>
        public void PublishInteractionCompleted(Target target)
        {
            Publish(new StoryEvent(StoryEventType.InteractionCompleted, target: target));
        }

        /// <summary>퍼즐 구현체가 해결 사실을 전달할 때 호출한다.</summary>
        public void PublishPuzzleCompleted(string puzzleId)
        {
            Publish(new StoryEvent(StoryEventType.PuzzleCompleted, subjectId: puzzleId));
        }

        /// <summary>체크포인트 복원 중에는 사건을 진행 상태로 전달하지 않는다.</summary>
        public void Publish(StoryEvent storyEvent)
        {
            if (Phase != GameSessionPhase.Playing)
            {
                return;
            }

            StoryEventPublished?.Invoke(storyEvent);
        }

        /// <summary>
        /// 초기 시작과 Restart가 공통으로 사용하는 복원 단계로 전환한다.
        /// </summary>
        public void BeginRestore()
        {
            SetPhase(GameSessionPhase.Restoring);
        }

        /// <summary>체크포인트 복원이 끝난 뒤 플레이를 시작한다.</summary>
        public void CompleteRestore()
        {
            SetPhase(GameSessionPhase.Playing);
            GameplayStarted?.Invoke();
        }

        /// <summary>사망 확정 시 Restart만 허용하는 단계로 전환한다.</summary>
        public void MarkDead()
        {
            SetPhase(GameSessionPhase.Dead);
        }

        /// <summary>세션 단계가 달라질 때만 저장하고 외부 시스템에 알린다.</summary>
        private void SetPhase(GameSessionPhase phase)
        {
            if (Phase == phase)
            {
                return;
            }

            GameLog.Log($"GameSession Phase 변경: {Phase} -> {phase}.", this);
            Phase = phase;
            PhaseChanged?.Invoke(Phase);
        }

        /// <summary>PlayerRoomTracker 사건을 세션 사건으로 변환한다.</summary>
        private void HandleRoomEntered(Room previousRoom, Room currentRoom)
        {
            PublishRoomEntered(currentRoom);
        }

        /// <summary>인벤토리 획득 사건을 세션 사건으로 변환한다.</summary>
        private void HandleItemAcquired(ItemDefinition item)
        {
            PublishItemAcquired(item);
        }

        /// <summary>TargetCommandRunner 완료 사건을 세션 사건으로 변환한다.</summary>
        private void HandleInteractionCompleted(Target target)
        {
            PublishInteractionCompleted(target);
        }

        /// <summary>사망은 스토리 사건이 아니라 세션 복원 단계 전환 신호로 처리한다.</summary>
        private void HandlePlayerDied()
        {
            GameLog.Log("GameSession이 PlayerDied 이벤트를 받아 Dead 상태로 전환합니다.", this);
            MarkDead();
        }

        /// <summary>컴포넌트를 처음 추가했을 때 현재 씬의 이벤트 소스를 연결한다.</summary>
        private void Reset()
        {
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            playerInventory = FindFirstObjectByType<PlayerInventory>();
            targetCommandRunner = FindFirstObjectByType<TargetCommandRunner>();
            captureDeathSystem = FindFirstObjectByType<PlayerCaptureDeathSystem>();
        }
    }
}
