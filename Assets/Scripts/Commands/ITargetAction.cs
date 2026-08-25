using System.Collections;

namespace HauntedHouse.Commands
{
    /// <summary>
    /// 플레이어가 Target의 접근 지점에 도착한 뒤 실행할 행동의 공통 계약이다.
    /// 즉시 끝나는 행동부터 UI 입력을 기다리는 행동까지 같은 코루틴 흐름으로 표현한다.
    /// </summary>
    public interface ITargetAction
    {
        /// <summary>
        /// 현재 명령의 실행 정보를 사용해 대상 고유의 행동을 수행한다.
        /// 반환한 코루틴이 끝나야 현재 Target 명령도 완료된 것으로 처리된다.
        /// </summary>
        /// <param name="context">현재 대상과 행동 주체인 플레이어를 담은 실행 정보.</param>
        IEnumerator Execute(TargetActionContext context);
    }
}
