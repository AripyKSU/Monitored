using System;
using System.Collections;
using System.Collections.Generic;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using HauntedHouse.World;
using UnityEngine;
using UnityEngine.AI;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 하나의 적을 고정 순찰 경로에 따라 이동시키고 상태와 현재 목적지를 공개한다.
    /// 추격 상태는 예약만 하고 실제 추격 규칙은 은신·포획 단계에서 추가한다.
    /// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyRoomTracker))]
[RequireComponent(typeof(EnemyDoorTraversal))]
[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyController : MonoBehaviour
    {
        [Header("실행 구성")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private EnemyRoomTracker roomTracker;
        [SerializeField] private EnemyDoorTraversal doorTraversal;
        [SerializeField] private RoomGraph roomGraph;
        [SerializeField] private PatrolRoute initialRoute;
        [SerializeField] private bool initiallyActive = true;
        [SerializeField] private GameSession gameSession;

        [Header("도착 판정")]
        [SerializeField, Min(0f)] private float arrivalTolerance = 0.05f;

        private Renderer[] renderers;
        private bool[] rendererInitialVisibility;
        private Collider[] colliders;
        private Rigidbody contactRigidbody;
        private int targetIndex;
        private float waitRemaining;
        private PatrolPoint currentDestination;
        private Coroutine traversalRoutine;
        private List<RoomGraphStep> activePath;
        private int activePathIndex;
        private Transform chaseTarget;
        private Room chaseTargetRoom;
        private bool chaseRoomReachedNotified;
        private bool chaseMovementHeld;
        private bool restoredFromCheckpoint;
        private bool scriptedSequencePaused;

        /// <summary>현재 적 상태를 반환한다.</summary>
        public EnemyState State { get; private set; } = EnemyState.Inactive;

        /// <summary>
        /// 기존 세부 상태를 스토리 흐름용 상위 모드로 변환해 반환한다.
        /// ScriptedSequence은 기존 State를 덮어쓰지 않고 일시 정지 플래그로 표현해 기존 AI와의 호환성을 유지한다.
        /// </summary>
        public EnemyMode Mode => State == EnemyState.Inactive
            ? EnemyMode.Inactive
            : scriptedSequencePaused
                ? EnemyMode.ScriptedSequence
                : State == EnemyState.Chasing ? EnemyMode.Chase : EnemyMode.Patrol;

        /// <summary>현재 적용된 순찰 경로를 반환한다.</summary>
        public PatrolRoute CurrentRoute { get; private set; }

        /// <summary>적이 현재 속한 방을 반환한다.</summary>
        public Room CurrentRoom => roomTracker != null ? roomTracker.CurrentRoom : null;

        /// <summary>
        /// 적이 다음으로 통과할 방을 반환한다.
        /// 여러 방을 건너뛰는 경로에서는 최종 PatrolPoint가 아니라 BFS 경로의 다음 방을 반환한다.
        /// </summary>
        public Room NextDestinationRoom
        {
            get
            {
                Room nextRoom = activePath != null && activePathIndex < activePath.Count
                    ? activePath[activePathIndex].DestinationRoom
                    : currentDestination != null ? currentDestination.Room : null;

                return nextRoom == CurrentRoom ? null : nextRoom;
            }
        }

        /// <summary>현재 플레이어 추격 상태인지 반환한다.</summary>
        public bool IsChasing => State == EnemyState.Chasing;

        /// <summary>현재 추격 대상으로 지정된 Transform을 반환한다.</summary>
        public Transform ChaseTarget => chaseTarget;

        /// <summary>현재 추격 중인 목표 방을 반환한다.</summary>
        public Room ChaseTargetRoom => chaseTargetRoom;

        /// <summary>적이 Doorway 통과 코루틴을 실행 중인지 반환한다.</summary>
        public bool IsTraversing => traversalRoutine != null ||
                                    (doorTraversal != null && doorTraversal.IsTraversing);

        /// <summary>상태가 변경될 때 이전 상태와 새 상태를 전달한다.</summary>
        public event Action<EnemyState, EnemyState> StateChanged;

        /// <summary>
        /// 다음 순찰 목적지가 결정되거나 해제될 때 목적지 방을 전달한다.
        /// 위험경보처럼 목적지 결정 시점을 관찰하는 시스템이 매 프레임 조회하지 않도록 한다.
        /// </summary>
        public event Action<Room> DestinationChanged;

        /// <summary>
        /// 적의 논리적 현재 방이 변경될 때 이전 방과 새 방을 전달한다.
        /// 실제 방 변경은 EnemyRoomTracker가 확정하고 이 이벤트는 외부 관찰자에게 중계한다.
        /// </summary>
        public event Action<Room, Room> RoomChanged;

        /// <summary>적 Collider가 Player와 접촉했을 때 적과 플레이어를 전달한다.</summary>
        public event Action<EnemyController, Player> PlayerContactDetected;

        /// <summary>추격 중 목표 방에 진입했을 때 자신을 전달한다.</summary>
        public event Action<EnemyController> ChaseRoomReached;

        /// <summary>필수 참조와 초기 순찰 구성을 확인하고 첫 상태를 시작한다.</summary>
        private void Awake()
        {
            agent ??= GetComponent<NavMeshAgent>();
            roomTracker ??= GetComponent<EnemyRoomTracker>();
            doorTraversal ??= GetComponent<EnemyDoorTraversal>();
            roomGraph ??= FindFirstObjectByType<RoomGraph>();
            gameSession ??= FindFirstObjectByType<GameSession>();
            contactRigidbody = GetComponent<Rigidbody>();
            ConfigureContactRigidbody();
            renderers = GetComponentsInChildren<Renderer>(true);
            rendererInitialVisibility = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                rendererInitialVisibility[i] = renderers[i] != null && renderers[i].enabled;
            }
            colliders = GetComponentsInChildren<Collider>(true);

            if (roomTracker != null)
            {
                roomTracker.RoomChanged += HandleRoomChanged;
            }
        }

        /// <summary>
        /// NavMeshAgent가 이동하는 적에서도 충돌 이벤트가 발생하도록 물리 바디를 구성한다.
        /// 적의 위치 결정은 계속 NavMeshAgent가 담당하고 Rigidbody는 접촉 감지에만 사용한다.
        /// </summary>
        private void ConfigureContactRigidbody()
        {
            if (contactRigidbody == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 Rigidbody가 없습니다.", this);
                return;
            }

            contactRigidbody.isKinematic = true;
            contactRigidbody.useGravity = false;
            contactRigidbody.detectCollisions = true;
            contactRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            GameLog.Log(
                $"EnemyController '{name}' 물리 접촉용 Rigidbody 구성 완료. " +
                $"IsKinematic={contactRigidbody.isKinematic}, DetectCollisions={contactRigidbody.detectCollisions}.",
                this);
        }

        /// <summary>
        /// 적 컴포넌트가 제거될 때 방 추적기 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDestroy()
        {
            if (roomTracker != null)
            {
                roomTracker.RoomChanged -= HandleRoomChanged;
            }
        }

        /// <summary>적 오브젝트가 비활성화될 때 진행 중인 Doorway 통과를 중단한다.</summary>
        private void OnDisable()
        {
            StopTraversal();
        }

        /// <summary>초기 경로를 적용하고 Inspector의 활성화 설정에 따라 순찰을 시작한다.</summary>
        private void Start()
        {
            if (restoredFromCheckpoint)
            {
                return;
            }

            // PatrolRoute가 자신의 지점·순서·RoomGraph 연결을 한 번 검증하고,
            // 검증에 성공한 경우에만 초기 순찰을 활성화한다.
            if (!SetRoute(initialRoute, initialRoute != null ? initialRoute.StartIndex : 0))
            {
                return;
            }

            if (gameSession == null || gameSession.IsGameplaySimulationEnabled)
            {
                SetActive(initiallyActive);
            }
        }

        /// <summary>현재 상태에 따라 이동 또는 대기 시간을 갱신한다.</summary>
        private void Update()
        {
            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                return;
            }

            // Sector2 스토리 흐름이 적의 이동을 잠시 소유한 동안에는 기존 Patrol·Chase 갱신을 멈춘다.
            // GamePauseService의 전역 timeScale을 건드리지 않아 플레이어 자동 이동과 연출 코루틴은 계속 진행할 수 있다.
            if (Mode == EnemyMode.ScriptedSequence)
            {
                return;
            }

            if (traversalRoutine != null)
            {
                return;
            }

            if (State == EnemyState.Patrolling)
            {
                UpdatePatrolling();
            }
            else if (State == EnemyState.Waiting)
            {
                UpdateWaiting();
            }
            else if (State == EnemyState.Chasing)
            {
                UpdateChasing();
            }
        }

        /// <summary>
        /// 새 경로를 적용하고 지정한 인덱스의 지점부터 순찰을 시작한다.
        /// 적이 비활성 상태이면 경로만 저장하고 재활성화 시 사용한다.
        /// </summary>
        /// <param name="newRoute">적용할 순찰 경로.</param>
        /// <param name="startIndex">순찰을 시작할 지점 인덱스.</param>
        /// <returns>경로 검증과 적용에 성공했으면 true.</returns>
        public bool SetRoute(PatrolRoute newRoute, int startIndex)
        {
            string error = string.Empty;
            if (newRoute == null || !newRoute.TryValidateConfiguration(roomGraph, out error))
            {
                GameLog.LogError(error ?? $"EnemyController '{name}'에 새 PatrolRoute가 없습니다.", this);
                return false;
            }

            if (startIndex < 0 || startIndex >= newRoute.Count)
            {
                GameLog.LogError($"EnemyController '{name}'의 경로 시작 인덱스가 범위를 벗어났습니다: {startIndex}", this);
                return false;
            }

            CurrentRoute = newRoute;
            targetIndex = startIndex;
            currentDestination = null;
            activePath = null;
            activePathIndex = 0;
            waitRemaining = 0f;

            if (State == EnemyState.Patrolling || State == EnemyState.Waiting)
            {
                BeginPatrolling();
            }

            return true;
        }

        /// <summary>경로의 Inspector 시작 인덱스를 사용해 새 경로를 적용한다.</summary>
        /// <param name="newRoute">적용할 순찰 경로.</param>
        /// <returns>경로 검증과 적용에 성공했으면 true.</returns>
        public bool SetRoute(PatrolRoute newRoute)
        {
            return SetRoute(newRoute, newRoute != null ? newRoute.StartIndex : 0);
        }

        /// <summary>
        /// 지정한 Player를 추격 대상으로 설정한다.
        /// 목표가 다른 방에 있으면 RoomGraph와 기존 EnemyDoorTraversal을 사용해 방 단위로 추격한다.
        /// </summary>
        /// <param name="target">추격할 플레이어 Transform.</param>
        /// <param name="targetRoom">플레이어가 논리적으로 위치한 방.</param>
        /// <returns>추격 명령을 수락했으면 true.</returns>
        public bool BeginChase(Transform target, Room targetRoom)
        {
            if (target == null || targetRoom == null || State == EnemyState.Inactive)
            {
                return false;
            }

            scriptedSequencePaused = false;
            StopTraversal();
            StopMovement();
            currentDestination = null;
            activePath = null;
            activePathIndex = 0;
            chaseTarget = target;
            chaseTargetRoom = targetRoom;
            chaseRoomReachedNotified = false;
            chaseMovementHeld = false;
            SetState(EnemyState.Chasing);
            BeginChaseDestination();
            return true;
        }

        /// <summary>
        /// 현재 적의 기존 이동을 중단하고 스토리 연출이 제어권을 갖는 모드로 전환한다.
        /// 전역 Pause를 사용하지 않으므로 Sector2 연출 코드가 플레이어 자동 이동과 함께 사용할 수 있다.
        /// </summary>
        /// <returns>활성 적을 성공적으로 일시 정지했으면 true.</returns>
        public bool PauseForScriptedSequence()
        {
            if (State == EnemyState.Inactive || State == EnemyState.Chasing)
            {
                return false;
            }

            StopTraversal();
            StopMovement();
            activePath = null;
            activePathIndex = 0;
            scriptedSequencePaused = true;
            DestinationChanged?.Invoke(null);
            return true;
        }

        /// <summary>
        /// ScriptedSequence 일시 정지를 해제하고 현재 PatrolRoute를 기준으로 순찰을 재개한다.
        /// </summary>
        /// <returns>순찰 재개에 성공했으면 true.</returns>
        public bool ResumePatrolFromScriptedSequence()
        {
            if (!scriptedSequencePaused || State == EnemyState.Inactive || CurrentRoute == null)
            {
                return false;
            }

            scriptedSequencePaused = false;
            BeginPatrolling();
            return Mode == EnemyMode.Patrol;
        }

        /// <summary>추격 중인 적의 목표 방을 갱신하고 현재 추격 경로를 다시 계산한다.</summary>
        /// <param name="targetRoom">새로운 플레이어 목표 방.</param>
        /// <returns>목표 갱신에 성공했으면 true.</returns>
        public bool SetChaseTargetRoom(Room targetRoom)
        {
            if (!IsChasing || targetRoom == null || chaseTarget == null)
            {
                return false;
            }

            chaseTargetRoom = targetRoom;
            chaseRoomReachedNotified = false;
            chaseMovementHeld = false;
            StopTraversal();
            StopMovement();
            activePath = null;
            activePathIndex = 0;
            BeginChaseDestination();
            return true;
        }

        /// <summary>
        /// 숨은 방에 도착한 적을 현재 위치에 잠시 세운다.
        /// 대기 시간과 원래 순찰 복귀는 EnemyChaseSystem이 결정한다.
        /// </summary>
        public void HoldChaseAtCurrentPosition()
        {
            if (!IsChasing)
            {
                return;
            }

            chaseMovementHeld = true;
            StopMovement();
        }

        /// <summary>추격 정보를 지우고 원래 PatrolRoute의 다음 지점으로 복귀한다.</summary>
        public void StopChaseAndResumeRoute()
        {
            scriptedSequencePaused = false;
            StopTraversal();
            StopMovement();
            chaseTarget = null;
            chaseTargetRoom = null;
            chaseRoomReachedNotified = false;
            chaseMovementHeld = false;
            currentDestination = null;
            activePath = null;
            activePathIndex = 0;

            if (State != EnemyState.Inactive)
            {
                BeginPatrolling();
            }
        }

        /// <summary>
        /// 적을 논리적으로 활성화하거나 비활성화한다.
        /// GameObject 자체는 유지해 외부 시스템이 같은 참조로 재활성화할 수 있게 한다.
        /// </summary>
        public void SetActive(bool value)
        {
            if (!value)
            {
                scriptedSequencePaused = false;
                StopTraversal();
                StopMovement();
                currentDestination = null;
                activePath = null;
                activePathIndex = 0;
                chaseTarget = null;
                chaseTargetRoom = null;
                chaseRoomReachedNotified = false;
                chaseMovementHeld = false;
                DestinationChanged?.Invoke(null);
                roomTracker?.ClearRoom();
                SetVisible(false);
                SetState(EnemyState.Inactive);
                return;
            }

            if (CurrentRoute == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 활성화할 PatrolRoute가 없습니다.", this);
                return;
            }

            if (!EnableAgentAt(transform.position))
            {
                return;
            }

            SetVisible(true);
            scriptedSequencePaused = false;
            BeginPatrolling();
        }

        /// <summary>
        /// 적을 지정한 순찰 지점에 배치하고 해당 지점의 방에서 순찰을 재개한다.
        /// </summary>
        public void SetActive(bool value, PatrolPoint spawnPoint)
        {
            if (!value)
            {
                SetActive(false);
                return;
            }

            string error = string.Empty;
            if (spawnPoint == null || !spawnPoint.TryValidateConfiguration(out error))
            {
                GameLog.LogError(error ?? $"EnemyController '{name}'의 SpawnPoint가 없습니다.", this);
                return;
            }

            if (CurrentRoute == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 활성화할 PatrolRoute가 없습니다.", this);
                return;
            }

            if (!EnableAgentAt(spawnPoint.Position))
            {
                return;
            }

            roomTracker?.ResetAt(spawnPoint);
            SetVisible(true);
            scriptedSequencePaused = false;
            BeginPatrolling();
        }

        /// <summary>
        /// 체크포인트 정의의 순찰 경로·시작 인덱스·활성 상태를 한 번에 적용한다.
        /// 실제 스폰 위치는 별도 저장값을 사용하지 않고 해당 경로의 첫 번째 지점으로 고정한다.
        /// 중간 상태에서 순찰이 시작하지 않도록 기존 추격과 이동을 먼저 정리한다.
        /// </summary>
        /// <param name="route">복원할 순찰 경로.</param>
        /// <param name="routeStartIndex">복원할 순찰 시작 인덱스.</param>
        /// <param name="active">복원 후 활성화할지 여부.</param>
        public bool RestoreCheckpointState(
            PatrolRoute route,
            int routeStartIndex,
            bool active)
        {
            restoredFromCheckpoint = true;
            SetActive(false);

            if (!SetRoute(route, routeStartIndex))
            {
                return false;
            }

            if (active)
            {
                // 체크포인트마다 별도 스폰 위치를 저장하면 순찰 경로와 실제 부활 위치가
                // 어긋날 수 있으므로, 항상 경로 배열의 첫 지점을 부활 기준으로 사용한다.
                PatrolPoint firstPoint = route.GetPoint(0);
                GameLog.Log(
                    $"EnemyController '{name}' 체크포인트 복원 위치를 PatrolRoute '{route.name}'의 첫 지점 " +
                    $"'{firstPoint.name}'({firstPoint.Room?.RoomId ?? "null"})으로 고정합니다.",
                    this);
                SetActive(true, firstPoint);
            }

            return true;
        }

        /// <summary>현재 목적지로 이동 중인지 확인하고 도착하면 대기 상태로 전환한다.</summary>
        private void UpdatePatrolling()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                GameLog.LogError($"EnemyController '{name}'의 NavMeshAgent가 유효하지 않습니다.", this);
                SetActive(false);
                return;
            }

            if (agent.pathPending)
            {
                return;
            }

            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                GameLog.LogError($"EnemyController '{name}'의 목적지 경로가 완성되지 않았습니다.", this);
                SetActive(false);
                return;
            }

            if (!HasArrived())
            {
                return;
            }

            StopMovement();
            waitRemaining = currentDestination.WaitDuration;
            currentDestination = null;
            DestinationChanged?.Invoke(null);
            SetState(EnemyState.Waiting);
        }

        /// <summary>현재 지점의 대기 시간이 끝나면 다음 순찰 지점으로 이동한다.</summary>
        private void UpdateWaiting()
        {
            waitRemaining -= Time.deltaTime;
            if (waitRemaining > 0f)
            {
                return;
            }

            targetIndex = (targetIndex + 1) % CurrentRoute.Count;
            BeginPatrolling();
        }

        /// <summary>현재 방에서는 플레이어 위치를 따라가고, 다른 방이면 RoomGraph 경로를 유지한다.</summary>
        private void UpdateChasing()
        {
            if (chaseMovementHeld)
            {
                return;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh || chaseTarget == null ||
                chaseTargetRoom == null)
            {
                return;
            }

            if (CurrentRoom != chaseTargetRoom)
            {
                if (traversalRoutine == null)
                {
                    BeginChaseDestination();
                }

                return;
            }

            // 같은 방에서는 고정 PatrolPoint가 아니라 매 프레임 플레이어의 현재 위치를 NavMesh 목표로 사용한다.
            if (!agent.SetDestination(chaseTarget.position))
            {
                GameLog.LogWarning($"EnemyController '{name}'가 추격 대상 위치를 목적지로 설정하지 못했습니다.", this);
                return;
            }

            agent.isStopped = false;
        }

        /// <summary>현재 인덱스의 순찰 지점을 다음 목적지로 지정하고 이동을 시작한다.</summary>
        private void BeginPatrolling()
        {
            if (CurrentRoute == null || CurrentRoute.Count == 0)
            {
                GameLog.LogError($"EnemyController '{name}'에 유효한 순찰 경로가 없습니다.", this);
                SetActive(false);
                return;
            }

            currentDestination = CurrentRoute.GetPoint(targetIndex);
            if (currentDestination == null)
            {
                GameLog.LogError($"EnemyController '{name}'의 순찰 목적지가 비어 있습니다.", this);
                SetActive(false);
                return;
            }

            if (CurrentRoom == null)
            {
                GameLog.LogError($"EnemyController '{name}'의 현재 Room이 없어 순찰을 시작할 수 없습니다.", this);
                SetActive(false);
                return;
            }

            if (CurrentRoom != currentDestination.Room)
            {
                if (roomGraph == null || !roomGraph.IsBaked ||
                    !roomGraph.TryFindPath(CurrentRoom, currentDestination.Room, out activePath) ||
                    activePath.Count == 0)
                {
                    GameLog.LogError(
                        $"EnemyController '{name}'가 '{CurrentRoom.RoomId}'에서 " +
                        $"'{currentDestination.Room.RoomId}'까지의 Bake된 RoomGraph 경로를 찾지 못했습니다.",
                        this);
                    SetActive(false);
                    return;
                }

                activePathIndex = 0;
                SetState(EnemyState.Patrolling);
                DestinationChanged?.Invoke(NextDestinationRoom);
                traversalRoutine = StartCoroutine(TraverseActivePath());
                return;
            }

            activePath = null;
            activePathIndex = 0;
            BeginMovementToDestination();
        }

        /// <summary>추격 목표 방까지 경로를 만들거나 같은 방의 플레이어 위치로 이동을 시작한다.</summary>
        private void BeginChaseDestination()
        {
            if (!IsChasing || chaseTarget == null || chaseTargetRoom == null || chaseMovementHeld)
            {
                return;
            }

            if (CurrentRoom == chaseTargetRoom)
            {
                BeginMovementToChaseTarget();
                return;
            }

            if (roomGraph == null || !roomGraph.IsBaked || CurrentRoom == null ||
                !roomGraph.TryFindPath(CurrentRoom, chaseTargetRoom, out activePath) || activePath.Count == 0)
            {
                GameLog.LogError(
                    $"EnemyController '{name}'가 '{CurrentRoom?.RoomId}'에서 추격 목표 방 " +
                    $"'{chaseTargetRoom.RoomId}'까지 경로를 찾지 못했습니다.", this);
                StopChaseAndResumeRoute();
                return;
            }

            activePathIndex = 0;
            SetState(EnemyState.Chasing);
            DestinationChanged?.Invoke(NextDestinationRoom);
            traversalRoutine = StartCoroutine(TraverseActivePath());
        }

        /// <summary>현재 방의 플레이어 위치를 NavMeshAgent 추격 목적지로 설정한다.</summary>
        private void BeginMovementToChaseTarget()
        {
            if (!IsChasing || chaseMovementHeld || agent == null || chaseTarget == null ||
                !agent.enabled || !agent.isOnNavMesh || !agent.SetDestination(chaseTarget.position))
            {
                return;
            }

            agent.isStopped = false;
            SetState(EnemyState.Chasing);
            DestinationChanged?.Invoke(NextDestinationRoom);
        }

        /// <summary>현재 방 안에 있는 최종 PatrolPoint로 NavMesh 이동을 시작한다.</summary>
        private void BeginMovementToDestination()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh || !agent.SetDestination(currentDestination.Position))
            {
                GameLog.LogError($"EnemyController '{name}'가 순찰 목적지를 설정하지 못했습니다.", this);
                SetActive(false);
                return;
            }

            agent.isStopped = false;
            SetState(EnemyState.Patrolling);
            DestinationChanged?.Invoke(NextDestinationRoom);
        }

        /// <summary>
        /// BFS 경로의 Doorway를 순서대로 통과한 뒤 최종 PatrolPoint 이동을 시작한다.
        /// </summary>
        private IEnumerator TraverseActivePath()
        {
            while (activePath != null && activePathIndex < activePath.Count)
            {
                RoomGraphStep step = activePath[activePathIndex];
                if (doorTraversal == null)
                {
                    GameLog.LogError($"EnemyController '{name}'에 EnemyDoorTraversal이 없습니다.", this);
                    yield break;
                }

                yield return doorTraversal.Execute(step);
                if (!doorTraversal.LastTraversalSucceeded)
                {
                    GameLog.LogError(
                        $"EnemyController '{name}'가 Doorway '{step.Doorway?.name}' 통과에 실패했습니다.",
                        this);
                    SetActive(false);
                    yield break;
                }

                activePathIndex++;
                DestinationChanged?.Invoke(NextDestinationRoom);
            }

            activePath = null;
            activePathIndex = 0;
            traversalRoutine = null;

            // 문 통과 정리와 traversalRoutine 해제가 끝난 뒤에만 외부 추격 전환을 허용한다.
            NotifyChaseRoomReachedIfNeeded();
            if (State == EnemyState.Chasing)
            {
                BeginMovementToChaseTarget();
            }
            else
            {
                BeginMovementToDestination();
            }
        }

        /// <summary>진행 중인 Doorway 통과 코루틴을 중단하고 문을 닫힌 상태로 복구한다.</summary>
        private void StopTraversal()
        {
            if (traversalRoutine != null)
            {
                StopCoroutine(traversalRoutine);
                traversalRoutine = null;
            }

            doorTraversal?.Abort();
        }

        /// <summary>Agent를 중지하고 현재 경로를 제거한다.</summary>
        private void StopMovement()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>Agent를 지정 위치에 배치하고 활성화한다.</summary>
        private bool EnableAgentAt(Vector3 position)
        {
            if (agent == null)
            {
                GameLog.LogError($"EnemyController '{name}'에 NavMeshAgent가 없습니다.", this);
                return false;
            }

            agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.Warp(position))
            {
                GameLog.LogError($"EnemyController '{name}'를 NavMesh 위치에 배치하지 못했습니다.", this);
                agent.enabled = false;
                return false;
            }

            return true;
        }

        /// <summary>적의 표시 Renderer와 충돌 Collider를 한꺼번에 전환한다.</summary>
        private void SetVisible(bool value)
        {
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer != null)
                    {
                        // 씬에서 비활성화한 placeholder 캡슐 Renderer는 적이 활성화돼도 계속 숨긴다.
                        renderer.enabled = value && rendererInitialVisibility[i];
                    }
                }
            }

            if (colliders != null)
            {
                foreach (Collider collider in colliders)
                {
                    if (collider != null) collider.enabled = value;
                }
            }
        }

        /// <summary>실제 상태가 달라질 때만 상태를 저장하고 이벤트를 발생시킨다.</summary>
        private void SetState(EnemyState value)
        {
            if (State == value)
            {
                return;
            }

            EnemyState previous = State;
            State = value;
            StateChanged?.Invoke(previous, State);
        }

        /// <summary>
        /// EnemyRoomTracker가 확정한 방 변경을 외부 시스템에 중계한다.
        /// </summary>
        private void HandleRoomChanged(Room previousRoom, Room currentRoom)
        {
            RoomChanged?.Invoke(previousRoom, currentRoom);
        }

        /// <summary>
        /// Doorway 전체 통과가 완료된 뒤 추격 목표 방 도착 이벤트를 한 번 발생시킨다.
        /// Sensor 통과 시점에는 아직 목적지 쪽 EntryPoint 이동이 남아 있을 수 있으므로
        /// 실제 대기 시작 시점은 TraverseActivePath 완료 직후로 제한한다.
        /// </summary>
        private void NotifyChaseRoomReachedIfNeeded()
        {
            if (State == EnemyState.Chasing && CurrentRoom == chaseTargetRoom && !chaseRoomReachedNotified)
            {
                chaseRoomReachedNotified = true;
                ChaseRoomReached?.Invoke(this);
            }
        }

        /// <summary>적의 물리 Collider가 플레이어와 접촉했을 때 포획 시스템에 알린다.</summary>
        /// <param name="other">접촉한 Collider.</param>
        private void OnTriggerEnter(Collider other)
        {
            NotifyPlayerContact(other);
        }

        /// <summary>Trigger가 아닌 일반 Collider 접촉도 포획 시스템에 전달한다.</summary>
        /// <param name="collision">충돌 정보.</param>
        private void OnCollisionEnter(Collision collision)
        {
            NotifyPlayerContact(collision != null ? collision.collider : null);
        }

        /// <summary>Collider의 부모 계층에서 Player를 찾아 접촉 이벤트를 발생시킨다.</summary>
        /// <param name="other">접촉한 Collider.</param>
        private void NotifyPlayerContact(Collider other)
        {
            if (State == EnemyState.Inactive || other == null)
            {
                return;
            }

            Player player = other.GetComponentInParent<Player>();
            if (player != null)
            {
                GameLog.Log(
                    $"EnemyController '{name}'이 Player '{player.name}'과 물리 접촉을 감지했습니다. " +
                    $"State={State}, CurrentRoom={CurrentRoom?.RoomId ?? "null"}.",
                    this);
                PlayerContactDetected?.Invoke(this, player);
            }
        }

        /// <summary>Agent가 허용 거리 안에 있고 실질적으로 정지했는지 확인한다.</summary>
        private bool HasArrived()
        {
            if (agent.pathPending || float.IsInfinity(agent.remainingDistance))
            {
                return false;
            }

            float allowedDistance = agent.stoppingDistance + arrivalTolerance;
            return agent.remainingDistance <= allowedDistance &&
                   (!agent.hasPath || agent.velocity.sqrMagnitude <= 0.01f);
        }

        /// <summary>컴포넌트를 처음 추가할 때 같은 오브젝트의 필수 참조를 연결한다.</summary>
        private void Reset()
        {
            agent = GetComponent<NavMeshAgent>();
            roomTracker = GetComponent<EnemyRoomTracker>();
            doorTraversal = GetComponent<EnemyDoorTraversal>();
            roomGraph = FindFirstObjectByType<RoomGraph>();
            gameSession = FindFirstObjectByType<GameSession>();
        }
    }
}
