using System.Collections;
using HauntedHouse.Core;
using HauntedHouse.Dialogue;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 최종 추격 중 플레이어가 Sector5 정문에 실제로 도착했을 때
    /// 일회성 도착 flag를 기록하고 정문 안내 대화를 재생한다.
    /// </summary>
    public sealed class Sector5EntranceController : MonoBehaviour
    {
        [Header("정문 도착 조건")]
        [SerializeField] private Room entranceRoom;

        [Header("공용 진행 시스템")]
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private GameSession gameSession;
        [SerializeField] private DialogueService dialogueService;

        [Header("도착 안내")]
        [SerializeField] private DialogueSequence entranceDialogue;
        [SerializeField, Min(0.05f)] private float dialogueRetryInterval = 0.1f;

        private Coroutine dialogueRetryRoutine;
        private bool dialoguePlayRequested;

        /// <summary>정문 도착 판정에 사용하는 Room 참조를 반환한다.</summary>
        public Room EntranceRoom => entranceRoom;

        /// <summary>정문 도착 시 재생할 안내 Dialogue를 반환한다.</summary>
        public DialogueSequence EntranceDialogue => entranceDialogue;

        /// <summary>현재 안내 Dialogue 재생을 기다리는 중인지 반환한다.</summary>
        public bool IsWaitingForDialogue => dialogueRetryRoutine != null;

        /// <summary>플레이어 방 변경과 세션 재시작 이벤트를 구독한다.</summary>
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
            }
        }

        /// <summary>이벤트 구독과 대기 중인 Dialogue 재시도 코루틴을 정리한다.</summary>
        private void OnDisable()
        {
            if (playerRoomTracker != null)
            {
                playerRoomTracker.RoomEntered -= HandleRoomEntered;
            }

            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }

            StopDialogueRetry();
        }

        /// <summary>
        /// 실제 RoomEntered 사건을 받은 뒤 최종 추격 중 정문 도착인지 검사한다.
        /// 도착 flag는 대화 재생 결과와 무관하게 먼저 기록한다.
        /// </summary>
        private void HandleRoomEntered(Room previousRoom, Room currentRoom)
        {
            if (!IsEntranceArrival(currentRoom))
            {
                return;
            }

            if (!gameSession.SetStoryFlag(StoryProgressFlags.EntranceReached))
            {
                return;
            }

            // 도착 순간 flag를 먼저 기록해 같은 정문을 왕복해도 안내가 반복되지 않게 한다.
            RequestEntranceDialogue();
        }

        /// <summary>새 Gameplay 세션이 시작될 때 이전 세션의 대기 상태를 제거한다.</summary>
        private void HandleGameplayStarted()
        {
            dialoguePlayRequested = false;
            StopDialogueRetry();
        }

        /// <summary>정문 도착과 최종 추격 선행 조건을 함께 검사한다.</summary>
        private bool IsEntranceArrival(Room currentRoom)
        {
            return gameSession != null &&
                   gameSession.IsGameplayInputEnabled &&
                   gameSession.HasStoryFlag(StoryProgressFlags.FinalChaseStarted) &&
                   !gameSession.HasStoryFlag(StoryProgressFlags.EntranceReached) &&
                   entranceRoom != null &&
                   currentRoom == entranceRoom;
        }

        /// <summary>안내 Dialogue를 즉시 시작하거나 현재 재생 종료 후 재시도를 예약한다.</summary>
        private void RequestEntranceDialogue()
        {
            if (dialoguePlayRequested)
            {
                return;
            }

            dialoguePlayRequested = true;

            if (!TryPlayEntranceDialogue())
            {
                if (dialogueService != null && dialogueService.IsPlaying)
                {
                    dialogueRetryRoutine = StartCoroutine(RetryEntranceDialogue());
                }
                else
                {
                    GameLog.LogError(
                        $"Sector5EntranceController '{name}'의 정문 안내 Dialogue를 재생하지 못했습니다.",
                        this);
                    dialoguePlayRequested = false;
                }
            }
        }

        /// <summary>DialogueService의 기존 대화가 끝날 때까지 기다린 뒤 안내를 한 번 재생한다.</summary>
        private IEnumerator RetryEntranceDialogue()
        {
            while (dialogueService != null && dialogueService.IsPlaying)
            {
                yield return new WaitForSecondsRealtime(dialogueRetryInterval);
            }

            dialogueRetryRoutine = null;

            if (!TryPlayEntranceDialogue())
            {
                GameLog.LogError(
                    $"Sector5EntranceController '{name}'이 정문 안내 Dialogue 재생을 재시도했지만 실패했습니다.",
                    this);
                dialoguePlayRequested = false;
            }
        }

        /// <summary>참조와 Dialogue 내용을 확인한 뒤 공용 DialogueService로 안내를 재생한다.</summary>
        private bool TryPlayEntranceDialogue()
        {
            if (dialogueService == null || entranceDialogue == null || entranceDialogue.Lines == null ||
                entranceDialogue.Lines.Count == 0)
            {
                return false;
            }

            return dialogueService.TryPlay(entranceDialogue);
        }

        /// <summary>대기 중인 안내 재생 코루틴과 재시도 상태를 정리한다.</summary>
        private void StopDialogueRetry()
        {
            if (dialogueRetryRoutine != null)
            {
                StopCoroutine(dialogueRetryRoutine);
                dialogueRetryRoutine = null;
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 현재 씬의 공용 시스템을 보수적으로 연결한다.</summary>
        private void Reset()
        {
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            gameSession = FindFirstObjectByType<GameSession>();
            dialogueService = FindFirstObjectByType<DialogueService>();
            entranceRoom = GetComponentInParent<Room>();
        }
    }
}
