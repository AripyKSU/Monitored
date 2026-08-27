using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// NursingRoom의 최초 손전등 요구, Item Use 결과, 재입장 조명 복원을 담당한다.
    /// 진행 여부의 단일 기준은 NursingRoomFlashlightActivated Story Flag다.
    /// </summary>
    [RequireComponent(typeof(Room))]
    public sealed class NursingRoomStoryController : MonoBehaviour
    {
        [SerializeField, TextArea] private string darkMessage = "[너무 어둡다]";
        [SerializeField] private GameSession gameSession;
        [SerializeField] private PlayerRoomTracker roomTracker;
        [SerializeField] private Player player;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField] private ItemUseTarget flashlightItemUse;
        [SerializeField] private NursingRoomFlashlightView flashlightView;
        [SerializeField] private NursingRoomForcedExitPoint forcedExitPoint;

        private Room nursingRoom;
        private Coroutine firstEntryRoutine;

        private void Awake()
        {
            ResolveReferences();
            flashlightView?.SetLightActive(false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (roomTracker != null)
            {
                roomTracker.RoomEntered -= HandleRoomEntered;
                roomTracker.RoomEntered += HandleRoomEntered;
            }

            if (gameSession != null)
            {
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }
        }

        private void OnDisable()
        {
            if (roomTracker != null) roomTracker.RoomEntered -= HandleRoomEntered;
            if (gameSession != null) gameSession.GameplayStarted -= HandleGameplayStarted;

            // 씬 종료나 오브젝트 파괴 순서에 따라 조명 View가 먼저 파괴될 수 있으므로
            // Unity 오브젝트의 파괴 상태를 확인한 뒤에만 비활성화한다.
            if (flashlightView != null)
            {
                flashlightView.SetLightActive(false);
            }
        }

        private void HandleGameplayStarted()
        {
            SynchronizeForCurrentRoom();
        }

        private void HandleRoomEntered(Room previousRoom, Room currentRoom)
        {
            if (previousRoom == nursingRoom && currentRoom != nursingRoom)
            {
                flashlightView?.SetLightActive(false);
            }

            if (currentRoom == nursingRoom)
            {
                SynchronizeForCurrentRoom();
            }
        }

        private void SynchronizeForCurrentRoom()
        {
            ResolveReferences();
            bool isInNursingRoom = roomTracker != null && roomTracker.CurrentRoom == nursingRoom;
            bool activated = gameSession != null &&
                gameSession.HasStoryFlag(StoryProgressFlags.NursingRoomFlashlightActivated);

            if (!isInNursingRoom)
            {
                flashlightView?.SetLightActive(false);
                return;
            }

            if (activated)
            {
                flashlightView?.SetLightActive(true);
                return;
            }

            flashlightView?.SetLightActive(false);
            if (firstEntryRoutine == null)
            {
                firstEntryRoutine = StartCoroutine(RunFirstEntryFlow());
            }
        }

        private IEnumerator RunFirstEntryFlow()
        {
            if (!ValidateRuntimeReferences())
            {
                firstEntryRoutine = null;
                yield break;
            }

            yield return messagePanel.Show(darkMessage);

            TargetActionContext context = new TargetActionContext(null, player);
            yield return flashlightItemUse.Execute(context);

            switch (flashlightItemUse.LastResult)
            {
                case ItemUseResult.CorrectItem:
                    if (gameSession.HasStoryFlag(StoryProgressFlags.NursingRoomFlashlightActivated))
                    {
                        flashlightView.SetLightActive(true);
                    }
                    break;

                case ItemUseResult.Cancel:
                case ItemUseResult.WrongItem:
                    yield return ForceExitToHallway();
                    break;
            }

            firstEntryRoutine = null;
        }

        private IEnumerator ForceExitToHallway()
        {
            Room hallwayRoom = forcedExitPoint != null ? forcedExitPoint.DestinationRoom : null;
            if (forcedExitPoint == null || hallwayRoom == null)
            {
                GameLog.LogError("NursingRoom 강제 퇴실 지점 또는 Hallway Room 참조가 없습니다.", this);
                yield break;
            }

            yield return player.MoveTo(forcedExitPoint.transform);
            if (!player.LastMoveSucceeded)
            {
                GameLog.LogError($"NursingRoom 강제 퇴실 이동 실패: {player.LastMoveFailureReason}", this);
                yield break;
            }

            if (roomTracker.CurrentRoom != hallwayRoom && !roomTracker.EnterRoom(hallwayRoom))
            {
                GameLog.LogError("NursingRoom 강제 퇴실 후 PlayerRoomTracker를 Hallway로 갱신하지 못했습니다.", this);
                yield break;
            }

            if (roomTracker.CurrentRoom != hallwayRoom)
            {
                GameLog.LogError("NursingRoom 강제 퇴실 후 CurrentRoom이 Hallway가 아닙니다.", this);
            }
        }

        private bool ValidateRuntimeReferences()
        {
            ResolveReferences();
            if (gameSession != null && roomTracker != null && player != null && messagePanel != null &&
                flashlightItemUse != null && flashlightView != null && forcedExitPoint != null)
            {
                return true;
            }

            GameLog.LogError($"NursingRoomStoryController '{name}'의 필수 참조가 누락됐습니다.", this);
            return false;
        }

        private void ResolveReferences()
        {
            nursingRoom ??= GetComponent<Room>();
            gameSession ??= FindFirstObjectByType<GameSession>();
            roomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            player ??= FindFirstObjectByType<Player>();
            messagePanel ??= FindFirstObjectByType<MessagePanel>();
            flashlightItemUse ??= GetComponent<ItemUseTarget>();
            flashlightView ??= FindFirstObjectByType<NursingRoomFlashlightView>();
            forcedExitPoint ??= FindFirstObjectByType<NursingRoomForcedExitPoint>();
        }

        private void Reset()
        {
            ResolveReferences();
        }
    }
}
