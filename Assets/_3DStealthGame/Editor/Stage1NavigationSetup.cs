using Unity.AI.Navigation;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UguiImage = UnityEngine.UI.Image;
using UguiCanvasScaler = UnityEngine.UI.CanvasScaler;

public static class Stage1NavigationSetup
{
    private const string Stage1Path = "Assets/_3DStealthGame/Scenes/Stage1.unity";
    private const string CommandTargetSupportPrefabPath = "Assets/_3DStealthGame/Prefabs/Commands/CommandTargetSupport.prefab";
    private const int SharedAgentTypeId = -887442657;

    [MenuItem("Haunted House/Navigation/Setup and Bake Stage 1")]
    public static void SetupAndBake()
    {
        Scene stage1 = EditorSceneManager.OpenScene(Stage1Path, OpenSceneMode.Single);
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (playerObject == null)
        {
            Debug.LogError("Stage1 setup failed: Player-tagged object was not found.");
            return;
        }

        SetupPlayer(playerObject);
        SetupWorldSelectionController();
        SetupCommandUi();
        IgnoreDoorsDuringNavMeshBake();

        NavMeshSurface surface = GetOrCreateSurface();
        surface.BuildNavMesh();

        List<CommandTarget> commandTargets = SetupInteractableTargets();
        ValidateNavigation(playerObject, commandTargets.Find(target => target != null));
        EditorSceneManager.MarkSceneDirty(stage1);
        EditorSceneManager.SaveScene(stage1);
        Debug.Log("Stage1 navigation setup and bake completed.");
    }

