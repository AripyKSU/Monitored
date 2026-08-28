using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Interaction;
using HauntedHouse.UI;
using HauntedHouse.World;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox에 배치된 Door_V3와 SlideDoor 프리팹 인스턴스를 구성하는 Editor 도구다.
/// 벽 Mesh나 Lintel 오브젝트가 아니라 실제 프리팹 원본과 그 인스턴스만 대상으로 삼는다.
/// </summary>
public static class CoreSandboxDoorSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string DoorV3PrefabPath = "Assets/Prefabs/Walls/Door_V3.prefab";
    private const string SlideDoorPrefabPath = "Assets/Prefabs/Walls/SlideDoor.prefab";
    private const string AnimationFolder = "Assets/Arts/Animations/PrefabDoors";
    private const string NavigationMaterialFolder = "Assets/Arts/GeneratedNavigation";
    private const string InvisibleNavigationMaterialPath = NavigationMaterialFolder + "/InvisibleNavMeshSource.mat";
    private const string OpenTrigger = "Open";
    private const string CloseTrigger = "Close";
    private const string ClosedState = "Closed";
    private const string OpenState = "Open";
    private const float AnimationDuration = 0.4f;
    private const string TraversalAnchorsName = "TraversalAnchors";
    private const string PositiveApproachName = "Positive_Approach";
    private const string NegativeApproachName = "Negative_Approach";
    private const string PositivePassageName = "Positive_Passage";
    private const string NegativePassageName = "Negative_Passage";
    private const float FallbackPlayerRadius = 2.45f;
    private const float DoorClearanceMargin = 3f;
    private const float PassageClearanceMargin = 0.35f;
    private const float MaximumCameraEntryDistance = 20f;
    private const float CameraEntryDistanceStep = 0.5f;
    private const float CameraViewportMargin = 0.2f;
    private const float PointHeight = 1.1f;
    private const float SlideDistance = 1.1f;

    private sealed class DoorAnimationData
    {
        public string Path;
        public Vector3 ClosedPosition;
        public Vector3 OpenPosition;
        public float ClosedY;
        public float OpenY;
    }

    private sealed class DoorInstance
    {
        public GameObject Root;
        public string PrefabPath;
        public Room SourceRoom;
        public Room DestinationRoom;
    }

    /// <summary>
    /// 실제 Door_V3·SlideDoor 프리팹 원본을 준비하고 CoreSandbox 인스턴스의 Doorway 참조를 저장한다.
    /// </summary>
    [MenuItem("Haunted House/Setup CoreSandbox Prefab Doors")]
    public static void Setup()
    {
        if (!TryGetCoreSandbox(out Scene scene))
        {
            return;
        }

        // 이전 잘못된 메시 기반 실행이 만든 Door_* 루트와 Lintel 이동을 먼저 되돌린다.
        int removedIncorrectRoots = CleanupIncorrectMeshDoorSetup();
        CleanupGeneratedDoorNavigation();
        CleanupOldGeneratedAnimationAssets();
        EnsureAnimationFolder();

        float playerRadius = GetPlayerCollisionRadius();
        ConfigurePrefabAsset(DoorV3PrefabPath, true, playerRadius);
        ConfigurePrefabAsset(SlideDoorPrefabPath, false, playerRadius);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        PlayerRoomTracker tracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        CameraDirector cameraDirector = UnityEngine.Object.FindFirstObjectByType<CameraDirector>(FindObjectsInactive.Include);
        TargetMarkerPool markerPool = UnityEngine.Object.FindFirstObjectByType<TargetMarkerPool>(FindObjectsInactive.Include);
        Camera worldCamera = Camera.main;

        int configuredCount = 0;
        List<string> skipped = new List<string>();
        foreach (DoorInstance door in CollectPrefabDoorInstances())
        {
            door.SourceRoom = ResolveSourceRoom(door.Root);
            door.DestinationRoom = ResolveDestinationRoom(door.Root.name, door.SourceRoom);

            if (door.SourceRoom == null || door.DestinationRoom == null || door.SourceRoom == door.DestinationRoom)
            {
                skipped.Add($"{door.Root.name}: 연결할 두 Room을 결정하지 못했습니다.");
                continue;
            }

            EnsureBidirectionalAdjacency(door.SourceRoom, door.DestinationRoom);
            ConfigureSceneDoor(door, tracker, cameraDirector, markerPool, worldCamera);
            configuredCount++;
        }

        // 안전 앵커 아래에 NavMesh가 없던 계단문 랜딩을 포함해 다시 Bake한 뒤 Link 끝점을 최종 폴리곤에 맞춘다.
        RebuildNavigationAndRefreshLinks();
        RestoreInstanceAnimatorControllers();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (skipped.Count == 0)
        {
            GameLog.Log(
                $"CoreSandbox 프리팹 door 설정 완료: {configuredCount}개 인스턴스 구성, 잘못된 루트 {removedIncorrectRoots}개 정리했습니다.");
        }
        else
        {
            GameLog.LogWarning(
                $"CoreSandbox 프리팹 door 설정 완료: {configuredCount}개 구성, {skipped.Count}개 보류\n- " +
                string.Join("\n- ", skipped));
        }
    }

    /// <summary>
    /// 실제 Door_V3·SlideDoor 프리팹 인스턴스에 필요한 구성 요소와 참조가 있는지 검사한다.
    /// </summary>
    [MenuItem("Haunted House/Validate CoreSandbox Prefab Doors")]
    public static void Validate()
    {
        if (!TryGetCoreSandbox(out _))
        {
            return;
        }

        List<string> problems = new List<string>();
        List<DoorInstance> instances = CollectPrefabDoorInstances().ToList();
        foreach (DoorInstance door in instances)
        {
            DoorController controller = door.Root.GetComponent<DoorController>();
            NavMeshModifier modifier = door.Root.GetComponent<NavMeshModifier>();
            Animator animator = door.Root.transform.Find("DoorVisual")?.GetComponent<Animator>();
            Doorway doorway = door.Root.GetComponent<Doorway>();

            if (controller == null || animator == null || animator.runtimeAnimatorController == null)
            {
                problems.Add($"{door.Root.name}: DoorVisual Animator 또는 DoorController가 없습니다.");
            }

            if (modifier == null || !modifier.ignoreFromBuild || !modifier.applyToChildren)
            {
                problems.Add($"{door.Root.name}: NavMeshModifier가 Remove Object가 아닙니다.");
            }

            if (doorway == null)
            {
                problems.Add($"{door.Root.name}: Doorway가 없습니다.");
            }
            else if (!doorway.TryValidateConfiguration(out string configurationError))
            {
                problems.Add(configurationError);
            }
            else
            {
                ValidateTraversalGeometry(door.Root, doorway, problems);
            }

            if (door.Root.GetComponentsInChildren<DoorTarget>(true).Length != 2 ||
                door.Root.GetComponentsInChildren<DoorwaySensor>(true).Length != 2)
            {
                problems.Add($"{door.Root.name}: 방향별 DoorTarget과 Sensor가 정확히 2개가 아닙니다.");
            }
        }

        if (instances.Count == 0)
        {
            problems.Add("Door_V3 또는 SlideDoor 프리팹 인스턴스를 찾지 못했습니다.");
        }

        if (problems.Count == 0)
        {
            GameLog.Log($"CoreSandbox 프리팹 door 검증 성공: {instances.Count}개 인스턴스가 유효합니다.");
        }
        else
        {
            GameLog.LogError("CoreSandbox 프리팹 door 검증 실패:\n- " + string.Join("\n- ", problems));
        }
    }

    /// <summary>
    /// 양방향 접근·통과 지점이 문 프레임 법선 위에 있고 플레이어 충돌체 전체가 문 밖에 놓이는지 검사한다.
    /// 각 지점이 실제 NavMesh에도 놓이는지 함께 확인해 런타임 이동 실패를 사전에 찾는다.
    /// </summary>
    private static void ValidateTraversalGeometry(GameObject doorRoot, Doorway doorway, List<string> problems)
    {
        Renderer frameRenderer = GetDoorFrameRenderer(doorRoot);
        Vector3 doorCenter = GetDoorPassageCenter(doorRoot);
        Vector3 normal = DetermineMeshNormal(doorRoot.transform, frameRenderer);
        float requiredDistance = GetProjectedHalfExtent(frameRenderer, normal) +
                                 GetPlayerCollisionRadius() + PassageClearanceMargin;

        if (!doorway.TryGetTraversal(doorway.RoomA, out DoorTraversal fromA) ||
            !doorway.TryGetTraversal(doorway.RoomB, out DoorTraversal fromB))
        {
            problems.Add($"{doorRoot.name}: 양방향 DoorTraversal을 읽을 수 없습니다.");
            return;
        }

        Transform[] points =
        {
            fromA.EntryPoint,
            fromA.PassageStart,
            fromA.DestinationEntryPoint,
            fromB.EntryPoint,
            fromB.PassageStart,
            fromB.DestinationEntryPoint
        };

        foreach (Transform point in points.Distinct())
        {
            Vector3 offset = Vector3.ProjectOnPlane(point.position - doorCenter, Vector3.up);
            float axialDistance = Mathf.Abs(Vector3.Dot(offset, normal));
            Vector3 lateralOffset = offset - normal * Vector3.Dot(offset, normal);
            if (lateralOffset.magnitude > 0.1f)
            {
                problems.Add($"{doorRoot.name}: '{point.name}'이 문 메시 통과 축에서 {lateralOffset.magnitude:F2}m 벗어났습니다.");
            }

            if (axialDistance + 0.05f < requiredDistance)
            {
                problems.Add(
                    $"{doorRoot.name}: '{point.name}'의 문 이격 거리 {axialDistance:F2}m가 안전 거리 {requiredDistance:F2}m보다 짧습니다.");
            }

            // 앵커 Y는 캐릭터 피벗 높이이므로 NavMesh 바닥까지의 수직 차이는 허용하고 수평 오차만 검사한다.
            if (!NavMesh.SamplePosition(point.position, out NavMeshHit hit, 4f, NavMesh.AllAreas) ||
                Vector3.ProjectOnPlane(hit.position - point.position, Vector3.up).magnitude > 0.35f)
            {
                problems.Add($"{doorRoot.name}: '{point.name}'이 유효한 NavMesh 위에 없습니다.");
            }
        }

        float aEntrySide = Vector3.Dot(fromA.EntryPoint.position - doorCenter, normal);
        float aPassageSide = Vector3.Dot(fromA.PassageStart.position - doorCenter, normal);
        if (Mathf.Sign(aEntrySide) == Mathf.Sign(aPassageSide))
        {
            problems.Add($"{doorRoot.name}: RoomA 통과 목표가 문 반대편을 향하지 않습니다.");
        }
    }

    /// <summary>
    /// 현재 활성 씬이 작업 대상인지 확인한다.
    /// </summary>
    private static bool TryGetCoreSandbox(out Scene scene)
    {
        scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            GameLog.LogError($"활성 씬이 '{ScenePath}'가 아닙니다.");
            return false;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogError("CoreSandbox 프리팹 door 설정은 Play Mode에서 실행할 수 없습니다.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 씬에서 실제 문 잎을 가진 Door_V3·SlideDoor 프리팹 루트만 수집한다.
    /// DoorV3_WallShell처럼 문 잎이 제거된 벽 쉘 인스턴스는 제외한다.
    /// </summary>
    private static IEnumerable<DoorInstance> CollectPrefabDoorInstances()
    {
        HashSet<GameObject> visited = new HashSet<GameObject>();
        foreach (Transform transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
            if (root == null || !visited.Add(root))
            {
                continue;
            }

            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (prefabPath != DoorV3PrefabPath && prefabPath != SlideDoorPrefabPath)
            {
                continue;
            }

            if (!HasMovingDoorLeaf(root))
            {
                continue;
            }

            yield return new DoorInstance
            {
                Root = root,
                PrefabPath = prefabPath
            };
        }
    }

    /// <summary>
    /// 프리팹 계층에 실제로 애니메이션할 Door 잎이 있는지 확인한다.
    /// </summary>
    private static bool HasMovingDoorLeaf(GameObject root)
    {
        return root.GetComponentsInChildren<Transform>(true).Any(transform =>
            (transform.name == "Door" || transform.name == "Door (1)") &&
            transform.GetComponent<MeshRenderer>() != null);
    }

    /// <summary>
    /// 잘못된 실행에서 생성한 Door_* 루트를 제거하고 Lintel 메시를 GeneratedArchitecture로 되돌린다.
    /// </summary>
    private static int CleanupIncorrectMeshDoorSetup()
    {
        GameObject generatedArchitecture = GameObject.Find("GeneratedArchitecture");
        List<Doorway> generatedDoorways = UnityEngine.Object.FindObjectsByType<Doorway>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(doorway => doorway.name.StartsWith("Door_", StringComparison.Ordinal) &&
                              doorway.name != "Door_A_B" &&
                              doorway.name != "Door_B_C" &&
                              !IsSupportedDoorPrefabInstance(doorway.gameObject))
            .ToList();

        foreach (Doorway doorway in generatedDoorways)
        {
            Transform[] panels = doorway.GetComponentsInChildren<Transform>(true)
                .Where(transform => IsIncorrectMeshPanel(transform.gameObject))
                .ToArray();

            foreach (Transform panel in panels)
            {
                if (generatedArchitecture != null)
                {
                    panel.SetParent(generatedArchitecture.transform, true);
                }

                // 최초 맵 메시가 정적 오브젝트였으므로 잘못된 실행에서 바뀐 Static 플래그도 복구한다.
                GameObjectUtility.SetStaticEditorFlags(panel.gameObject, (StaticEditorFlags)(-1));
            }

            Undo.DestroyObjectImmediate(doorway.gameObject);
        }

        return generatedDoorways.Count;
    }

    /// <summary>이전 실행에서 생성한 계단문 전용 Navigation 루트를 제거해 오래된 랜딩이 누적되지 않게 한다.</summary>
    private static void CleanupGeneratedDoorNavigation()
    {
        GameObject navigationRoot = GameObject.Find("GeneratedDoorNavigation");
        if (navigationRoot != null)
        {
            navigationRoot.GetComponent<NavMeshSurface>()?.RemoveData();
            Undo.DestroyObjectImmediate(navigationRoot);
        }
    }

    /// <summary>현재 오브젝트가 지원 대상 Door_V3 또는 SlideDoor 프리팹 인스턴스인지 확인한다.</summary>
    private static bool IsSupportedDoorPrefabInstance(GameObject gameObject)
    {
        GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
        if (root == null)
        {
            return false;
        }

        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
        return prefabPath == DoorV3PrefabPath || prefabPath == SlideDoorPrefabPath;
    }

    /// <summary>
    /// 이전 잘못된 실행이 만든 인스턴스별 애니메이션 에셋을 제거한다.
    /// TestMaps 샘플 Door_A_B·Door_B_C 에셋은 보존한다.
    /// </summary>
    private static void CleanupOldGeneratedAnimationAssets()
    {
        const string oldFolder = "Assets/Arts/Animations/Doors";
        if (!AssetDatabase.IsValidFolder(oldFolder))
        {
            return;
        }

        foreach (string guid in AssetDatabase.FindAssets("", new[] { oldFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.StartsWith("Door_A_B", StringComparison.Ordinal) ||
                fileName.StartsWith("Door_B_C", StringComparison.Ordinal))
            {
                continue;
            }

            AssetDatabase.DeleteAsset(path);
        }
    }

    /// <summary>
    /// 프리팹 원본에 DoorVisual Animator, DoorController, Remove Object를 구성한다.
    /// Room과 Doorway는 씬 인스턴스마다 다르므로 원본에는 저장하지 않는다.
    /// </summary>
    private static void ConfigurePrefabAsset(string prefabPath, bool hinged, float playerRadius)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform doorVisual = GetOrCreateChild(prefabRoot.transform, "DoorVisual");
            if (hinged)
            {
                MoveChildUnderVisual(prefabRoot.transform.Find("Door_Frame"), doorVisual);
            }
            else
            {
                MoveChildUnderVisual(prefabRoot.transform.Find("Door_V2"), doorVisual);
            }

            List<Transform> animatedTransforms = GetAnimatedTransforms(doorVisual);
            Animator animator = GetOrAdd<Animator>(doorVisual.gameObject);
            animator.runtimeAnimatorController = CreateAnimatorController(
                hinged ? "Door_V3" : "SlideDoor",
                doorVisual,
                animatedTransforms,
                hinged);

            DoorController controller = GetOrAdd<DoorController>(prefabRoot);
            Collider[] movingLeafColliders = doorVisual.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name == "Door" ||
                                    transform.name == "Door (1)" ||
                                    transform.name.Contains("Door_Frame", StringComparison.Ordinal))
                .SelectMany(transform => transform.GetComponentsInChildren<Collider>(true))
                .Distinct()
                .ToArray();
            ConfigureDoorController(controller, animator, AnimationDuration, movingLeafColliders);

            // 모든 인스턴스가 같은 메시 기준 위치를 상속하도록 통과 앵커를 프리팹 원본에 저장한다.
            ConfigureTraversalAnchors(prefabRoot, playerRadius);

            NavMeshModifier modifier = GetOrAdd<NavMeshModifier>(prefabRoot);
            modifier.ignoreFromBuild = true;
            modifier.applyToChildren = true;
            EditorUtility.SetDirty(modifier);
            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    /// <summary>
    /// 문 프레임 메시의 중심과 두께, 플레이어 충돌 반경을 사용해 양쪽 통과 앵커를 프리팹에 저장한다.
    /// 방 A/B는 인스턴스마다 달라질 수 있으므로 프리팹에는 양·음 방향의 중립적인 이름을 사용한다.
    /// </summary>
    private static void ConfigureTraversalAnchors(GameObject prefabRoot, float playerRadius)
    {
        Renderer frameRenderer = GetDoorFrameRenderer(prefabRoot);
        Vector3 doorCenter = GetDoorPassageCenter(prefabRoot);
        Vector3 positiveNormal = DetermineMeshNormal(prefabRoot.transform, frameRenderer);
        float frameHalfThickness = GetProjectedHalfExtent(frameRenderer, positiveNormal);
        float approachDistance = frameHalfThickness + Mathf.Max(0f, playerRadius) + DoorClearanceMargin;
        float passageDistance = frameHalfThickness + Mathf.Max(0f, playerRadius) + PassageClearanceMargin;

        Transform anchors = GetOrCreateChild(prefabRoot.transform, TraversalAnchorsName);
        anchors.localPosition = Vector3.zero;
        anchors.localRotation = Quaternion.identity;
        anchors.localScale = Vector3.one;

        Transform positiveApproach = GetOrCreateChild(anchors, PositiveApproachName);
        Transform negativeApproach = GetOrCreateChild(anchors, NegativeApproachName);
        Transform positivePassage = GetOrCreateChild(anchors, PositivePassageName);
        Transform negativePassage = GetOrCreateChild(anchors, NegativePassageName);

        // 최종 접근점과 통과 판정점 모두 플레이어 캡슐 전체가 프레임을 벗어난 위치에 둔다.
        SetAnchorPosition(prefabRoot.transform, positiveApproach, doorCenter, positiveNormal, approachDistance);
        SetAnchorPosition(prefabRoot.transform, negativeApproach, doorCenter, -positiveNormal, approachDistance);
        SetAnchorPosition(prefabRoot.transform, positivePassage, doorCenter, positiveNormal, passageDistance);
        SetAnchorPosition(prefabRoot.transform, negativePassage, doorCenter, -positiveNormal, passageDistance);
        OrientTowardPoint(positiveApproach, doorCenter);
        OrientTowardPoint(negativeApproach, doorCenter);
        EditorUtility.SetDirty(anchors.gameObject);
    }

    /// <summary>프리팹 루트 좌표계에 메시 중심 기준 앵커 위치를 저장한다.</summary>
    private static void SetAnchorPosition(
        Transform prefabRoot,
        Transform anchor,
        Vector3 doorCenter,
        Vector3 direction,
        float distance)
    {
        anchor.localPosition = ToLocalPoint(prefabRoot, doorCenter, direction, distance);
        anchor.localScale = Vector3.one;
    }

    /// <summary>
    /// 현재 씬 플레이어의 비트리거 Collider 월드 반경을 구한다.
    /// 씬 구성 누락 시에도 기존 캐릭터 크기에 맞춘 보수적인 기본값을 사용한다.
    /// </summary>
    private static float GetPlayerCollisionRadius()
    {
        Player player = UnityEngine.Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        if (player == null)
        {
            return FallbackPlayerRadius;
        }

        Collider[] colliders = player.GetComponentsInChildren<Collider>(true)
            .Where(collider => collider.enabled && !collider.isTrigger)
            .ToArray();
        if (colliders.Length == 0)
        {
            return FallbackPlayerRadius;
        }

        return colliders.Max(collider => Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.z));
    }

    /// <summary>
    /// 프리팹 원본의 기존 visual 브랜치를 DoorVisual 아래로 이동한다.
    /// </summary>
    private static void MoveChildUnderVisual(Transform child, Transform doorVisual)
    {
        if (child != null && child.parent != doorVisual)
        {
            child.SetParent(doorVisual, true);
        }
    }

    /// <summary>
    /// DoorVisual 하위에서 MeshRenderer를 가진 실제 Door 잎만 애니메이션 대상으로 반환한다.
    /// </summary>
    private static List<Transform> GetAnimatedTransforms(Transform doorVisual)
    {
        List<Transform> candidates = doorVisual.GetComponentsInChildren<Transform>(true)
            .Where(transform => transform != doorVisual &&
                                (transform.name == "Door" || transform.name == "Door (1)") &&
                                transform.GetComponent<MeshRenderer>() != null)
            .ToList();

        Renderer frameRenderer = doorVisual.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(renderer => renderer.name.Contains("Door_Frame", StringComparison.Ordinal));
        if (frameRenderer == null || candidates.Count <= 1)
        {
            return candidates;
        }

        // SlideDoor 원본에는 서로 다른 공간의 문 잎이 함께 들어 있으므로
        // 현재 Door_Frame 메시 중심에 가장 가까운 문 잎 하나만 이 인스턴스의 애니메이션 대상으로 사용한다.
        Transform closestLeaf = candidates
            .OrderBy(transform =>
                (transform.GetComponent<Renderer>().bounds.center - frameRenderer.bounds.center).sqrMagnitude)
            .First();
        return new List<Transform> { closestLeaf };
    }

    /// <summary>
    /// 프리팹 종류에 맞는 Animator Controller와 Open·Close AnimationClip을 생성한다.
    /// </summary>
    private static RuntimeAnimatorController CreateAnimatorController(
        string assetName,
        Transform doorVisual,
        List<Transform> animatedTransforms,
        bool hinged)
    {
        string openingPath = AnimationFolder + "/" + assetName + "_Opening.anim";
        string openPosePath = AnimationFolder + "/" + assetName + "_Open.anim";
        string closingPath = AnimationFolder + "/" + assetName + "_Closing.anim";
        string closedPosePath = AnimationFolder + "/" + assetName + "_Closed.anim";
        string controllerPath = AnimationFolder + "/" + assetName + ".controller";

        List<DoorAnimationData> animationData = BuildAnimationData(doorVisual, animatedTransforms, hinged);
        AnimationClip opening = CreateOrUpdateClip(openingPath, animationData, hinged, false);
        AnimationClip openPose = CreateOrUpdateClip(openPosePath, animationData, hinged, true);
        AnimationClip closing = CreateOrUpdateClip(closingPath, animationData, hinged, false, true);
        AnimationClip closedPose = CreateOrUpdateClip(closedPosePath, animationData, hinged, true, true);

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
        {
            AssetDatabase.DeleteAsset(controllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter(OpenTrigger, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(CloseTrigger, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState closed = stateMachine.AddState(ClosedState);
        AnimatorState openingState = stateMachine.AddState("Opening");
        AnimatorState open = stateMachine.AddState(OpenState);
        AnimatorState closingState = stateMachine.AddState("Closing");
        closed.motion = closedPose;
        openingState.motion = opening;
        open.motion = openPose;
        closingState.motion = closing;
        stateMachine.defaultState = closed;

        AddTriggerTransition(closed, openingState, OpenTrigger);
        AddExitTransition(openingState, open);
        AddTriggerTransition(open, closingState, CloseTrigger);
        AddExitTransition(closingState, closed);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// 문 잎의 현재 포즈와 프리팹 종류별 열린 포즈를 계산한다.
    /// </summary>
    private static List<DoorAnimationData> BuildAnimationData(
        Transform doorVisual,
        List<Transform> transforms,
        bool hinged)
    {
        Vector3 center = transforms.Count == 0
            ? Vector3.zero
            : transforms.Aggregate(Vector3.zero, (sum, transform) => sum + transform.localPosition) / transforms.Count;
        List<DoorAnimationData> data = new List<DoorAnimationData>();

        for (int index = 0; index < transforms.Count; index++)
        {
            Transform transform = transforms[index];
            Vector3 closedPosition = transform.localPosition;
            Vector3 openPosition = closedPosition;
            float closedY = transform.localEulerAngles.y;
            float openY = closedY;

            if (hinged)
            {
                openY += 90f;
            }
            else
            {
                float side = closedPosition.x - center.x;
                if (Mathf.Abs(side) < 0.01f)
                {
                    // SlideDoor처럼 문 잎 하나만 애니메이션 대상으로 남은 경우에는
                    // 양의 X축으로 밀어 반대편 고정 문 잎 뒤에 포개지도록 연다.
                    side = index % 2 == 0 ? 1f : -1f;
                }

                openPosition.x += Mathf.Sign(side) * SlideDistance;
            }

            data.Add(new DoorAnimationData
            {
                Path = AnimationUtility.CalculateTransformPath(transform, doorVisual),
                ClosedPosition = closedPosition,
                OpenPosition = openPosition,
                ClosedY = closedY,
                OpenY = openY
            });
        }

        return data;
    }

    /// <summary>
    /// 하나의 AnimationClip에 닫힘·열림 포즈 또는 전환 곡선을 저장한다.
    /// </summary>
    private static AnimationClip CreateOrUpdateClip(
        string path,
        List<DoorAnimationData> data,
        bool hinged,
        bool poseOnly,
        bool reverse = false)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(clip, path);
        }
        else
        {
            clip.ClearCurves();
        }

        float endTime = poseOnly ? 0.05f : AnimationDuration;
        foreach (DoorAnimationData item in data)
        {
            Vector3 fromPosition = reverse ? item.OpenPosition : item.ClosedPosition;
            Vector3 toPosition = reverse ? item.ClosedPosition : item.OpenPosition;
            float fromY = reverse ? item.OpenY : item.ClosedY;
            float toY = reverse ? item.ClosedY : item.OpenY;

            if (poseOnly)
            {
                fromPosition = toPosition = reverse ? item.ClosedPosition : item.OpenPosition;
                fromY = toY = reverse ? item.ClosedY : item.OpenY;
            }

            if (hinged)
            {
                clip.SetCurve(item.Path, typeof(Transform), "localEulerAnglesRaw.y", LinearCurve(fromY, toY, endTime));
            }
            else
            {
                clip.SetCurve(item.Path, typeof(Transform), "m_LocalPosition.x", LinearCurve(fromPosition.x, toPosition.x, endTime));
            }
        }

        clip.frameRate = 60f;
        clip.wrapMode = WrapMode.ClampForever;
        EditorUtility.SetDirty(clip);
        return clip;
    }

    /// <summary>
    /// 두 키를 사용하는 선형 애니메이션 곡선을 만든다.
    /// </summary>
    private static AnimationCurve LinearCurve(float from, float to, float duration)
    {
        return AnimationCurve.Linear(0f, from, duration, to);
    }

    /// <summary>
    /// Animator State 사이에 Trigger 조건 전이를 추가한다.
    /// </summary>
    private static void AddTriggerTransition(AnimatorState from, AnimatorState to, string trigger)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    /// <summary>
    /// AnimationClip 종료 후 다음 포즈 State로 전이한다.
    /// </summary>
    private static void AddExitTransition(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = 0f;
    }

    /// <summary>
    /// DoorController의 Animator와 Trigger 이름을 저장한다.
    /// </summary>
    private static void ConfigureDoorController(
        DoorController controller,
        Animator animator,
        float duration,
        Collider[] movingLeafColliders = null)
    {
        SerializedObject serializedObject = new SerializedObject(controller);
        serializedObject.FindProperty("animator").objectReferenceValue = animator;
        serializedObject.FindProperty("openTrigger").stringValue = OpenTrigger;
        serializedObject.FindProperty("closeTrigger").stringValue = CloseTrigger;
        serializedObject.FindProperty("closedStateName").stringValue = ClosedState;
        serializedObject.FindProperty("openStateName").stringValue = OpenState;
        serializedObject.FindProperty("animationDuration").floatValue = duration;
        if (movingLeafColliders != null)
        {
            SerializedProperty colliders = serializedObject.FindProperty("movingLeafColliders");
            colliders.arraySize = movingLeafColliders.Length;
            for (int index = 0; index < movingLeafColliders.Length; index++)
            {
                colliders.GetArrayElementAtIndex(index).objectReferenceValue = movingLeafColliders[index];
            }
        }
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    /// <summary>
    /// 실제 프리팹 인스턴스 루트에 Doorway, 방향별 Target, 통과 경계, Sensor를 구성한다.
    /// </summary>
    private static void ConfigureSceneDoor(
        DoorInstance door,
        PlayerRoomTracker tracker,
        CameraDirector cameraDirector,
        TargetMarkerPool markerPool,
        Camera worldCamera)
    {
        GameObject doorRoot = door.Root;
        Doorway doorway = GetOrAdd<Doorway>(doorRoot);
        Animator instanceAnimator = doorRoot.transform.Find("DoorVisual")?.GetComponent<Animator>();
        string controllerName = door.PrefabPath == DoorV3PrefabPath ? "Door_V3" : "SlideDoor";
        RuntimeAnimatorController instanceController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            AnimationFolder + "/" + controllerName + ".controller");
        if (instanceAnimator != null)
        {
            // 과거 씬 인스턴스의 null override가 프리팹 Controller를 가리는 경우가 있어 명시적으로 복구한다.
            instanceAnimator.runtimeAnimatorController = instanceController;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instanceAnimator);
            EditorUtility.SetDirty(instanceAnimator);
        }

        DoorController instanceDoorController = GetOrAdd<DoorController>(doorRoot);
        ConfigureDoorController(instanceDoorController, instanceAnimator, AnimationDuration);
        Vector3 doorCenter = GetDoorPassageCenter(doorRoot);
        Vector3 normal = DetermineDoorNormal(doorRoot.transform, doorCenter, door.SourceRoom, door.DestinationRoom);

        Transform targetA = GetOrCreateChild(doorRoot.transform, "From_A_Target");
        Transform targetB = GetOrCreateChild(doorRoot.transform, "From_B_Target");
        Transform sensorA = GetOrCreateChild(doorRoot.transform, "Sensor_Entering_A");
        Transform sensorB = GetOrCreateChild(doorRoot.transform, "Sensor_Entering_B");
        Transform positiveApproach = doorRoot.transform.Find($"{TraversalAnchorsName}/{PositiveApproachName}");
        Transform negativeApproach = doorRoot.transform.Find($"{TraversalAnchorsName}/{NegativeApproachName}");
        Transform positivePassage = doorRoot.transform.Find($"{TraversalAnchorsName}/{PositivePassageName}");
        Transform negativePassage = doorRoot.transform.Find($"{TraversalAnchorsName}/{NegativePassageName}");
        if (positiveApproach == null || negativeApproach == null ||
            positivePassage == null || negativePassage == null)
        {
            GameLog.LogError($"'{doorRoot.name}' 프리팹에 메시 기준 TraversalAnchors가 없습니다.", doorRoot);
            return;
        }

        // 프리팹의 중립적인 양·음 앵커를 이 인스턴스의 RoomA 방향에 맞춰 매핑한다.
        bool roomAUsesPositive = Vector3.Dot(positiveApproach.position - doorCenter, normal) > 0f;
        Transform approachFromA = roomAUsesPositive ? positiveApproach : negativeApproach;
        Transform approachFromB = roomAUsesPositive ? negativeApproach : positiveApproach;
        Transform passageFromA = roomAUsesPositive ? negativePassage : positivePassage;
        Transform passageFromB = roomAUsesPositive ? positivePassage : negativePassage;
        Transform sensorPositionA = roomAUsesPositive ? positivePassage : negativePassage;
        Transform sensorPositionB = roomAUsesPositive ? negativePassage : positivePassage;

        EnsureNavMeshLanding(doorRoot, positivePassage, "Positive");
        EnsureNavMeshLanding(doorRoot, negativePassage, "Negative");

        // 문 프레임 때문에 양쪽 NavMesh 폴리곤이 분리돼도 실제 열린 출입구를 통해 경로가 이어지게 한다.
        ConfigureDoorNavMeshLink(doorRoot, positivePassage, negativePassage);

        Vector3 approachPositionA = ResolveCameraVisibleApproachPosition(
            approachFromA,
            door.SourceRoom,
            doorCenter,
            worldCamera);
        Vector3 approachPositionB = ResolveCameraVisibleApproachPosition(
            approachFromB,
            door.DestinationRoom,
            doorCenter,
            worldCamera);

        // Target 오브젝트와 클릭 영역은 실제 문 중심에 두고,
        // 플레이어가 멈출 위치는 프리팹 원본의 메시 기준 앵커를 직접 참조한다.
        Vector3 localDoorCenter = doorRoot.transform.InverseTransformPoint(
            doorCenter + Vector3.up * PointHeight);
        targetA.localPosition = localDoorCenter;
        targetB.localPosition = localDoorCenter;
        sensorA.position = sensorPositionA.position;
        sensorB.position = sensorPositionB.position;

        ConfigureDoorSensor(sensorA.gameObject, doorway, door.SourceRoom, doorRoot.transform, normal);
        ConfigureDoorSensor(sensorB.gameObject, doorway, door.DestinationRoom, doorRoot.transform, normal);
        DoorTarget doorTargetA = ConfigureDoorTarget(
            targetA.gameObject,
            doorway,
            door.SourceRoom,
            tracker,
            cameraDirector,
            markerPool,
            worldCamera,
            approachPositionA);
        DoorTarget doorTargetB = ConfigureDoorTarget(
            targetB.gameObject,
            doorway,
            door.DestinationRoom,
            tracker,
            cameraDirector,
            markerPool,
            worldCamera,
            approachPositionB);

        // 클릭 접근 시에는 양쪽 모두 문 중심을 바라보게 하고,
        // 통과 완료 후 방향은 DoorTarget이 목적지 방 안쪽으로 별도 적용한다.
        OrientTowardPoint(doorTargetA.ApproachPoint, doorCenter);
        OrientTowardPoint(doorTargetB.ApproachPoint, doorCenter);

        SerializedObject serializedObject = new SerializedObject(doorway);
        serializedObject.FindProperty("roomA").objectReferenceValue = door.SourceRoom;
        serializedObject.FindProperty("roomB").objectReferenceValue = door.DestinationRoom;
        serializedObject.FindProperty("passageStartFromA").objectReferenceValue = passageFromA;
        serializedObject.FindProperty("passageStartFromB").objectReferenceValue = passageFromB;
        serializedObject.FindProperty("doorTargetFromA").objectReferenceValue = doorTargetA;
        serializedObject.FindProperty("doorTargetFromB").objectReferenceValue = doorTargetB;
        serializedObject.FindProperty("doorController").objectReferenceValue = doorRoot.GetComponent<DoorController>();
        serializedObject.FindProperty("sensorEnteringA").objectReferenceValue = sensorA.GetComponent<DoorwaySensor>();
        serializedObject.FindProperty("sensorEnteringB").objectReferenceValue = sensorB.GetComponent<DoorwaySensor>();
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(doorRoot);
        EditorUtility.SetDirty(doorRoot);
    }

    /// <summary>
    /// 안전 앵커 XZ에 NavMesh가 정확히 없으면 아래의 실제 바닥 Collider 높이에 작은 비가시 Bake 랜딩을 만든다.
    /// 계단 끝처럼 기존 Bake가 끊긴 예외만 보완하며 이미 유효한 일반 문에는 아무것도 추가하지 않는다.
    /// </summary>
    private static void EnsureNavMeshLanding(GameObject doorRoot, Transform anchor, string sideName)
    {
        bool isKnownStairLanding = sideName == "Positive" &&
                                   doorRoot.name.Contains("Stair", StringComparison.Ordinal);
        if (!isKnownStairLanding &&
            NavMesh.SamplePosition(anchor.position, out NavMeshHit hit, 4f, NavMesh.AllAreas) &&
            Vector3.ProjectOnPlane(hit.position - anchor.position, Vector3.up).magnitude <= 0.1f)
        {
            return;
        }

        Ray ray = new Ray(anchor.position + Vector3.up * 15f, Vector3.down);
        RaycastHit floorHit = Physics.RaycastAll(ray, 30f)
            .Where(candidate => !candidate.collider.isTrigger &&
                                !candidate.collider.transform.IsChildOf(doorRoot.transform))
            .OrderBy(candidate => candidate.distance)
            .FirstOrDefault();
        if (floorHit.collider == null)
        {
            GameLog.LogWarning($"'{doorRoot.name}'의 {sideName} 통과 앵커 아래 바닥을 찾지 못했습니다.", doorRoot);
            return;
        }

        Transform legacyLanding = doorRoot.transform.Find($"NavMeshLanding_{sideName}");
        if (legacyLanding != null)
        {
            Undo.DestroyObjectImmediate(legacyLanding.gameObject);
        }

        GameObject navigationRoot = GameObject.Find("GeneratedDoorNavigation");
        if (navigationRoot == null)
        {
            navigationRoot = new GameObject("GeneratedDoorNavigation");
            Undo.RegisterCreatedObjectUndo(navigationRoot, "Create generated door navigation root");
        }

        string landingName =
            $"{doorRoot.name}_NavMeshLanding_{sideName}_{Mathf.RoundToInt(anchor.position.x * 10f)}_{Mathf.RoundToInt(anchor.position.z * 10f)}";
        Transform landing = GetOrCreateChild(navigationRoot.transform, landingName);
        landing.position = new Vector3(anchor.position.x, floorHit.point.y - 0.1f, anchor.position.z);
        landing.rotation = Quaternion.identity;
        landing.localScale = Vector3.one;
        BoxCollider collider = GetOrAdd<BoxCollider>(landing.gameObject);
        collider.isTrigger = false;
        // Navigation Agent 반경이 2m이고 계단 벽 모서리에서 추가 수축되므로 충분한 폴리곤이 남도록 12m 랜딩을 사용한다.
        collider.size = new Vector3(12f, 0.2f, 12f);

        // CoreSandbox Surface가 RenderMeshes 모드이므로 렌더링은 숨기되 Bake Source로 수집될 Cube Mesh를 함께 둔다.
        Transform bakeMesh = GetOrCreateChild(landing, "NavMeshBakeMesh");
        bakeMesh.localPosition = Vector3.zero;
        bakeMesh.localRotation = Quaternion.identity;
        bakeMesh.localScale = new Vector3(12f, 0.2f, 12f);
        MeshFilter meshFilter = GetOrAdd<MeshFilter>(bakeMesh.gameObject);
        meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        MeshRenderer meshRenderer = GetOrAdd<MeshRenderer>(bakeMesh.gameObject);
        meshRenderer.enabled = true;
        meshRenderer.forceRenderingOff = false;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.sharedMaterial = GetOrCreateInvisibleNavigationMaterial();
        EditorUtility.SetDirty(landing.gameObject);
    }

    /// <summary>씬 Navigation Surface를 다시 Bake하고 새 폴리곤에 모든 문 Link 끝점을 재투영한다.</summary>
    private static void RebuildNavigationAndRefreshLinks()
    {
        NavMeshSurface surface = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        if (surface == null)
        {
            GameLog.LogError("CoreSandbox에 NavMeshSurface가 없습니다.");
            return;
        }

        surface.BuildNavMesh();
        GameObject navigationRoot = GameObject.Find("GeneratedDoorNavigation");
        if (navigationRoot != null)
        {
            // 주변 벽 Bake에 잘려 나가는 계단 랜딩은 전용 Surface에서 자식 Mesh만 독립적으로 Bake한다.
            NavMeshSurface landingSurface = GetOrAdd<NavMeshSurface>(navigationRoot);
            landingSurface.agentTypeID = surface.agentTypeID;
            landingSurface.collectObjects = CollectObjects.Children;
            landingSurface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            landingSurface.BuildNavMesh();
            EditorUtility.SetDirty(landingSurface);
        }

        foreach (DoorInstance door in CollectPrefabDoorInstances())
        {
            Transform positive = door.Root.transform.Find($"{TraversalAnchorsName}/{PositivePassageName}");
            Transform negative = door.Root.transform.Find($"{TraversalAnchorsName}/{NegativePassageName}");
            if (positive != null && negative != null)
            {
                ConfigureDoorNavMeshLink(door.Root, positive, negative);
            }
        }
    }

    /// <summary>에셋 재생성과 NavMesh Bake 뒤 인스턴스의 null Animator Controller override를 최종 복구한다.</summary>
    private static void RestoreInstanceAnimatorControllers()
    {
        foreach (DoorInstance door in CollectPrefabDoorInstances())
        {
            Animator animator = door.Root.transform.Find("DoorVisual")?.GetComponent<Animator>();
            if (animator == null)
            {
                continue;
            }

            string controllerName = door.PrefabPath == DoorV3PrefabPath ? "Door_V3" : "SlideDoor";
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                AnimationFolder + "/" + controllerName + ".controller");
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            EditorUtility.SetDirty(animator);
        }
    }

    /// <summary>NavMesh Bake에는 포함되지만 게임 화면에는 보이지 않는 투명 머티리얼을 준비한다.</summary>
    private static Material GetOrCreateInvisibleNavigationMaterial()
    {
        if (!AssetDatabase.IsValidFolder(NavigationMaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/Arts", "GeneratedNavigation");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(InvisibleNavigationMaterialPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        material = new Material(shader) { name = "InvisibleNavMeshSource" };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
        material.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;
        AssetDatabase.CreateAsset(material, InvisibleNavigationMaterialPath);
        return material;
    }

    /// <summary>
    /// 프리팹 안전 앵커부터 방 안쪽으로 이동하며 해당 Room CCTV와 NavMesh를 모두 만족하는 가장 가까운 지점을 찾는다.
    /// 카메라에 이미 잘 보이는 문은 프리팹 위치를 그대로 사용한다.
    /// </summary>
    private static Vector3 ResolveCameraVisibleApproachPosition(
        Transform minimumAnchor,
        Room ownerRoom,
        Vector3 doorCenter,
        Camera worldCamera)
    {
        if (minimumAnchor == null || ownerRoom?.RoomCamera?.VirtualCamera == null)
        {
            return minimumAnchor != null ? minimumAnchor.position : doorCenter;
        }

        Vector3 direction = Vector3.ProjectOnPlane(minimumAnchor.position - doorCenter, Vector3.up).normalized;
        float anchorDistance = Vector3.ProjectOnPlane(minimumAnchor.position - doorCenter, Vector3.up).magnitude;
        float minimumDistance = Mathf.Max(anchorDistance, GetRoomCameraMinimumEntryDistance(ownerRoom));
        float aspect = worldCamera != null ? worldCamera.aspect : 16f / 9f;
        float playerCenterHeight = GetPlayerVisualCenterHeight();

        for (float distance = minimumDistance;
             distance <= MaximumCameraEntryDistance;
             distance += CameraEntryDistanceStep)
        {
            Vector3 candidate = minimumAnchor.position + direction * (distance - anchorDistance);
            if (!IsHorizontallyOnNavMesh(candidate))
            {
                continue;
            }

            Vector3 playerVisualCenter = candidate + Vector3.up * (playerCenterHeight - PointHeight);
            if (IsVisibleFromRoomCamera(ownerRoom.RoomCamera, playerVisualCenter, aspect))
            {
                return candidate;
            }
        }

        return minimumAnchor.position;
    }

    /// <summary>
    /// 문 바로 앞을 촬영하지 않는 고정 CCTV 방에 대해 실제 플레이 검증으로 확인한 최소 진입 깊이를 반환한다.
    /// 설정 도구가 같은 Room의 모든 문에 동일 규칙을 적용하므로 인스턴스별 수동 오차가 생기지 않는다.
    /// </summary>
    private static float GetRoomCameraMinimumEntryDistance(Room room)
    {
        return room?.RoomId switch
        {
            "S3_DiningRoom" => 15.5f,
            "S5_MonitoringRoom" => 15.5f,
            "S3_HallwayB" => 8f,
            _ => 0f
        };
    }

    /// <summary>지점의 높이 차이를 제외한 XZ 위치가 NavMesh와 일치하는지 확인한다.</summary>
    private static bool IsHorizontallyOnNavMesh(Vector3 position)
    {
        return NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas) &&
               Vector3.ProjectOnPlane(hit.position - position, Vector3.up).magnitude <= 0.5f;
    }

    /// <summary>Cinemachine 고정 시점의 여유 있는 화면 영역 안에 월드 지점이 들어오는지 계산한다.</summary>
    private static bool IsVisibleFromRoomCamera(RoomCamera roomCamera, Vector3 worldPoint, float aspect)
    {
        var virtualCamera = roomCamera.VirtualCamera;
        // Follow/LookAt 보정을 포함한 실제 Cinemachine 최종 포즈를 강제로 갱신해 인게임 Brain과 같은 구도를 검사한다.
        virtualCamera.UpdateCameraState(Vector3.up, -1f);
        var state = virtualCamera.State;
        Vector3 finalPosition = state.GetFinalPosition();
        Quaternion finalOrientation = state.GetFinalOrientation();
        Vector3 localPoint = Quaternion.Inverse(finalOrientation) * (worldPoint - finalPosition);
        if (localPoint.z <= Mathf.Max(0.01f, virtualCamera.Lens.NearClipPlane))
        {
            return false;
        }

        float verticalLimit = Mathf.Tan(virtualCamera.Lens.FieldOfView * 0.5f * Mathf.Deg2Rad) * localPoint.z;
        float horizontalLimit = verticalLimit * Mathf.Max(0.1f, aspect);
        float usableRatio = 1f - CameraViewportMargin * 2f;
        return Mathf.Abs(localPoint.x) <= horizontalLimit * usableRatio &&
               Mathf.Abs(localPoint.y) <= verticalLimit * usableRatio;
    }

    /// <summary>플레이어 루트에서 실제 Renderer 중심까지의 월드 높이를 반환한다.</summary>
    private static float GetPlayerVisualCenterHeight()
    {
        Player player = UnityEngine.Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include);
        Renderer renderer = player != null
            ? player.GetComponentsInChildren<Renderer>(true).FirstOrDefault(candidate => candidate.enabled)
            : null;
        return renderer != null ? renderer.bounds.center.y - player.transform.position.y : 7f;
    }

    /// <summary>
    /// 프리팹 통과 앵커 양쪽의 가장 가까운 NavMesh 위치를 연결하는 양방향 Link를 구성한다.
    /// Link 끝점은 바닥 높이로 투영해 앵커의 캐릭터 피벗 높이가 연결 판정에 영향을 주지 않게 한다.
    /// </summary>
    private static void ConfigureDoorNavMeshLink(
        GameObject doorRoot,
        Transform positivePassage,
        Transform negativePassage)
    {
        NavMeshLink link = GetOrAdd<NavMeshLink>(doorRoot);
        NavMeshAgent playerAgent = UnityEngine.Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include)
            ?.GetComponent<NavMeshAgent>();
        link.agentTypeID = playerAgent != null ? playerAgent.agentTypeID : 0;
        link.startTransform = null;
        link.endTransform = null;
        link.startPoint = doorRoot.transform.InverseTransformPoint(GetNearestNavMeshPoint(positivePassage.position));
        link.endPoint = doorRoot.transform.InverseTransformPoint(GetNearestNavMeshPoint(negativePassage.position));
        link.width = 2f;
        link.bidirectional = true;
        link.autoUpdate = true;
        link.activated = true;
        EditorUtility.SetDirty(link);
    }

    /// <summary>앵커 주변 NavMesh 바닥 위치를 반환하고 찾지 못하면 원래 위치를 유지한다.</summary>
    private static Vector3 GetNearestNavMeshPoint(Vector3 position)
    {
        return NavMesh.SamplePosition(position, out NavMeshHit hit, 4f, NavMesh.AllAreas)
            ? hit.position
            : position;
    }

    /// <summary>
    /// 문 프레임 메시의 가장 얇은 수평 축을 통과 방향으로 사용하고 RoomA 쪽으로 부호를 맞춘다.
    /// 방 중심 간 방향은 부호에만 사용하므로 복도 형태나 방 크기가 달라도 문 축이 틀어지지 않는다.
    /// </summary>
    private static Vector3 DetermineDoorNormal(Transform doorRoot, Vector3 doorCenter, Room roomA, Room roomB)
    {
        Renderer frameRenderer = GetDoorFrameRenderer(doorRoot.gameObject);
        Vector3 axis = DetermineMeshNormal(doorRoot, frameRenderer);
        Vector3 centerA = GetRoomCenter(roomA);
        Vector3 centerB = GetRoomCenter(roomB);
        Vector3 directionToA = Vector3.ProjectOnPlane(centerA - doorCenter, Vector3.up);
        if (directionToA.sqrMagnitude < 0.01f)
        {
            directionToA = Vector3.ProjectOnPlane(doorCenter - centerB, Vector3.up);
        }

        if (directionToA.sqrMagnitude > 0.01f && Vector3.Dot(directionToA, axis) < 0f)
        {
            axis = -axis;
        }

        return axis.normalized;
    }

    /// <summary>
    /// 문 프레임의 월드 Bounds를 Door root의 전방·우측 축에 투영해 더 얇은 축을 통과 법선으로 반환한다.
    /// 현재 프리팹처럼 90도 회전된 배치에서도 방 중심 추정 없이 메시 평면을 정확히 따른다.
    /// </summary>
    private static Vector3 DetermineMeshNormal(Transform doorRoot, Renderer frameRenderer)
    {
        Vector3 forward = Vector3.ProjectOnPlane(doorRoot.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(doorRoot.right, Vector3.up).normalized;
        if (frameRenderer == null)
        {
            return forward.sqrMagnitude > 0.0001f ? forward : Vector3.forward;
        }

        float forwardExtent = GetProjectedHalfExtent(frameRenderer, forward);
        float rightExtent = GetProjectedHalfExtent(frameRenderer, right);
        return forwardExtent <= rightExtent ? forward : right;
    }

    /// <summary>월드 축 정렬 Bounds를 지정 방향에 투영한 반길이를 반환한다.</summary>
    private static float GetProjectedHalfExtent(Renderer renderer, Vector3 direction)
    {
        if (renderer == null || direction.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        Vector3 normalized = direction.normalized;
        Vector3 extents = renderer.bounds.extents;
        return Mathf.Abs(normalized.x) * extents.x +
               Mathf.Abs(normalized.y) * extents.y +
               Mathf.Abs(normalized.z) * extents.z;
    }

    /// <summary>
    /// Room의 자식 Renderer를 감싸는 중심점을 반환한다.
    /// </summary>
    private static Vector3 GetRoomCenter(Room room)
    {
        // Room 하위에 다른 구역의 메시나 대형 장식물이 포함돼도 방향 판정이 오염되지 않도록
        // RoomGraph와 동일한 명시적 기준 위치를 사용한다.
        return room != null ? room.GraphPosition : Vector3.zero;
    }

    /// <summary>
    /// Door root 기준으로 양쪽 방향의 높이 보정 위치를 계산한다.
    /// </summary>
    private static Vector3 ToLocalPoint(
        Transform doorRoot,
        Vector3 doorCenter,
        Vector3 direction,
        float distance)
    {
        Vector3 worldPoint = doorCenter + direction.normalized * distance + Vector3.up * PointHeight;
        return doorRoot.InverseTransformPoint(worldPoint);
    }

    /// <summary>
    /// 프리팹 루트가 실제 문 메시와 떨어져 있어도 Door_Frame Renderer의 월드 중심을 문 기준점으로 반환한다.
    /// 높이는 각 층의 바닥 기준을 유지하기 위해 프리팹 루트의 Y를 사용한다.
    /// </summary>
    private static Vector3 GetDoorVisualCenter(GameObject doorRoot)
    {
        Renderer[] renderers = doorRoot.GetComponentsInChildren<Renderer>(true);
        Renderer frameRenderer = GetDoorFrameRenderer(doorRoot);
        Renderer referenceRenderer = frameRenderer ?? renderers.FirstOrDefault();
        if (referenceRenderer == null)
        {
            return doorRoot.transform.position;
        }

        Vector3 boundsCenter = referenceRenderer.bounds.center;
        return new Vector3(boundsCenter.x, doorRoot.transform.position.y, boundsCenter.z);
    }

    /// <summary>
    /// 문 종류와 무관하게 열린 패널 사이에서 좌우 여유가 가장 큰 프레임 정중앙을 통과선으로 반환한다.
    /// SlideDoor는 열림 동안 패널 Collider를 비활성화하므로 시각적 개구부와 안전 통과선이 일치한다.
    /// </summary>
    private static Vector3 GetDoorPassageCenter(GameObject doorRoot)
    {
        Vector3 frameCenter = GetDoorVisualCenter(doorRoot);
        // SlideDoor의 움직이는 패널 Collider는 열린 동안 모두 비활성화되므로
        // 프레임 좌우 여유가 가장 큰 정중앙을 통과선으로 사용한다.
        return frameCenter;
    }

    /// <summary>실제 출입구 기준이 되는 Door_Frame Renderer를 반환한다.</summary>
    private static Renderer GetDoorFrameRenderer(GameObject doorRoot)
    {
        return doorRoot.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(renderer => renderer.name.Contains("Door_Frame", StringComparison.Ordinal));
    }

    /// <summary>Transform의 위치는 유지하고 지정한 월드 지점을 향하는 수평 회전만 적용한다.</summary>
    private static void OrientTowardPoint(Transform target, Vector3 worldPoint)
    {
        if (target == null)
        {
            return;
        }

        Vector3 direction = Vector3.ProjectOnPlane(worldPoint - target.position, Vector3.up);
        if (direction.sqrMagnitude > 0.0001f)
        {
            target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// Sensor에 목적지 Room, Trigger Collider, Kinematic Rigidbody를 저장한다.
    /// </summary>
    private static void ConfigureDoorSensor(
        GameObject sensorObject,
        Doorway doorway,
        Room destinationRoom,
        Transform doorRoot,
        Vector3 worldNormal)
    {
        Vector3 localNormal = doorRoot.InverseTransformDirection(worldNormal).normalized;
        sensorObject.transform.localRotation = Quaternion.LookRotation(localNormal, Vector3.up);
        BoxCollider boxCollider = GetOrAdd<BoxCollider>(sensorObject);
        boxCollider.isTrigger = true;
        boxCollider.size = new Vector3(2.2f, 2.2f, 0.35f);

        Rigidbody rigidbody = GetOrAdd<Rigidbody>(sensorObject);
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.constraints = RigidbodyConstraints.FreezeAll;

        DoorwaySensor sensor = GetOrAdd<DoorwaySensor>(sensorObject);
        SerializedObject serializedObject = new SerializedObject(sensor);
        serializedObject.FindProperty("doorway").objectReferenceValue = doorway;
        serializedObject.FindProperty("destinationRoom").objectReferenceValue = destinationRoom;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sensor);
    }

    /// <summary>
    /// 방향별 DoorTarget과 접근 위치, UI Anchor를 생성하고 Doorway 참조를 저장한다.
    /// </summary>
    private static DoorTarget ConfigureDoorTarget(
        GameObject targetObject,
        Doorway doorway,
        Room ownerRoom,
        PlayerRoomTracker tracker,
        CameraDirector cameraDirector,
        TargetMarkerPool markerPool,
        Camera worldCamera,
        Vector3 approachWorldPosition)
    {
        BoxCollider clickArea = GetOrAdd<BoxCollider>(targetObject);
        clickArea.isTrigger = true;
        clickArea.size = new Vector3(1.6f, 2.0f, 1.6f);

        Transform approachPoint = GetOrCreateChild(targetObject.transform, "ApproachPoint");
        approachPoint.position = approachWorldPosition;
        Transform uiAnchor = GetOrCreateChild(targetObject.transform, "UIAnchor");
        // 접근 지점은 문 앞에 있지만 마커는 실제 문 위에 보여야 하므로 Doorway 루트를 기준으로 배치한다.
        uiAnchor.position = GetDoorVisualCenter(doorway.gameObject) + Vector3.up * (PointHeight + 1.2f);

        Target target = GetOrAdd<Target>(targetObject);
        DoorTarget doorTarget = GetOrAdd<DoorTarget>(targetObject);
        ConfigureReference(target, "approachPoint", approachPoint);
        ConfigureReference(target, "ownerRoom", ownerRoom);
        ConfigureReference(target, "actionComponent", doorTarget);
        ConfigureReference(doorTarget, "doorway", doorway);
        ConfigureReference(doorTarget, "playerRoomTracker", tracker);
        ConfigureReference(doorTarget, "cameraDirector", cameraDirector);

        TargetPresenter presenter = GetOrAdd<TargetPresenter>(targetObject);
        ConfigureReference(presenter, "target", target);
        ConfigureReference(presenter, "clickArea", clickArea);
        ConfigureReference(presenter, "uiAnchor", uiAnchor);
        ConfigureReference(presenter, "playerRoomTracker", tracker);
        ConfigureReference(presenter, "worldCamera", worldCamera);
        ConfigureReference(presenter, "markerPool", markerPool);
        EditorUtility.SetDirty(targetObject);
        return doorTarget;
    }

    /// <summary>
    /// 부모 Room과 문 이름 규칙을 사용해 문이 연결하는 반대편 Room을 결정한다.
    /// </summary>
    private static Room ResolveDestinationRoom(string doorName, Room sourceRoom)
    {
        string roomId = null;
        if (doorName.Contains("To_Sector1", StringComparison.Ordinal)) roomId = "S1_Hallway";
        else if (doorName.Contains("To_Sector2", StringComparison.Ordinal)) roomId = "S2_Hallway";
        else if (doorName.Contains("To_Sector3Hallway", StringComparison.Ordinal)) roomId = "S3_HallwayA";
        else if (doorName.Contains("To_ServiceHallway", StringComparison.Ordinal)) roomId = "S3_HallwayB";
        else if (doorName.Contains("To_DiningRoom", StringComparison.Ordinal)) roomId = "S3_DiningRoom";
        else if (doorName.Contains("To_Kitchen", StringComparison.Ordinal)) roomId = "S3_Kitchen";
        else if (doorName.Contains("To_MonitoringRoom", StringComparison.Ordinal)) roomId = "S5_MonitoringRoom";
        else if (doorName.Contains("To_Reception", StringComparison.Ordinal)) roomId = "S5_Reception";
        else if (doorName.Contains("BasementStair", StringComparison.Ordinal) || doorName.Contains("ServiceHallwayToStair", StringComparison.Ordinal)) roomId = "S4_Stairwell";
        else if (doorName.Contains("MorgueRoom1", StringComparison.Ordinal)) roomId = "S4_MorgueRoom1";
        else if (doorName.Contains("MorgueRoom2", StringComparison.Ordinal)) roomId = "S4_MorgueRoom2";
        else if (doorName.Contains("ControlRoom", StringComparison.Ordinal)) roomId = "S4_ControlRoom";
        else if (doorName.Contains("UtilityRoom", StringComparison.Ordinal)) roomId = "S4_UtilityRoom";
        else if (doorName.Contains("Workshop", StringComparison.Ordinal)) roomId = "S4_Workshop";
        else if (doorName.Contains("Storage", StringComparison.Ordinal)) roomId = "S4_Storage";
        else if (doorName == "SlideDoor" || doorName == "Door_V3") roomId = "S1_Hallway";
        else if (doorName.Contains("To_Hallway", StringComparison.Ordinal)) roomId = sourceRoom?.RoomId.StartsWith("S2_", StringComparison.Ordinal) == true ? "S2_Hallway" : "S1_Hallway";

        Room destination = FindRoom(roomId);
        if (destination == sourceRoom)
        {
            destination = FindAdjacentRoom(sourceRoom);
        }

        return destination;
    }

    /// <summary>
    /// Door root의 부모 Room 또는 Sector4 이름 규칙을 사용해 시작 Room을 결정한다.
    /// </summary>
    private static Room ResolveSourceRoom(GameObject doorRoot)
    {
        Room parentRoom = doorRoot.GetComponentInParent<Room>(true);
        if (parentRoom != null)
        {
            return parentRoom;
        }

        if (doorRoot.name.Contains("ServiceHallwayToStair", StringComparison.Ordinal))
        {
            return FindRoom("S3_HallwayB");
        }

        if (doorRoot.name.Contains("MorgueRoom", StringComparison.Ordinal) ||
            doorRoot.name.Contains("ControlRoom", StringComparison.Ordinal) ||
            doorRoot.name.Contains("UtilityRoom", StringComparison.Ordinal) ||
            doorRoot.name.Contains("Storage", StringComparison.Ordinal) ||
            doorRoot.name.Contains("Workshop", StringComparison.Ordinal) ||
            doorRoot.name.Contains("BasementStair", StringComparison.Ordinal))
        {
            return FindRoom("S4_BasementCorridor");
        }

        return null;
    }

    /// <summary>
    /// RoomId로 현재 씬의 Room을 찾는다.
    /// </summary>
    private static Room FindRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room.RoomId == roomId);
    }

    /// <summary>
    /// 시작 Room의 인접 목록에서 목적지 후보를 반환한다.
    /// </summary>
    private static Room FindAdjacentRoom(Room sourceRoom)
    {
        return sourceRoom?.AdjacentRooms?.FirstOrDefault(room => room != null);
    }

    /// <summary>
    /// Doorway 검증이 양방향으로 통과하도록 Room 인접 목록을 서로 보완한다.
    /// </summary>
    private static void EnsureBidirectionalAdjacency(Room roomA, Room roomB)
    {
        AddAdjacentRoom(roomA, roomB);
        AddAdjacentRoom(roomB, roomA);
    }

    /// <summary>
    /// Room의 private adjacentRooms 배열에 중복 없이 방 참조를 저장한다.
    /// </summary>
    private static void AddAdjacentRoom(Room source, Room destination)
    {
        if (source == null || destination == null || source.IsAdjacentTo(destination))
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(source);
        SerializedProperty array = serializedObject.FindProperty("adjacentRooms");
        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = destination;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(source);
    }

    /// <summary>
    /// 자식 Transform을 찾거나 새로 만든다.
    /// </summary>
    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(childObject, "Create prefab door child");
        return childObject.transform;
    }

    /// <summary>
    /// 컴포넌트를 찾고 없으면 현재 GameObject에 추가한다.
    /// </summary>
    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    /// <summary>
    /// SerializedField 참조를 안전하게 저장한다.
    /// </summary>
    private static void ConfigureReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Serialized enum 필드를 저장한다.
    /// </summary>
    private static void ConfigureEnum(UnityEngine.Object target, string propertyName, int value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.enumValueIndex = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// 기존 프리팹 원본에 없는 애니메이션 폴더를 만든다.
    /// </summary>
    private static void EnsureAnimationFolder()
    {
        string parentFolder = "Assets/Arts/Animations";
        if (!AssetDatabase.IsValidFolder(parentFolder))
        {
            AssetDatabase.CreateFolder("Assets/Arts", "Animations");
        }

        if (!AssetDatabase.IsValidFolder(AnimationFolder))
        {
            AssetDatabase.CreateFolder(parentFolder, "PrefabDoors");
        }
    }

    /// <summary>
    /// 잘못된 실행에서 대상으로 삼았던 Lintel 또는 특수 벽 메시인지 확인한다.
    /// </summary>
    private static bool IsIncorrectMeshPanel(GameObject gameObject)
    {
        return gameObject.name.EndsWith("_Lintel_A", StringComparison.Ordinal) ||
               gameObject.name.EndsWith("_Lintel_B", StringComparison.Ordinal) ||
               gameObject.name == "East_AboveReceptionDoor" ||
               gameObject.name == "North_RightOfMachineDoor";
    }
}
