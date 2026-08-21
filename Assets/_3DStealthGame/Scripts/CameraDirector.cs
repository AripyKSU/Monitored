using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 현재 방의 CCTV 시점을 결정한다.
/// 방 전환은 블렌드 없이 Cut으로 처리한다.
/// </summary>
public class CameraDirector : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private CinemachineBrain brain;
    [SerializeField] private Room initialRoom;

    private Room currentRoom;

    private void Awake()
    {
        mainCamera ??= Camera.main;
        brain ??= mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;

        // Cinemachine Brain의 기본 전환을 Cut으로 고정해 순간 전환을 보장한다.
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.Cut,
                0f);
        }
    }

    private void Start()
    {
        if (initialRoom != null)
        {
            SwitchTo(initialRoom);
        }
    }

    /// <summary>
    /// 지정된 방의 카메라를 즉시 활성화한다.
    /// </summary>
    public void SwitchTo(Room room)
    {
        if (room == null || room.RoomCamera == null || room.RoomCamera.VirtualCamera == null)
        {
            return;
        }

        if (currentRoom == room)
        {
            return;
        }

        // 우선순위가 높은 가상 카메라를 Main Camera가 즉시 선택한다.
        if (currentRoom != null && currentRoom.RoomCamera != null)
        {
            currentRoom.RoomCamera.VirtualCamera.Priority = 0;
        }

        room.RoomCamera.VirtualCamera.Priority = 100;
        currentRoom = room;
    }
}
