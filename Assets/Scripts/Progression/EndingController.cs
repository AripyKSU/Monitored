using System;
using System.Collections;
using HauntedHouse.Enemies;
using HauntedHouse.UI;
using UnityEngine;
using UnityEngine.Playables;

namespace HauntedHouse.Progression
{
 /// <summary>
 /// 정문의 Bad/Happy Ending Target이 공통으로 사용하는 엔딩 진입 제어기다.
 /// Target은 EndingType만 전달하고, 추격 종료·세션 잠금·선택적 Timeline·임시 텍스트 표시를 이 컴포넌트가 소유한다.
 /// </summary>
 [DisallowMultipleComponent]
 public sealed class EndingController : MonoBehaviour
 {
 [Header("공용 진행 참조")]
 [SerializeField] private GameSession gameSession;
 [SerializeField] private EnemyController enemy;
 [SerializeField] private MessagePanel messagePanel;

 [Header("선택적 Timeline")]
 [SerializeField] private PlayableDirector badEndingDirector;
 [SerializeField] private PlayableDirector happyEndingDirector;

 [Header("Timeline 미구성 시 임시 문구")]
 [SerializeField, TextArea] private string badFallbackText = "[Bad Ending 텍스트 대체본]";
 [SerializeField, TextArea] private string happyFallbackText = "[Happy Ending 텍스트 대체본]";

 private Coroutine presentationRoutine;
 private PlayableDirector activeDirector;
 private bool activeDirectorStopped;

 /// <summary>엔딩 진입이 이미 확정되었는지 반환한다.</summary>
 public bool IsEnding { get; private set; }

 /// <summary>현재 확정된 엔딩 종류다. 아직 진입하지 않았다면 null이다.</summary>
 public EndingType? CurrentEnding { get; private set; }

 /// <summary>현재 세션에서 새 엔딩 진입을 받을 수 있는지 반환한다.</summary>
 public bool CanBeginEnding => !IsEnding && gameSession != null && gameSession.Phase == GameSessionPhase.Playing;

 /// <summary>엔딩 종류와 런타임 상태가 확정된 직후 발생한다.</summary>
 public event Action<EndingType> EndingStarted;

 /// <summary>Timeline 또는 임시 텍스트 표시가 끝나 task 36의 크레딧 단계로 넘길 수 있을 때 발생한다.</summary>
 public event Action<EndingType> EndingPresentationCompleted;

 private void Awake()
 {
 gameSession ??= FindFirstObjectByType<GameSession>();
 enemy ??= FindFirstObjectByType<EnemyController>();
 messagePanel ??= FindFirstObjectByType<MessagePanel>();
 }

 private void OnDisable()
 {
 if (presentationRoutine != null)
 {
 StopCoroutine(presentationRoutine);
 presentationRoutine = null;
 }

 UnsubscribeDirector();
 }

 /// <summary>필수 런타임 참조가 구성되었는지 검사한다.</summary>
 public bool TryValidateConfiguration(out string error)
 {
 if (gameSession == null)
 {
 error = $"EndingController '{name}'에 GameSession 참조가 없습니다.";
 return false;
 }

 if (enemy == null)
 {
 error = $"EndingController '{name}'에 EnemyController 참조가 없습니다.";
 return false;
 }

 if (messagePanel == null && !HasPlayable(badEndingDirector) && !HasPlayable(happyEndingDirector))
 {
 error = $"EndingController '{name}'에 Timeline도 MessagePanel도 없어 엔딩 화면을 표시할 수 없습니다.";
 return false;
 }

 error = string.Empty;
 return true;
 }

 /// <summary>
 /// 엔딩 종류를 확정하고 모든 게임플레이를 종료 상태로 전환한다.
 /// GameSession.MarkEnding은 TargetCommandRunner가 현재 코루틴을 중단시키므로 모든 필수 상태 확정과 프레젠테이션 시작 뒤 마지막에 호출한다.
 /// </summary>
 public bool TryBeginEnding(EndingType endingType)
 {
 if (!CanBeginEnding)
 {
 return false;
 }

 if (!TryValidateConfiguration(out string error))
 {
 GameLog.LogError(error, this);
 return false;
 }

 IsEnding = true;
 CurrentEnding = endingType;

 gameSession.SetStoryFlag(StoryProgressFlags.EndingStarted);
 gameSession.SetStoryStage(StoryStage.Ending);

 // AI Update 게이트뿐 아니라 NavMesh 이동과 추격 참조도 즉시 정리한다.
 enemy.SetActive(false);

 EndingStarted?.Invoke(endingType);
 presentationRoutine = StartCoroutine(RunPresentation(endingType));

 // 반드시 마지막에 잠근다. PhaseChanged를 받은 TargetCommandRunner가 실행 중 Target을 중단할 수 있다.
 gameSession.MarkEnding();
 return true;
 }

 private IEnumerator RunPresentation(EndingType endingType)
 {
 PlayableDirector director = GetDirector(endingType);
 if (HasPlayable(director))
 {
 activeDirector = director;
 activeDirectorStopped = false;
 activeDirector.stopped -= HandleDirectorStopped;
 activeDirector.stopped += HandleDirectorStopped;
 activeDirector.time = 0d;
 activeDirector.Play();

 while (!activeDirectorStopped)
 {
 yield return null;
 }

 UnsubscribeDirector();
 }
 else
 {
 string fallbackText = GetFallbackText(endingType);
 if (messagePanel != null)
 {
 yield return messagePanel.Show(fallbackText);
 }
 else
 {
 GameLog.Log(fallbackText, this);
 }
 }

 presentationRoutine = null;
 EndingPresentationCompleted?.Invoke(endingType);
 }

 private PlayableDirector GetDirector(EndingType endingType)
 {
 return endingType == EndingType.Happy ? happyEndingDirector : badEndingDirector;
 }

 private string GetFallbackText(EndingType endingType)
 {
 return endingType == EndingType.Happy ? happyFallbackText : badFallbackText;
 }

 private static bool HasPlayable(PlayableDirector director)
 {
 return director != null && director.playableAsset != null;
 }

 private void HandleDirectorStopped(PlayableDirector director)
 {
 if (director == activeDirector)
 {
 activeDirectorStopped = true;
 }
 }

 private void UnsubscribeDirector()
 {
 if (activeDirector != null)
 {
 activeDirector.stopped -= HandleDirectorStopped;
 activeDirector = null;
 }

 activeDirectorStopped = false;
 }

 private void Reset()
 {
 gameSession = FindFirstObjectByType<GameSession>();
 enemy = FindFirstObjectByType<EnemyController>();
 messagePanel = FindFirstObjectByType<MessagePanel>();
 }
 }
}
