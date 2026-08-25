using System.Collections.Generic;
using HauntedHouse.Commands;
using HauntedHouse.Enemies;
using HauntedHouse.Progression;
using HauntedHouse.Stealth;
using HauntedHouse.Threat;
using HauntedHouse.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HauntedHouse.Editor
{
    /// <summary>
    /// 현재 활성 씬의 TargetPresenter, EnemyController와 관련 시스템 참조를 일괄 연결하는 Editor 도구다.
    /// </summary>
    public static class SceneReferenceAutoSetup
    {
        private const string MenuPath = "Tools/Haunted House/씬 시스템 참조 자동 등록";

        /// <summary>현재 활성 씬의 시스템 배열 참조를 한 번에 수집하고 저장한다.</summary>
        [MenuItem(MenuPath)]
        public static void PopulateActiveSceneReferences()
        {
            // Play Mode에서는 씬 에셋을 편집하지 않도록 메뉴 동작을 차단한다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "씬 참조 자동 등록 실패",
                    "Play Mode가 끝난 뒤 다시 실행해 주세요.",
                    "확인");
                return;
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorUtility.DisplayDialog(
                    "씬 참조 자동 등록 실패",
                    "유효하게 로드된 활성 씬이 없습니다.",
                    "확인");
                return;
            }

            AssignSceneReferences(scene);
        }

        /// <summary>
        /// 활성 씬 메뉴의 실행 가능 여부를 검사해 Play Mode와 유효하지 않은 씬에서 메뉴를 비활성화한다.
        /// </summary>
        /// <returns>현재 메뉴를 실행할 수 있으면 true.</returns>
        [MenuItem(MenuPath, true)]
        private static bool ValidatePopulateActiveSceneReferences()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                   scene.IsValid() && scene.isLoaded;
        }

        /// <summary>
        /// 지정한 씬의 대상 컴포넌트를 수집해 관련 시스템의 배열 필드에 중복 없이 저장한다.
        /// </summary>
        /// <param name="scene">참조를 수집하고 저장할 씬.</param>
        private static void AssignSceneReferences(Scene scene)
        {

            SceneReferenceSet references = CollectSceneReferences(scene);
            var systems = new List<UnityEngine.Object>();
            systems.AddRange(references.Runners);
            systems.AddRange(references.HideSystems);
            systems.AddRange(references.ChaseSystems);
            systems.AddRange(references.AlertSystems);
            systems.AddRange(references.RespawnCoordinators);

            if (systems.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "씬 참조 자동 등록 실패",
                    "현재 씬에서 갱신할 시스템 컴포넌트를 찾을 수 없습니다.",
                    "확인");
                return;
            }

            // 실제 배열 필드를 모두 먼저 확인해 중간 단계까지만 저장되는 부분 갱신을 방지한다.
            if (!ValidateArrayProperty(references.Runners, "presenters") ||
                !ValidateArrayProperty(references.HideSystems, "targetPresenters") ||
                !ValidateArrayProperty(references.ChaseSystems, "enemies") ||
                !ValidateArrayProperty(references.AlertSystems, "enemies") ||
                !ValidateArrayProperty(references.RespawnCoordinators, "enemies") ||
                !ValidateArrayProperty(references.RespawnCoordinators, "completedStateParticipants"))
            {
                return;
            }

            // 모든 시스템을 하나의 Undo 작업으로 기록해 버튼 한 번의 결과를 통째로 되돌릴 수 있게 한다.
            Undo.RecordObjects(systems.ToArray(), "Register Scene System References");
            SetObjectArray(references.Runners, "presenters", references.TargetPresenters);
            SetObjectArray(references.HideSystems, "targetPresenters", references.TargetPresenters);
            SetObjectArray(references.ChaseSystems, "enemies", references.Enemies);
            SetObjectArray(references.AlertSystems, "enemies", references.Enemies);
            SetObjectArray(references.RespawnCoordinators, "enemies", references.Enemies);
            SetObjectArray(references.RespawnCoordinators, "completedStateParticipants", references.CompletedStateParticipants);

            EditorSceneManager.MarkSceneDirty(scene);

            EditorUtility.DisplayDialog(
                "씬 참조 자동 등록 완료",
                $"TargetPresenter {references.TargetPresenters.Count}개, EnemyController {references.Enemies.Count}개, " +
                $"완료 상태 참여자 {references.CompletedStateParticipants.Count}개를 " +
                $"시스템 {systems.Count}개에 중복 없이 등록했습니다.",
                "확인");
        }

        /// <summary>지정한 시스템 목록에 배열형 SerializedField가 존재하는지 사전 검증한다.</summary>
        /// <typeparam name="T">검사할 시스템 컴포넌트 유형.</typeparam>
        /// <param name="targets">검사할 시스템 목록.</param>
        /// <param name="propertyName">SerializedField 이름.</param>
        /// <returns>모든 대상에서 배열 필드를 찾았으면 true.</returns>
        private static bool ValidateArrayProperty<T>(List<T> targets, string propertyName)
            where T : UnityEngine.Object
        {
            foreach (T target in targets)
            {
                SerializedObject serializedObject = new SerializedObject(target);
                SerializedProperty property = serializedObject.FindProperty(propertyName);
                if (property != null && property.isArray)
                {
                    continue;
                }

                EditorUtility.DisplayDialog(
                    "씬 참조 자동 등록 실패",
                    $"{target.GetType().Name}.{propertyName} 배열 필드를 찾을 수 없습니다.",
                    "확인");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 비활성 오브젝트를 포함해 현재 씬의 대상 컴포넌트를 계층 순서대로 수집한다.
        /// </summary>
        /// <param name="scene">수집할 씬.</param>
        /// <returns>씬 시스템에 주입할 고유 참조 모음.</returns>
        private static SceneReferenceSet CollectSceneReferences(Scene scene)
        {
            var references = new SceneReferenceSet();

            // FindObjectsByType 대신 루트부터 순회해 씬 계층과 일치하는 결정적인 배열 순서를 유지한다.
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                CollectSceneReferences(rootObject.transform, references);
            }

            return references;
        }

        /// <summary>현재 계층 노드와 하위 노드에서 필요한 컴포넌트를 중복 없이 수집한다.</summary>
        /// <param name="current">현재 순회 중인 계층 노드.</param>
        /// <param name="references">수집 결과를 누적할 참조 모음.</param>
        private static void CollectSceneReferences(Transform current, SceneReferenceSet references)
        {
            // GetComponents는 비활성 GameObject의 컴포넌트도 반환하므로 비활성 적과 Target도 등록된다.
            AddUnique(current.GetComponents<TargetPresenter>(), references.TargetPresenters, references.UniquePresenters);
            AddUnique(current.GetComponents<EnemyController>(), references.Enemies, references.UniqueEnemies);
            AddUnique(current.GetComponents<TargetCommandRunner>(), references.Runners, references.UniqueRunners);
            AddUnique(current.GetComponents<StealthHideSystem>(), references.HideSystems, references.UniqueHideSystems);
            AddUnique(current.GetComponents<EnemyChaseSystem>(), references.ChaseSystems, references.UniqueChaseSystems);
            AddUnique(current.GetComponents<DangerAlertSystem>(), references.AlertSystems, references.UniqueAlertSystems);
            AddUnique(current.GetComponents<RespawnCoordinator>(), references.RespawnCoordinators, references.UniqueRespawnCoordinators);

            // Unity 직렬화는 인터페이스 배열을 지원하지 않으므로 MonoBehaviour로 저장하되,
            // 실제로 ICompletedState를 구현한 컴포넌트만 완료 상태 참여자 목록에 넣는다.
            foreach (MonoBehaviour component in current.GetComponents<MonoBehaviour>())
            {
                if (component is ICompletedState && references.UniqueCompletedStateParticipants.Add(component))
                {
                    references.CompletedStateParticipants.Add(component);
                }
            }

            // 자식 순서를 그대로 따라가므로 각 배열의 Inspector 순서도 씬 계층과 일치한다.
            foreach (Transform child in current)
            {
                CollectSceneReferences(child, references);
            }
        }

        /// <summary>배열 요소를 HashSet으로 검사한 뒤 처음 발견된 컴포넌트만 목록에 추가한다.</summary>
        /// <typeparam name="T">수집할 Unity 컴포넌트 유형.</typeparam>
        /// <param name="components">현재 GameObject에서 찾은 컴포넌트 배열.</param>
        /// <param name="values">고유 컴포넌트를 누적할 목록.</param>
        /// <param name="uniqueValues">중복 검사용 집합.</param>
        private static void AddUnique<T>(
            T[] components,
            List<T> values,
            HashSet<T> uniqueValues)
            where T : UnityEngine.Object
        {
            foreach (T component in components)
            {
                if (component != null && uniqueValues.Add(component))
                {
                    values.Add(component);
                }
            }
        }

        /// <summary>여러 시스템의 SerializedField 배열을 씬에서 수집한 참조로 교체한다.</summary>
        /// <typeparam name="T">배열을 소유한 시스템 컴포넌트 유형.</typeparam>
        /// <param name="targets">배열을 갱신할 시스템 목록.</param>
        /// <param name="propertyName">SerializedField 이름.</param>
        /// <param name="values">저장할 고유 참조 목록.</param>
        private static void SetObjectArray<T>(
            List<T> targets,
            string propertyName,
            List<TargetPresenter> values)
            where T : UnityEngine.Object
        {
            var objectValues = new UnityEngine.Object[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                objectValues[index] = values[index];
            }

            SetObjectArray(targets, propertyName, objectValues);
        }

        /// <summary>EnemyController 목록을 시스템 배열에 저장한다.</summary>
        /// <typeparam name="T">배열을 소유한 시스템 컴포넌트 유형.</typeparam>
        /// <param name="targets">배열을 갱신할 시스템 목록.</param>
        /// <param name="propertyName">SerializedField 이름.</param>
        /// <param name="values">저장할 고유 적 목록.</param>
        private static void SetObjectArray<T>(
            List<T> targets,
            string propertyName,
            List<EnemyController> values)
            where T : UnityEngine.Object
        {
            var objectValues = new UnityEngine.Object[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                objectValues[index] = values[index];
            }

            SetObjectArray(targets, propertyName, objectValues);
        }

        /// <summary>완료 상태 참여자 목록을 시스템 배열에 저장한다.</summary>
        /// <typeparam name="T">배열을 소유한 시스템 컴포넌트 유형.</typeparam>
        /// <param name="targets">배열을 갱신할 시스템 목록.</param>
        /// <param name="propertyName">SerializedField 이름.</param>
        /// <param name="values">저장할 완료 상태 참여자 목록.</param>
        private static void SetObjectArray<T>(
            List<T> targets,
            string propertyName,
            List<MonoBehaviour> values)
            where T : UnityEngine.Object
        {
            var objectValues = new UnityEngine.Object[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                objectValues[index] = values[index];
            }

            SetObjectArray(targets, propertyName, objectValues);
        }

        /// <summary>SerializedObject 배열 필드를 실제 Unity 오브젝트 참조 배열로 기록한다.</summary>
        /// <typeparam name="T">배열을 소유한 시스템 컴포넌트 유형.</typeparam>
        /// <param name="targets">배열을 갱신할 시스템 목록.</param>
        /// <param name="propertyName">SerializedField 이름.</param>
        /// <param name="values">저장할 참조 배열.</param>
        private static void SetObjectArray<T>(
            List<T> targets,
            string propertyName,
            UnityEngine.Object[] values)
            where T : UnityEngine.Object
        {
            foreach (T target in targets)
            {
                SerializedObject serializedObject = new SerializedObject(target);
                SerializedProperty property = serializedObject.FindProperty(propertyName);
                if (property == null || !property.isArray)
                {
                    throw new System.InvalidOperationException(
                        $"{target.GetType().Name}.{propertyName} 배열 필드를 찾을 수 없습니다.");
                }

                property.arraySize = values.Length;
                for (int index = 0; index < values.Length; index++)
                {
                    property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        /// <summary>씬 자동 등록에 사용되는 시스템과 고유 참조 목록을 보관한다.</summary>
        private sealed class SceneReferenceSet
        {
            public readonly List<TargetPresenter> TargetPresenters = new();
            public readonly List<EnemyController> Enemies = new();
            public readonly List<MonoBehaviour> CompletedStateParticipants = new();
            public readonly List<TargetCommandRunner> Runners = new();
            public readonly List<StealthHideSystem> HideSystems = new();
            public readonly List<EnemyChaseSystem> ChaseSystems = new();
            public readonly List<DangerAlertSystem> AlertSystems = new();
            public readonly List<RespawnCoordinator> RespawnCoordinators = new();

            public readonly HashSet<TargetPresenter> UniquePresenters = new();
            public readonly HashSet<EnemyController> UniqueEnemies = new();
            public readonly HashSet<MonoBehaviour> UniqueCompletedStateParticipants = new();
            public readonly HashSet<TargetCommandRunner> UniqueRunners = new();
            public readonly HashSet<StealthHideSystem> UniqueHideSystems = new();
            public readonly HashSet<EnemyChaseSystem> UniqueChaseSystems = new();
            public readonly HashSet<DangerAlertSystem> UniqueAlertSystems = new();
            public readonly HashSet<RespawnCoordinator> UniqueRespawnCoordinators = new();
        }
    }
}
