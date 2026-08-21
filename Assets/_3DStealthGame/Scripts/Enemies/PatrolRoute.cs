using UnityEngine;

namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 적이 순서대로 방문할 순찰 지점 목록과 시작 인덱스를 소유한다.
    /// 마지막 지점 다음에는 항상 첫 지점으로 돌아간다.
    /// </summary>
    public sealed class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private PatrolPoint[] points = System.Array.Empty<PatrolPoint>();
        [SerializeField, Min(0)] private int startIndex;

        /// <summary>등록된 순찰 지점 수를 반환한다.</summary>
        public int Count => points != null ? points.Length : 0;

        /// <summary>초기 순찰에 사용할 시작 인덱스를 반환한다.</summary>
        public int StartIndex => startIndex;

        /// <summary>
        /// 지정한 인덱스의 순찰 지점을 반환한다. 호출 전 경로 검증을 완료해야 한다.
        /// </summary>
        public PatrolPoint GetPoint(int index)
        {
            return points[index];
        }

        /// <summary>
        /// 지점 배열, 시작 인덱스와 방 연결이 순찰에 유효한지 검사한다.
        /// </summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (Count == 0)
            {
                error = $"PatrolRoute '{name}'에 PatrolPoint가 없습니다.";
                return false;
            }

            if (startIndex < 0 || startIndex >= Count)
            {
                error = $"PatrolRoute '{name}'의 StartIndex가 범위를 벗어났습니다: {startIndex}";
                return false;
            }

            for (int index = 0; index < Count; index++)
            {
                PatrolPoint point = points[index];
                if (point == null)
                {
                    error = $"PatrolRoute '{name}'의 {index}번째 지점 참조가 비어 있습니다.";
                    return false;
                }

                if (!point.TryValidateConfiguration(out error))
                {
                    error = $"PatrolRoute '{name}'의 {index}번째 지점이 유효하지 않습니다. {error}";
                    return false;
                }

                if (index == 0)
                {
                    continue;
                }

                Room previousRoom = points[index - 1].Room;
                Room currentRoom = point.Room;
                if (previousRoom != currentRoom && !previousRoom.IsAdjacentTo(currentRoom))
                {
                    error =
                        $"PatrolRoute '{name}'의 연속 지점 방이 직접 연결되지 않았습니다: " +
                        $"'{previousRoom.RoomId}' → '{currentRoom.RoomId}'";
                    return false;
                }
            }

            // 마지막 지점에서 첫 지점으로 돌아가는 순환 구간도 직접 이동 가능한지 확인한다.
            Room lastRoom = points[Count - 1].Room;
            Room firstRoom = points[0].Room;
            if (lastRoom != firstRoom && !lastRoom.IsAdjacentTo(firstRoom))
            {
                error =
                    $"PatrolRoute '{name}'의 순환 연결이 유효하지 않습니다: " +
                    $"'{lastRoom.RoomId}' → '{firstRoom.RoomId}'";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 컴포넌트를 처음 추가할 때 자식 순찰 지점을 기본 목록으로 연결한다.
        /// </summary>
        private void Reset()
        {
            points = GetComponentsInChildren<PatrolPoint>(true);
        }
    }
}
