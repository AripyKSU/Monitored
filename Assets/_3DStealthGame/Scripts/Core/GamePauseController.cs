using System;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedHouse.Core
{
    /// <summary>
    /// 게임 전체의 일시정지 요청을 중첩 가능한 Handle로 관리한다.
    /// 실제 Time.timeScale 변경은 이 클래스만 수행하고 마지막 요청이 해제될 때 이전 배율을 복원한다.
    /// </summary>
    public sealed class GamePauseController : MonoBehaviour
    {
        private readonly Dictionary<int, UnityEngine.Object> activeRequests = new();
        private int nextRequestId = 1;
        private float timeScaleBeforePause = 1f;

        public bool IsPaused => activeRequests.Count > 0;
        public int ActiveRequestCount => activeRequests.Count;

        /// <summary>
        /// 첫 일시정지 요청과 마지막 해제 시점에 현재 정지 여부를 알린다.
        /// unscaled time으로 진행되는 시스템은 이 이벤트나 IsPaused를 사용해 별도로 정지할 수 있다.
        /// </summary>
        public event Action<bool> PauseChanged;

        /// <summary>
        /// 호출자 전용 일시정지 Handle을 발급한다. 반환된 Handle은 반드시 Dispose해야 한다.
        /// </summary>
        public PauseHandle AcquirePause(UnityEngine.Object owner)
        {
            int requestId = nextRequestId++;
            bool wasPaused = IsPaused;
            activeRequests.Add(requestId, owner);

            if (!wasPaused)
            {
                timeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;
                PauseChanged?.Invoke(true);
            }

            return new PauseHandle(this, requestId);
        }

        /// <summary>
        /// Handle 하나의 요청을 해제하고 남은 요청이 없을 때만 게임 시간을 복원한다.
        /// </summary>
        private void ReleasePause(int requestId)
        {
            if (!activeRequests.Remove(requestId) || IsPaused)
            {
                return;
            }

            Time.timeScale = timeScaleBeforePause;
            PauseChanged?.Invoke(false);
        }

        /// <summary>
        /// 씬 종료나 컴포넌트 비활성화 시 정지 상태가 다음 실행에 남지 않도록 즉시 복원한다.
        /// </summary>
        private void OnDisable()
        {
            if (!IsPaused)
            {
                return;
            }

            activeRequests.Clear();
            Time.timeScale = timeScaleBeforePause;
            PauseChanged?.Invoke(false);
        }

        /// <summary>
        /// 하나의 일시정지 요청 수명을 나타낸다. 중복 Dispose는 무시한다.
        /// </summary>
        public sealed class PauseHandle : IDisposable
        {
            private GamePauseController controller;
            private readonly int requestId;

            internal PauseHandle(GamePauseController controller, int requestId)
            {
                this.controller = controller;
                this.requestId = requestId;
            }

            public void Dispose()
            {
                if (controller == null)
                {
                    return;
                }

                GamePauseController owner = controller;
                controller = null;
                owner.ReleasePause(requestId);
            }
        }
    }
}
