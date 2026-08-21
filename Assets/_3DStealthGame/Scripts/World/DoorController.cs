using System.Collections;
using UnityEngine;

namespace HauntedHouse.World
{
    /// <summary>
    /// Collider가 없는 문짝 Transform을 단순 회전시켜 열고 닫는 연출만 담당한다.
    /// 잠금, 방 변경, 통과 감지는 알지 못하며 DoorTarget이 코루틴 완료를 기다릴 수 있게 한다.
    /// </summary>
    public sealed class DoorController : MonoBehaviour
    {
        [SerializeField] private Transform doorLeaf;
        [SerializeField] private float openAngle = 90f;
        [SerializeField, Min(0f)] private float animationDuration = 0.25f;

        private Quaternion closedRotation;

        public bool IsOpen { get; private set; }

        /// <summary>
        /// 씬에 배치된 문짝의 초기 로컬 회전을 닫힌 기준 회전으로 저장한다.
        /// </summary>
        private void Awake()
        {
            if (doorLeaf == null)
            {
                GameLog.LogError($"DoorController '{name}'에 DoorLeaf가 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }

            closedRotation = doorLeaf.localRotation;
            IsOpen = false;
        }

        /// <summary>
        /// 문이 닫혀 있으면 설정된 각도로 회전하고 완료까지 기다린다.
        /// </summary>
        public IEnumerator Open()
        {
            if (IsOpen || doorLeaf == null)
            {
                yield break;
            }

            Quaternion openRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
            yield return AnimateTo(openRotation);
            IsOpen = true;
        }

        /// <summary>
        /// 플레이어가 목적지 방 진입 위치에 도착한 뒤 문을 닫힌 회전으로 되돌린다.
        /// </summary>
        public IEnumerator Close()
        {
            if (!IsOpen || doorLeaf == null)
            {
                yield break;
            }

            yield return AnimateTo(closedRotation);
            IsOpen = false;
        }

        /// <summary>
        /// 이동 실패나 컴포넌트 비활성화 시 문을 즉시 닫아 중간 회전 상태가 남지 않게 한다.
        /// </summary>
        public void ForceClosed()
        {
            if (doorLeaf != null)
            {
                doorLeaf.localRotation = closedRotation;
            }

            IsOpen = false;
        }

        /// <summary>
        /// 시작 회전에서 목적 회전까지 지정 시간 동안 부드럽게 보간한다.
        /// </summary>
        private IEnumerator AnimateTo(Quaternion targetRotation)
        {
            Quaternion startRotation = doorLeaf.localRotation;
            if (animationDuration <= 0f)
            {
                doorLeaf.localRotation = targetRotation;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / animationDuration);
                doorLeaf.localRotation = Quaternion.Slerp(startRotation, targetRotation, progress);
                yield return null;
            }

            doorLeaf.localRotation = targetRotation;
        }
    }
}
