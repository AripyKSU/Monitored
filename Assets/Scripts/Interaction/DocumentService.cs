using System.Collections;
using HauntedHouse.Core;
using HauntedHouse.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HauntedHouse.Interaction
{
    /// <summary>공용 문서 화면의 표시, 일시정지와 다음 마우스 클릭 닫기를 관리한다.</summary>
    public sealed class DocumentService : MonoBehaviour
    {
        [SerializeField] private DocumentView view;
        [SerializeField] private GamePauseService pauseService;

        private GamePauseService.PauseHandle pauseHandle;

        public bool IsShowing { get; private set; }

        private void Awake()
        {
            view ??= GetComponentInChildren<DocumentView>(true);
            pauseService ??= FindFirstObjectByType<GamePauseService>();
        }

        /// <summary>문서를 표시하고 표시 이후의 첫 왼쪽 클릭까지 기다린 뒤 닫는다.</summary>
        public IEnumerator Show(DocumentDefinition document)
        {
            if (document == null || view == null || !view.IsValid() || pauseService == null || IsShowing)
            {
                GameLog.LogError($"DocumentService '{name}'의 문서·View·Pause 참조가 올바르지 않거나 이미 표시 중입니다.", this);
                yield break;
            }

            IsShowing = true;
            pauseHandle = pauseService.AcquirePause(this);
            view.Show(document);

            try
            {
                // Target을 선택한 클릭이 같은 프레임에 문서 닫기로도 소비되지 않게 분리한다.
                yield return null;
                while (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                {
                    yield return null;
                }
            }
            finally
            {
                view.Hide();
                pauseHandle?.Dispose();
                pauseHandle = null;
                IsShowing = false;
            }
        }

        private void OnDisable()
        {
            view?.Hide();
            pauseHandle?.Dispose();
            pauseHandle = null;
            IsShowing = false;
        }
    }
}
