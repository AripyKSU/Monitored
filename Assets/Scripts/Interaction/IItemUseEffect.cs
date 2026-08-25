using HauntedHouse.Commands;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// ItemUseTarget이 올바른 아이템을 소비한 뒤 대상에 적용할 즉시 효과의 계약이다.
    /// 시간 기반 연출은 일시정지가 해제된 별도 명령 흐름에서 처리한다.
    /// </summary>
    public interface IItemUseEffect
    {
        void ApplyItemEffect(TargetActionContext context);
    }
}
