using System;
using HauntedHouse.Core;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// 스토리의 GateActivated flag와 중앙 복도의 논리 Doorway, 철창 표현 Root를 동기화한다.
    /// 실제 철창 해제 입력은 MachineRoom 장치가 담당하고, 이 컴포넌트는 결과 상태를 월드에 반영한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GateController : MonoBehaviour
    {
        /// <summary>
        /// 중앙 복도 철창 발견 대화가 완료된 사실을 체크포인트에 보존하기 위한 내부 flag다.
        /// CenterHallFirstEntered와 분리해 Room 진입 시 체크포인트가 먼저 활성화되어도 대화를 건너뛰지 않게 한다.
        /// </summary>
        public const string GateDiscoveryDialogueShownFlag = "CenterHallGateDiscoveryDialogueShown";

        [Header("논리 연결")]
        [Tooltip("실제 철창 모델을 담는 시각 Root입니다. 논리 Doorway 자체를 지정하지 않습니다.")]
        [SerializeField] private GameObject gateRoot;
        [Tooltip("CenterHallway_A와 CenterHallway_B를 연결하는 논리 Doorway입니다.")]
        [SerializeField] private Doorway logicalGate;

        [Header("공용 진행 상태")]
        [SerializeField] private GameSession gameSession;

        [Header("철창 표현")]
        [Tooltip("활성화 시 Root를 끌지 않고 열림 표현이나 Animation을 외부에서 유지할 때 사용합니다.")]
        [SerializeField] private GateVisualMode visualMode = GateVisualMode.DeactivateRoot;

        private bool isActivated;

        /// <summary>현재 GameSession의 GateActivated flag가 반영된 상태인지 반환한다.</summary>
        public bool IsActivated => isActivated;

        /// <summary>철창 시각 표현을 배치할 Root를 반환한다.</summary>
        public GameObject GateRoot => gateRoot;

        /// <summary>철창이 활성화되거나 복원되어 상태가 바뀌었을 때 호출된다.</summary>
        public event Action<bool> StateChanged;

        /// <summary>씬의 공용 진행 상태와 논리 Doorway 참조를 보완한다.</summary>
        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>활성화될 때 flag 변경을 구독하고 현재 체크포인트 상태를 즉시 반영한다.</summary>
        private void OnEnable()
        {
            ResolveReferences();

            if (gameSession != null)
            {
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.StoryFlags.Changed += HandleStoryFlagsChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }

            RefreshState();
        }

        /// <summary>비활성화될 때 진행 상태 이벤트 구독을 해제한다.</summary>
        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }
        }

        /// <summary>
        /// MachineRoom 장치가 호출할 철창 해제 진입점이다.
        /// 호출자는 SFX와 Screen Shake를 먼저 재생한 뒤 이 메서드를 실행해야 한다.
        /// </summary>
        /// <returns>이번 호출로 새 flag가 추가됐으면 true.</returns>
        public bool Activate()
        {
            ResolveReferences();
            if (gameSession == null)
            {
                GameLog.LogError($"GateController '{name}'에 GameSession이 연결되지 않았습니다.", this);
                return false;
            }

            // GameSession.SetStoryFlag가 StoryFlags.Changed를 동기적으로 발생시키므로
            // flag를 추가하는 순간 논리 Doorway와 시각 Root도 같은 상태로 갱신된다.
            return gameSession.SetStoryFlag(StoryProgressFlags.GateActivated);
        }

        /// <summary>체크포인트 복원 또는 외부 flag 변경 후 철창 상태를 다시 계산한다.</summary>
        public void RefreshState()
        {
            bool nextActivated = gameSession != null &&
                                 gameSession.HasStoryFlag(StoryProgressFlags.GateActivated);
            bool changed = isActivated != nextActivated;
            isActivated = nextActivated;

            // 물리 모델과 관계없이 논리 Doorway는 flag를 유일한 잠금 기준으로 사용한다.
            logicalGate?.SetLocked(!isActivated);
            ApplyVisualState();

            if (changed)
            {
                StateChanged?.Invoke(isActivated);
            }
        }

        /// <summary>논리 Doorway와 선택적 시각 Root 구성이 실행 가능한지 검사한다.</summary>
        /// <param name="error">검사 실패 시 사람이 확인할 수 있는 설명.</param>
        /// <returns>철창 상태를 적용할 최소 구성이 유효하면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (logicalGate == null)
            {
                error = $"GateController '{name}'에 LogicalGate Doorway가 연결되지 않았습니다.";
                return false;
            }

            if (gameSession == null)
            {
                error = $"GateController '{name}'에 GameSession이 연결되지 않았습니다.";
                return false;
            }

            if (gateRoot == null)
            {
                error = $"GateController '{name}'에 GateRoot가 연결되지 않았습니다. 최종 에셋이 없어도 빈 Root를 만들어 연결해야 합니다.";
                return false;
            }

            if (gateRoot == gameObject || gateRoot == logicalGate.gameObject)
            {
                error = $"GateController '{name}'의 GateRoot가 논리 Doorway 오브젝트 자체를 가리킵니다. 시각 Root를 별도 자식 또는 별도 오브젝트로 분리해야 합니다.";
                return false;
            }

            if (!logicalGate.TryValidateConfiguration(out string doorwayError))
            {
                error = doorwayError;
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>스토리 flag가 추가·복원되면 논리 Doorway와 Root를 다시 갱신한다.</summary>
        private void HandleStoryFlagsChanged()
        {
            RefreshState();
        }

        /// <summary>체크포인트 복원이 끝난 뒤 복원된 flag 기준으로 상태를 확정한다.</summary>
        private void HandleGameplayStarted()
        {
            RefreshState();
        }

        /// <summary>GateActivated 여부를 선택한 표현 방식에 적용한다.</summary>
        private void ApplyVisualState()
        {
            if (gateRoot == null)
            {
                return;
            }

            if (visualMode == GateVisualMode.KeepRootActive)
            {
                // 열린 Mesh 또는 Animation을 사용하는 경우 Root 수명은 유지하고
                // StateChanged를 구독한 표현 컴포넌트가 실제 포즈를 결정한다.
                if (!gateRoot.activeSelf)
                {
                    gateRoot.SetActive(true);
                }

                return;
            }

            if (gateRoot == gameObject || gateRoot == logicalGate?.gameObject)
            {
                // 논리 Doorway까지 끄면 Target과 Sensor도 사라져 해제 후 통과할 수 없으므로
                // 잘못된 Root 설정에서는 시각 상태를 건드리지 않고 오류만 남긴다.
                GameLog.LogError(
                    $"GateController '{name}'의 GateRoot가 논리 Doorway와 같아 비활성화할 수 없습니다.",
                    this);
                return;
            }

            // 현재 단계는 실제 Animation 대신 닫힌 Root를 숨기는 방식으로도 동작해야 한다.
            gateRoot.SetActive(!isActivated);
        }

        /// <summary>Inspector 누락 참조를 관례적인 계층과 현재 씬 검색으로 보완한다.</summary>
        private void ResolveReferences()
        {
            logicalGate ??= GetComponent<Doorway>() ?? GetComponentInParent<Doorway>();
            gameSession ??= FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);

            if (gateRoot == null)
            {
                Transform child = transform.Find("GateRoot");
                gateRoot = child != null ? child.gameObject : null;
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 논리 Doorway와 공용 세션을 기본 연결한다.</summary>
        private void Reset()
        {
            logicalGate = GetComponent<Doorway>() ?? GetComponentInParent<Doorway>();
            gameSession = FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
            Transform child = transform.Find("GateRoot");
            gateRoot = child != null ? child.gameObject : null;
        }
    }

    /// <summary>GateActivated 이후 시각 Root를 처리하는 방식을 선택한다.</summary>
    public enum GateVisualMode
    {
        /// <summary>닫힌 철창 Root를 비활성화해 통과 가능한 상태를 표현한다.</summary>
        DeactivateRoot,

        /// <summary>Root를 유지하고 외부 Mesh 또는 Animation 컴포넌트가 열린 포즈를 표현한다.</summary>
        KeepRootActive
    }
}
