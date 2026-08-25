using System;
using HauntedHouse.Enemies;
using UnityEngine;

namespace HauntedHouse.Stealth
{
    /// <summary>
    /// 적 접촉 포획을 플레이어 사망 흐름으로 전달하는 호출 허브다.
    /// 애니메이션·사망 UI·세이브 포인트 복원은 구현하지 않고 확장 이벤트만 제공한다.
    /// </summary>
    public sealed class PlayerCaptureDeathSystem : MonoBehaviour
    {
        [SerializeField] private StealthHideSystem stealthHideSystem;
        [SerializeField] private EnemyChaseSystem enemyChaseSystem;

        /// <summary>현재 포획·사망 진행 상태를 반환한다.</summary>
        public PlayerCaptureState State { get; private set; } = PlayerCaptureState.Alive;

        /// <summary>포획한 적을 전달한다. 추후 포획 애니메이션 재생기가 구독한다.</summary>
        public event Action<EnemyController> CaptureStarted;

        /// <summary>포획 애니메이션 종료 후 사망 UI를 요청할 이벤트다.</summary>
        public event Action DeathUiRequested;

        /// <summary>
        /// 포획 애니메이션이 끝나 사망 상태로 확정될 때 발생한다.
        /// 스토리 사건이 아니라 GameSession 생명주기를 Dead로 바꾸는 제어 신호다.
        /// </summary>
        public event Action PlayerDied;

        /// <summary>기본 참조를 보완한다.</summary>
        private void Awake()
        {
            stealthHideSystem ??= FindFirstObjectByType<StealthHideSystem>();
            enemyChaseSystem ??= FindFirstObjectByType<EnemyChaseSystem>();
            GameLog.Log(
                $"PlayerCaptureDeathSystem 초기화. StealthHideSystem={stealthHideSystem?.name ?? "null"}, " +
                $"EnemyChaseSystem={enemyChaseSystem?.name ?? "null"}.",
                this);
        }

        /// <summary>적 접촉을 포획으로 확정한다. 은신 중이거나 이미 처리된 포획은 무시한다.</summary>
        /// <param name="attacker">플레이어와 접촉한 적.</param>
        /// <returns>이번 호출에서 포획을 새로 확정했으면 true.</returns>
        public bool TryCapture(EnemyController attacker)
        {
            GameLog.Log(
                $"PlayerCaptureDeathSystem.TryCapture 호출. Attacker={attacker?.name ?? "null"}, State={State}.",
                this);

            if (State != PlayerCaptureState.Alive ||
                (stealthHideSystem != null && stealthHideSystem.IsHidden))
            {
                GameLog.LogWarning("포획 요청이 현재 상태 또는 은신 상태 때문에 거부됐습니다.", this);
                return false;
            }

            State = PlayerCaptureState.Captured;
            enemyChaseSystem?.StopAllChases();
            CaptureStarted?.Invoke(attacker);

            // 현재 단계에는 별도의 포획 애니메이션 재생기가 없으므로
            // 충돌 포획 직후 사망을 확정해 GameSession과 Restart UI에 즉시 전달한다.
            // 추후 애니메이션 재생기가 연결되면 이 호출을 해당 재생기의 완료 콜백으로 이동한다.
            NotifyDeathAnimationFinished();
            return true;
        }

        /// <summary>
        /// 외부 애니메이션 시스템이 포획 연출 종료 후 호출할 지점이다.
        /// 사망 UI는 이 이벤트를 받아 나중에 구현한다.
        /// </summary>
        public void NotifyDeathAnimationFinished()
        {
            if (State != PlayerCaptureState.Captured)
            {
                return;
            }

            State = PlayerCaptureState.Dead;
            GameLog.Log("PlayerCaptureDeathSystem이 사망을 확정하고 PlayerDied/DeathUiRequested를 발행합니다.", this);
            PlayerDied?.Invoke();
            DeathUiRequested?.Invoke();
        }

        /// <summary>
        /// 다시하기 구현 시 세이브 포인트 복원 완료 후 사용할 상태 초기화 지점이다.
        /// </summary>
        public void ResetAfterRetry()
        {
            State = PlayerCaptureState.Alive;
            stealthHideSystem?.RestorePresentation();
        }

        /// <summary>컴포넌트를 처음 추가했을 때 기본 참조를 연결한다.</summary>
        private void Reset()
        {
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
            enemyChaseSystem = FindFirstObjectByType<EnemyChaseSystem>();
        }
    }
}
