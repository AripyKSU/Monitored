using System;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedHouse.Core
{
    /// <summary>
    /// 인벤토리, 대화, 아이템 선택과 퍼즐이 공유하는 게임 일시정지 서비스다.
    /// 요청별 Handle을 보관해 모든 요청이 해제된 경우에만 게임 시간을 복원한다.
    /// </summary>
    public sealed class GamePauseService : MonoBehaviour
    {
        private readonly Dictionary<int, UnityEngine.Object> activeRequests = new();
        private int nextRequestId = 1;
        private float timeScaleBeforePause = 1f;

        /// <summary>현재 하나 이상의 일시정지 요청이 등록되어 있는지 반환한다.</summary>
        public bool IsPaused => activeRequests.Count > 0;

        /// <summary>현재 해제되지 않은 일시정지 요청 수를 반환한다.</summary>
        public int ActiveRequestCount => activeRequests.Count;

        /// <summary>실제 게임 정지 상태가 변경될 때 호출된다.</summary>
        public event Action<bool> PauseStateChanged;

        /// <summary>
        /// 호출자 전용 요청 Handle을 발급하고 첫 요청이면 게임 시간을 정지한다.
        /// 반환된 Handle은 호출자가 닫힐 때 반드시 Dispose해야 한다.
        /// </summary>
        public PauseHandle AcquirePause(UnityEngine.Object owner)
        {
            int requestId = nextRequestId++;
            bool wasPaused = IsPaused;
            activeRequests.Add(requestId, owner);

            if (!wasPaused)
            {
                // 서비스 외부에서 timeScale을 변경하지 않는다는 전제 아래 현재 배율을 보존한다.
                timeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;
                PauseStateChanged?.Invoke(true);
            }

            return new PauseHandle(this, requestId);
        }

        /// <summary>
        /// 지정된 요청 하나만 제거하고 마지막 요청이면 보존한 시간 배율을 복원한다.
        /// </summary>
        private void ReleasePause(int requestId)
        {
            if (!activeRequests.Remove(requestId) || IsPaused)
            {
                return;
            }

            Time.timeScale = timeScaleBeforePause;
            PauseStateChanged?.Invoke(false);
        }

        /// <summary>씬 전환이나 비활성화 시 남은 요청과 정지 상태를 정리한다.</summary>
        private void OnDisable()
        {
            if (!IsPaused)
            {
                return;
            }

            activeRequests.Clear();
            Time.timeScale = timeScaleBeforePause;
            PauseStateChanged?.Invoke(false);
        }

        /// <summary>하나의 일시정지 요청 수명을 표현하며 중복 Dispose를 안전하게 무시한다.</summary>
        public sealed class PauseHandle : IDisposable
        {
            private GamePauseService service;
            private readonly int requestId;

            internal PauseHandle(GamePauseService service, int requestId)
            {
                this.service = service;
                this.requestId = requestId;
            }

            /// <summary>이 요청을 해제한다. 여러 번 호출해도 한 번만 적용된다.</summary>
            public void Dispose()
            {
                if (service == null)
                {
                    return;
                }

                GamePauseService owner = service;
                service = null;
                owner.ReleasePause(requestId);
            }
        }
    }
}
