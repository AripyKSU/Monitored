using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace HauntedHouse.Audio
{
    public sealed class AudioSourcePool : MonoBehaviour
    {
        [SerializeField, Min(1)] private int initialSize = 12;
        [SerializeField] private AudioMixerGroup outputGroup;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 18f;

        private readonly Queue<AudioSource> free = new();
        private readonly List<AudioSource> active = new();
        private int created;

        private void Awake() => Prewarm(initialSize);

        private void Update()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                AudioSource source = active[i];
                if (source != null && source.isPlaying) continue;
                active.RemoveAt(i);
                Release(source);
            }
        }

        public AudioSource Play(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, float blend = -1f)
        {
            if (clip == null) return null;
            AudioSource source = free.Count > 0 ? free.Dequeue() : Create();
            source.transform.position = position;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, -3f, 3f);
            source.spatialBlend = blend < 0f ? spatialBlend : Mathf.Clamp01(blend);
            source.gameObject.SetActive(true);
            source.Play();
            active.Add(source);
            return source;
        }

        public void Prewarm(int count)
        {
            while (created < count) free.Enqueue(Create());
        }

        private AudioSource Create()
        {
            var go = new GameObject($"Pooled Audio Source {created:00}");
            go.transform.SetParent(transform, false);

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.outputAudioMixerGroup = outputGroup;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = Mathf.Max(minDistance, maxDistance);
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            go.SetActive(false);
            created++;
            return source;
        }

        private void Release(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = spatialBlend;
            source.transform.localPosition = Vector3.zero;
            source.gameObject.SetActive(false);
            free.Enqueue(source);
        }
    }
}
