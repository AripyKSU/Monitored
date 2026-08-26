using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방의 고정 식별 정보, CCTV, 직접 인접한 방 목록을 소유한다.
/// 플레이어나 적의 현재 방 상태는 관리하지 않고 스테이지의 고정 연결 정보만 제공한다.
/// </summary>
public sealed class Room : MonoBehaviour
{
    [SerializeField] private string roomId;
    [SerializeField] private RoomCamera roomCamera;
    [Header("그래프 시각화 기준점")]
    [Tooltip("RoomGraph가 이 방의 노드 위치로 사용할 Transform입니다. 비워 두면 RoomCamera 위치를 사용합니다.")]
    [SerializeField] private Transform graphPositionSource;
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Room[] adjacentRooms = Array.Empty<Room>();

    public string RoomId => roomId;
    public RoomCamera RoomCamera => roomCamera;
    public Transform GraphPositionSource => graphPositionSource;
    public Transform PlayerSpawnPoint => playerSpawnPoint;
    public IReadOnlyList<Room> AdjacentRooms => adjacentRooms;

    /// <summary>
    /// RoomGraph 시각화와 베이크가 사용할 방의 월드 기준점을 반환한다.
    /// 명시적인 기준점이 없으면 실제 방 안에 배치된 RoomCamera를 사용하고,
    /// 카메라도 없을 때만 논리 Room 오브젝트의 위치로 되돌아간다.
    /// </summary>
    public Vector3 GraphPosition
    {
        get
        {
            // 그래프 기준점은 물리 메쉬를 이동시키지 않고 위치만 읽기 위한 참조다.
            if (graphPositionSource != null)
            {
                return graphPositionSource.position;
            }

            // 기존 씬의 Room 루트가 섹터의 논리 컨테이너인 경우에도,
            // 실제 방 안에 배치된 CCTV 위치로 그래프 노드를 표시한다.
            if (roomCamera != null)
            {
                return roomCamera.transform.position;
            }

            // RoomCamera가 없는 임시 구성에서도 그래프가 완전히 깨지지 않도록 보수적으로 폴백한다.
            return transform.position;
        }
    }

    /// <summary>
    /// 지정한 방이 문 하나로 직접 연결된 인접 방인지 확인한다.
    /// </summary>
    public bool IsAdjacentTo(Room other)
    {
        if (other == null || adjacentRooms == null)
        {
            return false;
        }

        foreach (Room adjacentRoom in adjacentRooms)
        {
            if (adjacentRoom == other)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 방 ID, CCTV, 인접 목록의 기본 구성이 유효한지 검사한다.
    /// </summary>
    public bool TryValidateConfiguration(out string error)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            error = $"Room '{name}'의 RoomId가 비어 있습니다.";
            return false;
        }

        if (roomCamera == null || !roomCamera.IsConfigured)
        {
            error = $"Room '{name}'에 유효한 RoomCamera가 연결되지 않았습니다.";
            return false;
        }

        if (adjacentRooms != null)
        {
            for (int index = 0; index < adjacentRooms.Length; index++)
            {
                Room adjacentRoom = adjacentRooms[index];
                if (adjacentRoom == null)
                {
                    error = $"Room '{name}'의 인접 방 목록에 빈 참조가 있습니다.";
                    return false;
                }

                if (adjacentRoom == this)
                {
                    error = $"Room '{name}'이 자기 자신을 인접 방으로 참조합니다.";
                    return false;
                }

                for (int duplicateIndex = index + 1; duplicateIndex < adjacentRooms.Length; duplicateIndex++)
                {
                    if (adjacentRooms[duplicateIndex] == adjacentRoom)
                    {
                        error = $"Room '{name}'의 인접 방 '{adjacentRoom.name}'이 중복 등록됐습니다.";
                        return false;
                    }
                }
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 컴포넌트를 처음 추가했을 때 자식 CCTV를 자동으로 연결한다.
    /// </summary>
    private void Reset()
    {
        roomCamera = GetComponentInChildren<RoomCamera>();
    }
}
