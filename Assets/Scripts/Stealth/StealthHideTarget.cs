using System.Collections;
using HauntedHouse.Commands;
using UnityEngine;

namespace HauntedHouse.Stealth
{
    /// <summary>
    /// 기존 Target 명령이 은신 시스템에 진입하도록 연결하는 행동 컴포넌트다.
    /// </summary>
    public sealed class StealthHideTarget : MonoBehaviour, ITargetAction
    {
        [SerializeField] private StealthHideSystem stealthHideSystem;
        private Target target;

        /// <summary>은신 시스템과 부모 Target을 캐시한다.</summary>
        private void Awake()
        {
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
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
            target = GetComponent<Target>();
        }
    }
}
