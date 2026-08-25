using System;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Stealth
{
    /// <summary>
    /// 플레이어의 은신 진입·해제와 은신 중 Target 선택 제한을 관리한다.
    /// 적의 방 판정은 EnemyChaseSystem에 위임하고, 이 클래스는 플레이어 표시와 은신 상태만 소유한다.
    /// </summary>
    public sealed class StealthHideSystem : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Player player;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;
        [SerializeField] private EnemyChaseSystem enemyChaseSystem;
        [SerializeField] private TargetPresenter[] targetPresenters = Array.Empty<TargetPresenter>();

        private Renderer[] playerRenderers = Array.Empty<Renderer>();
        private bool[] playerRendererInitialVisibility = Array.Empty<bool>();

        /// <summary>현재 은신 상태를 반환한다.</summary>
        public StealthState State { get; private set; } = StealthState.Visible;

        /// <summary>플레이어가 적의 탐지·포획 대상에서 제외된 상태인지 반환한다.</summary>
        public bool IsHidden => State == StealthState.Hidden;

        /// <summary>현재 은신한 방을 반환한다.</summary>
        public Room HidingRoom { get; private set; }

        /// <summary>은신 상태가 바뀔 때 새 상태와 방을 전달한다.</summary>
        public event Action<StealthState, Room> StateChanged;

        /// <summary>은신에 성공했을 때 발생하는 확장 이벤트다.</summary>
        public event Action<Room> HideEntered;

        /// <summary>은신을 해제했을 때 발생하는 확장 이벤트다.</summary>
        public event Action<Room> HideExited;

        /// <summary>기본 참조와 플레이어 Renderer 목록을 캐시한다.</summary>
        private void Awake()
        {
            player ??= FindFirstObjectByType<Player>();
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            enemyChaseSystem ??= FindFirstObjectByType<EnemyChaseSystem>();
            playerRenderers = player != null
                ? player.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            playerRendererInitialVisibility = new bool[playerRenderers.Length];
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                playerRendererInitialVisibility[i] = playerRenderers[i] != null && playerRenderers[i].enabled;
            }

            if (targetPresenters == null || targetPresenters.Length == 0)
            {
                targetPresenters = FindObjectsByType<TargetPresenter>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            }
        }

        /// <summary>
        /// 지정한 방에서 은신을 시작할 수 있는지 검사한다.
        /// </summary>
        /// <param name="hidingRoom">은신 Target이 소유한 방.</param>
        /// <returns>현재 방과 같고 활성 적이 없으면 true.</returns>
        public bool CanEnterHide(Room hidingRoom)
        {
            if (State != StealthState.Visible || hidingRoom == null || playerRoomTracker == null ||
                playerRoomTracker.CurrentRoom != hidingRoom)
            {
                return false;
            }

            return enemyChaseSystem == null || !enemyChaseSystem.HasActiveEnemyInRoom(hidingRoom);
        }

        /// <summary>
        /// 은신 Target 행동이 호출하는 은신 진입점이다.
        /// </summary>
        /// <param name="actor">은신 행동을 실행한 플레이어.</param>
        /// <param name="hidingRoom">은신 Target이 소유한 방.</param>
        /// <returns>은신 진입에 성공했으면 true.</returns>
        public bool TryEnterHide(Player actor, Room hidingRoom)
        {
            if (actor == null || actor != player || !CanEnterHide(hidingRoom))
            {
                return false;
            }

            // 은신 중에도 방 통과와 논리 상태가 유지되도록 Collider는 끄지 않고 외형만 숨긴다.
            SetPlayerRenderersVisible(false);
            SetTargetPresentationsEnabled(false);
            HidingRoom = hidingRoom;
            SetState(StealthState.Hidden);
            HideEntered?.Invoke(HidingRoom);
            return true;
        }

        /// <summary>
        /// 임시 나가기 버튼 또는 이후 전용 UI가 호출할 은신 해제 진입점이다.
        /// </summary>
        /// <returns>은신 중이어서 해제에 성공했으면 true.</returns>
        public bool TryExitHide()
        {
            if (State != StealthState.Hidden)
            {
                return false;
            }

            Room exitedRoom = HidingRoom;
            HidingRoom = null;
            SetPlayerRenderersVisible(true);
            SetState(StealthState.Visible);
            SetTargetPresentationsEnabled(true);
            HideExited?.Invoke(exitedRoom);
            return true;
        }

        /// <summary>
        /// 다시하기 구현 시 외형과 은신 표시를 복구할 임시 호출 지점이다.
        /// 세이브 포인트 이동이나 사망 UI는 수행하지 않는다.
        /// </summary>
        public void RestorePresentation()
        {
            HidingRoom = null;
            SetPlayerRenderersVisible(true);
            SetTargetPresentationsEnabled(true);
            SetState(StealthState.Visible);
        }

        /// <summary>플레이어 하위 Renderer의 활성 상태를 일괄 변경한다.</summary>
        private void SetPlayerRenderersVisible(bool value)
        {
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                Renderer renderer = playerRenderers[i];
                if (renderer != null)
                {
                    // 숨김 해제 시에도 씬에서 원래 꺼져 있던 캡슐·프록시 Renderer는 다시 켜지지 않게 한다.
                    renderer.enabled = value && playerRendererInitialVisibility[i];
                }
            }
        }

        /// <summary>은신 중 모든 TargetPresenter의 표시와 클릭 요청을 잠근다.</summary>
        private void SetTargetPresentationsEnabled(bool value)
        {
            if (targetPresenters == null)
            {
                return;
            }

            foreach (TargetPresenter presenter in targetPresenters)
            {
                presenter?.SetPresentationEnabled(value);
            }
        }

        /// <summary>상태가 달라질 때만 저장하고 구독자에게 알린다.</summary>
        private void SetState(StealthState value)
        {
            if (State == value)
            {
                return;
            }

            State = value;
            StateChanged?.Invoke(State, HidingRoom);
        }

        /// <summary>컴포넌트를 처음 추가할 때 기본 참조를 연결한다.</summary>
        private void Reset()
        {
            player = FindFirstObjectByType<Player>();
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            enemyChaseSystem = FindFirstObjectByType<EnemyChaseSystem>();
            targetPresenters = FindObjectsByType<TargetPresenter>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }
    }
}
