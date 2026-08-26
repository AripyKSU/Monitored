using System;
using System.Collections;
using HauntedHouse.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HauntedHouse.Dialogue
{
    /// <summary>
    /// DialogueSequence 단위의 재생, Pause 수명과 진행 입력을 관리한다.
    /// 진행 중인 Sequence가 있으면 새 요청은 거부한다.
    /// </summary>
    public sealed class DialogueService : MonoBehaviour
    {
        [SerializeField] private DialoguePresenter presenter;
        [SerializeField] private GamePauseService pauseService;

        private Coroutine playRoutine;
        private GamePauseService.PauseHandle pauseHandle;

        public bool IsPlaying { get; private set; }

        public event Action<DialogueSequence> DialogueStarted;
        public event Action<DialogueSequence> DialogueCompleted;

        private void Awake()
        {
            presenter ??= GetComponentInChildren<DialoguePresenter>(true);
            pauseService ??= FindFirstObjectByType<GamePauseService>();
        }

        /// <summary>
        /// 대화를 시작한다. 이미 다른 대화가 재생 중이면 false를 반환한다.
        /// </summary>
        public bool TryPlay(DialogueSequence sequence)
        {
            if (IsPlaying)
            {
                GameLog.LogWarning(
                    $"DialogueService '{name}'가 재생 중이라 새 대화 요청을 무시했습니다.",
                    this);
                return false;
            }

            if (!ValidateSequence(sequence))
            {
                return false;
            }

            if (presenter == null || !presenter.IsValid())
            {
                GameLog.LogError(
                    $"DialogueService '{name}'의 DialoguePresenter 또는 View 참조가 올바르지 않습니다.",
                    this);
                return false;
            }

            if (sequence.PauseGame && pauseService == null)
            {
                GameLog.LogError(
                    $"DialogueService '{name}'에서 Pause 대화를 재생하려면 GamePauseService가 필요합니다.",
                    this);
                return false;
            }

            playRoutine = StartCoroutine(PlaySequence(sequence));
            return true;
        }

        private IEnumerator PlaySequence(DialogueSequence sequence)
        {
            IsPlaying = true;

            if (sequence.PauseGame)
            {
                pauseHandle = pauseService.AcquirePause(this);
            }

            DialogueStarted?.Invoke(sequence);

            try
            {
                for (int i = 0; i < sequence.Lines.Count; i++)
                {
                    DialogueLine line = sequence.Lines[i];

                    if (sequence.PauseGame)
                    {
                        yield return PlayPausedLine(line);
                    }
                    else
                    {
                        yield return PlayRealtimeLine(line, sequence.AutoAdvanceDelay);
                    }
                }
            }
            finally
            {
                FinishPlayback(sequence);
            }
        }

        /// <summary>
        /// Pause 대화는 타이핑 중 첫 클릭으로 즉시 완성하고,
        /// 완성 이후의 다음 클릭으로 다음 Line에 진입한다.
        /// </summary>
        private IEnumerator PlayPausedLine(DialogueLine line)
        {
            Coroutine typingRoutine = StartCoroutine(presenter.ShowLine(line));

            while (presenter.IsTyping)
            {
                if (WasAdvancePressed())
                {
                    presenter.CompleteTyping();
                }

                yield return null;
            }

            if (typingRoutine != null)
            {
                yield return typingRoutine;
            }

            // 타이핑을 끝낸 클릭이 같은 프레임에 다음 Line 진행까지 소비되지 않도록
            // 최소 한 프레임을 분리한다.
            yield return null;

            while (!WasAdvancePressed())
            {
                yield return null;
            }
        }

        private IEnumerator PlayRealtimeLine(DialogueLine line, float autoAdvanceDelay)
        {
            yield return presenter.ShowLine(line);

            if (autoAdvanceDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(autoAdvanceDelay);
            }
        }

        private static bool WasAdvancePressed()
        {
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        private bool ValidateSequence(DialogueSequence sequence)
        {
            if (sequence == null)
            {
                GameLog.LogError($"DialogueService '{name}'에 null DialogueSequence가 전달됐습니다.", this);
                return false;
            }

            if (sequence.Lines == null || sequence.Lines.Count == 0)
            {
                GameLog.LogWarning(
                    $"DialogueSequence '{sequence.name}'에 재생할 Line이 없습니다.",
                    sequence);
                return false;
            }

            return true;
        }

        private void FinishPlayback(DialogueSequence sequence)
        {
            presenter?.Hide();

            pauseHandle?.Dispose();
            pauseHandle = null;

            playRoutine = null;
            IsPlaying = false;

            DialogueCompleted?.Invoke(sequence);
        }

        private void OnDisable()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            presenter?.Hide();

            pauseHandle?.Dispose();
            pauseHandle = null;

            IsPlaying = false;
        }

        private void Reset()
        {
            presenter = GetComponentInChildren<DialoguePresenter>(true);
            pauseService = FindFirstObjectByType<GamePauseService>();
        }
    }
}
