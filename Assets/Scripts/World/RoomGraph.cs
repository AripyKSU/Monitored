using System;
using System.Collections.Generic;
using HauntedHouse.Enemies;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// 에디터에서 방과 Doorway 연결을 Bake하고 런타임 BFS 경로 탐색을 제공하는 고정 그래프다.
    /// 게임 실행 중에는 씬을 탐색하거나 그래프를 다시 만들지 않고 Bake된 데이터만 사용한다.
    /// </summary>
    public sealed class RoomGraph : MonoBehaviour
    {
        [SerializeField] private RoomGraphNodeData[] bakedNodes = Array.Empty<RoomGraphNodeData>();

        private readonly Dictionary<Room, RoomGraphNodeData> nodesByRoom =
            new Dictionary<Room, RoomGraphNodeData>();

        /// <summary>그래프가 에디터에서 Bake된 상태인지 반환한다.</summary>
        public bool IsBaked => bakedNodes != null && bakedNodes.Length > 0;

        /// <summary>에디터 그래프 표시기가 읽을 수 있는 Bake 노드 목록을 반환한다.</summary>
        public IReadOnlyList<RoomGraphNodeData> BakedNodes => bakedNodes;

        /// <summary>씬이 시작될 때 Bake된 노드로 런타임 조회 테이블을 만든다.</summary>
        private void Awake()
        {
            BuildRuntimeLookup();
        }

        /// <summary>
        /// 현재 방에서 목적지 방까지 Doorway를 순서대로 통과하는 BFS 경로를 찾는다.
        /// 방이 이미 같으면 빈 경로를 성공으로 반환한다.
        /// </summary>
        /// <param name="startRoom">경로를 시작할 방.</param>
        /// <param name="destinationRoom">최종 목적지 방.</param>
        /// <param name="path">직접 통과해야 할 Doorway 순서.</param>
        /// <returns>경로를 찾았으면 true.</returns>
        public bool TryFindPath(
            Room startRoom,
            Room destinationRoom,
            out List<RoomGraphStep> path)
        {
            path = new List<RoomGraphStep>();

            if (startRoom == null || destinationRoom == null)
            {
                return false;
            }

            if (startRoom == destinationRoom)
            {
                return true;
            }

            BuildRuntimeLookup();
            if (!nodesByRoom.ContainsKey(startRoom) || !nodesByRoom.ContainsKey(destinationRoom))
            {
                return false;
            }

            var visited = new HashSet<Room> { startRoom };
            var previousRoom = new Dictionary<Room, Room>();
            var previousDoorway = new Dictionary<Room, Doorway>();
            var pendingRooms = new Queue<Room>();
            pendingRooms.Enqueue(startRoom);

            while (pendingRooms.Count > 0)
            {
                Room currentRoom = pendingRooms.Dequeue();
                RoomGraphNodeData currentNode = nodesByRoom[currentRoom];

                foreach (RoomGraphEdgeData edge in currentNode.Edges)
                {
                    if (edge.DestinationRoom == null || edge.Doorway == null || !visited.Add(edge.DestinationRoom))
                    {
                        continue;
                    }

                    previousRoom[edge.DestinationRoom] = currentRoom;
                    previousDoorway[edge.DestinationRoom] = edge.Doorway;

                    if (edge.DestinationRoom == destinationRoom)
                    {
                        BuildPath(startRoom, destinationRoom, previousRoom, previousDoorway, path);
                        return true;
                    }

                    pendingRooms.Enqueue(edge.DestinationRoom);
                }
            }

            path.Clear();
            return false;
        }

        /// <summary>
        /// 씬의 Room과 Doorway를 읽어 그래프 데이터를 Bake한다.
        /// 이 메서드는 RoomGraph 커스텀 Inspector의 Bake 버튼에서 호출한다.
        /// </summary>
        /// <param name="error">실패 시 Bake할 수 없는 이유.</param>
        /// <returns>그래프 데이터가 정상적으로 생성됐으면 true.</returns>
        public bool BakeFromScene(out string error)
        {
            Room[] rooms = FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Doorway[] doorways = FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (rooms.Length == 0)
            {
                error = "씬에 Room이 없습니다.";
                return false;
            }

            Array.Sort(rooms, CompareRooms);

            var roomSet = new HashSet<Room>(rooms);
            var roomIds = new HashSet<string>(StringComparer.Ordinal);
            var edgeLists = new Dictionary<Room, List<RoomGraphEdgeData>>();
            foreach (Room room in rooms)
            {
                if (room == null || !roomSet.Contains(room))
                {
                    error = "씬의 Room 목록에 유효하지 않은 참조가 있습니다.";
                    return false;
                }

                if (!room.TryValidateConfiguration(out error))
                {
                    return false;
                }

                if (!roomIds.Add(room.RoomId))
                {
                    error = $"RoomId가 중복됐습니다: '{room.RoomId}'";
                    return false;
                }

                edgeLists.Add(room, new List<RoomGraphEdgeData>());
            }

            foreach (Doorway doorway in doorways)
            {
                error = string.Empty;
                if (doorway == null)
                {
                    error = "씬의 Doorway 목록에 빈 참조가 있습니다.";
                    return false;
                }

                if (!doorway.TryValidateConfiguration(out error))
                {
                    return false;
                }

                if (!roomSet.Contains(doorway.RoomA) || !roomSet.Contains(doorway.RoomB))
                {
                    error = $"Doorway '{doorway.name}'이 Bake 대상 Room 목록 밖의 방을 참조합니다.";
                    return false;
                }

                if (!TryAddEdge(edgeLists[doorway.RoomA], doorway.RoomB, doorway, out error) ||
                    !TryAddEdge(edgeLists[doorway.RoomB], doorway.RoomA, doorway, out error))
                {
                    return false;
                }
            }

            var newNodes = new RoomGraphNodeData[rooms.Length];
            for (int index = 0; index < rooms.Length; index++)
            {
                List<RoomGraphEdgeData> edges = edgeLists[rooms[index]];
                edges.Sort(CompareEdges);
                // 그래프 위치는 Room 루트를 이동하지 않고 현재 방의 전용 기준점에서 캡처한다.
                // 따라서 Room 아래에 있는 물리 메쉬와 Collider의 위치에는 영향을 주지 않는다.
                newNodes[index] = new RoomGraphNodeData(
                    rooms[index],
                    rooms[index].GraphPosition,
                    edges.ToArray());
            }

            bakedNodes = newNodes;
            BuildRuntimeLookup();
            error = string.Empty;
            return true;
        }

        /// <summary>잘못된 Bake 결과를 제거해 그래프를 다시 구성할 수 있는 빈 상태로 만든다.</summary>
        public void ClearBakedGraph()
        {
            bakedNodes = Array.Empty<RoomGraphNodeData>();
            nodesByRoom.Clear();
        }

        /// <summary>직접 연결 중복을 검사하면서 방향성 간선을 추가한다.</summary>
        private static bool TryAddEdge(
            List<RoomGraphEdgeData> edges,
            Room destinationRoom,
            Doorway doorway,
            out string error)
        {
            foreach (RoomGraphEdgeData edge in edges)
            {
                if (edge.DestinationRoom == destinationRoom)
                {
                    error =
                        $"같은 두 방 사이에 Doorway가 여러 개입니다: " +
                        $"'{edge.Doorway.name}', '{doorway.name}'";
                    return false;
                }
            }

            edges.Add(new RoomGraphEdgeData(destinationRoom, doorway));
            error = string.Empty;
            return true;
        }

        /// <summary>방 이름을 기준으로 Bake 순서를 고정해 BFS 결과를 재현 가능하게 만든다.</summary>
        private static int CompareRooms(Room left, Room right)
        {
            return string.CompareOrdinal(left != null ? left.RoomId : string.Empty, right != null ? right.RoomId : string.Empty);
        }

        /// <summary>목적지 방 이름과 Doorway 이름을 기준으로 간선 순서를 고정한다.</summary>
        private static int CompareEdges(RoomGraphEdgeData left, RoomGraphEdgeData right)
        {
            int roomComparison = CompareRooms(left.DestinationRoom, right.DestinationRoom);
            return roomComparison != 0
                ? roomComparison
                : string.CompareOrdinal(left.Doorway != null ? left.Doorway.name : string.Empty,
                    right.Doorway != null ? right.Doorway.name : string.Empty);
        }

        /// <summary>이전 방과 Doorway 기록을 목적지에서 시작점 방향으로 되짚어 경로를 만든다.</summary>
        private static void BuildPath(
            Room startRoom,
            Room destinationRoom,
            Dictionary<Room, Room> previousRoom,
            Dictionary<Room, Doorway> previousDoorway,
            List<RoomGraphStep> path)
        {
            var reversedPath = new List<RoomGraphStep>();
            Room currentRoom = destinationRoom;
            while (currentRoom != startRoom)
            {
                Room sourceRoom = previousRoom[currentRoom];
                reversedPath.Add(new RoomGraphStep(sourceRoom, currentRoom, previousDoorway[currentRoom]));
                currentRoom = sourceRoom;
            }

            for (int index = reversedPath.Count - 1; index >= 0; index--)
            {
                path.Add(reversedPath[index]);
            }
        }

        /// <summary>Bake된 배열을 Dictionary로 변환해 런타임 BFS가 씬 검색 없이 동작하게 한다.</summary>
        private void BuildRuntimeLookup()
        {
            nodesByRoom.Clear();
            if (bakedNodes == null)
            {
                return;
            }

            foreach (RoomGraphNodeData node in bakedNodes)
            {
                if (node != null && node.Room != null)
                {
                    nodesByRoom[node.Room] = node;
                }
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 같은 오브젝트의 RectTransform은 사용하지 않는다.</summary>
        private void Reset()
        {
            bakedNodes = Array.Empty<RoomGraphNodeData>();
        }
    }

    /// <summary>
    /// Bake된 그래프에서 하나의 방 간선을 통과하기 위해 필요한 출발 방·도착 방·Doorway를 묶는다.
    /// </summary>
    public readonly struct RoomGraphStep
    {
        /// <summary>간선 출발 방을 반환한다.</summary>
        public Room SourceRoom { get; }

        /// <summary>간선 도착 방을 반환한다.</summary>
        public Room DestinationRoom { get; }

        /// <summary>두 방을 연결하는 Doorway를 반환한다.</summary>
        public Doorway Doorway { get; }

        /// <summary>그래프 간선 데이터를 생성한다.</summary>
        public RoomGraphStep(Room sourceRoom, Room destinationRoom, Doorway doorway)
        {
            SourceRoom = sourceRoom;
            DestinationRoom = destinationRoom;
            Doorway = doorway;
        }
    }

    /// <summary>RoomGraph가 직렬화하는 한 방향의 Doorway 간선 데이터다.</summary>
    [Serializable]
    public struct RoomGraphEdgeData
    {
        [SerializeField] private Room destinationRoom;
        [SerializeField] private Doorway doorway;

        /// <summary>이 간선이 도착하는 방을 반환한다.</summary>
        public Room DestinationRoom => destinationRoom;

        /// <summary>이 간선이 사용하는 Doorway를 반환한다.</summary>
        public Doorway Doorway => doorway;

        /// <summary>직렬화할 간선 데이터를 생성한다.</summary>
        public RoomGraphEdgeData(Room destinationRoom, Doorway doorway)
        {
            this.destinationRoom = destinationRoom;
            this.doorway = doorway;
        }
    }

    /// <summary>Room 하나와 해당 방에서 출발하는 Bake 간선 목록을 직렬화한다.</summary>
    [Serializable]
    public sealed class RoomGraphNodeData
    {
        [SerializeField] private Room room;
        [SerializeField] private Vector3 graphPosition;
        [SerializeField] private bool hasBakedGraphPosition;
        [SerializeField] private RoomGraphEdgeData[] edges = Array.Empty<RoomGraphEdgeData>();

        /// <summary>이 노드가 나타내는 Room을 반환한다.</summary>
        public Room Room => room;

        /// <summary>
        /// 이 노드가 표시될 월드 위치를 반환한다.
        /// 이전 버전에서 베이크된 데이터는 저장 위치가 없으므로 현재 Room 기준점을 사용한다.
        /// </summary>
        public Vector3 GraphPosition => hasBakedGraphPosition
            ? graphPosition
            : room != null ? room.GraphPosition : Vector3.zero;

        /// <summary>이 Room에서 직접 이동할 수 있는 간선 목록을 반환한다.</summary>
        public IReadOnlyList<RoomGraphEdgeData> Edges => edges;

        /// <summary>Room, 베이크 시점의 그래프 위치, 간선 배열을 가진 Bake 노드를 생성한다.</summary>
        public RoomGraphNodeData(Room room, Vector3 graphPosition, RoomGraphEdgeData[] edges)
        {
            this.room = room;
            this.graphPosition = graphPosition;
            hasBakedGraphPosition = true;
            this.edges = edges ?? Array.Empty<RoomGraphEdgeData>();
        }
    }
}
