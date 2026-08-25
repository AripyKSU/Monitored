using Unity.Cinemachine;
using HauntedHouse.World;
using UnityEngine;

/// <summary>
/// PlayerRoomTracker가 가리키는 방의 CCTV 시점을 Main Camera에 적용한다.
/// 방 전환은 항상 블렌드 없는 Cut으로 처리하고 논리 방 상태는 직접 변경하지 않는다.
/// </summary>
public sealed class CameraDirector : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private CinemachineBrain brain;
    [SerializeField] private PlayerRoomTracker playerRoomTracker;

    private Room currentRoom;

    public Room CurrentRoom => currentRoom;

    private void Awake()
    {
        mainCamera ??= Camera.main;
        brain ??= mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;
        playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();

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
        // 초기 방 정보는 PlayerRoomTracker 한 곳에서만 가져와 두 시스템의 초기값 중복을 피한다.
        if (playerRoomTracker != null && playerRoomTracker.CurrentRoom != null)
        {
            CutTo(playerRoomTracker.CurrentRoom);
        }
    }

    /// <summary>
    /// 지정된 방의 CCTV를 즉시 활성화한다.
    /// 유효한 CCTV가 없으면 논리 방 변경 전에 호출자가 중단할 수 있도록 false를 반환한다.
    /// </summary>
    public bool CutTo(Room room)
    {
        if (brain == null)
        {
            GameLog.LogError("CameraDirector에 CinemachineBrain이 연결되지 않았습니다.", this);
            return false;
        }

        if (room == null || room.RoomCamera == null || !room.RoomCamera.IsConfigured)
        {
            GameLog.LogError("전환할 Room에 유효한 RoomCamera가 없습니다.", room);
            return false;
        }

        if (currentRoom == room)
        {
            room.RoomCamera.SetPriority(100);
            return true;
        }

        // 우선순위가 높은 가상 카메라를 Main Camera가 즉시 선택한다.
        if (currentRoom != null && currentRoom.RoomCamera != null)
        {
            currentRoom.RoomCamera.SetPriority(0);
        }

        room.RoomCamera.SetPriority(100);
        currentRoom = room;
        return true;
    }

    /// <summary>
    /// 씬 구성 시 Main Camera와 방 추적기를 기본 연결한다.
    /// </summary>
    private void Reset()
    {
        mainCamera = Camera.main;
        brain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;
        playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
    }
}
