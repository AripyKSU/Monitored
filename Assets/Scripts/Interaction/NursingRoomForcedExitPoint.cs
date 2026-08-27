using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// NursingRoom 강제 퇴실 시 Player.MoveTo가 향하는 명시적 지점이다.
    /// 반드시 Hallway Room 아래에 배치해 목적 Room도 함께 식별한다.
    /// </summary>
    public sealed class NursingRoomForcedExitPoint : MonoBehaviour
    {
        public Room DestinationRoom => GetComponentInParent<Room>();
    }
}
