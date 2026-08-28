#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CoreSandbox의 DiningRoom CCTV 두 위치를 Pose로 저장하고 진입 방향별 동적 순찰을 구성하는 Editor 도구다.
/// 임시 위치 오브젝트는 Pose 복사가 성공한 뒤 Undo 가능한 방식으로 제거한다.
/// </summary>
public static class HospitalDiningRoomCctvSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string DiningRoomId = "S3_DiningRoom";
    private const string KitchenId = "S3_Kitchen";
    private const string CenterHallwayBId = "CenterHallway_B";
    private const string LowerHallwayId = "S3_HallwayA";
    private const string ActualCctvName = "CCTV_Sector3_DiningRoom";
    private const string TemporaryPoseBName = "CCTV_Sector3_DiningRoom (1)";

    /// <summary>DiningRoom 실제 CCTV에 두 Pose와 진입 방향 규칙을 멱등적으로 구성한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector3 DiningRoom CCTV")]
    public static void Configure()
    {
        EnsureScene();

        Room diningRoom = FindRequiredRoom(DiningRoomId);
        Room kitchen = FindRequiredRoom(KitchenId);
        Room centerHallwayB = FindRequiredRoom(CenterHallwayBId);
        Room lowerHallway = FindRequiredRoom(LowerHallwayId);
        PlayerRoomTracker tracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        RoomCamera roomCamera = FindRequiredRoomCamera(diningRoom);
        Transform actualCctv = roomCamera.transform;

        if (actualCctv.name != ActualCctvName)
        {
            throw new InvalidOperationException(
                $"S3_DiningRoom의 RoomCamera 이름이 '{ActualCctvName}'가 아닙니다: {actualCctv.name}");
        }

        if (diningRoom.RoomCamera != roomCamera)
        {
            Undo.RecordObject(diningRoom, "Assign DiningRoom CCTV");
            SerializedObject serializedRoom = new(diningRoom);
            serializedRoom.FindProperty("roomCamera").objectReferenceValue = roomCamera;
            serializedRoom.ApplyModifiedPropertiesWithoutUndo();
        }

        List<Transform> temporaryPoses = FindTransformsByName(TemporaryPoseBName);
        if (temporaryPoses.Count > 1)
        {
            throw new InvalidOperationException(
                $"임시 Pose B 오브젝트 '{TemporaryPoseBName}'가 여러 개입니다. 하나만 남긴 뒤 다시 실행하세요.");
        }

        CctvPosePatrol patrol = actualCctv.GetComponent<CctvPosePatrol>();
        if (patrol == null)
        {
            patrol = Undo.AddComponent<CctvPosePatrol>(actualCctv.gameObject);
        }

        Undo.RecordObject(patrol, "Configure DiningRoom CCTV Pose Patrol");
        patrol.ConfigureEntryRules(
            tracker,
            diningRoom,
            new[] { lowerHallway },
            new[] { centerHallwayB, kitchen });

        // 최초 실행에서 원본은 Pose A, 임시 (1) 위치는 Pose B로 월드 Transform 전체를 저장한다.
        // 재실행 시 임시 오브젝트가 이미 없다면 저장된 Pose를 그대로 유지한다.
        if (temporaryPoses.Count == 1)
        {
            Transform temporaryPoseB = temporaryPoses[0];
            patrol.SetPoseData(
                new CctvPosePatrol.PoseData(actualCctv.position, actualCctv.rotation),
                new CctvPosePatrol.PoseData(temporaryPoseB.position, temporaryPoseB.rotation));
            Undo.DestroyObjectImmediate(temporaryPoseB.gameObject);
        }

        EditorUtility.SetDirty(patrol);
        EditorUtility.SetDirty(actualCctv.gameObject);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!ValidateConfiguration(out string report))
        {
            throw new InvalidOperationException(report);
        }

        GameLog.Log($"DiningRoom 동적 CCTV 구성을 완료했습니다: {actualCctv.name}");
    }

    /// <summary>DiningRoom 동적 CCTV의 참조, 진입 규칙과 임시 오브젝트 상태를 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector3 DiningRoom CCTV")]
    public static void Validate()
    {
        EnsureScene();
        if (ValidateConfiguration(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>현재 씬의 DiningRoom CCTV 구성이 요청된 진입 방향 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room diningRoom = FindRoomOrNull(DiningRoomId);
        Room kitchen = FindRoomOrNull(KitchenId);
        Room centerHallwayB = FindRoomOrNull(CenterHallwayBId);
        Room lowerHallway = FindRoomOrNull(LowerHallwayId);
        PlayerRoomTracker tracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        RoomCamera roomCamera = FindRoomCameraOrNull(diningRoom);
        CctvPosePatrol patrol = roomCamera != null ? roomCamera.GetComponent<CctvPosePatrol>() : null;

        Check(errors, diningRoom != null, $"RoomId '{DiningRoomId}'가 존재합니다.");
        Check(errors, kitchen != null, $"RoomId '{KitchenId}'가 존재합니다.");
        Check(errors, centerHallwayB != null, $"RoomId '{CenterHallwayBId}'가 존재합니다.");
        Check(errors, lowerHallway != null, $"RoomId '{LowerHallwayId}'가 존재합니다.");
        Check(errors, tracker != null, "PlayerRoomTracker 참조가 존재합니다.");
        Check(errors, roomCamera != null, $"'{ActualCctvName}' RoomCamera가 존재합니다.");
        Check(errors, diningRoom == null || diningRoom.RoomCamera == roomCamera,
            "S3_DiningRoom.RoomCamera가 실제 DiningRoom CCTV를 참조합니다.");
        Check(errors, roomCamera == null || roomCamera.IsConfigured,
            "DiningRoom RoomCamera에 CinemachineCamera가 연결되어 있습니다.");
        Check(errors, roomCamera == null || roomCamera.gameObject.name == ActualCctvName,
            $"실제 DiningRoom CCTV 이름이 '{ActualCctvName}'입니다.");
        Check(errors, FindTransformsByName(TemporaryPoseBName).Count == 0,
            $"임시 Pose B 오브젝트 '{TemporaryPoseBName}'가 제거되어 있습니다.");
        Check(errors, diningRoom == null || diningRoom.GetComponentsInChildren<RoomCamera>(true).Length == 1,
            "DiningRoom에 RoomCamera가 정확히 하나만 존재합니다.");
        Check(errors, patrol != null, "실제 DiningRoom CCTV에 CctvPosePatrol이 연결되어 있습니다.");

        if (patrol != null)
        {
            Check(errors, patrol.PlayerRoomTracker == tracker, "PlayerRoomTracker 참조가 일치합니다.");
            Check(errors, patrol.PatrolRoom == diningRoom, "순찰 대상 Room이 S3_DiningRoom입니다.");
            Check(errors, ContainsExactly(patrol.PoseAStartRooms, lowerHallway),
                "아래쪽 복도 진입은 Pose A에서 시작합니다.");
            Check(errors, ContainsExactly(patrol.PoseBStartRooms, centerHallwayB, kitchen),
                "CenterHallway_B와 Kitchen 진입은 Pose B에서 시작합니다.");
            Check(errors, patrol.MoveDuration >= 0f, "Move Duration이 음수가 아닙니다.");
            Check(errors, patrol.EndWaitDuration >= 0f, "End Wait Duration이 음수가 아닙니다.");
            bool posesDiffer = Vector3.Distance(patrol.PoseA.Position, patrol.PoseB.Position) > 0.001f ||
                               Quaternion.Angle(patrol.PoseA.Rotation, patrol.PoseB.Rotation) > 0.1f;
            Check(errors, posesDiffer, "Pose A와 Pose B가 위치 또는 회전에서 서로 다릅니다.");
        }

        report = errors.Count == 0
            ? "Sector3 DiningRoom 동적 CCTV 구성이 유효합니다."
            : "Sector3 DiningRoom 동적 CCTV 검증 실패:\n- " + string.Join("\n- ", errors);
        return errors.Count == 0;
    }

    /// <summary>설정 도구가 CoreSandbox Edit Mode에서만 실행되도록 보장한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("DiningRoom CCTV 설정·검증은 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException($"DiningRoom CCTV 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>필수 Room을 RoomId로 하나만 찾아 반환한다.</summary>
    private static Room FindRequiredRoom(string roomId)
    {
        Room[] rooms = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(room => room != null && room.RoomId == roomId).ToArray();
        if (rooms.Length != 1)
        {
            throw new InvalidOperationException($"RoomId '{roomId}'가 정확히 하나여야 합니다. 현재 개수: {rooms.Length}");
        }

        return rooms[0];
    }

    /// <summary>현재 씬에서 RoomId가 일치하는 Room을 반환한다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == roomId);
    }

    /// <summary>필수 전역 컴포넌트를 하나 찾아 반환한다.</summary>
    private static T FindRequired<T>(string objectName) where T : Component
    {
        T component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return component != null
            ? component
            : throw new InvalidOperationException($"'{objectName}'에 필요한 {typeof(T).Name}을 찾지 못했습니다.");
    }

    /// <summary>DiningRoom의 실제 RoomCamera를 찾고 없으면 설정을 중단한다.</summary>
    private static RoomCamera FindRequiredRoomCamera(Room diningRoom)
    {
        return FindRoomCameraOrNull(diningRoom) ??
               throw new InvalidOperationException($"RoomId '{DiningRoomId}'에서 '{ActualCctvName}' RoomCamera를 찾지 못했습니다.");
    }

    /// <summary>Room의 명시적 참조 또는 이름이 일치하는 자식 RoomCamera를 반환한다.</summary>
    private static RoomCamera FindRoomCameraOrNull(Room room)
    {
        if (room == null)
        {
            return null;
        }

        return room.RoomCamera != null
            ? room.RoomCamera
            : room.GetComponentsInChildren<RoomCamera>(true)
                .FirstOrDefault(camera => camera != null && camera.gameObject.name == ActualCctvName);
    }

    /// <summary>씬 전체에서 지정한 이름의 Transform을 비활성 오브젝트까지 포함해 찾는다.</summary>
    private static List<Transform> FindTransformsByName(string objectName)
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(candidate => candidate != null && candidate.name == objectName).ToList();
    }

    /// <summary>Room 목록이 null 없이 기대 목록과 정확히 같은 참조를 포함하는지 검사한다.</summary>
    private static bool ContainsExactly(IReadOnlyList<Room> actual, params Room[] expected)
    {
        return actual != null && actual.Count == expected.Length && expected.All(actual.Contains);
    }

    /// <summary>검사 조건을 만족하지 않을 때 오류 메시지를 누적한다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
#endif
