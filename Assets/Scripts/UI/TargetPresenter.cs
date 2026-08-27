using System;
using HauntedHouse.Commands;
using HauntedHouse.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HauntedHouse.UI
{
    /// <summary>
    /// Target의 화면 마커와 클릭 입력을 담당하는 표현 컴포넌트다.
    /// 게임 규칙이나 행동을 실행하지 않고 선택 요청만 TargetCommandRunner에 전달한다.
    /// </summary>
    public sealed class TargetPresenter : MonoBehaviour, IPointerClickHandler
    {
        [Header("표현 대상")]
        [SerializeField] private Target target;
        [SerializeField] private Collider clickArea;
        [SerializeField] private Transform uiAnchor;
        [SerializeField] private PlayerRoomTracker playerRoomTracker;

        [Header("화면 마커")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private TargetMarkerPool markerPool;

        private bool presentationEnabled = true;
        private RectTransform activeMarker;
        private Button activeMarkerButton;

        /// <summary>
        /// 사용자가 Collider 또는 마커를 클릭했을 때 선택을 요청하는 이벤트다.
        /// Runner가 현재 상태와 Target 설정을 다시 검사한 뒤 실제 실행 여부를 결정한다.
        /// </summary>
        public event Action<Target> SelectionRequested;

        /// <summary>
        /// 이 Presenter가 표현하는 게임 규칙 대상이다.
        /// </summary>
        public Target Target => target;

        /// <summary>
        /// 참조를 보완하고 UI Button 클릭을 하나의 선택 요청으로 연결한다.
        /// </summary>
        private void Awake()
        {
            target ??= GetComponent<Target>();
            clickArea ??= GetComponentInChildren<Collider>();
            uiAnchor ??= transform.Find("UIAnchor");
            worldCamera ??= Camera.main;
            playerRoomTracker ??= FindFirstObjectByType<PlayerRoomTracker>();
            markerPool ??= FindFirstObjectByType<TargetMarkerPool>();
        }

        /// <summary>
        /// Presenter가 비활성화될 때 사용 중인 Marker를 풀에 반납한다.
        /// </summary>
        private void OnDisable()
        {
            ReleaseMarker();
        }

        /// <summary>
        /// 활성 CCTV가 반영된 Main Camera를 기준으로 매 프레임 마커 위치와 표시 여부를 갱신한다.
        /// </summary>
        private void LateUpdate()
        {
            Room currentRoom = playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null;
            bool shouldShow = presentationEnabled &&
                              target != null &&
                              target.IsSelectableIn(currentRoom) &&
                              uiAnchor != null;
            if (!shouldShow)
            {
                ReleaseMarker();
                SetClickAreaActive(false);
                return;
            }

            worldCamera ??= Camera.main;
            if (worldCamera == null)
            {
                ReleaseMarker();
                SetClickAreaActive(false);
                return;
            }

            if (!EnsureMarkerAcquired())
            {
                SetClickAreaActive(false);
                return;
            }

            // Screen Space Overlay Canvas의 RectTransform은 화면 좌표를 position에 바로 사용할 수 있다.
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(uiAnchor.position);
            bool isInsideScreen = screenPosition.z > 0f &&
                                  screenPosition.x >= 0f && screenPosition.x <= worldCamera.pixelWidth &&
                                  screenPosition.y >= 0f && screenPosition.y <= worldCamera.pixelHeight;

            SetClickAreaActive(isInsideScreen);
            if (isInsideScreen)
            {
                // WorldToScreenPoint의 z는 카메라로부터의 월드 깊이이므로 Overlay UI 위치에 사용하지 않는다.
                // 깊이값까지 RectTransform에 넣으면 마커가 Canvas 평면에서 밀려 렌더링되지 않을 수 있다.
                activeMarker.position = new Vector3(screenPosition.x, screenPosition.y, 0f);
            }
            else
            {
                ReleaseMarker();
            }
        }

        /// <summary>
        /// PhysicsRaycaster가 클릭 Collider를 감지했을 때 Target 선택을 요청한다.
        /// </summary>
        /// <param name="eventData">클릭 버튼과 실제 Raycast 대상을 포함한 포인터 이벤트.</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsConfiguredClickArea(eventData))
            {
                return;
            }

            RequestSelection();
        }

        /// <summary>
        /// Runner 상태에 따라 마커와 클릭 요청을 한꺼번에 활성화하거나 차단한다.
        /// Collider 자체는 이동 충돌에 사용될 수 있으므로 비활성화하지 않는다.
        /// </summary>
        /// <param name="value">표현과 입력을 허용하려면 true.</param>
        public void SetPresentationEnabled(bool value)
        {
            presentationEnabled = value;

            // 명령이 시작되는 프레임에 즉시 마커를 숨겨 다음 LateUpdate 전의 추가 클릭도 막는다.
            if (!value)
            {
                ReleaseMarker();
                SetClickAreaActive(false);
            }
        }

        /// <summary>
        /// UI 마커 Button 클릭을 월드 Collider 클릭과 동일한 선택 요청으로 변환한다.
        /// </summary>
        private void HandleMarkerClicked()
        {
            RequestSelection();
        }

        /// <summary>
        /// 현재 표시·선택 조건을 검사한 뒤 Runner가 구독한 이벤트를 발생시킨다.
        /// </summary>
        private void RequestSelection()
        {
            Room currentRoom = playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null;
            if (!presentationEnabled || target == null || !target.IsSelectableIn(currentRoom))
            {
                return;
            }

            SelectionRequested?.Invoke(target);
        }

        /// <summary>
        /// 포인터 이벤트가 이 Presenter에 지정된 클릭 영역에서 시작됐는지 확인한다.
        /// </summary>
        private bool IsConfiguredClickArea(PointerEventData eventData)
        {
            if (clickArea == null)
            {
                return false;
            }

            GameObject raycastObject = eventData.pointerPressRaycast.gameObject;
            if (raycastObject == null)
            {
                raycastObject = eventData.pointerCurrentRaycast.gameObject;
            }

            Collider hitCollider = raycastObject != null ? raycastObject.GetComponent<Collider>() : null;
            return hitCollider == clickArea;
        }

        /// <summary>
        /// 아직 Marker 인스턴스가 없으면 풀에서 하나를 빌리고 Button 클릭을 연결한다.
        /// </summary>
        private bool EnsureMarkerAcquired()
        {
            if (activeMarker != null)
            {
                return true;
            }

            if (markerPool == null)
            {
                GameLog.LogError($"TargetPresenter '{name}'에 MarkerPool이 연결되지 않았습니다.", this);
                return false;
            }

            // 모든 Target은 동일한 공용 MoveTarget Marker 템플릿을 사용하되,
            // 실제 인스턴스의 생성과 재사용은 기존 풀에 맡긴다.
            activeMarker = markerPool.Acquire();
            if (activeMarker == null)
            {
                return false;
            }

            activeMarkerButton = activeMarker.GetComponent<Button>();
            if (activeMarkerButton != null)
            {
                activeMarkerButton.onClick.RemoveListener(HandleMarkerClicked);
                activeMarkerButton.onClick.AddListener(HandleMarkerClicked);
            }

            return true;
        }

        /// <summary>
        /// 현재 Presenter가 빌린 Marker의 Button 구독을 해제하고 풀에 반납한다.
        /// </summary>
        private void ReleaseMarker()
        {
            if (activeMarkerButton != null)
            {
                activeMarkerButton.onClick.RemoveListener(HandleMarkerClicked);
            }

            if (activeMarker != null && markerPool != null)
            {
                markerPool.Release(activeMarker);
            }

            activeMarkerButton = null;
            activeMarker = null;
        }

        /// <summary>
        /// 마커와 동일한 선택 조건으로 클릭 전용 Collider를 활성화한다.
        /// 현재 방이 아닌 방향의 DoorTarget이 같은 문 위치에서 Raycast를 가로막는 것을 방지한다.
        /// </summary>
        private void SetClickAreaActive(bool value)
        {
            if (clickArea != null && clickArea.enabled != value)
            {
                clickArea.enabled = value;
            }
        }

        /// <summary>
        /// 컴포넌트를 처음 추가했을 때 같은 오브젝트 계층의 기본 참조를 연결한다.
        /// UIAnchor와 marker는 씬 배치에 따라 달라지므로 명시적으로 지정한다.
        /// </summary>
        private void Reset()
        {
            target = GetComponent<Target>();
            clickArea = GetComponentInChildren<Collider>();
            uiAnchor = transform.Find("UIAnchor");
            worldCamera = Camera.main;
            playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
            markerPool = FindFirstObjectByType<TargetMarkerPool>();
        }
    }
}
