using UnityEngine;
using UnityEngine.Rendering;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// NursingRoom 전용 후처리 Volume을 방 판정에 따라 켜고 끈다.
    /// 고정 CCTV 위치와 무관하게 적용되도록 Global Volume으로 사용하되 Weight만 제어한다.
    /// </summary>
    public sealed class NursingRoomDarkVolumeView : MonoBehaviour
    {
        [SerializeField] private Volume darkVolume;
        [SerializeField] private float activeWeight = 1f;
        [SerializeField] private float inactiveWeight;
        [SerializeField] private float priority = 10f;

        /// <summary>NursingRoom 어둠 효과가 현재 적용 중인지 반환한다.</summary>
        public bool IsDarknessActive => darkVolume != null && darkVolume.weight > inactiveWeight;

        /// <summary>Volume 참조와 고정 CCTV용 런타임 설정을 준비하고 기본적으로 효과를 끈다.</summary>
        private void Awake()
        {
            ResolveReference();
            ConfigureVolume();
            SetDarknessActive(false);
        }

        /// <summary>NursingRoom 입장 여부를 전용 Volume의 Weight에 반영한다.</summary>
        /// <param name="active">어둠 효과를 적용하려면 true.</param>
        public void SetDarknessActive(bool active)
        {
            ResolveReference();
            if (darkVolume == null)
            {
                return;
            }

            darkVolume.weight = active
                ? Mathf.Clamp01(activeWeight)
                : Mathf.Clamp01(inactiveWeight);
        }

        /// <summary>고정 CCTV의 물리 위치에 영향받지 않도록 Global과 우선순위를 설정한다.</summary>
        private void ConfigureVolume()
        {
            if (darkVolume == null)
            {
                return;
            }

            darkVolume.isGlobal = true;
            darkVolume.priority = priority;
        }

        /// <summary>같은 오브젝트에서 제어할 Volume을 찾는다.</summary>
        private void ResolveReference()
        {
            darkVolume ??= GetComponent<Volume>();
        }

        /// <summary>Inspector에서 컴포넌트를 추가할 때 같은 오브젝트의 Volume을 자동 연결한다.</summary>
        private void Reset()
        {
            ResolveReference();
        }

        /// <summary>오브젝트가 비활성화될 때 전용 후처리 효과가 남지 않게 해제한다.</summary>
        private void OnDisable()
        {
            SetDarknessActive(false);
        }
    }
}
