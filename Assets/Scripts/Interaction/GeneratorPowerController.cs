using HauntedHouse.Commands;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>
    /// GeneratorRoom 발전기의 Fuse 사용 성공 효과와 완료 상태 동기화를 담당한다.
    /// 진행 상태의 단일 기준은 StoryProgressFlags.GeneratorPowered이며,
    /// Fuse 설치 성공 시 FuseInstalled와 GeneratorPowered를 함께 기록한다.
    /// </summary>
    [RequireComponent(typeof(Target))]
    [RequireComponent(typeof(ItemUseTarget))]
    public sealed class GeneratorPowerController : MonoBehaviour, IItemUseEffect
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private Target target;

        /// <summary>현재 발전기가 이미 작동 완료 상태인지 반환한다.</summary>
        public bool IsPowered => gameSession != null &&
                                 gameSession.HasStoryFlag(StoryProgressFlags.GeneratorPowered);

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (gameSession != null)
            {
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.StoryFlags.Changed += HandleStoryFlagsChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
                gameSession.GameplayStarted += HandleGameplayStarted;
            }

            RefreshTargetState();
        }

        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.StoryFlags.Changed -= HandleStoryFlagsChanged;
                gameSession.GameplayStarted -= HandleGameplayStarted;
            }
        }

        /// <summary>Fuse가 올바르게 사용된 직후 발전기 진행 flag를 기록한다.</summary>
        public void ApplyItemEffect(TargetActionContext context)
        {
            ResolveReferences();
            if (gameSession == null)
            {
                GameLog.LogError($"GeneratorPowerController '{name}'에 GameSession 참조가 없습니다.", this);
                return;
            }

            gameSession.SetStoryFlag(StoryProgressFlags.FuseInstalled);
            gameSession.SetStoryFlag(StoryProgressFlags.GeneratorPowered);
            RefreshTargetState();
        }

        private void HandleStoryFlagsChanged()
        {
            RefreshTargetState();
        }

        private void HandleGameplayStarted()
        {
            RefreshTargetState();
        }

        /// <summary>완료된 발전기에서는 Item Use UI를 다시 열 수 없도록 Target 선택 상태를 갱신한다.</summary>
        private void RefreshTargetState()
        {
            if (target != null)
            {
                target.SetSelectable(!IsPowered);
            }
        }

        private void ResolveReferences()
        {
            target ??= GetComponent<Target>();
            ItemUseTarget itemUseTarget = GetComponent<ItemUseTarget>();
            if (target != null && itemUseTarget != null && target.Action != itemUseTarget)
            {
                target.SetAction(itemUseTarget);
            }

            gameSession ??= FindFirstObjectByType<GameSession>();
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
            RefreshTargetState();
        }
    }
}
