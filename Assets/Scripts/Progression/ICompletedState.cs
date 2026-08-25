namespace HauntedHouse.Progression
{
    /// <summary>
    /// 체크포인트 복원 시 완료·미완료 상태를 적용할 수 있는 월드 상태 구성요소의 계약이다.
    /// 실제 외형, Target 선택 가능 여부와 내부 진행 데이터는 구현체가 소유한다.
    /// </summary>
    public interface ICompletedState
    {
        /// <summary>체크포인트 목록에서 식별할 고유 상태 ID다.</summary>
        string CompletionId { get; }

        /// <summary>현재 상태가 완료되었는지 반환한다.</summary>
        bool IsCompleted { get; }

        /// <summary>체크포인트가 지정한 완료 여부를 월드와 내부 상태에 적용한다.</summary>
        void RestoreCompletedState(bool completed);
    }
}
