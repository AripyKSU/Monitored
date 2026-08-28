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
        [SerializeField] private TargetCommandRunner targetCommandRunner;
        [SerializeField] private CameraDirector cameraDirector;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField] private ItemUseTarget flashlightItemUse;
        [SerializeField] private NursingRoomFlashlightView flashlightView;
        [SerializeField] private NursingRoomDarkVolumeView darkVolumeView;
        [SerializeField] private NursingRoomForcedExitPoint forcedExitPoint;
        [SerializeField] private DoorTarget forcedExitDoorTarget;

        private Room nursingRoom;
        private Coroutine firstEntryRoutine;

        private void Awake()
        {
            ResolveReferences();
            flashlightView?.SetLightActive(false);
            darkVolumeView?.SetDarknessActive(false);
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

            darkVolumeView?.SetDarknessActive(false);
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
                darkVolumeView?.SetDarknessActive(false);
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

            darkVolumeView?.SetDarknessActive(isInNursingRoom);

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

            // RoomEntered는 DoorTarget이 PassageStart를 지난 직후 발생하지만, 실제 입장 명령은
            // 목적지 ApproachPoint 도착과 문 닫힘까지 계속 실행된다. 그 명령이 끝나기 전에
            // 아이템 UI를 열면 취소 퇴장과 입장 이동이 같은 PlayerMover를 동시에 제어하므로
            // 반드시 기존 문 명령이 완전히 정리된 뒤 최초 입장 연출을 시작한다.
            while (targetCommandRunner.IsBusy)
            {
                yield return null;
            }

            // 대기 중 체크포인트 복원이나 다른 시스템으로 방이 바뀌었다면 뒤늦은 UI를 띄우지 않는다.
            if (roomTracker.CurrentRoom != nursingRoom)
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
            ResolveForcedExitDoorTarget(hallwayRoom);
            Doorway doorway = forcedExitDoorTarget != null ? forcedExitDoorTarget.Doorway : null;
            if (forcedExitPoint == null || hallwayRoom == null || doorway == null ||
                !doorway.TryGetTraversal(nursingRoom, out DoorTraversal traversal))
            {
                GameLog.LogError("NursingRoom 강제 퇴실 Doorway 또는 Hallway 통과 정보가 없습니다.", this);
                yield break;
            }

            // 강제 퇴장은 플레이어가 선택한 일반 문 이동이 아니라 실패 복구 성격의 스토리 전환이다.
            // NavMesh 경로와 도착 타임아웃에 다시 의존하지 않고 문을 연 뒤 목적지 진입점으로 원자적으로 배치한다.
            player.AbortMove();
            yield return doorway.Open();

            if (!player.WarpTo(traversal.DestinationEntryPoint))
            {
                doorway.ForceClosed();
                GameLog.LogError("NursingRoom 강제 퇴실 위치로 Player를 배치하지 못했습니다.", this);
                yield break;
            }

            // 위치, 카메라, 논리 방을 한 프레임 안에서 함께 확정해 중간 상태가 다음 입력에 노출되지 않게 한다.
            if (!cameraDirector.CutTo(hallwayRoom) || !roomTracker.EnterRoom(hallwayRoom))
            {
                player.WarpTo(traversal.EntryPoint);
                cameraDirector.CutTo(nursingRoom);
                doorway.ForceClosed();
                GameLog.LogError("NursingRoom 강제 퇴실 중 카메라 또는 논리 방 전환에 실패했습니다.", this);
                yield break;
            }

            doorway.NotifyPassageStarted(player.gameObject, traversal);
            player.FaceDirection(
                traversal.DestinationEntryPoint.position - traversal.PassageStart.position);
            yield return doorway.Close();
        }

        private bool ValidateRuntimeReferences()
        {
            ResolveReferences();
            if (gameSession != null && roomTracker != null && player != null && messagePanel != null &&
                targetCommandRunner != null && cameraDirector != null &&
                flashlightItemUse != null && flashlightView != null && darkVolumeView != null &&
                forcedExitPoint != null && forcedExitDoorTarget != null)
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
            targetCommandRunner ??= FindFirstObjectByType<TargetCommandRunner>();
            cameraDirector ??= FindFirstObjectByType<CameraDirector>();
            messagePanel ??= FindFirstObjectByType<MessagePanel>();
            flashlightItemUse ??= GetComponent<ItemUseTarget>();
            flashlightView ??= FindFirstObjectByType<NursingRoomFlashlightView>();
            darkVolumeView ??= GetComponentInChildren<NursingRoomDarkVolumeView>(true);
            forcedExitPoint ??= FindFirstObjectByType<NursingRoomForcedExitPoint>();
            ResolveForcedExitDoorTarget(forcedExitPoint != null ? forcedExitPoint.DestinationRoom : null);
        }

        /// <summary>
        /// NursingRoom과 지정 Hallway를 연결하며 NursingRoom 방향에서 사용하는 기존 DoorTarget을 찾는다.
        /// </summary>
        /// <param name="hallwayRoom">강제 퇴실 목적 Hallway Room.</param>
        private void ResolveForcedExitDoorTarget(Room hallwayRoom)
        {
            if (forcedExitDoorTarget != null || nursingRoom == null || hallwayRoom == null)
            {
                return;
            }

            Doorway[] doorways = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Doorway doorway in doorways)
            {
                bool connectsRooms = doorway != null &&
                    ((doorway.RoomA == nursingRoom && doorway.RoomB == hallwayRoom) ||
                     (doorway.RoomA == hallwayRoom && doorway.RoomB == nursingRoom));
                if (!connectsRooms)
                {
                    continue;
                }

                DoorTarget[] doorTargets = doorway.GetComponentsInChildren<DoorTarget>(true);
                foreach (DoorTarget doorTarget in doorTargets)
                {
                    Target target = doorTarget.GetComponent<Target>();
                    if (target != null && target.OwnerRoom == nursingRoom)
                    {
                        forcedExitDoorTarget = doorTarget;
                        return;
                    }
                }
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }
    }
}
