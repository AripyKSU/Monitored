using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Interaction
{
 /// <summary>
 /// Sector5 Entrance의 개별 엔딩 Target을 EndingController에 연결하는 얇은 어댑터다.
 /// 각 Target은 EndingType만 보유하고 실제 엔딩 진입 순서는 EndingController가 전담한다.
 /// </summary>
 [RequireComponent(typeof(Target))]
 public sealed class EndingTargetAction : MonoBehaviour, ITargetAction
 {
 [SerializeField] private EndingController endingController;
 [SerializeField] private EndingType endingType;

 private Target target;

 /// <summary>이 Target이 전달할 엔딩 종류다.</summary>
 public EndingType EndingType => endingType;

 private void Awake()
 {
 target ??= GetComponent<Target>();
 endingController ??= FindFirstObjectByType<EndingController>();

 // RequireComponent 때문에 Target이 먼저 추가된 경우에도 런타임에 액션 참조를 복구한다.
 if (target != null && target.Action == null)
 {
 target.SetAction(this);
 }
 }

 public IEnumerator Execute(TargetActionContext context)
 {
 target ??= context?.Target ?? GetComponent<Target>();
 endingController ??= FindFirstObjectByType<EndingController>();

 if (target == null || endingController == null)
 {
 GameLog.LogError($"EndingTargetAction '{name}'의 Target 또는 EndingController 참조가 없습니다.", this);
 yield break;
 }

 if (!endingController.CanBeginEnding)
 {
 GameLog.LogWarning($"EndingTargetAction '{name}'이 현재 엔딩 진입 불가 상태에서 실행되었습니다.", this);
 yield break;
 }

 // MarkEnding 직후 TargetCommandRunner가 이 코루틴을 중단할 수 있으므로 완료 표시는 먼저 확정한다.
 target.SetSelectable(false);
 context?.MarkCompleted();

 if (!endingController.TryBeginEnding(endingType))
 {
 // 같은 프레임 내 예상치 못한 검증 실패가 난 경우 플레이가 계속 가능하도록 Target을 되돌린다.
 target.SetSelectable(true);
 GameLog.LogError($"EndingTargetAction '{name}'이 {endingType} Ending 진입을 시작하지 못했습니다.", this);
 }
 }

 private void Reset()
 {
 target = GetComponent<Target>();
 endingController = FindFirstObjectByType<EndingController>();
 target?.SetAction(this);
 }
 }
}
