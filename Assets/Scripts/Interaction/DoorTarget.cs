using System;
using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// Target 접근 완료 후 문 열기, 경계 통과, CCTV와 논리 방 변경, 목적지 진입, 자동 닫기를 실행한다.
    /// 클릭과 마커는 TargetPresenter, 방향 감지는 Doorway, 실제 이동은 Player에 위임한다.
    /// </summary>
    public sealed class DoorTarget : MonoBehaviour, ITargetAction, IItemUseEffect
    {
        [SerializeField] private Doorway doorway;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private CameraDirector cameraDirector;
        [SerializeField] private ItemUseTarget lockedItemUseTarget;
        [SerializeField] private MessagePanel lockedMessagePanel;
        [SerializeField, TextArea] private string lockedMessage = "잠겨있다.";

        /// <summary>
        /// 이 방향의 문 앞에서 Actor가 멈춰야 하는 접근 지점을 반환한다.
        /// Doorway는 방향별 DoorTraversal을 만들 때 이 값을 사용해 위치 중복을 줄인다.
        /// </summary>
        public Transform ApproachPoint
        {
            get
            {
                Target target = GetComponent<Target>();
                return target != null ? target.ApproachPoint : null;
            }
        }

        /// <summary>
        /// 스토리 연출처럼 일반 Target 명령 밖에서 같은 문 연결 정보를 사용해야 할 때 Doorway를 제공한다.
        /// 문 통과 규칙 자체는 계속 Doorway가 소유하며 외부에서는 연결 정보만 읽는다.
        /// </summary>
        public Doorway Doorway => doorway;

        /// <summary>
        /// 현재 방에서 Doorway 반대편 방으로 플레이어를 통과시키고 문을 자동으로 닫는다.
        /// </summary>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (!TryResolveTraversal(context, out DoorTraversal traversal))
            {
                yield break;
            }

            if (doorway.IsLocked)
            {
                if (lockedItemUseTarget == null || !lockedItemUseTarget.HasRequiredItem)
                {
                    if (lockedMessagePanel == null)
                    {
                        GameLog.LogError(
                            $"잠긴 DoorTarget '{name}'에 열쇠 선택 Target 또는 잠금 메시지 UI가 연결되지 않았습니다.",
                            this);
                    }
                    else
                    {
                        // 열쇠를 아직 보유하지 않은 경우에는 기존 단순 Message UI만 보여 주고 이동을 막는다.
                        yield return lockedMessagePanel.Show(lockedMessage);
                    }

                    yield break;
                }

                yield return lockedItemUseTarget.Execute(context);
                if (doorway.IsLocked == false)
                {
                    context.MarkCompleted();
                }
                yield break;
            }

            bool passageCommitted = false;
            bool transitionFailed = false;
            bool closedNormally = false;

            // Trigger 콜백에서 카메라와 논리 방을 같은 프레임에 확정한다.
            void CommitPassage()
            {
                if (passageCommitted || transitionFailed)
                {
                    return;
                }

                if (!cameraDirector.CutTo(traversal.DestinationRoom))
                {
                    transitionFailed = true;
                    return;
                }

                if (!playerRoomTracker.EnterRoom(traversal.DestinationRoom))
                {
                    // 논리 방 변경이 예외적으로 실패하면 이전 CCTV로 즉시 되돌려 두 시스템을 일치시킨다.
                    cameraDirector.CutTo(traversal.SourceRoom);
                    transitionFailed = true;
                    return;
                }

                passageCommitted = true;
            }

            void HandlePassageStarted(GameObject actor, DoorTraversal detectedTraversal)
            {
                if (passageCommitted || transitionFailed || actor != context.Player.gameObject)
                {
                    return;
                }

                if (detectedTraversal.SourceRoom != traversal.SourceRoom ||
                    detectedTraversal.DestinationRoom != traversal.DestinationRoom)
                {
                    return;
                }

                CommitPassage();
            }

            doorway.PassageStarted += HandlePassageStarted;

            try
            {
                yield return doorway.Open();

                // PassageStart는 목적지 쪽 Sensor 내부에 배치해 이동 중 Trigger가 반드시 발생하게 한다.
                yield return context.Player.MoveTo(traversal.PassageStart);
                if (!context.Player.LastMoveSucceeded)
                {
                    // 서로 다른 NavMesh Surface를 잇는 Link가 같은 프레임에 갱신 중이면 한 프레임 뒤 한 번만 재시도한다.
                    yield return null;
                    yield return context.Player.MoveTo(traversal.PassageStart);
                }
                if (!context.Player.LastMoveSucceeded)
                {
                    GameLog.LogError(
                        $"문 통과 시작 지점 이동에 실패했습니다: {context.Player.LastMoveFailureReason}",
                        this);
                    yield break;
                }

                // Trigger 콜백 순서가 물리 프레임에 따라 늦어져도 Passage 지점 도착을 전환 확정의 안전 기준으로 사용한다.
                if (!passageCommitted && !transitionFailed)
                {
                    CommitPassage();
                }

                if (transitionFailed || !passageCommitted)
                {
                    GameLog.LogError(
                        $"Doorway '{doorway.name}'의 통과 Trigger가 플레이어 방 진입을 확정하지 못했습니다.",
                        doorway);
                    yield break;
                }

                // 카메라와 논리 방이 이미 목적지로 바뀐 상태에서 방 내부 진입 위치까지 이동한다.
                yield return context.Player.MoveTo(traversal.DestinationEntryPoint);
                if (!context.Player.LastMoveSucceeded)
                {
                    GameLog.LogError(
                        $"목적지 방 진입 위치 이동에 실패했습니다: {context.Player.LastMoveFailureReason}",
                        this);
                    yield break;
                }


                // 목적지 접근 지점은 반대 방향에서 문을 클릭할 때도 사용되므로,
                // 통과 완료 후에는 문 경계에서 방 안쪽으로 향하는 방향을 명시적으로 적용한다.
                context.Player.FaceDirection(
                    traversal.DestinationEntryPoint.position - traversal.PassageStart.position);

                // 플레이어가 문을 완전히 통과한 뒤에만 0.25초 닫기 연출을 실행한다.
                yield return doorway.Close();
                closedNormally = true;
                context.MarkCompleted();
            }
            finally
            {
                doorway.PassageStarted -= HandlePassageStarted;

                // 이동 실패나 컴포넌트 중단으로 코루틴이 끝나도 열린 문이 남지 않게 한다.
                if (!closedNormally)
                {
                    doorway.ForceClosed();
                }
            }
        }

        /// <summary>
        /// 올바른 열쇠가 소비되면 공용 Doorway 잠금 상태만 해제한다.
        /// 실제 문 통과는 다음 DoorTarget 명령에서 기존 흐름으로 실행한다.
        /// </summary>
        public void ApplyItemEffect(TargetActionContext context)
        {
            if (doorway == null)
            {
                GameLog.LogError($"DoorTarget '{name}'에 Doorway가 연결되지 않았습니다.", this);
                return;
            }

            doorway.SetLocked(false);
        }

        /// <summary>
        /// 현재 방과 Doorway 설정을 검사하고 이번 방향의 통과 데이터를 구한다.
        /// </summary>
        private bool TryResolveTraversal(TargetActionContext context, out DoorTraversal traversal)
        {
            traversal = default;

            if (context?.Player == null || doorway == null || playerRoomTracker == null || cameraDirector == null)
            {
                GameLog.LogError($"DoorTarget '{name}'의 필수 참조가 누락됐습니다.", this);
                return false;
            }

            if (!doorway.TryValidateConfiguration(out string configurationError))
            {
                GameLog.LogError(configurationError, doorway);
                return false;
            }

            Room currentRoom = playerRoomTracker.CurrentRoom;
            if (!doorway.TryGetTraversal(currentRoom, out traversal))
            {
                GameLog.LogError(
                    $"현재 방 '{currentRoom?.RoomId}'에서 Doorway '{doorway.name}'을 통과할 수 없습니다.",
                    this);
                return false;
            }

            if (!playerRoomTracker.CanEnter(traversal.DestinationRoom))
            {
                GameLog.LogError(
                    $"Doorway 목적지 '{traversal.DestinationRoom.RoomId}'가 현재 방과 직접 인접하지 않습니다.",
                    this);
                return false;
            }

            return true;
        }

        /// <summary>컴포넌트가 처음 추가될 때 현재 씬의 공용 문·이동·메시지 참조를 보완한다.</summary>
        private void Reset()
        {
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            cameraDirector = FindFirstObjectByType<CameraDirector>();
            lockedMessagePanel = FindFirstObjectByType<MessagePanel>();
        }
    }
}
