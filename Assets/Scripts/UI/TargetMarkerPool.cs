using System;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedHouse.UI
{
    /// <summary>
    /// TargetMarkerPoolGroup을 Marker 종류별로 등록하고 해당 그룹의 인스턴스 재사용을 중계한다.
    /// 실제 풀 인스턴스는 Target UI/Markers 아래 종류별 부모 오브젝트의 자식으로 생성된다.
    /// </summary>
    public sealed class TargetMarkerPool : MonoBehaviour
    {
        [SerializeField] private RectTransform poolRoot;
        [SerializeField] private TargetMarkerPoolGroup[] groups = Array.Empty<TargetMarkerPoolGroup>();

        private readonly Dictionary<TargetMarkerType, TargetMarkerPoolGroup> groupsByType =
            new Dictionary<TargetMarkerType, TargetMarkerPoolGroup>();

        /// <summary>Marker 종류별 그룹이 배치된 루트를 반환한다.</summary>
        public RectTransform PoolRoot => poolRoot;

        /// <summary>풀 루트와 종류별 그룹을 초기화하고 그룹 중복을 검사한다.</summary>
        private void Awake()
        {
            poolRoot ??= GetComponent<RectTransform>();

            if (groups == null || groups.Length == 0)
            {
                groups = GetComponentsInChildren<TargetMarkerPoolGroup>(true);
            }

            groupsByType.Clear();
            foreach (TargetMarkerPoolGroup group in groups)
            {
                if (group == null)
                {
                    continue;
                }

                if (groupsByType.ContainsKey(group.MarkerType))
                {
                    GameLog.LogError(
                        $"TargetMarkerPool에 Marker 종류가 중복 등록됐습니다: {group.MarkerType}",
                        group);
                    continue;
                }

                group.Initialize();
                groupsByType.Add(group.MarkerType, group);
            }
        }

        /// <summary>지정한 Marker 종류의 그룹에서 인스턴스를 하나 꺼낸다.</summary>
        /// <param name="markerType">사용할 Marker 종류.</param>
        /// <returns>표시할 Marker 인스턴스. 등록되지 않은 종류면 null.</returns>
        public RectTransform Acquire(TargetMarkerType markerType)
        {
            if (!groupsByType.TryGetValue(markerType, out TargetMarkerPoolGroup group))
            {
                GameLog.LogError($"TargetMarkerPool에 Marker 종류가 등록되지 않았습니다: {markerType}", this);
                return null;
            }

            return group.Acquire();
        }

        /// <summary>지정한 Marker 종류의 그룹으로 사용이 끝난 인스턴스를 반납한다.</summary>
        /// <param name="markerType">반납할 Marker 종류.</param>
        /// <param name="instance">반납할 Marker 인스턴스.</param>
        public void Release(TargetMarkerType markerType, RectTransform instance)
        {
            if (groupsByType.TryGetValue(markerType, out TargetMarkerPoolGroup group))
            {
                group.Release(instance);
            }
        }

        /// <summary>컴포넌트를 처음 추가할 때 루트와 종류별 그룹을 자동 연결한다.</summary>
        private void Reset()
        {
            poolRoot = GetComponent<RectTransform>();
            groups = GetComponentsInChildren<TargetMarkerPoolGroup>(true);
        }
    }
}
