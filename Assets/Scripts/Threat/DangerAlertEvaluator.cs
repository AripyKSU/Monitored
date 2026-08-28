using HauntedHouse.World;

namespace HauntedHouse.Threat
{
    /// <summary>
    /// 적 한 명의 방 상태를 위험경보 단계로 변환하는 순수 계산 클래스다.
    /// Unity 생명주기나 UI를 참조하지 않아 단위 테스트와 규칙 검증에 사용할 수 있다.
    /// </summary>
    public static class DangerAlertEvaluator
    {
        /// <summary>
        /// 현재 플레이어 방과 적의 현재·다음 방을 기준으로 경보 단계를 계산한다.
        /// </summary>
        /// <param name="state">계산에 필요한 플레이어와 적의 방 상태.</param>
        /// <returns>해당 적이 발생시키는 위험경보 단계.</returns>
        public static DangerAlertLevel Evaluate(DangerAlertState state)
        {
            // 비활성 적이나 방 정보가 없는 적은 경보 계산에서 제외한다.
            if (!state.IsEnemyActive || state.PlayerRoom == null || state.EnemyRoom == null)
            {
                return DangerAlertLevel.None;
            }

            // 추격 또는 강제 은신 연출이 시작된 뒤에는 여러 방을 이동하는 동안에도 열린 눈 경보를 유지한다.
            // 적이 이미 플레이어 방에 있거나 플레이어 방으로 이동을 결정한 순간도 긴급이다.
            if (state.IsEnemyChasing || state.IsEnemyInScriptedSequence ||
                state.EnemyRoom == state.PlayerRoom ||
                state.NextDestinationRoom == state.PlayerRoom)
            {
                return DangerAlertLevel.Emergency;
            }

            // 현재 방이 직접 연결된 인접 방이면 다음 행동을 준비하도록 주의만 표시한다.
            return state.PlayerRoom.IsAdjacentTo(state.EnemyRoom)
                ? DangerAlertLevel.Caution
                : DangerAlertLevel.None;
        }
    }
}
