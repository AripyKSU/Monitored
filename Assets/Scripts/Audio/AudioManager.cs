using UnityEngine;
using UnityEngine.Audio;

namespace HauntedHouse.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [SerializeField] private AudioSourcePool sfxPool;
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private AudioMixerGroup bgmMixerGroup;
 [SerializeField] private AudioClip defaultBgm;
 [SerializeField, Range(0f, 1f)] private float defaultBgmVolume = 1f;

        public AudioSourcePool SfxPool => sfxPool;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureReferences();
        }

        private void Start()
 {
 if (defaultBgm != null)
 {
 PlayBgm(defaultBgm, defaultBgmVolume);
 }
 }

 public AudioSource PlaySfx(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            EnsureReferences();
            return sfxPool.Play(clip, position, volume, pitch);
        }

        public AudioSource PlayUiSfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            EnsureReferences();
            return sfxPool.Play(clip, transform.position, volume, pitch, 0f);
        }

        public void PlayBgm(AudioClip clip, float volume = 1f)
        {
            EnsureReferences();
            if (clip == null)
            {
                StopBgm();
                return;
            }

            if (bgmSource.clip != clip)
            {
                bgmSource.Stop();
                bgmSource.clip = clip;
            }

            bgmSource.volume = Mathf.Clamp01(volume);
            if (!bgmSource.isPlaying)
            {
                bgmSource.Play();
            }
        }

        public void StopBgm()
        {
            if (bgmSource == null)
            {
                return;
            }

            bgmSource.Stop();
            bgmSource.clip = null;
        }

        public void PlayAmbient(AudioClip clip, float volume = 1f) => PlayBgm(clip, volume);
        public void StopAmbient() => StopBgm();

        private void EnsureReferences()
        {
            sfxPool ??= GetComponent<AudioSourcePool>();
            if (sfxPool == null)
            {
                sfxPool = gameObject.AddComponent<AudioSourcePool>();
            }

            if (bgmSource == null)
            {
                Transform child = transform.Find("BGM Audio");
                GameObject target = child != null ? child.gameObject : new GameObject("BGM Audio");
                target.transform.SetParent(transform, false);
                bgmSource = target.GetComponent<AudioSource>() ?? target.AddComponent<AudioSource>();
            }

            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
            bgmSource.spatialBlend = 0f;
            bgmSource.outputAudioMixerGroup = bgmMixerGroup;
        }
    }
}
