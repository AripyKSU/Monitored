using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// Doorway를 한 방향으로 통과할 때 필요한 방과 접근 및 경계 지점을 묶는다.
    /// </summary>
    public readonly struct DoorTraversal
    {
        /// <summary>통과를 시작하는 현재 방이다.</summary>
        public Room SourceRoom { get; }
        /// <summary>통과가 완료된 뒤 기록할 목적지 방이다.</summary>
        public Room DestinationRoom { get; }
        /// <summary>현재 방향의 DoorTarget이 가진 문 앞 접근 지점이다.</summary>
        public Transform EntryPoint { get; }
        /// <summary>Sensor 경계를 지난 뒤 목적지 쪽 DoorTarget 접근 지점까지 이동할 위치다.</summary>
        public Transform DestinationEntryPoint { get; }
        /// <summary>Doorway Sensor가 통과를 감지하기 시작하는 경계 지점이다.</summary>
        public Transform PassageStart { get; }

        /// <summary>
        /// 한 방향의 문 통과 정보를 생성한다.
        /// 양쪽 지점 모두 DoorTarget의 ApproachPoint를 사용해 문 주변 위치를 재사용한다.
        /// </summary>
        /// <param name="sourceRoom">통과를 시작하는 방.</param>
        /// <param name="destinationRoom">통과가 완료된 뒤 도착할 방.</param>
        /// <param name="doorApproachPoint">문을 열기 전에 Actor가 멈출 DoorTarget 접근 지점.</param>
        /// <param name="passageStart">Sensor 감지를 시작할 경계 지점.</param>
        /// <param name="entryPoint">목적지 쪽 DoorTarget의 접근 지점.</param>
        public DoorTraversal(
            Room sourceRoom,
            Room destinationRoom,
            Transform doorApproachPoint,
            Transform passageStart,
            Transform entryPoint)
        {
            SourceRoom = sourceRoom;
            DestinationRoom = destinationRoom;
            EntryPoint = doorApproachPoint;
            PassageStart = passageStart;
            DestinationEntryPoint = entryPoint;
        }
    }
}
