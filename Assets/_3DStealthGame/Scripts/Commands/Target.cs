using UnityEngine;

namespace HauntedHouse.Commands
{
    /// <summary>
    /// 클릭 명령의 목적지가 제공해야 하는 게임 규칙 정보를 소유한다.
    /// 접근 지점, 현재 선택 가능 상태, 도착 후 행동만 관리하며 클릭과 마커 표현은 TargetPresenter에 위임한다.
    /// </summary>
    public sealed class Target : MonoBehaviour
    {
        [Header("명령 목적지")]
        [SerializeField] private Transform approachPoint;
        [SerializeField] private Room ownerRoom;
        [SerializeField] private bool selectable = true;

        [Header("도착 후 행동")]
        [Tooltip("ITargetAction을 구현한 MonoBehaviour를 연결한다.")]
        [SerializeField] private MonoBehaviour actionComponent;

        /// <summary>
        /// 플레이어가 Target 행동을 실행하기 전에 도착해야 하는 지점이다.
        /// Transform의 회전은 도착 후 플레이어가 바라볼 방향으로도 사용된다.
        /// </summary>
        public Transform ApproachPoint => approachPoint;

        /// <summary>
        /// 이 Target을 선택할 수 있는 소유 방이다.
        /// 카메라에 다른 방 일부가 보이더라도 현재 방과 다르면 선택하지 않는다.
        /// </summary>
        public Room OwnerRoom => ownerRoom;

        /// <summary>
        /// 현재 Target이 게임 규칙상 선택 가능한지 반환한다.
        /// 오브젝트나 컴포넌트가 비활성화된 경우에도 선택할 수 없다.
        /// </summary>
        public bool IsSelectable => selectable && isActiveAndEnabled;

        /// <summary>
        /// 접근 지점 도착 후 실행할 행동을 반환한다.
        /// 잘못된 컴포넌트가 연결된 경우 null을 반환하고 Runner가 씬 설정 오류를 기록한다.
        /// </summary>
        public ITargetAction Action => actionComponent as ITargetAction;

        /// <summary>
        /// Target 자체 상태와 플레이어의 현재 방을 함께 검사한다.
        /// </summary>
        public bool IsSelectableIn(Room currentRoom)
        {
            return IsSelectable && ownerRoom != null && ownerRoom == currentRoom;
        }

        /// <summary>
        /// 스토리 진행, 아이템 소모 등의 결과에 따라 선택 가능 상태를 변경한다.
        /// Presenter는 다음 표시 갱신 때 변경된 상태를 마커에 반영한다.
        /// </summary>
        /// <param name="value">선택 가능하게 만들려면 true.</param>
        public void SetSelectable(bool value)
        {
            selectable = value;
        }

        /// <summary>
        /// 명령 실행에 필요한 참조가 올바르게 구성됐는지 검사한다.
        /// 누락된 참조는 정상 플레이 분기가 아니라 CoreSandbox 또는 스테이지 설정 오류로 취급한다.
        /// </summary>
        /// <param name="error">검사 실패 시 사람이 확인할 수 있는 오류 설명.</param>
        /// <returns>명령 실행에 필요한 설정이 모두 있으면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (approachPoint == null)
            {
                error = $"Target '{name}'에 ApproachPoint가 연결되지 않았습니다.";
                return false;
            }

            if (ownerRoom == null)
            {
                error = $"Target '{name}'에 OwnerRoom이 연결되지 않았습니다.";
                return false;
            }

            if (actionComponent == null)
            {
                error = $"Target '{name}'에 도착 후 행동 컴포넌트가 연결되지 않았습니다.";
                return false;
            }

            if (actionComponent is not ITargetAction)
            {
                error = $"Target '{name}'의 행동 컴포넌트가 ITargetAction을 구현하지 않았습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 컴포넌트를 처음 추가했을 때 관례적인 자식 이름을 사용해 접근 지점을 자동 연결한다.
        /// 행동 컴포넌트는 대상 유형에 따라 달라지므로 자동 생성하지 않는다.
        /// </summary>
        private void Reset()
        {
            approachPoint = transform.Find("ApproachPoint");
            ownerRoom = GetComponentInParent<Room>();
        }
    }
}
