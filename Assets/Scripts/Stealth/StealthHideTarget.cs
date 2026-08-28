using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using HauntedHouse.UI;
using UnityEngine;

namespace HauntedHouse.Stealth
{
    /// <summary>
    /// 기존 Target 명령이 은신 시스템에 진입하도록 연결하는 행동 컴포넌트다.
    /// </summary>
    public sealed class StealthHideTarget : MonoBehaviour, ITargetAction
    {
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private GameSession gameSession;
        [SerializeField] private MessagePanel messagePanel;
        [SerializeField, TextArea] private string finalChaseBlockedMessage = "지금은 숨을 수 없다.";
        private Target target;

        /// <summary>은신 시스템과 부모 Target을 캐시한다.</summary>
        private void Awake()
        {
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
            gameSession ??= FindFirstObjectByType<GameSession>();
            messagePanel ??= FindFirstObjectByType<MessagePanel>();
            target = GetComponent<Target>();
        }

        /// <summary>Target 도착 후 현재 방의 은신 가능 여부를 검사하고 은신을 시작한다.</summary>
        /// <param name="context">현재 Target과 행동 플레이어 정보.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (stealthHideSystem == null || target == null)
            {
                GameLog.LogError($"StealthHideTarget '{name}'의 참조가 올바르지 않습니다.", this);
                yield break;
            }

            if (gameSession != null && gameSession.HasStoryFlag(StoryProgressFlags.FinalChaseStarted))
            {
                // 최종 추격 중에는 Hide Target을 남겨 두되, 실제 은신 상태로 전환하지 않는다.
                // MessagePanel은 자체적으로 게임을 정지하지 않으므로 적 추격과 플레이어 이동은 계속된다.
                if (messagePanel == null)
                {
                    GameLog.LogError(
                        $"StealthHideTarget '{name}'이 최종 추격 차단 메시지를 표시할 MessagePanel을 찾지 못했습니다.",
                        this);
                    yield break;
                }

                yield return messagePanel.Show(finalChaseBlockedMessage);
                yield break;
            }

            if (!stealthHideSystem.TryEnterHide(context.Player, target.OwnerRoom))
            {
                GameLog.LogWarning($"HideSpot '{name}'은 현재 은신할 수 없는 상태입니다.", this);
            }
            else
            {
                // 은신 진입은 Target 명령이 성공적으로 완료된 것으로 기록한다.
                context.MarkCompleted();
            }

            yield break;
        }

        /// <summary>컴포넌트를 처음 추가했을 때 기본 참조를 연결한다.</summary>
        private void Reset()
        {
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
            gameSession = FindFirstObjectByType<GameSession>();
            messagePanel = FindFirstObjectByType<MessagePanel>();
            target = GetComponent<Target>();
        }
    }
}
