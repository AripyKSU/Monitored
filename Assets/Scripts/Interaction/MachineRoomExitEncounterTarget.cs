using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// MachineRoom 출구의 기존 DoorTarget과 Sector2 강제 숨기 이벤트를 조건에 따라 분기한다.
    /// 이벤트가 끝난 뒤에는 같은 DoorTarget을 그대로 사용해 정상 퇴실을 수행한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    public sealed class MachineRoomExitEncounterTarget : MonoBehaviour, ITargetAction
    {
        [Header("행동 참조")]
        [SerializeField] private DoorTarget normalExitDoor;
        [SerializeField] private Sector2HideEncounterController encounterController;

        /// <summary>
        /// 강제 숨기 이벤트 조건이면 이벤트를 시작하고, 그 외에는 기존 문 통과를 실행한다.
        /// </summary>
        /// <param name="context">현재 Target과 플레이어 문맥.</param>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (encounterController != null && encounterController.ShouldStartEncounter)
            {
                yield return encounterController.Execute(context);
                yield break;
            }

            if (normalExitDoor == null)
            {
                GameLog.LogError($"MachineRoomExitEncounterTarget '{name}'에 기존 DoorTarget이 없습니다.", this);
                yield break;
            }

            yield return normalExitDoor.Execute(context);
        }

        /// <summary>출구 분기 컴포넌트와 기존 DoorTarget 참조가 유효한지 검사한다.</summary>
        /// <param name="error">검사 실패 원인.</param>
        /// <returns>정상 분기할 수 있으면 true.</returns>
        public bool TryValidateConfiguration(out string error)
        {
            if (normalExitDoor == null || encounterController == null)
            {
                error = $"MachineRoomExitEncounterTarget '{name}'의 DoorTarget 또는 이벤트 Controller가 누락됐습니다.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>컴포넌트를 처음 추가할 때 같은 오브젝트의 문 행동을 연결한다.</summary>
        private void Reset()
        {
            normalExitDoor = GetComponent<DoorTarget>();
            encounterController = FindFirstObjectByType<Sector2HideEncounterController>();
        }
    }
}
