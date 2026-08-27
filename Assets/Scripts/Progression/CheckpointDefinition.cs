using System;
using System.Collections.Generic;
using HauntedHouse.Enemies;
using HauntedHouse.Inventory;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Progression
{
    /// <summary>
    /// 사망 후 복원할 스토리·월드 상태를 씬 안에서 정의한다.
    /// 현재는 디스크 저장을 하지 않으므로 씬 오브젝트를 직접 참조하는 MonoBehaviour로 구성한다.
    /// </summary>
    public sealed class CheckpointDefinition : MonoBehaviour
    {
        [Header("식별")]
        [SerializeField] private string checkpointId;

        [Header("플레이어 복원")]
        [SerializeField] private Room respawnRoom;
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private ItemDefinition[] inventoryItems = Array.Empty<ItemDefinition>();

        [Header("스토리 복원")]
        [SerializeField] private StoryStage storyStage = StoryStage.None;
        [SerializeField] private string[] progressFlags = Array.Empty<string>();
        [SerializeField] private CheckpointCompletedState[] completedStates = Array.Empty<CheckpointCompletedState>();

        [Header("문 복원")]
        [SerializeField] private CheckpointDoorwayState[] doorwayStates = Array.Empty<CheckpointDoorwayState>();

        [Header("적 복원")]
        [SerializeField] private CheckpointEnemyState[] enemyStates = Array.Empty<CheckpointEnemyState>();
        [SerializeField] private RoomGraph roomGraph;

        /// <summary>체크포인트 고유 ID다.</summary>
        public string CheckpointId => checkpointId;

        /// <summary>플레이어가 부활할 논리 방이다.</summary>
        public Room RespawnRoom => respawnRoom;

        /// <summary>플레이어가 부활할 NavMesh 위치와 방향이다.</summary>
        public Transform RespawnPoint => respawnPoint;

        /// <summary>복원 시 교체할 고유 아이템 목록이다.</summary>
        public ItemDefinition[] InventoryItems => inventoryItems;

        /// <summary>복원 시 적용할 큰 스토리 진행 구간이다.</summary>
        public StoryStage StoryStage => storyStage;

        /// <summary>복원 시 적용할 스토리 flag 목록이다.</summary>
        public string[] ProgressFlags => progressFlags;

        /// <summary>복원 시 적용할 대상·퍼즐 완료 상태 목록이다.</summary>
        public CheckpointCompletedState[] CompletedStates => completedStates;

        /// <summary>복원 시 적용할 문 잠금 상태 목록이다.</summary>
        public CheckpointDoorwayState[] DoorwayStates => doorwayStates;

        /// <summary>복원 시 적용할 적 상태 목록이다.</summary>
        public CheckpointEnemyState[] EnemyStates => enemyStates;

        /// <summary>
        /// 체크포인트의 씬 참조와 고유 데이터를 검증한다.
        /// 정상 플레이에서 발생할 수 없는 누락은 런타임 분기가 아니라 스테이지 오류로 기록한다.
        /// </summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (string.IsNullOrWhiteSpace(checkpointId))
            {
                error = $"CheckpointDefinition '{name}'의 CheckpointId가 비어 있습니다.";
                return false;
            }

            if (respawnRoom == null || respawnPoint == null)
            {
                error = $"CheckpointDefinition '{name}'의 부활 방 또는 위치가 누락됐습니다.";
                return false;
            }

            Room respawnPointRoom = respawnPoint.GetComponentInParent<Room>();
            if (respawnPointRoom != respawnRoom)
            {
                error = $"CheckpointDefinition '{name}'의 RespawnPoint가 RespawnRoom 아래에 있지 않습니다.";
                return false;
            }

            if (!ValidateItems(out error) || !ValidateFlags(out error) ||
                !ValidateCompletedStates(out error) || !ValidateDoorwayStates(out error) ||
                !ValidateEnemyStates(out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>체크포인트 아이템이 모두 유효하고 중복되지 않는지 검사한다.</summary>
        private bool ValidateItems(out string error)
        {
            for (int index = 0; index < inventoryItems.Length; index++)
            {
                ItemDefinition item = inventoryItems[index];
                if (item == null)
                {
                    error = $"CheckpointDefinition '{name}'의 아이템 참조가 비어 있습니다.";
                    return false;
                }

                if (!item.TryValidate(out error))
                {
                    return false;
                }

                for (int duplicateIndex = index + 1; duplicateIndex < inventoryItems.Length; duplicateIndex++)
                {
                    if (inventoryItems[duplicateIndex] != null &&
                        string.Equals(item.ItemId, inventoryItems[duplicateIndex].ItemId, StringComparison.Ordinal))
                    {
                        error = $"CheckpointDefinition '{name}'에 ItemId '{item.ItemId}'가 중복됩니다.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>체크포인트 flag가 비어 있지 않고 중복되지 않는지 검사한다.</summary>
        private bool ValidateFlags(out string error)
        {
            for (int index = 0; index < progressFlags.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(progressFlags[index]))
                {
                    error = $"CheckpointDefinition '{name}'의 ProgressFlags에 빈 값이 있습니다.";
                    return false;
                }

                for (int duplicateIndex = index + 1; duplicateIndex < progressFlags.Length; duplicateIndex++)
                {
                    if (string.Equals(progressFlags[index], progressFlags[duplicateIndex], StringComparison.Ordinal))
                    {
                        error = $"CheckpointDefinition '{name}'의 flag '{progressFlags[index]}'가 중복됩니다.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>완료 상태 참조가 ICompletedState를 구현하고 중복되지 않는지 검사한다.</summary>
        private bool ValidateCompletedStates(out string error)
        {
            for (int index = 0; index < completedStates.Length; index++)
            {
                if (!completedStates[index].TryValidate(out error))
                {
                    return false;
                }

                for (int duplicateIndex = index + 1; duplicateIndex < completedStates.Length; duplicateIndex++)
                {
                    if (completedStates[index].CompletionId == completedStates[duplicateIndex].CompletionId)
                    {
                        error = $"CheckpointDefinition '{name}'의 완료 상태 ID가 중복됩니다: {completedStates[index].CompletionId}";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>문 복원 목록에 빈 참조나 같은 문의 중복 정의가 없는지 검사한다.</summary>
        private bool ValidateDoorwayStates(out string error)
        {
            var doorways = new HashSet<Doorway>();
            foreach (CheckpointDoorwayState state in doorwayStates)
            {
                if (state == null || state.Doorway == null)
                {
                    error = $"CheckpointDefinition '{name}'의 DoorwayStates에 빈 참조가 있습니다.";
                    return false;
                }

                if (!doorways.Add(state.Doorway))
                {
                    error = $"CheckpointDefinition '{name}'에 Doorway '{state.Doorway.name}'이 중복됩니다.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>적 상태의 참조·경로·스폰 지점을 검사한다.</summary>
        private bool ValidateEnemyStates(out string error)
        {
            for (int index = 0; index < enemyStates.Length; index++)
            {
                if (!enemyStates[index].TryValidate(roomGraph, out error))
                {
                    return false;
                }

                for (int duplicateIndex = index + 1; duplicateIndex < enemyStates.Length; duplicateIndex++)
                {
                    if (enemyStates[index].Enemy == enemyStates[duplicateIndex].Enemy)
                    {
                        error = $"CheckpointDefinition '{name}'에 같은 적이 중복 등록됐습니다.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>컴포넌트를 처음 추가했을 때 자식 부활 지점을 찾는다.</summary>
        private void Reset()
        {
            respawnRoom = GetComponentInParent<Room>();
            respawnPoint = transform.Find("RespawnPoint");
            roomGraph = FindFirstObjectByType<RoomGraph>();
        }
    }

    /// <summary>체크포인트가 특정 대상·퍼즐에 적용할 완료 여부를 보관한다.</summary>
    [Serializable]
    public sealed class CheckpointCompletedState
    {
        [SerializeField] private MonoBehaviour participant;
        [SerializeField] private bool completed = true;

        /// <summary>복원 대상 컴포넌트를 ICompletedState로 반환한다.</summary>
        public ICompletedState Participant => participant as ICompletedState;

        /// <summary>완료 상태의 고유 ID를 반환한다.</summary>
        public string CompletionId => Participant != null ? Participant.CompletionId : string.Empty;

        /// <summary>복원할 완료 여부를 반환한다.</summary>
        public bool Completed => completed;

        /// <summary>참조가 완료 상태 계약을 구현했는지 검사한다.</summary>
        public bool TryValidate(out string error)
        {
            if (Participant == null)
            {
                error = "CheckpointCompletedState의 Participant가 ICompletedState를 구현하지 않았습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Participant.CompletionId))
            {
                error = "CheckpointCompletedState Participant의 CompletionId가 비어 있습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    /// <summary>체크포인트 복원 시 Doorway 하나에 적용할 잠금 상태다.</summary>
    [Serializable]
    public sealed class CheckpointDoorwayState
    {
        [SerializeField] private Doorway doorway;
        [SerializeField] private bool locked;

        /// <summary>복원할 Doorway를 반환한다.</summary>
        public Doorway Doorway => doorway;

        /// <summary>복원 후 문을 잠글지 반환한다.</summary>
        public bool Locked => locked;
    }

    /// <summary>체크포인트에서 적 하나를 복원하기 위한 경로·위치·활성 상태다.</summary>
    [Serializable]
    public sealed class CheckpointEnemyState
    {
        [SerializeField] private EnemyController enemy;
        [SerializeField] private bool active = true;
        [SerializeField] private PatrolRoute patrolRoute;
        [SerializeField] private int routeStartIndex;

        /// <summary>복원 대상 적이다.</summary>
        public EnemyController Enemy => enemy;

        /// <summary>복원 후 적을 활성화할지 반환한다.</summary>
        public bool Active => active;

        /// <summary>복원 후 적용할 순찰 경로다.</summary>
        public PatrolRoute PatrolRoute => patrolRoute;

        /// <summary>복원 후 순찰을 시작할 경로 인덱스다.</summary>
        public int RouteStartIndex => routeStartIndex;

        /// <summary>적 상태 참조와 경로 설정을 검사한다. 스폰 위치는 항상 경로의 첫 지점으로 계산한다.</summary>
        public bool TryValidate(RoomGraph roomGraph, out string error)
        {
            if (enemy == null)
            {
                error = "CheckpointEnemyState의 Enemy가 비어 있습니다.";
                return false;
            }

            if (patrolRoute == null)
            {
                error = $"CheckpointEnemyState '{enemy.name}'의 PatrolRoute가 비어 있습니다.";
                return false;
            }

            if (!patrolRoute.TryValidateConfiguration(roomGraph, out error))
            {
                return false;
            }

            if (routeStartIndex < 0 || routeStartIndex >= patrolRoute.Count)
            {
                error = $"CheckpointEnemyState '{enemy.name}'의 routeStartIndex가 범위를 벗어났습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
