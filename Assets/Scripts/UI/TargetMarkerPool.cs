using UnityEngine;

namespace HauntedHouse.UI
{
    /// <summary>
    /// Target UI의 공용 MoveTarget Marker 그룹에 대한 인스턴스 재사용을 중계한다.
    /// 실제 인스턴스는 기존 풀 그룹의 자식으로 생성·반납되므로 오브젝트 풀 구조를 유지한다.
    /// </summary>
    public sealed class TargetMarkerPool : MonoBehaviour
    {
        [SerializeField] private RectTransform poolRoot;
        [Tooltip("모든 Target이 공유하는 MoveTarget Marker 템플릿의 풀 그룹")]
        [SerializeField] private TargetMarkerPoolGroup commonGroup;

        /// <summary>공용 Marker 풀 그룹이 배치된 루트를 반환한다.</summary>
        public RectTransform PoolRoot => poolRoot;

        /// <summary>
        /// 풀 루트와 공용 그룹을 초기화하고 MoveTarget Marker 템플릿을 준비한다.
        /// </summary>
        private void Awake()
        {
            poolRoot ??= GetComponent<RectTransform>();

            // 씬에 참조가 아직 저장되지 않은 경우에도 Move 그룹을 찾아 기존 씬을 안전하게 지원한다.
            commonGroup ??= FindCommonGroup();
            if (commonGroup == null)
            {
                GameLog.LogError("TargetMarkerPool에 공용 MoveTarget Marker 그룹이 연결되지 않았습니다.", this);
                return;
            }

            commonGroup.Initialize();
        }

        /// <summary>
        /// 공용 MoveTarget Marker 풀에서 인스턴스를 하나 꺼낸다.
        /// </summary>
        /// <returns>표시할 Marker 인스턴스. 공용 그룹이 없으면 null.</returns>
        public RectTransform Acquire()
        {
            if (commonGroup == null)
            {
                GameLog.LogError("TargetMarkerPool에 공용 Marker 그룹이 등록되지 않았습니다.", this);
                return null;
            }

            // 대상별 구분 없이 같은 그룹의 available/active 스택을 공유한다.
            return commonGroup.Acquire();
        }

        /// <summary>
        /// 사용이 끝난 공용 Marker 인스턴스를 풀에 반납한다.
        /// </summary>
        /// <param name="instance">반납할 Marker 인스턴스.</param>
        public void Release(RectTransform instance)
        {
            if (commonGroup != null)
            {
                commonGroup.Release(instance);
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 루트와 공용 Move 그룹을 자동 연결한다.</summary>
        private void Reset()
        {
            poolRoot = GetComponent<RectTransform>();
            commonGroup = FindCommonGroup();
        }

        /// <summary>
        /// 자식 풀 그룹 중 MoveTarget Marker 템플릿을 가진 그룹을 찾는다.
        /// 공용 템플릿을 찾지 못하면 다른 종류의 마커가 잘못 사용되지 않도록 null을 반환한다.
        /// </summary>
        private TargetMarkerPoolGroup FindCommonGroup()
        {
            TargetMarkerPoolGroup[] candidateGroups = GetComponentsInChildren<TargetMarkerPoolGroup>(true);
            foreach (TargetMarkerPoolGroup candidate in candidateGroups)
            {
                // 현재 씬의 풀 그룹 이름을 우선 확인해 템플릿 참조가 아직 보완되지 않은 경우도 지원한다.
                if (candidate.name == "Common" || candidate.name == "Move")
                {
                    return candidate;
                }
            }

            foreach (TargetMarkerPoolGroup candidate in candidateGroups)
            {
                if (candidate.Template != null && candidate.Template.name == "MoveTarget Marker")
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
