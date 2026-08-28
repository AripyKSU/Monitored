using HauntedHouse.World;

namespace HauntedHouse.Threat
{
    /// <summary>
    /// 하나의 적에 대해 위험경보 계산에 필요한 현재 방 정보를 묶는다.
    /// 경보 단계 자체는 포함하지 않으며 DangerAlertEvaluator가 이 입력을 판정한다.
    /// </summary>
    public readonly struct DangerAlertState
    {
        /// <summary>플레이어가 논리적으로 현재 있는 방을 반환한다.</summary>
        public Room PlayerRoom { get; }

        /// <summary>적이 논리적으로 현재 있는 방을 반환한다.</summary>
        public Room EnemyRoom { get; }

        /// <summary>적이 현재 이동 대상으로 결정한 다음 방을 반환한다.</summary>
        public Room NextDestinationRoom { get; }

        /// <summary>적이 현재 경보 계산 대상인지 반환한다.</summary>
        public bool IsEnemyActive { get; }

        /// <summary>적이 현재 플레이어를 추격 중인지 반환한다.</summary>
        public bool IsEnemyChasing { get; }

        /// <summary>적이 플레이어에게 위협을 주는 스토리 연출 중인지 반환한다.</summary>
        public bool IsEnemyInScriptedSequence { get; }

        /// <summary>
        /// 위험경보 계산에 필요한 플레이어·적 상태를 생성한다.
        /// </summary>
        public DangerAlertState(
            Room playerRoom,
            Room enemyRoom,
            Room nextDestinationRoom,
            bool isEnemyActive,
            bool isEnemyChasing,
            bool isEnemyInScriptedSequence)
        {
            PlayerRoom = playerRoom;
            EnemyRoom = enemyRoom;
            NextDestinationRoom = nextDestinationRoom;
            IsEnemyActive = isEnemyActive;
            IsEnemyChasing = isEnemyChasing;
            IsEnemyInScriptedSequence = isEnemyInScriptedSequence;
        }
    }
}
