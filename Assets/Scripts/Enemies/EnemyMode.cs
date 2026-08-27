namespace HauntedHouse.Enemies
{
    /// <summary>
    /// 기존 EnemyState의 세부 이동 상태를 스토리 흐름 관점에서 묶은 상위 적 모드다.
    /// Patrol 안에는 기존 Patrolling·Waiting 상태가 포함되며 기존 AI 계약은 유지한다.
    /// </summary>
    public enum EnemyMode
    {
        /// <summary>적이 비활성화되어 이동·추격하지 않는 상태다.</summary>
        Inactive,

        /// <summary>스토리 연출이 적의 기존 이동을 잠시 소유한 상태다.</summary>
        ScriptedSequence,

        /// <summary>기존 PatrolRoute를 따라 이동하거나 순찰 지점에서 대기하는 상태다.</summary>
        Patrol,

        /// <summary>기존 EnemyChaseSystem을 통해 플레이어를 추적하는 상태다.</summary>
        Chase
    }
}
