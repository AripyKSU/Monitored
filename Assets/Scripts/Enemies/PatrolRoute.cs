using HauntedHouse.World;
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
        /// 지점 배열, 시작 인덱스와 RoomGraph를 통한 방 연결이 순찰에 유효한지 검사한다.
        /// 각 순찰 지점 사이에는 직접 인접이 아니더라도 Bake된 그래프 경로가 존재해야 한다.
        /// </summary>
        /// <param name="roomGraph">순찰 지점 사이의 방 경로를 확인할 Bake된 RoomGraph.</param>
        /// <param name="error">검증 실패 시 사용자에게 표시할 오류 메시지.</param>
        public bool TryValidateConfiguration(RoomGraph roomGraph, out string error)
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

            // 먼저 모든 순찰 지점 자체의 참조와 설정을 검증해 이후 방 경로 검사에서
            // null 참조가 발생하지 않도록 한다.
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
            }

            // 마지막 지점에서 첫 지점으로 돌아가는 구간까지 포함해 모든 순찰 구간을
            // RoomGraph의 BFS 경로로 검사한다. 따라서 서로 떨어진 방도 중간 방을
            // 거치는 경로만 존재하면 유효한 순찰 경로로 인정된다.
            for (int index = 0; index < Count; index++)
            {
                Room sourceRoom = points[index].Room;
                Room destinationRoom = points[(index + 1) % Count].Room;

                // 같은 방 안의 이동은 NavMeshAgent가 처리하므로 RoomGraph가 필요하지 않다.
                if (sourceRoom == destinationRoom)
                {
                    continue;
                }

                if (roomGraph == null)
                {
                    error = $"PatrolRoute '{name}'에 사용할 RoomGraph가 연결되지 않았습니다.";
                    return false;
                }

                if (!roomGraph.IsBaked)
                {
                    error = $"PatrolRoute '{name}'에 사용할 RoomGraph가 아직 Bake되지 않았습니다.";
                    return false;
                }

                if (!roomGraph.TryFindPath(sourceRoom, destinationRoom, out _))
                {
                    error =
                        $"PatrolRoute '{name}'의 순찰 구간에 RoomGraph 경로가 없습니다: " +
                        $"'{sourceRoom.RoomId}' → '{destinationRoom.RoomId}'";
                    return false;
                }
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
