using System;
using System.Collections;
using System.Collections.Generic;
using HauntedHouse.Commands;
using HauntedHouse.Dialogue;
using HauntedHouse.Enemies;
using HauntedHouse.Stealth;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// Sector2 기계실을 나갈 때 한 번만 실행되는 강제 숨기 이벤트의 순서를 소유한다.
    /// 플레이어 이동·살인마 ScriptedSequence·은신 판정·완료 flag를 하나의 상태 흐름으로 조정한다.
    /// </summary>
    public sealed class Sector2HideEncounterController : MonoBehaviour
    {
        private enum EncounterState
        {
            Idle,
            KillerAppearing,
            ReturningPlayer,
            ShowingWarning,
            WaitingForHide,
            Searching,
            KillerLeaving,
            Completed,
            Exposed,
            Failed
        }

        [Header("진행 참조")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private Player player;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private RoomGraph roomGraph;
        [SerializeField] private Room machineRoom;

        [Header("살인마 연출 지점")]
        [SerializeField] private EnemyController enemy;
        [SerializeField] private PatrolPoint enemySpawnPoint;
        [SerializeField] private Room enemyRevealRoom;
        [SerializeField] private Transform enemyRevealPoint;
        [SerializeField] private Room enemySearchRoom;
        [SerializeField] private Transform enemySearchPoint;
        [SerializeField] private Room enemyExitRoom;
        [SerializeField] private Transform enemyExitPoint;

        [Header("플레이어 연출")]
        [SerializeField] private Transform forcedReturnPoint;
        [SerializeField] private DialogueService dialogueService;
        [SerializeField] private DialogueSequence warningDialogue;
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private Target toolCaseTarget;
        [SerializeField, Min(0f)] private float searchDuration = 3f;

        private Coroutine encounterRoutine;
        private EncounterState state;
        private bool warningDialogueCompleted;

        /// <summary>현재 이벤트가 진행 중인지 반환한다.</summary>
        public bool IsRunning => encounterRoutine != null;

        /// <summary>현재 이벤트가 기계실 출구 행동을 가로채야 하는지 반환한다.</summary>
        public bool ShouldStartEncounter => !IsRunning &&
                                            state != EncounterState.Completed &&
                                            state != EncounterState.Exposed &&
                                            gameSession != null &&
                                            gameSession.HasStoryFlag(StoryProgressFlags.GateActivated) &&
                                            !gameSession.HasStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted);

        /// <summary>초기화된 이벤트 상태를 반환한다.</summary>
        public string StateName => state.ToString();

        /// <summary>직렬화된 참조를 우선 사용하고 공용 시스템 참조만 안전하게 보완한다.</summary>
        private void Awake()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            player ??= FindFirstObjectByType<Player>();
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            roomGraph ??= FindFirstObjectByType<RoomGraph>();
            dialogueService ??= FindFirstObjectByType<DialogueService>();
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
        }

        /// <summary>세션 복원 완료 시 체크포인트 flag에 맞춰 일회성 상태를 다시 계산한다.</summary>
        private void OnEnable()
        {
            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }
        }

        /// <summary>
        /// 출구 Target이 호출하는 강제 숨기 이벤트 진입점이다.
        /// 플레이어를 기계실로 되돌리고 경고 대화를 보여준 뒤 살인마 연출을 백그라운드에서 이어간다.
        /// </summary>
        /// <param name="context">현재 출구 Target과 플레이어 문맥.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            string error = string.Empty;
            if (!ShouldStartEncounter || !TryValidateConfiguration(out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    GameLog.LogError(error, this);
                }

                yield break;
            }

            // TargetCommandRunner가 이 코루틴을 실행하는 동안 일반 입력을 점유한다.
            encounterRoutine = StartCoroutine(RunEncounter(context));
            yield return encounterRoutine;
        }

        /// <summary>
        /// 이벤트에 필요한 명시적 참조와 대화·경로 설정을 검사한다.
        /// 씬 전체 검색으로 누락 참조를 보완하지 않아 다른 작업자의 씬 변경과 충돌하지 않게 한다.
        /// </summary>
        /// <param name="error">검사 실패 원인.</param>
        /// <returns>이벤트를 실행할 수 있으면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (gameSession == null || player == null || playerRoomTracker == null || roomGraph == null ||
                machineRoom == null || enemy == null || enemySpawnPoint == null || forcedReturnPoint == null ||
                dialogueService == null || warningDialogue == null || stealthHideSystem == null ||
                enemyRevealPoint == null || enemySearchPoint == null || enemyExitPoint == null ||
                enemyRevealRoom == null || enemySearchRoom == null || enemyExitRoom == null)
            {
                error = $"Sector2HideEncounterController '{name}'의 필수 참조가 누락됐습니다.";
                return false;
            }

            if (!warningDialogue.PauseGame)
            {
                error = $"Sector2HideEncounterController '{name}'의 경고 대화는 PauseGame이어야 합니다.";
                return false;
            }

            if (warningDialogue.Lines == null || warningDialogue.Lines.Count == 0)
            {
                error = $"Sector2HideEncounterController '{name}'의 경고 대화에 Line이 없습니다.";
                return false;
            }

            if (!roomGraph.IsBaked)
            {
                error = $"Sector2HideEncounterController '{name}'이 사용할 RoomGraph가 Bake되지 않았습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 현재 이벤트를 순서대로 실행하고 실패 시 살인마·Target 상태를 정리한다.
        /// </summary>
        /// <param name="context">출구 Target 실행 문맥.</param>
        private IEnumerator RunEncounter(TargetActionContext context)
        {
            state = EncounterState.KillerAppearing;
            SetToolCaseSelectable(false);

            bool scriptedStarted = enemy.BeginScriptedSequence(enemySpawnPoint);
            if (!scriptedStarted)
            {
                FailEncounter("살인마 ScriptedSequence을 시작하지 못했습니다.");
                yield break;
            }

            // EnemySpawnRoom에서 Reveal 지점까지의 문 이동은 기존 RoomGraph와 EnemyDoorTraversal을 사용한다.
            if (!TryGetPath(enemy.CurrentRoom, enemyRevealRoom, out List<RoomGraphStep> revealPath))
            {
                FailEncounter("살인마 Reveal 지점까지의 RoomGraph 경로를 찾지 못했습니다.");
                yield break;
            }

            yield return enemy.ExecuteScriptedPath(revealPath);
            if (!enemy.LastScriptedPathSucceeded || enemy.CurrentRoom != enemyRevealRoom)
            {
                FailEncounter("살인마가 Reveal 지점 방에 도착하지 못했습니다.");
                yield break;
            }

            yield return enemy.MoveScriptedTo(enemyRevealPoint);
            if (!enemy.LastScriptedMoveSucceeded)
            {
                FailEncounter("살인마 Reveal 지점 이동이 완료되지 않았습니다.");
                yield break;
            }

            state = EncounterState.ReturningPlayer;
            yield return player.MoveTo(forcedReturnPoint);
            if (!player.LastMoveSucceeded)
            {
                FailEncounter($"플레이어 강제 복귀에 실패했습니다: {player.LastMoveFailureReason}");
                yield break;
            }

            // 강제 이동 완료 후 논리 방을 반드시 검사해 적 추격 기준과 물리 위치를 일치시킨다.
            if (playerRoomTracker.CurrentRoom != machineRoom)
            {
                if (!playerRoomTracker.EnterRoom(machineRoom) || playerRoomTracker.CurrentRoom != machineRoom)
                {
                    FailEncounter("플레이어 강제 복귀 후 CurrentRoom이 MachineRoom이 아닙니다.");
                    yield break;
                }
            }

            state = EncounterState.ShowingWarning;
            yield return PlayWarningDialogue();
            if (!warningDialogueCompleted)
            {
                FailEncounter("강제 숨기 경고 대화가 정상 완료되지 않았습니다.");
                yield break;
            }

            if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
            {
                FailEncounter("플레이어 세션이 종료되어 강제 숨기 이벤트를 중단했습니다.");
                yield break;
            }

            SetToolCaseSelectable(true);

            // 여기서 출구 Target 코루틴을 끝내 플레이어가 ToolCase를 선택할 수 있게 한다.
            // 살인마의 진입·검색·퇴장은 별도 코루틴으로 계속 실행한다.
            encounterRoutine = StartCoroutine(ContinueAfterWarning());
        }

        /// <summary>경고 대화 이후 살인마의 진입과 은신 탐색을 진행한다.</summary>
        private IEnumerator ContinueAfterWarning()
        {
            state = EncounterState.WaitingForHide;

            // 대화가 끝난 뒤 살인마가 기계실까지 진입한다.
            if (!TryGetPath(enemy.CurrentRoom, enemySearchRoom, out List<RoomGraphStep> entryPath))
            {
                FailEncounter("살인마 MachineRoom 진입 경로를 찾지 못했습니다.");
                yield break;
            }

            yield return enemy.ExecuteScriptedPath(entryPath);
            if (!enemy.LastScriptedPathSucceeded || enemy.CurrentRoom != enemySearchRoom)
            {
                FailEncounter("살인마가 MachineRoom에 진입하지 못했습니다.");
                yield break;
            }

            yield return enemy.MoveScriptedTo(enemySearchPoint);
            if (!enemy.LastScriptedMoveSucceeded)
            {
                FailEncounter("살인마 Search 지점 이동이 완료되지 않았습니다.");
                yield break;
            }

            if (stealthHideSystem.IsHidden)
            {
                state = EncounterState.Searching;
                float elapsed = 0f;
                while (elapsed < searchDuration && stealthHideSystem.IsHidden &&
                       (gameSession == null || gameSession.IsGameplaySimulationEnabled))
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
                {
                    FailEncounter("플레이어 세션이 종료되어 강제 숨기 이벤트를 중단했습니다.");
                    yield break;
                }
            }
            else
            {
                // 노출 상태는 기존 EnemyChaseSystem/PlayerCaptureDeathSystem 흐름으로 넘긴다.
                state = EncounterState.Exposed;
                if (gameSession == null || gameSession.IsGameplaySimulationEnabled)
                {
                    enemy.BeginChase(player.transform, playerRoomTracker.CurrentRoom);
                }

                encounterRoutine = null;
                yield break;
            }

            state = EncounterState.KillerLeaving;
            if (!TryGetPath(enemy.CurrentRoom, enemyExitRoom, out List<RoomGraphStep> exitPath))
            {
                FailEncounter("살인마 퇴장 경로를 찾지 못했습니다.");
                yield break;
            }

            yield return enemy.ExecuteScriptedPath(exitPath);
            if (!enemy.LastScriptedPathSucceeded || enemy.CurrentRoom != enemyExitRoom)
            {
                FailEncounter("살인마가 MachineRoom 밖으로 퇴장하지 못했습니다.");
                yield break;
            }

            yield return enemy.MoveScriptedTo(enemyExitPoint);
            if (!enemy.LastScriptedMoveSucceeded)
            {
                FailEncounter("살인마 퇴장 지점 이동이 완료되지 않았습니다.");
                yield break;
            }

            enemy.EndScriptedSequence(true);
            gameSession.SetStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted);
            SetToolCaseSelectable(false);
            state = EncounterState.Completed;
            encounterRoutine = null;
        }

        /// <summary>지정한 두 방 사이의 Bake된 경로를 조회한다.</summary>
        private bool TryGetPath(Room sourceRoom, Room destinationRoom, out List<RoomGraphStep> path)
        {
            path = new List<RoomGraphStep>();
            if (sourceRoom == null || destinationRoom == null ||
                !roomGraph.TryFindPath(sourceRoom, destinationRoom, out path))
            {
                return false;
            }

            return true;
        }

        /// <summary>경고 대화를 재생하고 동일 Sequence의 완료 이벤트를 기다린다.</summary>
        private IEnumerator PlayWarningDialogue()
        {
            bool completed = false;
            void HandleDialogueCompleted(DialogueSequence sequence)
            {
                if (sequence == warningDialogue)
                {
                    completed = true;
                }
            }

            dialogueService.DialogueCompleted += HandleDialogueCompleted;
            try
            {
                warningDialogueCompleted = false;
                if (!dialogueService.TryPlay(warningDialogue))
                {
                    yield break;
                }

                while (dialogueService.IsPlaying && !completed)
                {
                    yield return null;
                }

                warningDialogueCompleted = completed;
            }
            finally
            {
                dialogueService.DialogueCompleted -= HandleDialogueCompleted;
            }
        }

        /// <summary>은신 Target의 표시 가능 여부를 이벤트 단계에 맞춰 변경한다.</summary>
        private void SetToolCaseSelectable(bool value)
        {
            toolCaseTarget?.SetSelectable(value);
        }

        /// <summary>이벤트 실패 시 적을 비활성화하고 재실행 가능한 실패 상태로 남긴다.</summary>
        private void FailEncounter(string reason)
        {
            GameLog.LogError(reason, this);
            enemy?.EndScriptedSequence(true);
            SetToolCaseSelectable(false);
            state = EncounterState.Failed;
            encounterRoutine = null;
        }

        /// <summary>컴포넌트가 처음 추가될 때 씬 공용 참조를 보완한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            player = FindFirstObjectByType<Player>();
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            roomGraph = FindFirstObjectByType<RoomGraph>();
            dialogueService = FindFirstObjectByType<DialogueService>();
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
        }

        /// <summary>체크포인트 복원 후 완료 flag와 이벤트 내부 상태를 일치시킨다.</summary>
        private void HandleGameplayStarted()
        {
            if (IsRunning)
            {
                return;
            }

            state = gameSession != null &&
                    gameSession.HasStoryFlag(StoryProgressFlags.Sector2HideEncounterCompleted)
                ? EncounterState.Completed
                : EncounterState.Idle;
            SetToolCaseSelectable(false);
        }

        /// <summary>씬 비활성화·Restart 시 진행 중인 이벤트와 임시 Target 상태를 정리한다.</summary>
        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }

            if (encounterRoutine != null)
            {
                StopCoroutine(encounterRoutine);
                encounterRoutine = null;
            }

            if (enemy != null && enemy.Mode == EnemyMode.ScriptedSequence)
            {
                enemy.EndScriptedSequence(true);
            }

            SetToolCaseSelectable(false);
            if (state != EncounterState.Completed)
            {
                state = EncounterState.Idle;
            }
        }
    }
}
