using System;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// 플레이어의 논리적 현재 방을 단일 기준으로 관리한다.
    /// 카메라나 이동을 직접 제어하지 않고 방 변경을 확정한 뒤 RoomEntered 이벤트를 발생시킨다.
    /// </summary>
    public sealed class PlayerRoomTracker : MonoBehaviour
    {
        [SerializeField] private Room initialRoom;

        public Room CurrentRoom { get; private set; }

        /// <summary>
        /// 플레이어의 논리적 방이 실제로 변경된 직후 이전 방과 새 방을 전달한다.
        /// </summary>
        public event Action<Room, Room> RoomEntered;

        /// <summary>
        /// 씬에서 지정한 초기 방을 현재 방의 단일 초기값으로 사용한다.
        /// </summary>
        private void Awake()
        {
            CurrentRoom = initialRoom;

            if (CurrentRoom == null)
            {
                GameLog.LogError($"PlayerRoomTracker '{name}'에 InitialRoom이 연결되지 않았습니다.", this);
            }
        }

        /// <summary>
        /// 현재 방에서 지정한 방으로 논리적 진입이 가능한지 검사한다.
        /// </summary>
        public bool CanEnter(Room nextRoom)
        {
            if (nextRoom == null)
            {
                return false;
            }

            return CurrentRoom == null || CurrentRoom == nextRoom || CurrentRoom.IsAdjacentTo(nextRoom);
        }

        /// <summary>
        /// 플레이어의 현재 방을 변경하고 RoomEntered 이벤트를 동기적으로 발생시킨다.
        /// 직접 인접하지 않은 방으로의 변경은 스테이지 연결 오류로 거부한다.
        /// </summary>
        public bool EnterRoom(Room nextRoom)
        {
            if (!CanEnter(nextRoom))
            {
                GameLog.LogError(
                    $"현재 방 '{CurrentRoom?.RoomId}'에서 '{nextRoom?.RoomId}'로 직접 진입할 수 없습니다.",
                    this);
                return false;
            }

            if (CurrentRoom == nextRoom)
            {
                return true;
            }

            Room previousRoom = CurrentRoom;
            CurrentRoom = nextRoom;

            // 상태를 먼저 확정한 다음 알리므로 구독자는 이벤트 안에서 CurrentRoom을 안전하게 조회할 수 있다.
            RoomEntered?.Invoke(previousRoom, CurrentRoom);
            return true;
        }

        /// <summary>
        /// 체크포인트 복원용으로 현재 방을 직접 교체한다.
        /// 일반 이동이 아니므로 인접성 검사나 RoomEntered 이벤트를 발생시키지 않는다.
        /// </summary>
        /// <param name="restoredRoom">복원할 논리 방.</param>
        public bool RestoreRoom(Room restoredRoom)
        {
            if (restoredRoom == null)
            {
                GameLog.LogError($"PlayerRoomTracker '{name}'에 복원할 Room이 없습니다.", this);
                return false;
            }

            CurrentRoom = restoredRoom;
            return true;
        }
    }
}
