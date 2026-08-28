using System;
using System.Collections;
using HauntedHouse.Commands;
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
        [SerializeField] private PlayerAnimationController playerAnimationController;
        [SerializeField, Min(1f)] private float captureAnimationTimeout = 10f;

        private Coroutine captureAnimationRoutine;
        private EnemyController capturedAttacker;
        private EnemyAnimationController capturedEnemyAnimation;

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
            playerAnimationController ??= FindFirstObjectByType<PlayerAnimationController>();
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

            capturedAttacker = attacker;
            if (capturedAttacker != null && !capturedAttacker.PauseForScriptedSequence())
            {
                GameLog.LogWarning("포획 공격자를 ScriptedSequence 정지 상태로 전환하지 못했습니다.", capturedAttacker);
            }

            capturedEnemyAnimation = capturedAttacker != null
                ? capturedAttacker.GetComponent<EnemyAnimationController>()
                : null;

            bool playerAnimationStarted = playerAnimationController != null &&
                                          playerAnimationController.PlayCaptureDeath();
            if (capturedEnemyAnimation != null && !capturedEnemyAnimation.PlayCaptureAttack())
            {
                GameLog.LogWarning("포획 공격 애니메이션을 시작하지 못했습니다.", capturedEnemyAnimation);
            }

            CaptureStarted?.Invoke(attacker);

            if (captureAnimationRoutine != null)
            {
                StopCoroutine(captureAnimationRoutine);
            }

            if (playerAnimationStarted)
            {
                captureAnimationRoutine = StartCoroutine(WaitForCaptureDeathAnimation());
            }
            else
            {
                GameLog.LogWarning("플레이어 포획 사망 애니메이션을 시작하지 못해 사망을 즉시 확정합니다.", this);
                NotifyDeathAnimationFinished();
            }

            return true;
        }

        /// <summary>플레이어 사망 애니메이션이 마지막 프레임에 도달할 때까지 기다린다.</summary>
        private IEnumerator WaitForCaptureDeathAnimation()
        {
            float elapsed = 0f;
            while (State == PlayerCaptureState.Captured && elapsed < captureAnimationTimeout)
            {
                if (playerAnimationController != null &&
                    playerAnimationController.IsCaptureDeathAnimationFinished())
                {
                    captureAnimationRoutine = null;
                    NotifyDeathAnimationFinished();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            captureAnimationRoutine = null;
            if (State == PlayerCaptureState.Captured)
            {
                GameLog.LogWarning("포획 사망 애니메이션 완료 대기가 시간 초과되어 사망을 확정합니다.", this);
                NotifyDeathAnimationFinished();
            }
        }

        /// <summary>
        /// 외부 애니메이션 시스템이 포획 연출 종료 후 호출할 지점이다.
        /// 사망 UI는 이 이벤트를 받아 나중에 구현한다.
        /// </summary>
        public void NotifyDeathAnimationFinished()
        {
            captureAnimationRoutine = null;
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
            if (captureAnimationRoutine != null)
            {
                StopCoroutine(captureAnimationRoutine);
                captureAnimationRoutine = null;
            }

            playerAnimationController?.ResetCapturePresentation();

            if (capturedAttacker != null)
            {
                capturedAttacker.ResumePatrolFromScriptedSequence();
            }

            capturedEnemyAnimation?.ResetCapturePresentation();
            capturedAttacker = null;
            capturedEnemyAnimation = null;
            State = PlayerCaptureState.Alive;
            stealthHideSystem?.RestorePresentation();
        }

        /// <summary>컴포넌트를 처음 추가했을 때 기본 참조를 연결한다.</summary>
        private void Reset()
        {
            stealthHideSystem = FindFirstObjectByType<StealthHideSystem>();
            enemyChaseSystem = FindFirstObjectByType<EnemyChaseSystem>();
            playerAnimationController = FindFirstObjectByType<PlayerAnimationController>();
        }
    }
}
