using System;
using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using HauntedHouse.Enemies;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// S4_Stairwell에 실제로 도착한 뒤 최종 추격 시작 연출을 한 번 실행한다.
    /// 대화가 끝나는 순간 Story 상태와 최초 적 추격을 넘기고, 마지막에 전용 Pause를 해제한다.
    /// </summary>
    public sealed class Sector5FinalChaseController : MonoBehaviour
    {
        [Header("진행 참조")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private Player player;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private TargetCommandRunner targetCommandRunner;
        [SerializeField] private GamePauseService pauseService;
        [SerializeField] private DialogueService dialogueService;

        [Header("실제 도착 판정")]
        [SerializeField] private Target stairwellArrivalTarget;
        [SerializeField] private Room stairwellRoom;

        [Header("살인마 시작 연출")]
        [SerializeField] private EnemyController enemy;
        [SerializeField] private PatrolPoint enemySpawnPoint;
        [SerializeField] private DialogueSequence runDialogue;

        private Coroutine startRoutine;
        private GamePauseService.PauseHandle pauseHandle;
        private bool dialogueCompleted;

        /// <summary>최종 추격 시작 연출이 실행 중인지 반환한다.</summary>
        public bool IsRunning => startRoutine != null;

        /// <summary>필수 참조와 연출 에셋이 구성되어 있는지 반환한다.</summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (gameSession == null || player == null || playerRoomTracker == null ||
                targetCommandRunner == null || pauseService == null || dialogueService == null ||
                stairwellArrivalTarget == null || stairwellRoom == null || enemy == null ||
                enemySpawnPoint == null || runDialogue == null)
            {
                error = $"Sector5FinalChaseController '{name}'의 필수 참조가 누락됐습니다.";
                return false;
            }

            if (stairwellRoom.RoomId != "S4_Stairwell")
            {
                error = $"Sector5FinalChaseController '{name}'의 Stairwell RoomId가 S4_Stairwell이 아닙니다: {stairwellRoom.RoomId}";
                return false;
            }

            if (enemySpawnPoint.Room == null || enemySpawnPoint.Room.RoomId != "S4_ControlRoom")
            {
                error = $"Sector5FinalChaseController '{name}'의 EnemySpawnPoint가 S4_ControlRoom에 속하지 않습니다.";
                return false;
            }

            if (!runDialogue.PauseGame)
            {
                error = $"Sector5FinalChaseController '{name}'의 시작 Dialogue는 PauseGame이어야 합니다.";
                return false;
            }

            if (runDialogue.Lines == null || runDialogue.Lines.Count == 0)
            {
                error = $"Sector5FinalChaseController '{name}'의 시작 Dialogue에 Line이 없습니다.";
                return false;
            }

            if (stairwellArrivalTarget.OwnerRoom == null)
            {
                error = "Sector5FinalChaseController의 Stairwell 도착 Target OwnerRoom이 비어 있습니다.";
                return false;
            }

            if (!stairwellArrivalTarget.TryValidateConfiguration(out string targetError))
            {
                error = $"Sector5FinalChaseController의 Stairwell 도착 Target 설정이 잘못됐습니다: {targetError}";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>공용 진행 시스템 참조를 보완하고 도착 완료 이벤트를 구독한다.</summary>
        private void Awake()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            player ??= FindFirstObjectByType<Player>();
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            targetCommandRunner ??= FindFirstObjectByType<TargetCommandRunner>();
            pauseService ??= FindFirstObjectByType<GamePauseService>();
            dialogueService ??= FindFirstObjectByType<DialogueService>();
        }

        /// <summary>Target 명령 완료를 구독해 실제 Stairwell 도착 시점을 감시한다.</summary>
        private void OnEnable()
        {
            if (targetCommandRunner != null)
            {
                targetCommandRunner.InteractionCompleted -= HandleInteractionCompleted;
                targetCommandRunner.InteractionCompleted += HandleInteractionCompleted;
            }
        }

        /// <summary>이벤트 구독과 진행 중 연출의 Pause를 정리한다.</summary>
        private void OnDisable()
        {
            if (targetCommandRunner != null)
            {
                targetCommandRunner.InteractionCompleted -= HandleInteractionCompleted;
            }

            if (startRoutine != null)
            {
                StopCoroutine(startRoutine);
                startRoutine = null;
            }

            if (enemy != null && enemy.Mode == EnemyMode.ScriptedSequence)
            {
                // Unity가 Coroutine 중단 시 finally를 실행하지 않는 경로에서도
                // 적이 ScriptedSequence에 고정되지 않도록 비활성화한다.
                enemy.EndScriptedSequence(true);
            }

            // DialogueService에 남은 완료 핸들러가 비활성화 뒤 재호출되지 않도록
            // Coroutine 중단과 별개로 명시적으로 구독을 해제한다.
            UnsubscribeDialogue();
            ReleasePause();
        }

        /// <summary>
        /// Target 행동이 완전히 끝난 뒤 지정한 Stairwell 도착 Target인지 검사하고 연출을 예약한다.
        /// 클릭 시점이 아닌 TargetCommandRunner의 완료 이벤트를 사용해 문 통과와 방 전환을 보장한다.
        /// </summary>
        /// <param name="completedTarget">완료된 Target.</param>
        private void HandleInteractionCompleted(Target completedTarget)
        {
            if (completedTarget != stairwellArrivalTarget || IsRunning ||
                gameSession == null || !gameSession.IsGameplayInputEnabled ||
                !gameSession.HasStoryFlag(StoryProgressFlags.MasterKeyAcquired) ||
                gameSession.HasStoryFlag(StoryProgressFlags.FinalChaseStarted) ||
                playerRoomTracker == null || playerRoomTracker.CurrentRoom != stairwellRoom)
            {
                return;
            }

            startRoutine = StartCoroutine(RunStartSequence());
        }

        /// <summary>Pause·등장·대화·Story 상태·최초 Chase를 명세 순서대로 실행한다.</summary>
        private IEnumerator RunStartSequence()
        {
            bool sequenceSucceeded = false;
            try
            {
                if (!TryValidateConfiguration(out string error))
                {
                    GameLog.LogError(error, this);
                    yield break;
                }

                // TargetCommandRunner의 완료 정리보다 먼저 전용 Pause를 확보해
                // 대화 완료와 Chase 인계 사이에 입력이 열리는 순간을 차단한다.
                pauseHandle = pauseService.AcquirePause(this);

                if (!enemy.BeginScriptedSequence(enemySpawnPoint))
                {
                    GameLog.LogError("최종 추격 시작을 위한 ControlRoom 살인마 등장에 실패했습니다.", this);
                    yield break;
                }

                if (!PlayRunDialogue())
                {
                    GameLog.LogError("최종 추격 시작 Dialogue를 재생하지 못했습니다.", this);
                    yield break;
                }

                while (!dialogueCompleted)
                {
                    if (gameSession != null && !gameSession.IsGameplaySimulationEnabled)
                    {
                        GameLog.LogWarning("세션이 Playing 상태가 아니어서 최종 추격 시작 연출을 중단합니다.", this);
                        yield break;
                    }

                    yield return null;
                }

                // 명세의 상태 전환 순서를 보존한다. 최초 BeginChase 실패 시
                // 이 컨트롤러가 만든 flag와 Stage를 되돌려 Save5 재시작 조건을 유지한다.
                StoryStage previousStage = gameSession.CurrentStoryStage;
                gameSession.SetStoryFlag(StoryProgressFlags.FinalChaseStarted);
                gameSession.SetStoryStage(StoryStage.FinalChase);

                if (!enemy.BeginChase(player.transform, stairwellRoom))
                {
                    gameSession.ClearStoryFlag(StoryProgressFlags.FinalChaseStarted);
                    gameSession.SetStoryStage(previousStage);
                    GameLog.LogError("최종 추격 시작을 위한 Enemy BeginChase에 실패했습니다.", this);
                    yield break;
                }

                sequenceSucceeded = true;
            }
            finally
            {
                if (!sequenceSucceeded && enemy != null && enemy.Mode == EnemyMode.ScriptedSequence)
                {
                    // 실패한 시작 연출이 적을 영구 ScriptedSequence에 남기지 않도록
                    // 적을 비활성화하고 다음 Save5 재시도에서 정상 복원되게 한다.
                    enemy.EndScriptedSequence(true);
                }

                UnsubscribeDialogue();
                ReleasePause();
                startRoutine = null;
            }
        }

        /// <summary>시작 Dialogue를 재생하고 완료 이벤트를 연결한다.</summary>
        private bool PlayRunDialogue()
        {
            dialogueCompleted = false;
            dialogueService.DialogueCompleted -= HandleDialogueCompleted;
            dialogueService.DialogueCompleted += HandleDialogueCompleted;
            return dialogueService.TryPlay(runDialogue);
        }

        /// <summary>지정한 시작 Dialogue가 끝났는지 기록한다.</summary>
        /// <param name="completedSequence">완료된 DialogueSequence.</param>
        private void HandleDialogueCompleted(DialogueSequence completedSequence)
        {
            if (completedSequence == runDialogue)
            {
                dialogueCompleted = true;
            }
        }

        /// <summary>시작 Dialogue 완료 이벤트를 해제한다.</summary>
        private void UnsubscribeDialogue()
        {
            if (dialogueService != null)
            {
                dialogueService.DialogueCompleted -= HandleDialogueCompleted;
            }
        }

        /// <summary>컨트롤러가 획득한 Pause Handle을 마지막에 해제한다.</summary>
        private void ReleasePause()
        {
            pauseHandle?.Dispose();
            pauseHandle = null;
        }

        /// <summary>컴포넌트 추가 시 공용 시스템 참조를 연결한다.</summary>
        private void Reset()
        {
            gameSession = FindFirstObjectByType<GameSession>();
            player = FindFirstObjectByType<Player>();
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            targetCommandRunner = FindFirstObjectByType<TargetCommandRunner>();
            pauseService = FindFirstObjectByType<GamePauseService>();
            dialogueService = FindFirstObjectByType<DialogueService>();
        }
    }
}
