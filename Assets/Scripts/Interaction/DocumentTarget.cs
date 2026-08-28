using System.Collections;
using HauntedHouse.Commands;
using HauntedHouse.Progression;
using UnityEngine;

namespace HauntedHouse.Interaction
{
    /// <summary>일기장·힌트 보드 문서를 표시하고 기존 완료 ID와 Story Flag를 보존하는 Target 행동이다.</summary>
    [RequireComponent(typeof(Target))]
    public sealed class DocumentTarget : MonoBehaviour, ITargetAction, ICompletedState
    {
        [SerializeField] private DocumentService documentService;
        [SerializeField] private DocumentDefinition document;
        [SerializeField] private string completionId;
        [SerializeField] private GameSession gameSession;
        [SerializeField] private string completionFlagId;

        public string CompletionId => string.IsNullOrWhiteSpace(completionId) ? name : completionId;
        public bool IsCompleted { get; private set; }

        /// <summary>문서를 끝까지 확인한 뒤 완료 상태와 선택적인 Story Flag를 기록한다.</summary>
        public IEnumerator Execute(TargetActionContext context)
        {
            if (documentService == null || document == null)
            {
                GameLog.LogError($"DocumentTarget '{name}'의 DocumentService 또는 Document가 없습니다.", this);
                yield break;
            }

            yield return documentService.Show(document);
            if (documentService.IsShowing)
            {
                yield break;
            }

            IsCompleted = true;
            if (gameSession != null && !string.IsNullOrWhiteSpace(completionFlagId))
            {
                gameSession.SetStoryFlag(completionFlagId);
            }

            GetComponent<Target>()?.SetSelectable(false);
            context?.MarkCompleted();
        }

        /// <summary>체크포인트의 완료 상태와 Story Flag를 Target 선택 가능 상태에 반영한다.</summary>
        public void RestoreCompletedState(bool completed)
        {
            bool completedByFlag = gameSession != null &&
                                   !string.IsNullOrWhiteSpace(completionFlagId) &&
                                   gameSession.HasStoryFlag(completionFlagId);
            IsCompleted = completed || completedByFlag;
            GetComponent<Target>()?.SetSelectable(!IsCompleted);
        }

        private void Reset()
        {
            documentService = FindFirstObjectByType<DocumentService>();
            gameSession = FindFirstObjectByType<GameSession>();
            completionId = name;
        }
    }
}
