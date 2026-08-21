namespace HauntedHouse.Commands
{
    /// <summary>
    /// ITargetAction 실행 시 필요한 최소 런타임 정보를 전달하는 불변 객체다.
    /// 이동 구현체나 UI 같은 세부 시스템을 노출하지 않고 대상과 플레이어만 전달한다.
    /// </summary>
    public sealed class TargetActionContext
    {
        /// <summary>
        /// 행동을 실행하는 현재 Target이다.
        /// </summary>
        public Target Target { get; }

        /// <summary>
        /// Target에 도착해 행동을 수행하는 플레이어다.
        /// </summary>
        public Player Player { get; }

        /// <summary>
        /// Target 행동 실행에 필요한 문맥을 생성한다.
        /// </summary>
        /// <param name="target">현재 실행 중인 대상.</param>
        /// <param name="player">행동을 수행하는 플레이어.</param>
        public TargetActionContext(Target target, Player player)
        {
            Target = target;
            Player = player;
        }
    }
}
