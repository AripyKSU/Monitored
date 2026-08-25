using System;
using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// 두 Room 사이의 고정 연결, 방향별 통과 지점, 공용 잠금 상태와 경계 Trigger를 소유한다.
    /// 플레이어나 적이 Sensor를 통과하면 방 진입 방향을 계산해 PassageStarted 이벤트로 알린다.
    /// </summary>
    public sealed class Doorway : MonoBehaviour
    {
        [Header("연결된 방")]
        [SerializeField] private Room roomA;
        [SerializeField] private Room roomB;

        [Header("문 통과 경계")]
        [SerializeField] private Transform passageStartFromA;
        [SerializeField] private Transform passageStartFromB;

        [Header("방향별 DoorTarget")]
        [Tooltip("RoomA에서 이 문으로 접근할 때 사용하는 DoorTarget입니다.")]
        [SerializeField] private DoorTarget doorTargetFromA;
        [Tooltip("RoomB에서 이 문으로 접근할 때 사용하는 DoorTarget입니다.")]
        [SerializeField] private DoorTarget doorTargetFromB;

        [Header("문 상태와 감지")]
        [SerializeField] private DoorController doorController;
        [SerializeField] private DoorwaySensor sensorEnteringA;
        [SerializeField] private DoorwaySensor sensorEnteringB;
        [SerializeField] private bool locked;

        // DoorTarget의 Inspector 참조에서 읽은 접근 지점을 런타임에 재사용한다.
        // Inspector에는 원본 DoorTarget만 노출해 중복 Transform 참조가 생기지 않게 한다.
        private Transform approachPointFromA;
        private Transform approachPointFromB;
        private bool approachPointsCached;

        public Room RoomA => roomA;
        public Room RoomB => roomB;
        public bool IsLocked => locked;
        public bool IsOpen => doorController != null && doorController.IsOpen;

        /// <summary>
        /// 경계 Trigger를 통과한 주체와 통과 방향을 알린다.
        /// DoorTarget은 플레이어 이벤트만 소비하고 이후 적 시스템도 같은 이벤트를 사용할 수 있다.
        /// </summary>
        public event Action<GameObject, DoorTraversal> PassageStarted;

        /// <summary>
        /// 양쪽 Sensor에 Doorway와 각 Sensor가 의미하는 목적지 방을 연결한다.
        /// </summary>
        private void Awake()
        {
            CacheApproachPoints();
            sensorEnteringA?.Configure(this, roomA);
            sensorEnteringB?.Configure(this, roomB);
        }

        /// <summary>
        /// Inspector에서 DoorTarget 참조가 변경되면 접근 지점 캐시를 갱신한다.
        /// </summary>
        private void OnValidate()
        {
            CacheApproachPoints();
        }

        /// <summary>
        /// 현재 방을 기준으로 양방향 통과 데이터를 반환한다.
        /// </summary>
        public bool TryGetTraversal(Room sourceRoom, out DoorTraversal traversal)
        {
            // 에디터 Bake처럼 Awake 이전에 호출되는 경우에도 캐시를 준비한다.
            if (!approachPointsCached)
            {
                CacheApproachPoints();
            }

            if (sourceRoom == roomA)
            {
                traversal = new DoorTraversal(
                    roomA,
                    roomB,
                    approachPointFromA,
                    passageStartFromA,
                    approachPointFromB);
                return IsTraversalConfigured(traversal);
            }

            if (sourceRoom == roomB)
            {
                traversal = new DoorTraversal(
                    roomB,
                    roomA,
                    approachPointFromB,
                    passageStartFromB,
                    approachPointFromA);
                return IsTraversalConfigured(traversal);
            }

            traversal = default;
            return false;
        }

        /// <summary>
        /// 방향별 DoorTarget이 소유한 Target.ApproachPoint를 private 캐시로 복사한다.
        /// </summary>
        private void CacheApproachPoints()
        {
            approachPointFromA = doorTargetFromA != null
                ? doorTargetFromA.ApproachPoint
                : null;
            approachPointFromB = doorTargetFromB != null
                ? doorTargetFromB.ApproachPoint
                : null;
            approachPointsCached = true;
        }

        /// <summary>
        /// 아이템 사용이나 스토리 진행 결과에 따라 공용 문 잠금 상태를 변경한다.
        /// </summary>
        public void SetLocked(bool value)
        {
            locked = value;
        }

        /// <summary>
        /// DoorController의 열기 연출 완료까지 기다린다.
        /// </summary>
        public IEnumerator Open()
        {
            if (doorController == null)
            {
                GameLog.LogError($"Doorway '{name}'에 DoorController가 연결되지 않았습니다.", this);
                yield break;
            }

            yield return doorController.Open();
        }

        /// <summary>
        /// DoorController의 닫기 연출 완료까지 기다린다.
        /// </summary>
        public IEnumerator Close()
        {
            if (doorController == null)
            {
                GameLog.LogError($"Doorway '{name}'에 DoorController가 연결되지 않았습니다.", this);
                yield break;
            }

            yield return doorController.Close();
        }

        /// <summary>
        /// 통과 실패 시 문짝을 즉시 닫힌 회전으로 복구한다.
        /// </summary>
        public void ForceClosed()
        {
            doorController?.ForceClosed();
        }

        /// <summary>
        /// Sensor가 감지한 Collider의 대표 GameObject와 목적지 방향을 계산해 이벤트를 발생시킨다.
        /// </summary>
        public void NotifyPassageStarted(Collider actorCollider, Room destinationRoom)
        {
            if (actorCollider == null || destinationRoom == null)
            {
                return;
            }

            Room sourceRoom = destinationRoom == roomA ? roomB : destinationRoom == roomB ? roomA : null;
            if (!TryGetTraversal(sourceRoom, out DoorTraversal traversal))
            {
                GameLog.LogError($"Doorway '{name}'이 Sensor 목적지 방의 통과 방향을 찾지 못했습니다.", this);
                return;
            }

            Player player = actorCollider.GetComponentInParent<Player>();
            GameObject actor = player != null ? player.gameObject : actorCollider.transform.root.gameObject;
            NotifyPassageStarted(actor, traversal);
        }

        /// <summary>
        /// 물리 Sensor를 거치지 않는 Actor가 통과를 확정할 때도 플레이어와 같은 이벤트를 발생시킨다.
        /// 적의 명시적 DoorTraversal이 방 변경 규칙을 중복 구현하지 않도록 Doorway가 이벤트 발행을 소유한다.
        /// </summary>
        /// <param name="actor">통과를 시작한 Actor의 루트 GameObject.</param>
        /// <param name="traversal">Actor가 통과한 방향 정보.</param>
        public void NotifyPassageStarted(GameObject actor, DoorTraversal traversal)
        {
            if (actor == null || traversal.DestinationRoom == null)
            {
                return;
            }

            PassageStarted?.Invoke(actor, traversal);
        }

        /// <summary>
        /// DoorTarget 실행 전에 양쪽 방, 방향별 DoorTarget, 통과 지점, 문 연출 참조가 모두 있는지 검사한다.
        /// </summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (roomA == null || roomB == null || roomA == roomB)
            {
                error = $"Doorway '{name}'의 양쪽 Room 구성이 올바르지 않습니다.";
                return false;
            }

            if (!roomA.IsAdjacentTo(roomB) || !roomB.IsAdjacentTo(roomA))
            {
                error = $"Doorway '{name}'의 양쪽 Room이 서로 인접 목록에 등록되지 않았습니다.";
                return false;
            }

            if (!TryGetTraversal(roomA, out _) || !TryGetTraversal(roomB, out _))
            {
                error = $"Doorway '{name}'의 방향별 DoorTarget 또는 통과 지점이 누락됐습니다.";
                return false;
            }

            if (doorController == null || sensorEnteringA == null || sensorEnteringB == null)
            {
                error = $"Doorway '{name}'의 DoorController 또는 Sensor가 연결되지 않았습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 방향별 통과 구조체에 목적지와 접근, 경계, 목적지 지점이 모두 있는지 확인한다.
        /// </summary>
        private static bool IsTraversalConfigured(DoorTraversal traversal)
        {
            return traversal.SourceRoom != null &&
                   traversal.DestinationRoom != null &&
                   traversal.EntryPoint != null &&
                   traversal.PassageStart != null &&
                   traversal.DestinationEntryPoint != null;
        }
    }
}
