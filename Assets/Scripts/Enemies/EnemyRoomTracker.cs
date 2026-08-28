using System;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 적의 논리적 현재 방만 관리한다.
    /// 방 이동 자체는 EnemyController가 수행하고, 실제 방 변경은 Doorway 통과 이벤트로 확정한다.
    /// </summary>
    public sealed class EnemyRoomTracker : MonoBehaviour
    {
        [SerializeField] private Room initialRoom;
        [SerializeField] private Doorway[] doorways;

        /// <summary>적이 현재 속한 논리적 방을 반환한다.</summary>
        public Room CurrentRoom { get; private set; }

        /// <summary>현재 방이 변경된 직후 이전 방과 새 방을 전달한다.</summary>
        public event Action<Room, Room> RoomChanged;

        /// <summary>
        /// 초기 방을 설정하고 Doorway 통과 이벤트 구독에 사용할 참조를 보완한다.
        /// </summary>
        private void Awake()
        {
            CurrentRoom = initialRoom;

            // Inspector에 초기 샌드박스 Doorway 목록이 남아 있으면 병원 맵에서 나중에 추가된 문을
            // 구독하지 못한다. 방 변경 이벤트는 모든 실제 문에서 받아야 하므로 실행 시 현재 씬의
            // 전체 Doorway 목록으로 갱신해 중간 경유 방 진입도 빠짐없이 추적한다.
            doorways = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (CurrentRoom == null)
            {
                GameLog.LogError($"EnemyRoomTracker '{name}'에 InitialRoom이 연결되지 않았습니다.", this);
            }
        }

        /// <summary>활성화 시 모든 Doorway의 통과 이벤트를 구독한다.</summary>
        private void OnEnable()
        {
            if (doorways == null)
            {
                return;
            }

            foreach (Doorway doorway in doorways)
            {
                if (doorway != null)
                {
                    doorway.PassageStarted += HandlePassageStarted;
                }
            }
        }

        /// <summary>비활성화 시 Doorway 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (doorways == null)
            {
                return;
            }

            foreach (Doorway doorway in doorways)
            {
                if (doorway != null)
                {
                    doorway.PassageStarted -= HandlePassageStarted;
                }
            }
        }

        /// <summary>
        /// Doorway가 알린 통과 주체가 자신일 때 목적지 방으로 현재 방을 변경한다.
        /// </summary>
        private void HandlePassageStarted(GameObject actor, DoorTraversal traversal)
        {
            if (actor != gameObject)
            {
                return;
            }

            SetRoom(traversal.DestinationRoom);
        }

        /// <summary>
        /// 스폰 또는 체크포인트 복원 시 적의 현재 방을 지정한 방으로 설정한다.
        /// </summary>
        internal void SetRoom(Room room)
        {
            if (room == null || CurrentRoom == room)
            {
                return;
            }

            Room previousRoom = CurrentRoom;
            CurrentRoom = room;
            RoomChanged?.Invoke(previousRoom, CurrentRoom);
        }

        /// <summary>적이 비활성화될 때 경보 계산에서 사용할 현재 방을 제거한다.</summary>
        internal void ClearRoom()
        {
            if (CurrentRoom == null)
            {
                return;
            }

            Room previousRoom = CurrentRoom;
            CurrentRoom = null;
            RoomChanged?.Invoke(previousRoom, null);
        }

        /// <summary>
        /// 지정한 순찰 지점의 위치로 재활성화될 적의 논리적 방을 초기화한다.
        /// </summary>
        internal void ResetAt(PatrolPoint spawnPoint)
        {
            SetRoom(spawnPoint != null ? spawnPoint.Room : null);
        }

        /// <summary>
        /// 컴포넌트를 처음 추가할 때 자식이 속한 방과 씬의 Doorway를 기본 연결한다.
        /// </summary>
        private void Reset()
        {
            initialRoom = GetComponentInParent<Room>();
            doorways = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }
}
