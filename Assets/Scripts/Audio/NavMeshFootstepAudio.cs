using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

namespace HauntedHouse.Audio
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NavMeshFootstepAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip defaultClip;
        [SerializeField] private AudioClip alternateClip;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private NavMeshAgent agent;
        [SerializeField, Min(0f)] private float movementThreshold = 0.05f;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        private bool useAlternateClip;

        public bool UseAlternateClip => useAlternateClip;

        private void Awake()
        {
            EnsureReferences();
            ApplySourceSettings();
        }

        private void Update()
        {
            if (agent == null || audioSource == null)
            {
                return;
            }

            bool moving = agent.enabled && agent.isOnNavMesh && !agent.isStopped &&
                          agent.hasPath && agent.velocity.sqrMagnitude > movementThreshold * movementThreshold;
            AudioClip desiredClip = useAlternateClip && alternateClip != null ? alternateClip : defaultClip;

            if (!moving || desiredClip == null)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                return;
            }

            if (audioSource.clip != desiredClip)
            {
                audioSource.Stop();
                audioSource.clip = desiredClip;
            }

            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        public void SetAlternate(bool value)
        {
            if (useAlternateClip == value)
            {
                return;
            }

            useAlternateClip = value;
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        public void Configure(AudioClip normal, AudioClip alternate, AudioMixerGroup mixerGroup,
            float sourceVolume, float minDistance, float maxDistance)
        {
            defaultClip = normal;
            alternateClip = alternate;
            volume = Mathf.Clamp01(sourceVolume);
            EnsureReferences();
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.minDistance = Mathf.Max(0.01f, minDistance);
            audioSource.maxDistance = Mathf.Max(audioSource.minDistance, maxDistance);
            ApplySourceSettings();
        }

        private void EnsureReferences()
        {
            agent ??= GetComponent<NavMeshAgent>();
            audioSource ??= GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        private void ApplySourceSettings()
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            audioSource.volume = volume;
        }
    }
}