    private static void SetupPlayer(GameObject playerObject)
    {
        GetOrAddComponent<Player>(playerObject);
        GetOrAddComponent<PlayerMovement>(playerObject);
        GetOrAddComponent<PlayerAnimatorController>(playerObject);
        GetOrAddComponent<PlayerStateController>(playerObject);

        if (playerObject.GetComponentInChildren<ObservationRangeVisualizer>(true) == null)
        {
            GameObject rangeObject = new GameObject("Observation Range");
            rangeObject.transform.SetParent(playerObject.transform, false);
            rangeObject.AddComponent<ObservationRangeVisualizer>();
        }

        NavMeshAgent agent = GetOrAddComponent<NavMeshAgent>(playerObject);
        agent.agentTypeID = SharedAgentTypeId;
        agent.radius = 0.25f;
        agent.height = 1.6f;
        agent.stoppingDistance = 0.05f;
        agent.angularSpeed = 360f;

        Rigidbody rigidbody = playerObject.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
        }
    }

    private static void SetupWorldSelectionController()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("Stage1 setup failed: Main Camera was not found.");
            return;
        }

        GetOrAddComponent<WorldSelectionController>(mainCamera.gameObject);
    }

    private static void SetupCommandUi()
    {
        GameObject commandUiObject = GameObject.Find("Command UI");
        if (commandUiObject == null)
        {
            commandUiObject = new GameObject("Command UI");
        }

        UIDocument document = GetOrAddComponent<UIDocument>(commandUiObject);
        document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
            "Assets/_3DStealthGame/UI/PanelSettings.asset");
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/_3DStealthGame/UI/MainUI.uxml");

        GetOrAddComponent<CommandUiController>(commandUiObject);
    }

    private static NavMeshSurface GetOrCreateSurface()
    {
        GameObject navigationObject = GameObject.Find("Navigation");

        if (navigationObject == null)
        {
            navigationObject = new GameObject("Navigation");
        }

        NavMeshSurface surface = GetOrAddComponent<NavMeshSurface>(navigationObject);
        surface.agentTypeID = SharedAgentTypeId;
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        return surface;
    }

    private static void IgnoreDoorsDuringNavMeshBake()
    {
        foreach (Door door in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
        {
            NavMeshModifier modifier = GetOrAddComponent<NavMeshModifier>(door.gameObject);
            modifier.ignoreFromBuild = true;
        }
    }

    private static Door FindReachableDoor(GameObject playerObject)
    {
        if (!NavMesh.SamplePosition(playerObject.transform.position, out NavMeshHit playerHit, 2f, NavMesh.AllAreas))
        {
            return null;
        }

        Door closestReachableDoor = null;
        float shortestPathLength = float.MaxValue;

        foreach (Door door in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
        {
            List<Transform> approachPoints = GetOrCreateDoorApproachPoints(door.transform);
            if (approachPoints.Count == 0)
            {
                continue;
            }

            NavMeshPath bestPath = null;
            foreach (Transform approachPoint in approachPoints)
            {
                NavMeshPath candidatePath = new NavMeshPath();
                if (!NavMesh.CalculatePath(playerHit.position, approachPoint.position, NavMesh.AllAreas, candidatePath) ||
                    candidatePath.status != NavMeshPathStatus.PathComplete ||
                    PathCrossesClosedDoor(candidatePath))
                {
                    continue;
                }

                if (bestPath == null || GetPathLength(candidatePath) < GetPathLength(bestPath))
                {
                    bestPath = candidatePath;
                }
            }

            if (bestPath == null)
            {
                continue;
            }

            float pathLength = GetPathLength(bestPath);
            if (pathLength < shortestPathLength)
            {
                closestReachableDoor = door;
                shortestPathLength = pathLength;
            }
        }

        return closestReachableDoor;
    }

    private static List<CommandTarget> SetupInteractableTargets()
    {
        GameObject supportPrefab = GetOrCreateCommandTargetSupportPrefab();
        List<CommandTarget> configuredTargets = new List<CommandTarget>();

        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour is not IInteractable)
            {
                continue;
            }

            CommandTarget target = SetupInteractableTarget(behaviour.transform, supportPrefab);
            if (target != null)
            {
                configuredTargets.Add(target);
            }
        }

        if (configuredTargets.Count == 0)
        {
            Debug.LogError("Stage1 setup failed: no IInteractable objects were found for command targeting.");
        }

        return configuredTargets;
    }

    private static CommandTarget SetupInteractableTarget(Transform interactableTransform, GameObject supportPrefab)
    {
        if (interactableTransform == null || supportPrefab == null)
        {
            return null;
        }

        Transform support = GetOrCreateSupportInstance(interactableTransform, supportPrefab);
        CommandTarget target = GetOrAddComponent<CommandTarget>(interactableTransform.gameObject);
        List<Transform> approachPoints = new List<Transform>
        {
            support.Find("CommandApproachPoint_A"),
            support.Find("CommandApproachPoint_B")
        };
        Transform selectionAnchor = support.Find("CommandSelectionAnchor");
        CommandTargetMarker marker = support.GetComponentInChildren<CommandTargetMarker>(true);

        PositionApproachPoints(interactableTransform, approachPoints);
        PositionSelectionAnchor(interactableTransform, selectionAnchor);
        Transform imageTransform = marker.transform.Find("Eye");
        marker.ConfigureForStage(marker.gameObject, imageTransform.GetComponent<RectTransform>(),
            imageTransform.GetComponent<UguiImage>(), selectionAnchor,
            LoadMarkerSprite("Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeClosed.png"),
            LoadMarkerSprite("Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeOpen.png"));
        target.ConfigureForStage(approachPoints, selectionAnchor, marker);
        return target;
    }

    private static GameObject GetOrCreateCommandTargetSupportPrefab()
    {
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommandTargetSupportPrefabPath);
        if (existingPrefab != null)
        {
            return existingPrefab;
        }

        string prefabDirectory = System.IO.Path.GetDirectoryName(CommandTargetSupportPrefabPath);
        if (!AssetDatabase.IsValidFolder(prefabDirectory))
        {
            AssetDatabase.CreateFolder("Assets/_3DStealthGame/Prefabs", "Commands");
        }

        GameObject template = new GameObject("CommandTargetSupport");
        CreateSupportChild(template.transform, "CommandApproachPoint_A");
        CreateSupportChild(template.transform, "CommandApproachPoint_B");
        Transform selectionAnchor = CreateSupportChild(template.transform, "CommandSelectionAnchor");

        GameObject markerObject = new GameObject("CommandMarker", typeof(RectTransform), typeof(Canvas), typeof(UguiCanvasScaler));
        markerObject.transform.SetParent(template.transform, false);
        Canvas canvas = markerObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        UguiCanvasScaler canvasScaler = markerObject.GetComponent<UguiCanvasScaler>();
        canvasScaler.uiScaleMode = UguiCanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject imageObject = new GameObject("Eye", typeof(RectTransform), typeof(UguiImage));
        imageObject.transform.SetParent(markerObject.transform, false);
        imageObject.GetComponent<RectTransform>().sizeDelta = new Vector2(72f, 72f);
        imageObject.GetComponent<UguiImage>().raycastTarget = false;

        CommandTargetMarker marker = markerObject.AddComponent<CommandTargetMarker>();
        marker.ConfigureForStage(markerObject, imageObject.GetComponent<RectTransform>(), imageObject.GetComponent<UguiImage>(),
            selectionAnchor,
            LoadMarkerSprite("Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeClosed.png"),
            LoadMarkerSprite("Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeOpen.png"));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(template, CommandTargetSupportPrefabPath);
        Object.DestroyImmediate(template);
        return prefab;
    }

    private static Transform GetOrCreateSupportInstance(Transform interactableTransform, GameObject supportPrefab)
    {
        Transform support = interactableTransform.Find("CommandTargetSupport");
        if (support != null)
        {
            return support;
        }

        RemoveStageCommandTarget(interactableTransform);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(supportPrefab, interactableTransform);
        instance.name = "CommandTargetSupport";
        return instance.transform;
    }

    private static Transform CreateSupportChild(Transform parent, string childName)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static void PositionApproachPoints(Transform interactableTransform, List<Transform> approachPoints)
    {
        const float distanceFromObject = 1.1f;
        Vector3 direction = interactableTransform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
        direction.Normalize();
        approachPoints[0].position = interactableTransform.position + direction * distanceFromObject;
        approachPoints[1].position = interactableTransform.position - direction * distanceFromObject;
    }

    private static void PositionSelectionAnchor(Transform interactableTransform, Transform selectionAnchor)
    {
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in interactableTransform.GetComponentsInChildren<Renderer>())
        {
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        selectionAnchor.position = hasBounds ? bounds.center : interactableTransform.position;
    }

    private static List<Transform> GetOrCreateDoorApproachPoints(Transform doorTransform)
    {
        const float distanceFromDoor = 1.1f;
        Transform firstPoint = GetOrCreateChild(doorTransform, "CommandApproachPoint_A");
        Transform secondPoint = GetOrCreateChild(doorTransform, "CommandApproachPoint_B");
        Vector3 direction = doorTransform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
        direction.Normalize();

        firstPoint.position = doorTransform.position + direction * distanceFromDoor;
        secondPoint.position = doorTransform.position - direction * distanceFromDoor;
        return new List<Transform> { firstPoint, secondPoint };
    }

    private static float GetPathLength(NavMeshPath path)
    {
        float length = 0f;

        for (int index = 1; index < path.corners.Length; index++)
        {
            length += Vector3.Distance(path.corners[index - 1], path.corners[index]);
        }

        return length;
    }

    private static bool PathCrossesClosedDoor(NavMeshPath path)
    {
        Vector3[] corners = path.corners;

        for (int index = 1; index < corners.Length; index++)
        {
            Vector3 start = corners[index - 1] + Vector3.up * 0.8f;
            Vector3 end = corners[index] + Vector3.up * 0.8f;
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            float validationDistance = index == corners.Length - 1
                ? Mathf.Max(0f, distance - 0.4f)
                : distance;

            if (validationDistance <= Mathf.Epsilon)
            {
                continue;
            }

            foreach (RaycastHit hit in Physics.RaycastAll(
                         start,
                         direction / distance,
                         validationDistance,
                         ~0,
                         QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<Door>() != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void RemoveStageCommandTarget(Transform doorTransform)
    {
        CommandTarget target = doorTransform.GetComponent<CommandTarget>();
        if (target != null)
        {
            Object.DestroyImmediate(target);
        }

        Transform marker = doorTransform.Find("CommandMarker");
        if (marker != null)
        {
            Object.DestroyImmediate(marker.gameObject);
        }

        DestroyChildIfPresent(doorTransform, "CommandApproachPoint");
        DestroyChildIfPresent(doorTransform, "CommandApproachPoint_A");
        DestroyChildIfPresent(doorTransform, "CommandApproachPoint_B");
        DestroyChildIfPresent(doorTransform, "CommandSelectionAnchor");
    }

    private static CommandTargetMarker GetOrCreateDoorMarker(Transform doorTransform, Transform selectionAnchor)
    {
        Sprite closedEye = LoadMarkerSprite("Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeClosed.png");
        Sprite openEye = LoadMarkerSprite("Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeOpen.png");

        Transform existing = doorTransform.Find("CommandMarker");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject markerObject = new GameObject("CommandMarker", typeof(RectTransform), typeof(Canvas), typeof(UguiCanvasScaler));
        markerObject.transform.SetParent(doorTransform);

        Canvas canvas = markerObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        UguiCanvasScaler canvasScaler = markerObject.GetComponent<UguiCanvasScaler>();
        canvasScaler.uiScaleMode = UguiCanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject imageObject = new GameObject("Eye", typeof(RectTransform), typeof(UguiImage));
        imageObject.transform.SetParent(markerObject.transform, false);
        RectTransform imageRectTransform = imageObject.GetComponent<RectTransform>();
        imageRectTransform.sizeDelta = new Vector2(72f, 72f);

        UguiImage image = imageObject.GetComponent<UguiImage>();
        image.raycastTarget = false;

        CommandTargetMarker marker = GetOrAddComponent<CommandTargetMarker>(markerObject);
        marker.ConfigureForStage(markerObject, imageRectTransform, image, selectionAnchor, closedEye, openEye);
        return marker;
    }

    private static Transform GetOrCreateDoorSelectionAnchor(Transform doorTransform)
    {
        Transform existing = GetOrCreateChild(doorTransform, "CommandSelectionAnchor");
        Bounds bounds = default;
        bool hasBounds = false;
        Renderer[] renderers = doorTransform.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Vector3 anchorPosition = hasBounds ? bounds.center : doorTransform.position;

        existing.position = anchorPosition;
        return existing;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent);
        return childObject.transform;
    }

    private static void DestroyChildIfPresent(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static Sprite LoadMarkerSprite(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    private static void ValidateNavigation(GameObject playerObject, CommandTarget doorTarget)
    {
        if (doorTarget == null || !NavMesh.SamplePosition(playerObject.transform.position, out NavMeshHit playerHit, 2f, NavMesh.AllAreas))
        {
            Debug.LogError("Stage1 NavMesh validation failed: Player spawn point is not on the baked NavMesh.");
            return;
        }

        bool hasReachableApproachPoint = false;
        foreach (Transform approachPoint in doorTarget.ApproachPoints)
        {
            if (approachPoint == null)
            {
                continue;
            }

            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(playerHit.position, approachPoint.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete &&
                !PathCrossesClosedDoor(path))
            {
                hasReachableApproachPoint = true;
                break;
            }
        }

        if (!hasReachableApproachPoint)
        {
            Debug.LogError("Stage1 NavMesh validation failed: Player spawn point has no complete path to the door target.");
            return;
        }

        Debug.Log("Stage1 NavMesh validation passed: Player spawn point can reach the door target.");
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(gameObject);
    }
}
