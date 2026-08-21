using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 이동 지점의 상호작용을 제공한다.
/// 목적지 방의 카메라를 즉시 전환하고 플레이어를 목적지 위치로 이동시킨다.
/// </summary>
public class RoomTransition : MonoBehaviour, IInteractionActionProvider
{
    [SerializeField] private Room destinationRoom;
    [SerializeField] private Transform destinationPoint;
    [SerializeField] private CameraDirector cameraDirector;
    [SerializeField] private PlayerStateController playerStateController;
    [SerializeField] private string actionName = "Enter";

    /// <summary>
    /// 상호작용 UI에 표시할 방 이동 액션을 만든다.
    /// </summary>
    public IReadOnlyList<InteractionAction> GetInteractionActions(Player player)
    {
        return new List<InteractionAction>
        {
            new InteractionAction(actionName, () => ExecuteTransition(player))
        };
    }

    /// <summary>
    /// 목적지 카메라를 Cut으로 전환한 뒤 플레이어 이동을 시작한다.
    /// </summary>
    private void ExecuteTransition(Player player)
    {
        if (destinationRoom == null || player == null)
        {
            return;
        }

        cameraDirector ??= FindFirstObjectByType<CameraDirector>();
        playerStateController ??= player.GetComponent<PlayerStateController>();

        // CCTV 화면은 방에 들어가는 순간 바뀌어야 하므로 블렌드 없이 전환한다.
        cameraDirector?.SwitchTo(destinationRoom);

        Transform targetPoint = destinationPoint != null
            ? destinationPoint
            : destinationRoom.PlayerSpawnPoint;

        // 목적지 포인트가 없으면 카메라만 전환하고 잘못된 이동 요청은 보내지 않는다.
        if (targetPoint != null)
        {
            playerStateController?.CommandMove(targetPoint.position);
        }
    }
}
