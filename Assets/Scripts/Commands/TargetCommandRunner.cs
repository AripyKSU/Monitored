using System;
using System.Collections;
using HauntedHouse.Core;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Commands
{
    /// <summary>
    /// 선택 요청으로 받은 하나의 Target에 대해 이동과 도착 후 행동을 차례로 실행한다.
    /// 명령 대기열이나 예약은 관리하지 않으며 현재 실행이 끝날 때까지 추가 요청을 거부한다.
    /// </summary>
    public sealed class TargetCommandRunner : MonoBehaviour
    {
        [Header("명령 실행 주체")]
        [SerializeField] private Player player;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private GameSession gameSession;
        [SerializeField] private GamePauseService pauseService;

        [Header("선택 입력과 마커")]
        [SerializeField] private TargetPresenter[] presenters;

        private Coroutine activeCommand;

        /// <summary>
        /// 현재 Target 명령의 실행 단계다.
        /// </summary>
        public TargetCommandState State { get; private set; } = TargetCommandState.Idle;

        /// <summary>
        /// 이동 또는 행동 실행 중이라 새 Target을 받을 수 없는지 나타낸다.
        /// </summary>
        public bool IsBusy => State != TargetCommandState.Idle;

        /// <summary>
        /// 외부 UI나 디버그 도구가 상태 변화를 관찰할 수 있도록 알린다.
        /// </summary>
        public event Action<TargetCommandState> StateChanged;

        /// <summary>성공적으로 완료된 Target을 진행 Core에 전달한다.</summary>
        public event Action<Target> InteractionCompleted;

        /// <summary>
        /// 씬에 명시적으로 연결되지 않은 기본 참조를 한 번 보완한다.
        /// </summary>
        private void Awake()
        {
            player ??= FindFirstObjectByType<Player>();
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
            gameSession ??= FindFirstObjectByType<GameSession>();
            pauseService ??= FindFirstObjectByType<GamePauseService>();

            // 씬 설정 후 Target이 추가됐더라도 직렬화된 이전 목록에 누락되지 않도록
            // 현재 씬의 전체 Presenter를 시작 시점에 다시 수집한다.
            presenters = FindObjectsByType<TargetPresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }

        /// <summary>
        /// 각 Presenter의 선택 요청을 구독하고 현재 상태에 맞춰 표시를 초기화한다.
        /// </summary>
        private void OnEnable()
        {
            if (stealthHideSystem != null)
            {
                stealthHideSystem.StateChanged -= HandleStealthStateChanged;
                stealthHideSystem.StateChanged += HandleStealthStateChanged;
            }

            if (gameSession != null)
            {
                gameSession.PhaseChanged -= HandleGameSessionPhaseChanged;
                gameSession.PhaseChanged += HandleGameSessionPhaseChanged;
            }

            if (pauseService != null)
            {
                pauseService.PauseStateChanged -= HandlePauseStateChanged;
                pauseService.PauseStateChanged += HandlePauseStateChanged;
            }

            SubscribePresenters();
            SetPresentersEnabled(State == TargetCommandState.Idle);
        }

        /// <summary>
        /// 실행 중 Runner가 비활성화되면 코루틴과 입력 잠금을 정리해 마커가 영구적으로 숨지 않게 한다.
        /// </summary>
        private void OnDisable()
        {
            if (stealthHideSystem != null)
            {
                stealthHideSystem.StateChanged -= HandleStealthStateChanged;
            }

            if (gameSession != null)
            {
                gameSession.PhaseChanged -= HandleGameSessionPhaseChanged;
            }

            if (pauseService != null)
            {
                pauseService.PauseStateChanged -= HandlePauseStateChanged;
            }

            UnsubscribePresenters();

            if (activeCommand != null)
            {
                StopCoroutine(activeCommand);
                activeCommand = null;
            }

            SetState(TargetCommandState.Idle);
            SetPresentersEnabled(true);
        }

        /// <summary>
        /// Target 하나를 현재 명령으로 받아 실행을 시작한다.
        /// 실행 중인 명령이 있거나 Target 설정이 잘못된 경우 시작하지 않는다.
        /// </summary>
        /// <param name="target">접근하고 행동을 실행할 대상.</param>
        /// <returns>명령을 정상적으로 접수했으면 true.</returns>
        public bool TryExecute(Target target)
        {
            if (IsBusy ||
                (gameSession != null && !gameSession.IsGameplayInputEnabled) ||
                (pauseService != null && pauseService.IsPaused))
            {
                // 이번 단계에서는 명령 예약을 지원하지 않으므로 실행 중 요청은 단순히 거부한다.
                return false;
            }

            if (player == null)
            {
                GameLog.LogError("TargetCommandRunner에 Player가 연결되지 않았습니다.", this);
                return false;
            }

            if (playerRoomTracker == null || playerRoomTracker.CurrentRoom == null)
            {
                GameLog.LogError("TargetCommandRunner에 현재 방을 제공할 PlayerRoomTracker가 없습니다.", this);
                return false;
            }

            if (target == null || !target.IsSelectableIn(playerRoomTracker.CurrentRoom))
            {
                return false;
            }

            if (!target.TryValidateConfiguration(out string configurationError))
            {
                GameLog.LogError(configurationError, target);
                return false;
            }

            // 코루틴을 시작하기 전에 상태와 UI를 먼저 잠가 같은 프레임의 연속 클릭을 차단한다.
            SetState(TargetCommandState.Moving);
            SetPresentersEnabled(false);
            activeCommand = StartCoroutine(ExecuteTarget(target));
            return true;
        }

        /// <summary>
        /// 플레이어를 Target의 접근 지점으로 이동시킨 뒤 Target 행동이 끝날 때까지 기다린다.
        /// 성공과 실패 어느 경우에도 마지막 정리 구간에서 Idle과 마커 표시를 복구한다.
        /// </summary>
        private IEnumerator ExecuteTarget(Target target)
        {
            try
            {
                // Player는 내부 PlayerMover를 사용하지만 Runner는 Player의 공개 이동 기능만 호출한다.
                yield return player.MoveTo(target.ApproachPoint);

                if (!player.LastMoveSucceeded)
                {
                    GameLog.LogError(
                        $"Target '{target.name}' 접근에 실패했습니다: {player.LastMoveFailureReason}",
                        target);
                    yield break;
                }

                // 이동 중 대상 소멸이나 비활성화는 정상 분기가 아니라 스테이지 구성 오류다.
                if (target == null || !target.IsSelectable)
                {
                    GameLog.LogError("이동 중 Target이 제거되거나 선택 불가능 상태가 됐습니다.", this);
                    yield break;
                }

                ITargetAction action = target.Action;
                if (action == null)
                {
                    GameLog.LogError($"Target '{target.name}'의 행동을 실행할 수 없습니다.", target);
                    yield break;
                }

                SetState(TargetCommandState.Executing);
                TargetActionContext context = new TargetActionContext(target, player);

                // null 코루틴은 즉시 완료 행동으로 취급하고, 반환된 코루틴은 끝까지 기다린다.
                IEnumerator actionRoutine = action.Execute(context);
                if (actionRoutine != null)
                {
                    yield return actionRoutine;
                }

                if (context.IsCompleted)
                {
                    InteractionCompleted?.Invoke(target);
                }
            }
            finally
            {
                // 어떤 종료 경로에서도 다음 Target을 선택할 수 있도록 실행 상태와 마커를 복구한다.
                activeCommand = null;
                SetState(TargetCommandState.Idle);
                SetPresentersEnabled(true);
            }
        }

        /// <summary>
        /// 모든 Presenter의 선택 요청을 현재 Runner에 연결한다.
        /// </summary>
        private void SubscribePresenters()
        {
            if (presenters == null)
            {
                return;
            }

            foreach (TargetPresenter presenter in presenters)
            {
                if (presenter != null)
                {
                    presenter.SelectionRequested -= HandleSelectionRequested;
                    presenter.SelectionRequested += HandleSelectionRequested;
                }
            }
        }

        /// <summary>
        /// Runner가 비활성화될 때 Presenter 이벤트 구독을 해제한다.
        /// </summary>
        private void UnsubscribePresenters()
        {
            if (presenters == null)
            {
                return;
            }

            foreach (TargetPresenter presenter in presenters)
            {
                if (presenter != null)
                {
                    presenter.SelectionRequested -= HandleSelectionRequested;
                }
            }
        }

        /// <summary>
        /// Presenter가 전달한 Target을 현재 명령으로 실행해 본다.
        /// </summary>
        private void HandleSelectionRequested(Target target)
        {
            TryExecute(target);
        }

        /// <summary>
        /// 명령 단계에 맞춰 모든 Target 마커와 클릭 요청을 한꺼번에 전환한다.
        /// </summary>
        private void SetPresentersEnabled(bool value)
        {
            // ExecuteTarget의 finally가 명령 종료 후 UI를 다시 켜도 은신 중에는 계속 잠근다.
            value = value &&
                    (gameSession == null || gameSession.IsGameplayInputEnabled) &&
                    (stealthHideSystem == null || !stealthHideSystem.IsHidden) &&
                    (pauseService == null || !pauseService.IsPaused);

            if (presenters == null)
            {
                return;
            }

            foreach (TargetPresenter presenter in presenters)
            {
                presenter?.SetPresentationEnabled(value);
            }
        }

        /// <summary>
        /// 은신 상태가 바뀌면 현재 명령 상태를 기준으로 Target 표시를 즉시 갱신한다.
        /// </summary>
        /// <param name="state">새 은신 상태.</param>
        /// <param name="hidingRoom">은신 중인 방.</param>
        private void HandleStealthStateChanged(StealthState state, Room hidingRoom)
        {
            SetPresentersEnabled(State == TargetCommandState.Idle && state != StealthState.Hidden);
        }

        /// <summary>세션 복원·사망 단계에 맞춰 Target 표시와 입력을 즉시 갱신한다.</summary>
        private void HandleGameSessionPhaseChanged(GameSessionPhase phase)
        {
            if (phase != GameSessionPhase.Playing && activeCommand != null)
            {
                AbortForRespawn();
                return;
            }

            SetPresentersEnabled(phase == GameSessionPhase.Playing && State == TargetCommandState.Idle);
        }

        /// <summary>
        /// 대화나 아이템 선택처럼 공용 일시정지가 시작·종료될 때 Target 입력과 마커를 갱신한다.
        /// </summary>
        /// <param name="isPaused">현재 공용 게임 일시정지 상태.</param>
        private void HandlePauseStateChanged(bool isPaused)
        {
            SetPresentersEnabled(!isPaused && State == TargetCommandState.Idle);
        }

        /// <summary>
        /// 초기 복원이나 Restart 전에 현재 명령과 플레이어 이동을 중단한다.
        /// 복원 단계에서는 Target 표시를 다시 켜지 않는다.
        /// </summary>
        public void AbortForRespawn()
        {
            if (activeCommand != null)
            {
                StopCoroutine(activeCommand);
                activeCommand = null;
            }

            player?.AbortMove();
            SetState(TargetCommandState.Idle);
            SetPresentersEnabled(false);
        }

        /// <summary>
        /// 실제로 상태가 달라질 때만 저장하고 관찰자에게 변경을 알린다.
        /// </summary>
        private void SetState(TargetCommandState value)
        {
            if (State == value)
            {
                return;
            }

            State = value;
            StateChanged?.Invoke(State);
        }

        /// <summary>
        /// 컴포넌트를 처음 추가할 때 현재 씬의 Player를 기본 실행 주체로 연결한다.
        /// Presenter 목록은 Awake에서 한 번 보완한다.
        /// </summary>
        private void Reset()
        {
            player = FindFirstObjectByType<Player>();
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
            gameSession = FindFirstObjectByType<GameSession>();
            pauseService = FindFirstObjectByType<GamePauseService>();
        }
    }
}
