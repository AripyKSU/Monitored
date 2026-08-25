using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// Doorway 한쪽 경계의 Trigger 충돌을 감지해 어느 방으로 진입했는지 Doorway에 전달하는 부속 컴포넌트다.
    /// 문짝과 분리된 Trigger이므로 문 Visual에는 Collider가 필요하지 않다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DoorwaySensor : MonoBehaviour
    {
        [SerializeField] private Doorway doorway;
        [SerializeField] private Room destinationRoom;

        /// <summary>
        /// Doorway가 양쪽 Sensor에 자신과 목적지 방을 연결한다.
        /// </summary>
        public void Configure(Doorway owner, Room destination)
        {
            doorway = owner;
            destinationRoom = destination;

            BoxCollider trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
        }

        /// <summary>
        /// 플레이어나 이후 추가될 적의 Collider가 경계에 들어오면 Doorway에 진입 사실을 알린다.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (doorway != null && destinationRoom != null)
            {
                doorway.NotifyPassageStarted(other, destinationRoom);
            }
        }
    }
}
