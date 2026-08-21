using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// Doorway를 한 방향으로 통과할 때 필요한 출발 방, 목적지 방, 경계 지점과 진입 완료 지점을 묶는다.
    /// </summary>
    public readonly struct DoorTraversal
    {
        public Room SourceRoom { get; }
        public Room DestinationRoom { get; }
        public Transform PassageStart { get; }
        public Transform EntryPoint { get; }

        public DoorTraversal(
            Room sourceRoom,
            Room destinationRoom,
            Transform passageStart,
            Transform entryPoint)
        {
            SourceRoom = sourceRoom;
            DestinationRoom = destinationRoom;
            PassageStart = passageStart;
            EntryPoint = entryPoint;
        }
    }
}
