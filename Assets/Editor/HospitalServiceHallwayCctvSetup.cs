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
/// CoreSandbox의 ServiceHallway CCTV Pose를 복사하고 동적 순찰 컴포넌트를 구성하는 Editor 도구다.
/// 임시 Pose B 오브젝트는 복사가 성공한 뒤 Undo 가능한 방식으로 제거한다.
/// </summary>
public static class HospitalServiceHallwayCctvSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string ServiceHallwayId = "S3_HallwayB";
    private const string KitchenId = "S3_Kitchen";
    private const string CenterHallwayBId = "CenterHallway_B";
    private const string ActualCctvName = "CCTV_Sector3_Hallway_B";
    private const string TemporaryPoseBName = "CCTV_Sector3_Hallway_B (1)";

    /// <summary>ServiceHallway의 실제 CCTV에 Pose 순찰과 방 참조를 멱등적으로 구성한다.</summary>
    [MenuItem("Haunted House/Setup/Hospital Sector3 ServiceHallway CCTV")]
    public static void Configure()
    {
        EnsureScene();

        Room serviceHallway = FindRequiredRoom(ServiceHallwayId);
        Room kitchen = FindRequiredRoom(KitchenId);
        Room centerHallwayB = FindRequiredRoom(CenterHallwayBId);
        PlayerRoomTracker tracker = FindRequired<PlayerRoomTracker>("PlayerRoomTracker");
        RoomCamera roomCamera = FindRequiredRoomCamera(serviceHallway);
        Transform actualCctv = roomCamera.transform;
        if (actualCctv.name != ActualCctvName)
        {
            throw new InvalidOperationException(
                $"S3_HallwayB의 RoomCamera 이름이 '{ActualCctvName}'가 아닙니다: {actualCctv.name}");
        }

        // RoomCamera 필드가 비어 있던 기존 씬에서도 실제 CCTV를 명시적으로 연결한다.
        if (serviceHallway.RoomCamera != roomCamera)
        {
            Undo.RecordObject(serviceHallway, "Assign ServiceHallway CCTV");
            SerializedObject serializedRoom = new(serviceHallway);
            serializedRoom.FindProperty("roomCamera").objectReferenceValue = roomCamera;
            serializedRoom.ApplyModifiedPropertiesWithoutUndo();
        }

        List<Transform> temporaryPoseObjects = FindTransformsByName(TemporaryPoseBName);
        if (temporaryPoseObjects.Count > 1)
        {
            throw new InvalidOperationException(
                $"임시 Pose B 오브젝트 '{TemporaryPoseBName}'가 여러 개입니다. 하나만 남긴 뒤 다시 실행하세요.");
        }

        CctvPosePatrol patrol = actualCctv.GetComponent<CctvPosePatrol>();
        if (patrol == null)
        {
            patrol = Undo.AddComponent<CctvPosePatrol>(actualCctv.gameObject);
        }

        Undo.RecordObject(patrol, "Configure ServiceHallway CCTV Pose Patrol");
        patrol.ConfigureReferences(tracker, serviceHallway, kitchen, centerHallwayB);

        // 첫 설정에서는 실제 CCTV와 임시 오브젝트의 월드 Transform을 각각 Pose A/B로 저장한다.
        // 이미 임시 오브젝트가 제거된 재실행에서는 기존 Pose B를 보존해 멱등성을 유지한다.
        if (temporaryPoseObjects.Count == 1)
        {
            Transform temporaryPoseB = temporaryPoseObjects[0];
            patrol.SetPoseData(
                new CctvPosePatrol.PoseData(actualCctv.position, actualCctv.rotation),
                new CctvPosePatrol.PoseData(temporaryPoseB.position, temporaryPoseB.rotation));

            // Pose 데이터가 컴포넌트에 저장된 뒤에만 임시 오브젝트를 제거한다.
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

        GameLog.Log($"ServiceHallway 동적 CCTV 구성을 완료했습니다: {actualCctv.name}");
    }

    /// <summary>ServiceHallway 동적 CCTV의 Scene 참조와 임시 오브젝트 상태를 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Hospital Sector3 ServiceHallway CCTV")]
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

    /// <summary>설정 도구가 CoreSandbox Edit Mode에서만 실행되도록 보장한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "ServiceHallway CCTV 설정·검증은 다른 Play Mode 작업이 끝난 뒤 Edit Mode에서 실행해야 합니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException(
                $"ServiceHallway CCTV 설정은 CoreSandbox 씬에서만 실행할 수 있습니다: {ScenePath}");
        }
    }

    /// <summary>현재 씬의 ServiceHallway CCTV 구성이 계획서 계약을 만족하는지 검사한다.</summary>
    public static bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();
        Room serviceHallway = FindRoomOrNull(ServiceHallwayId);
        Room kitchen = FindRoomOrNull(KitchenId);
        Room centerHallwayB = FindRoomOrNull(CenterHallwayBId);
        PlayerRoomTracker tracker = UnityEngine.Object.FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        RoomCamera roomCamera = FindRoomCameraOrNull(serviceHallway);
        CctvPosePatrol patrol = roomCamera != null
            ? roomCamera.GetComponent<CctvPosePatrol>()
            : null;

        Check(errors, serviceHallway != null, $"RoomId '{ServiceHallwayId}'를 찾았습니다.");
        Check(errors, kitchen != null, $"RoomId '{KitchenId}'를 찾았습니다.");
        Check(errors, centerHallwayB != null, $"RoomId '{CenterHallwayBId}'를 찾았습니다.");
        Check(errors, tracker != null, "PlayerRoomTracker 참조가 존재합니다.");
        Check(errors, roomCamera != null, $"'{ActualCctvName}' RoomCamera가 존재합니다.");
        Check(errors, serviceHallway == null || serviceHallway.RoomCamera == roomCamera,
            "S3_HallwayB.RoomCamera가 실제 ServiceHallway CCTV를 참조합니다.");
        Check(errors, roomCamera == null || roomCamera.IsConfigured,
            "ServiceHallway RoomCamera에 CinemachineCamera가 연결되어 있습니다.");
        Check(errors, roomCamera == null || roomCamera.gameObject.name == ActualCctvName,
            $"실제 ServiceHallway CCTV 이름이 '{ActualCctvName}'입니다.");
        Check(errors, FindTransformsByName(TemporaryPoseBName).Count == 0,
            $"임시 Pose B 오브젝트 '{TemporaryPoseBName}'가 제거되어 있습니다.");
        Check(errors, serviceHallway == null || serviceHallway.GetComponentsInChildren<RoomCamera>(true).Length == 1,
            "ServiceHallway에 RoomCamera가 정확히 하나만 존재합니다.");
        Check(errors, patrol != null, "실제 CCTV에 CctvPosePatrol이 연결되어 있습니다.");

        if (patrol != null)
        {
            Check(errors, patrol.PlayerRoomTracker == tracker,
                "CctvPosePatrol이 PlayerRoomTracker를 명시적으로 참조합니다.");
            Check(errors, patrol.ServiceHallwayRoom == serviceHallway,
                "CctvPosePatrol의 ServiceHallway Room 참조가 유효합니다.");
            Check(errors, patrol.KitchenRoom == kitchen,
                "CctvPosePatrol의 Kitchen Room 참조가 유효합니다.");
            Check(errors, patrol.CenterHallwayBRoom == centerHallwayB,
                "CctvPosePatrol의 CenterHallway_B Room 참조가 유효합니다.");
            Check(errors, patrol.MoveDuration >= 0f, "CctvPosePatrol의 Move Duration이 음수가 아닙니다.");
            Check(errors, patrol.EndWaitDuration >= 0f, "CctvPosePatrol의 End Wait Duration이 음수가 아닙니다.");
            bool posesDiffer = Vector3.Distance(patrol.PoseA.Position, patrol.PoseB.Position) > 0.001f ||
                               Quaternion.Angle(patrol.PoseA.Rotation, patrol.PoseB.Rotation) > 0.1f;
            Check(errors, posesDiffer, "Pose A와 Pose B가 위치 또는 회전에서 서로 다릅니다.");
        }

        report = errors.Count == 0
            ? "Sector3 ServiceHallway 동적 CCTV 구성이 유효합니다."
            : "Sector3 ServiceHallway 동적 CCTV 검증 실패:\n- " + string.Join("\n- ", errors);
        return errors.Count == 0;
    }

    /// <summary>필수 Room을 RoomId로 찾고 없으면 설정을 중단한다.</summary>
    private static Room FindRequiredRoom(string roomId)
    {
        Room[] rooms = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(candidate => candidate != null && candidate.RoomId == roomId)
            .ToArray();
        if (rooms.Length == 0)
        {
            throw new InvalidOperationException($"RoomId '{roomId}'를 찾지 못했습니다.");
        }

        if (rooms.Length > 1)
        {
            throw new InvalidOperationException($"RoomId '{roomId}'가 여러 개 발견되었습니다.");
        }

        return rooms[0];
    }

    /// <summary>현재 씬에서 RoomId가 일치하는 Room을 찾는다.</summary>
    private static Room FindRoomOrNull(string roomId)
    {
        return UnityEngine.Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(room => room != null && room.RoomId == roomId);
    }

    /// <summary>필수 전역 컴포넌트를 찾고 없으면 설정을 중단한다.</summary>
    private static T FindRequired<T>(string objectName) where T : Component
    {
        T component = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (component == null)
        {
            throw new InvalidOperationException($"'{objectName}'에 필요한 {typeof(T).Name}을 찾지 못했습니다.");
        }

        return component;
    }

    /// <summary>ServiceHallway에 있는 단일 RoomCamera를 반환한다.</summary>
    private static RoomCamera FindRequiredRoomCamera(Room serviceHallway)
    {
        RoomCamera roomCamera = FindRoomCameraOrNull(serviceHallway);
        if (roomCamera == null)
        {
            throw new InvalidOperationException(
                $"RoomId '{ServiceHallwayId}'에서 '{ActualCctvName}' RoomCamera를 찾지 못했습니다.");
        }

        return roomCamera;
    }

    /// <summary>ServiceHallway의 RoomCamera를 찾아 반환하거나 null을 반환한다.</summary>
    private static RoomCamera FindRoomCameraOrNull(Room serviceHallway)
    {
        if (serviceHallway == null)
        {
            return null;
        }

        RoomCamera configured = serviceHallway.RoomCamera;
        if (configured != null)
        {
            return configured;
        }

        return serviceHallway.GetComponentsInChildren<RoomCamera>(true)
            .FirstOrDefault(camera => camera != null && camera.gameObject.name == ActualCctvName);
    }

    /// <summary>씬 전체에서 지정한 이름의 Transform을 비활성 오브젝트까지 포함해 찾는다.</summary>
    private static List<Transform> FindTransformsByName(string objectName)
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(transform => transform != null && transform.gameObject.name == objectName)
            .ToList();
    }

    /// <summary>검사 조건을 만족하지 않을 때 오류를 누적하고 성공 시 개발 로그를 남긴다.</summary>
    private static void Check(List<string> errors, bool condition, string message)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}
#endif
