using HauntedHouse.Commands;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>NursingRoom에서 손전등 정답 사용 시 Story Flag와 Story Spot Light를 갱신한다.</summary>
    public sealed class NursingRoomFlashlightUseEffect : MonoBehaviour, IItemUseEffect
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private NursingRoomFlashlightView flashlightView;

        public void ApplyItemEffect(TargetActionContext context)
        {
            ResolveReferences();
            if (gameSession == null || flashlightView == null)
            {
                GameLog.LogError($"NursingRoomFlashlightUseEffect '{name}'의 필수 참조가 누락됐습니다.", this);
                return;
            }

            gameSession.SetStoryFlag(StoryProgressFlags.NursingRoomFlashlightActivated);
            flashlightView.SetLightActive(true);
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            gameSession ??= FindFirstObjectByType<GameSession>();
            flashlightView ??= FindFirstObjectByType<NursingRoomFlashlightView>();
        }
    }
}
