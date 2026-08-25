using HauntedHouse.Threat;
using UnityEngine;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 현재 위험경보 단계에 맞는 반복 사운드를 AudioSource에 유지한다.
    /// 경보 규칙을 계산하지 않고 상태별 AudioClip 재생과 중지만 담당한다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DangerAlertAudioPlayer : MonoBehaviour
    {
        [Header("상태별 반복 사운드")]
        [SerializeField] private AudioClip cautionLoop;
        [SerializeField] private AudioClip emergencyLoop;

        [Header("재생")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private DangerAlertSystem alertSystem;

        /// <summary>현재 AudioSource에 반영된 경보 단계를 반환한다.</summary>
        public DangerAlertLevel CurrentLevel { get; private set; } = DangerAlertLevel.None;

        /// <summary>AudioSource와 경보 시스템 참조를 보완하고 자동 재생을 차단한다.</summary>
        private void Awake()
        {
            audioSource ??= GetComponent<AudioSource>();
            alertSystem ??= FindFirstObjectByType<DangerAlertSystem>();

            if (audioSource != null)
            {
                audioSource.playOnAwake = false;
                audioSource.loop = true;
            }
        }

        /// <summary>활성화 시 현재 경보 상태를 구독하고 사운드를 동기화한다.</summary>
        private void OnEnable()
        {
            if (alertSystem == null)
            {
                return;
            }

            alertSystem.AlertChanged -= HandleAlertChanged;
            alertSystem.AlertChanged += HandleAlertChanged;
            SetAlert(alertSystem.CurrentLevel);
        }

        /// <summary>비활성화 시 경보 이벤트를 해제하고 재생 중인 사운드를 정리한다.</summary>
        private void OnDisable()
        {
            if (alertSystem != null)
            {
                alertSystem.AlertChanged -= HandleAlertChanged;
            }

            StopAudio();
        }

        /// <summary>경보 시스템에서 전달된 현재 단계를 AudioSource에 반영한다.</summary>
        private void HandleAlertChanged(DangerAlertLevel level)
        {
            SetAlert(level);
        }

        /// <summary>
        /// 현재 단계에 맞는 반복 사운드를 재생하거나 None이면 사운드를 중지한다.
        /// 동일한 단계와 같은 Clip이면 재생 위치를 유지한다.
        /// </summary>
        /// <param name="level">AudioSource에 반영할 경보 단계.</param>
        public void SetAlert(DangerAlertLevel level)
        {
            CurrentLevel = level;

            if (audioSource == null)
            {
                return;
            }

            AudioClip nextClip = level switch
            {
                DangerAlertLevel.Caution => cautionLoop,
                DangerAlertLevel.Emergency => emergencyLoop,
                _ => null
            };

            if (nextClip == null)
            {
                StopAudio();
                return;
            }

            if (audioSource.clip == nextClip && audioSource.isPlaying)
            {
                return;
            }

            audioSource.Stop();
            audioSource.clip = nextClip;
            audioSource.loop = true;
            audioSource.Play();
        }

        /// <summary>현재 AudioSource 사운드를 중지하고 Clip 참조를 비운다.</summary>
        private void StopAudio()
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.Stop();
            audioSource.clip = null;
        }

        /// <summary>컴포넌트를 처음 추가할 때 AudioSource와 경보 시스템을 연결한다.</summary>
        private void Reset()
        {
            audioSource = GetComponent<AudioSource>();
            alertSystem = FindFirstObjectByType<DangerAlertSystem>();
        }
    }
}
