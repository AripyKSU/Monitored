using System.Collections.Generic;
using UnityEngine;

namespace HauntedHouse.UI
{
    /// <summary>
    /// 하나의 TargetMarkerType에 해당하는 템플릿과 재사용 인스턴스를 보관하는 풀 그룹이다.
    /// 씬 계층에서 종류별 부모 오브젝트로 보이므로 Inspector와 Hierarchy에서 용도를 확인하기 쉽다.
    /// </summary>
    public sealed class TargetMarkerPoolGroup : MonoBehaviour
    {
        [SerializeField] private TargetMarkerType markerType;
        [SerializeField] private RectTransform template;

        private readonly Stack<RectTransform> availableMarkers = new Stack<RectTransform>();
        private readonly HashSet<RectTransform> activeMarkers = new HashSet<RectTransform>();

        /// <summary>이 그룹이 제공하는 Marker 종류를 반환한다.</summary>
        public TargetMarkerType MarkerType => markerType;

        /// <summary>이 그룹에서 복제하는 Marker 템플릿을 반환한다.</summary>
        public RectTransform Template => template;

        /// <summary>
        /// 템플릿 참조를 보완하고 템플릿을 숨겨 런타임 인스턴스와 구분한다.
        /// </summary>
        public void Initialize()
        {
            template ??= FindTemplateChild();
            if (template == null)
            {
                GameLog.LogError($"TargetMarkerPoolGroup '{name}'에 Marker 템플릿이 없습니다.", this);
                return;
            }

            template.gameObject.SetActive(false);
        }

        /// <summary>사용 가능한 인스턴스를 꺼내거나 템플릿을 복제해 반환한다.</summary>
        public RectTransform Acquire()
        {
            if (template == null)
            {
                return null;
            }

            RectTransform instance = availableMarkers.Count > 0
                ? availableMarkers.Pop()
                : Instantiate(template, transform);

            instance.name = $"{template.name} (Pooled)";
            instance.SetParent(transform, false);
            instance.gameObject.SetActive(true);
            activeMarkers.Add(instance);
            return instance;
        }

        /// <summary>사용이 끝난 Marker 인스턴스를 이 종류의 풀에 반납한다.</summary>
        public void Release(RectTransform instance)
        {
            if (instance == null || !activeMarkers.Remove(instance))
            {
                return;
            }

            instance.gameObject.SetActive(false);
            instance.SetParent(transform, false);
            availableMarkers.Push(instance);
        }

        /// <summary>직접 자식 중 첫 번째 RectTransform을 템플릿으로 찾는다.</summary>
        private RectTransform FindTemplateChild()
        {
            for (int index = 0; index < transform.childCount; index++)
            {
                RectTransform child = transform.GetChild(index) as RectTransform;
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>컴포넌트를 처음 추가할 때 자식 Marker를 템플릿으로 연결한다.</summary>
        private void Reset()
        {
            template = FindTemplateChild();
        }
    }
}
