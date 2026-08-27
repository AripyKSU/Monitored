namespace HauntedHouse.Interaction
{
    /// <summary>
    /// ItemUseTarget이 아이템 선택을 처리한 최종 결과다.
    /// 대상별 효과는 이 결과를 받아 정답·오답·취소를 서로 다른 흐름으로 연결할 수 있다.
    /// </summary>
    public enum ItemUseResult
    {
        /// <summary>아직 결과가 확정되지 않은 초기 상태다.</summary>
        None,

        /// <summary>필요한 아이템을 선택해 효과 적용까지 성공했다.</summary>
        CorrectItem,

        /// <summary>아이템을 선택했지만 필요한 아이템과 일치하지 않았다.</summary>
        WrongItem,

        /// <summary>아이템 선택을 취소했거나 선택 없이 패널이 닫혔다.</summary>
        Cancel
    }
}
