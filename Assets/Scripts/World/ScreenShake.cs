using Unity.Cinemachine;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// Main Camera에 연결해 Cinemachine Impulse 기반의 가벼운 화면 흔들림을 제공한다.
    /// 발동 요청은 MachineRoom 같은 특정 스토리 흐름에서 직접 호출하며 별도 전역 서비스는 만들지 않는다.
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    [RequireComponent(typeof(CinemachineExternalImpulseListener))]
    public sealed class ScreenShake : MonoBehaviour
    {
        [Header("기본 흔들림")]
        [SerializeField, Min(0f)] private float defaultIntensity = 1f;
        [SerializeField, Min(0.01f)] private float defaultDuration = 0.25f;
        [SerializeField] private Vector3 defaultVelocity = new Vector3(0.35f, 0.6f, 0.35f);

        [Header("Cinemachine")]
        [SerializeField] private CinemachineImpulseSource impulseSource;
        [SerializeField] private CinemachineExternalImpulseListener impulseListener;

        /// <summary>Impulse 송신기와 Main Camera 수신기가 모두 연결됐는지 반환한다.</summary>
        public bool IsConfigured => impulseSource != null && impulseListener != null;

        /// <summary>Inspector 기본 강도와 지속시간으로 화면을 흔든다.</summary>
        public void Shake()
        {
            Shake(defaultIntensity, defaultDuration);
        }

        /// <summary>
        /// 지정한 강도와 지속시간으로 현재 Cinemachine 카메라 출력을 흔든다.
        /// 지속시간은 ImpulseDefinition에 반영하고 위치 수신기는 Main Camera에 둬 활성 가상 카메라와 함께 동작하게 한다.
        /// </summary>
        /// <param name="intensity">흔들림 세기.</param>
        /// <param name="duration">흔들림 지속 시간(초).</param>
        public void Shake(float intensity, float duration)
        {
            if (!IsConfigured || impulseSource.ImpulseDefinition == null)
            {
                GameLog.LogError($"ScreenShake '{name}'의 Cinemachine 참조가 구성되지 않았습니다.", this);
                return;
            }

            // Source의 기본 방향과 Impulse 지속 시간을 매 호출 시 확정해
            // MachineRoom 외부 흐름에서도 Inspector 기본값과 명시값을 동일하게 사용한다.
            impulseSource.DefaultVelocity = defaultVelocity.sqrMagnitude > 0.0001f
                ? defaultVelocity
                : Vector3.down;
            impulseSource.ImpulseDefinition.ImpulseDuration = Mathf.Max(0.01f, duration);
            impulseSource.GenerateImpulseWithForce(Mathf.Max(0f, intensity));
        }

        /// <summary>같은 GameObject의 Cinemachine 구성요소를 자동으로 연결한다.</summary>
        private void Reset()
        {
            impulseSource = GetComponent<CinemachineImpulseSource>();
            impulseListener = GetComponent<CinemachineExternalImpulseListener>();

            if (impulseListener != null)
            {
                impulseListener.ChannelMask = 1;
                impulseListener.Gain = 1f;
                impulseListener.UseLocalSpace = true;
            }
        }

        /// <summary>스크립트로 추가된 경우에도 필수 컴포넌트를 한 번 보완한다.</summary>
        private void Awake()
        {
            impulseSource ??= GetComponent<CinemachineImpulseSource>();
            impulseListener ??= GetComponent<CinemachineExternalImpulseListener>();
        }
    }
}
