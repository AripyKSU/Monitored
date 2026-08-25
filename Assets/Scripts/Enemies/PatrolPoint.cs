using UnityEngine;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 적 순찰 경로의 하나의 목적지와 목적지 도착 후 대기 시간을 소유한다.
    /// 실제 위치와 방은 씬에 배치된 오브젝트의 참조로 관리한다.
    /// </summary>
    public sealed class PatrolPoint : MonoBehaviour
    {
        [SerializeField] private Room room;
        [SerializeField, Min(0f)] private float waitDuration = 1f;

        /// <summary>이 지점의 월드 위치를 반환한다.</summary>
        public Vector3 Position => transform.position;

        /// <summary>이 지점이 소속된 방을 반환한다.</summary>
        public Room Room => room;

        /// <summary>도착 후 대기할 시간을 초 단위로 반환한다.</summary>
        public float WaitDuration => waitDuration;

        /// <summary>
        /// 순찰 지점에 방이 연결되었는지 검사한다.
        /// </summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (room == null)
            {
                error = $"PatrolPoint '{name}'에 Room이 연결되지 않았습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 컴포넌트를 처음 추가할 때 부모 또는 자신에서 방 참조를 보완한다.
        /// </summary>
        private void Reset()
        {
            room = GetComponentInParent<Room>();
        }
    }
}
