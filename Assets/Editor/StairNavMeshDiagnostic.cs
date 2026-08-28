using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>계단 Door Target과 NavMeshLink 사이의 실제 경로 연결 상태를 검사한다.</summary>
public static class StairNavMeshDiagnostic
{
    /// <summary>계단 문의 모든 ApproachPoint를 가장 가까운 NavMesh 폴리곤 위로 이동해 부분 경로를 방지한다.</summary>
    [MenuItem("Haunted House/Setup/Repair Stair Approach Points")]
    public static void RepairStairApproachPoints()
    {
        Doorway[] stairDoors = FindStairDoors();
        int repairedCount = 0;

        foreach (Doorway doorway in stairDoors)
        {
            foreach (string targetName in new[] { "From_A_Target", "From_B_Target" })
            {
                Target target = doorway.transform.Find(targetName)?.GetComponent<Target>();
                Transform approachPoint = target != null ? target.ApproachPoint : null;
                if (approachPoint == null ||
                    !NavMesh.SamplePosition(approachPoint.position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                {
                    GameLog.LogError($"'{doorway.name}/{targetName}'의 ApproachPoint를 NavMesh에 투영하지 못했습니다.", doorway);
                    continue;
                }

                float offset = Vector3.Distance(approachPoint.position, hit.position);
                if (offset <= 0.05f)
                {
                    continue;
                }

                Undo.RecordObject(approachPoint, "Repair Stair Approach Point");
                approachPoint.position = hit.position;
                EditorUtility.SetDirty(approachPoint);
                repairedCount++;
                GameLog.Log(
                    $"'{doorway.name}/{targetName}/ApproachPoint'를 NavMesh 위로 {offset:F2}m 이동했습니다.",
                    doorway);
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        GameLog.Log($"계단 ApproachPoint {repairedCount}개를 NavMesh 위로 복구했습니다.");
    }

    /// <summary>계단 문 양쪽 ApproachPoint를 NavMesh에 투영하고 양방향 완전 경로를 검사한다.</summary>
    [MenuItem("Haunted House/Validate/Stair NavMesh Path")]
    public static void ValidateStairNavMeshPath()
    {
        Doorway[] stairDoors = FindStairDoors();

        if (stairDoors.Length == 0)
        {
            GameLog.LogError("이름에 Stair가 포함된 Doorway를 찾지 못했습니다.");
            return;
        }

        bool allValid = true;
        foreach (Doorway doorway in stairDoors)
        {
            Target fromA = doorway.transform.Find("From_A_Target")?.GetComponent<Target>();
            Target fromB = doorway.transform.Find("From_B_Target")?.GetComponent<Target>();
            allValid &= ValidateDirection(doorway, fromA, fromB, "A → B");
            allValid &= ValidateDirection(doorway, fromB, fromA, "B → A");
        }

        if (allValid)
        {
            GameLog.Log($"계단 Doorway {stairDoors.Length}개의 양방향 ApproachPoint NavMesh 경로가 모두 완전합니다.");
        }
    }

    /// <summary>현재 씬에서 이름에 Stair가 포함된 계단 Doorway를 모두 반환한다.</summary>
    private static Doorway[] FindStairDoors()
    {
        return Object.FindObjectsByType<Doorway>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(door => door != null && door.name.Contains("Stair"))
            .ToArray();
    }

    /// <summary>출발·도착 ApproachPoint의 NavMesh 투영 거리와 완전 경로 여부를 검사한다.</summary>
    private static bool ValidateDirection(Doorway doorway, Target source, Target destination, string direction)
    {
        if (source?.ApproachPoint == null || destination?.ApproachPoint == null)
        {
            GameLog.LogError($"'{doorway.name}' {direction} 방향 ApproachPoint가 누락됐습니다.", doorway);
            return false;
        }

        bool hasSource = NavMesh.SamplePosition(source.ApproachPoint.position, out NavMeshHit sourceHit, 4f, NavMesh.AllAreas);
        bool hasDestination = NavMesh.SamplePosition(destination.ApproachPoint.position, out NavMeshHit destinationHit, 4f, NavMesh.AllAreas);
        if (!hasSource || !hasDestination)
        {
            GameLog.LogError(
                $"'{doorway.name}' {direction} NavMesh 투영 실패. " +
                $"Source={hasSource}, Destination={hasDestination}.", doorway);
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        bool calculated = NavMesh.CalculatePath(sourceHit.position, destinationHit.position, NavMesh.AllAreas, path);
        bool isComplete = calculated && path.status == NavMeshPathStatus.PathComplete;
        if (!isComplete)
        {
            GameLog.LogError(
                $"'{doorway.name}' {direction} 경로가 완전하지 않습니다. " +
                $"Status={path.status}, SourceOffset={Vector3.Distance(source.ApproachPoint.position, sourceHit.position):F2}m, " +
                $"DestinationOffset={Vector3.Distance(destination.ApproachPoint.position, destinationHit.position):F2}m.", doorway);
            return false;
        }

        GameLog.Log(
            $"'{doorway.name}' {direction} 완전 경로 확인. " +
            $"SourceOffset={Vector3.Distance(source.ApproachPoint.position, sourceHit.position):F2}m, " +
            $"DestinationOffset={Vector3.Distance(destination.ApproachPoint.position, destinationHit.position):F2}m.", doorway);
        return true;
    }
}
