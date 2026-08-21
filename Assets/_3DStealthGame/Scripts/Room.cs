using UnityEngine;

/// <summary>
/// 방의 식별 정보와 해당 방의 CCTV 카메라를 소유한다.
/// </summary>
public class Room : MonoBehaviour
{
    [SerializeField] private string roomId;
    [SerializeField] private RoomCamera roomCamera;
    [SerializeField] private Transform playerSpawnPoint;

    public string RoomId => roomId;
    public RoomCamera RoomCamera => roomCamera;
    public Transform PlayerSpawnPoint => playerSpawnPoint;

    private void Reset()
    {
        // 방 프리팹 안에 RoomCamera가 있으면 자동으로 연결한다.
        roomCamera = GetComponentInChildren<RoomCamera>();
    }
}
