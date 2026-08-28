using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// NursingRoom 강제 퇴실의 목적 Room을 명시하는 표식이다.
    /// 실제 배치 위치는 연결 Doorway의 목적지 진입점을 사용하며, 이 표식은 반드시 Hallway Room 아래에 둔다.
    /// </summary>
    public sealed class NursingRoomForcedExitPoint : MonoBehaviour
    {
        public Room DestinationRoom => GetComponentInParent<Room>();
    }
}
