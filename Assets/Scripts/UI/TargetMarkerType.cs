namespace HauntedHouse.UI
{
    /// <summary>
    /// TargetPresenter가 사용할 Marker의 의미 있는 종류를 정의한다.
    /// 방향이 다른 문도 동일한 Door 종류의 Marker를 공유한다.
    /// </summary>
    public enum TargetMarkerType
    {
        Move,
        Item,
        Hide,
        ItemUse,
        Door
    }
}
