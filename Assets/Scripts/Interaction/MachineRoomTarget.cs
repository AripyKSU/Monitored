using System.Collections;
using HauntedHouse.Audio;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using HauntedHouse.World;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// MachineRoom의 통과 장치를 조사하고 발전기 전원에 따라 철창 해제를 실행한다.
    /// 성공 상태는 ICompletedState로 체크포인트에 기록하며, 철창의 실제 표현은 GateController에 위임한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public sealed class MachineRoomTarget : MonoBehaviour, ITargetAction, ICompletedState
    {
        /// <summary>체크포인트에서 사용할 기계 장치의 기본 완료 상태 ID다.</summary>
        public const string DefaultCompletionId = "S2_MachineRoom_Tank_1";

        [Header("공용 진행 상태")]
        [SerializeField] private GameSession gameSession;
        [SerializeField] private GateController gateController;

        [Header("연출")]
        [SerializeField] private ScreenShake screenShake;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField] private AudioClip gateActivationSound;
        [SerializeField, Range(0f, 1f)] private float gateActivationSoundVolume = 1f;

        [Header("메시지")]
        [SerializeField, TextArea] private string noPowerMessage = "파워가 들어오지 않는다.";

        [Header("체크포인트")]
        [SerializeField] private string completionId = DefaultCompletionId;

        private Target target;
        private bool isExecuting;

        /// <summary>기계 장치가 성공적으로 작동했는지 반환한다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>체크포인트에서 이 장치의 완료 상태를 식별할 ID를 반환한다.</summary>
        public string CompletionId => string.IsNullOrWhiteSpace(completionId)
            ? DefaultCompletionId
            : completionId;

        /// <summary>
        /// 플레이어가 장치에 도착한 뒤 전원 상태를 확인하고 철창 해제 순서대로 연출을 실행한다.
        /// 전원이 없을 때는 메시지만 표시하며 완료 상태나 GateActivated flag를 변경하지 않는다.
        /// </summary>
        /// <param name="context">현재 Target과 행동 플레이어를 담은 실행 문맥.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (isExecuting || IsCompleted)
            {
                // Runner 외부에서 같은 행동이 직접 호출되는 경우도 고려해 중복 실행을 막는다.
                yield break;
            }

            if (!TryValidateConfiguration(out string error))
            {
                GameLog.LogError(error, this);
                yield break;
            }

            isExecuting = true;
            try
            {
                if (!gameSession.HasStoryFlag(StoryProgressFlags.GeneratorPowered))
                {
                    // MessagePanel은 확인 입력을 받을 때까지 명령 코루틴을 유지하므로
                    // 전원이 없다는 결과가 표시된 동안 같은 Target의 중복 선택도 차단된다.
                    yield return messagePanel.Show(noPowerMessage);
                    yield break;
                }

                if (gameSession.HasStoryFlag(StoryProgressFlags.GateActivated))
                {
                    // 다른 흐름이 먼저 flag를 기록한 경우에도 완료 상태와 Target 표시를 동기화한다.
                    CompleteInteraction(context);
                    yield break;
                }

                // 계획된 연출 순서를 유지한다. AudioClip은 비어 있을 수 있으므로 선택적으로만 재생한다.
                if (gateActivationSound != null)
                {
                    AudioManager.Instance?.PlaySfx(
                        gateActivationSound,
                        transform.position,
                        gateActivationSoundVolume);
                }

                screenShake.Shake();

                // GateController.Activate는 GateActivated flag 기록과 논리 Doorway·시각 Root 갱신을 담당한다.
                bool activated = gateController.Activate();
                if (!activated && !gameSession.HasStoryFlag(StoryProgressFlags.GateActivated))
                {
                    GameLog.LogError(
                        $"MachineRoom 장치 '{name}'이 GateActivated 상태를 기록하지 못했습니다.",
                        this);
                    yield break;
                }

                CompleteInteraction(context);
            }
            finally
            {
                // 예외나 명령 중단 뒤에도 다음 정상 실행을 받을 수 있도록 실행 잠금을 해제한다.
                isExecuting = false;
            }
        }

        /// <summary>현재 장치가 필요한 공용 참조를 모두 가지고 있는지 검사한다.</summary>
        /// <param name="error">검사 실패 시 사람이 확인할 오류 설명.</param>
        /// <returns>기계 장치 행동을 실행할 수 있으면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (gameSession == null || gateController == null || screenShake == null || messagePanel == null)
            {
                error = $"MachineRoomTarget '{name}'의 GameSession, GateController, ScreenShake 또는 MessagePanel 참조가 누락됐습니다.";
                return false;
            }

            if (!gateController.TryValidateConfiguration(out string gateError))
            {
                error = gateError;
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>기계 작동 완료를 내부 상태와 Target 선택 가능 여부에 반영한다.</summary>
        /// <param name="context">현재 행동의 완료 상태를 함께 갱신할 실행 문맥.</param>
        private void CompleteInteraction(TargetActionContext context)
        {
            IsCompleted = true;
            target ??= GetComponent<Target>();
            target?.SetSelectable(false);
            context?.MarkCompleted();
        }

        /// <summary>체크포인트가 지정한 기계 작동 완료 여부를 복원한다.</summary>
        /// <param name="completed">완료 상태로 복원하려면 true.</param>
        public void RestoreCompletedState(bool completed)
        {
            // RespawnCoordinator가 진행 중인 명령을 중단한 뒤 호출하므로
            // 복원 시점에는 이전 실행의 내부 잠금도 함께 초기화한다.
            isExecuting = false;
            IsCompleted = completed;
            target ??= GetComponent<Target>();
            target?.SetSelectable(!completed);
        }

        /// <summary>씬 비활성화나 명령 중단 뒤 남을 수 있는 내부 실행 잠금을 해제한다.</summary>
        private void OnDisable()
        {
            isExecuting = false;
        }

        /// <summary>컴포넌트를 처음 추가할 때 Target과 현재 씬의 공용 참조를 연결한다.</summary>
        private void Reset()
        {
            target = GetComponent<Target>();
            gameSession = FindFirstObjectByType<GameSession>(FindObjectsInactive.Include);
            gateController = FindFirstObjectByType<GateController>(FindObjectsInactive.Include);
            screenShake = FindFirstObjectByType<ScreenShake>(FindObjectsInactive.Include);
            messagePanel = FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
            completionId = DefaultCompletionId;
        }
    }
}
