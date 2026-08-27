using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// NursingRoom 전용 Story Spot Light의 표시 상태만 관리한다.
    /// 실제 위치와 방향은 Player 하위 NursingRoomFlashlightAnchor Transform에서 조정한다.
    /// </summary>
    public sealed class NursingRoomFlashlightView : MonoBehaviour
    {
        [SerializeField] private Light storySpotLight;

        public bool IsLightActive => storySpotLight != null && storySpotLight.enabled;

        private void Awake()
        {
            EnsureReference();
            SetLightActive(false);
        }

        private void Reset()
        {
            EnsureReference();
        }

        public void SetLightActive(bool active)
        {
            EnsureReference();

            if (storySpotLight == null)
            {
                return;
            }

            storySpotLight.enabled = active;
        }

        private void EnsureReference()
        {
            if (storySpotLight == null)
            {
                storySpotLight = GetComponentInChildren<Light>(true);
            }
        }

        private void OnDisable()
        {
            SetLightActive(false);
        }
    }
}
