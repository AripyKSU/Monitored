using System.Collections.Generic;
using HauntedHouse.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HauntedHouse.Editor
{
    /// <summary>
    /// RoomGraph의 Bake 버튼과 Scene 뷰 그래프 시각화를 제공한다.
    /// </summary>
    [CustomEditor(typeof(RoomGraph))]
    public sealed class RoomGraphEditor : UnityEditor.Editor
    {
        /// <summary>RoomGraph 기본 Inspector와 Bake·초기화 버튼을 그린다.</summary>
        public override void OnInspectorGUI()
        {
            RoomGraph graph = (RoomGraph)target;

            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (GUILayout.Button("Bake Room Graph"))
            {
                Undo.RecordObject(graph, "Bake Room Graph");
                if (graph.BakeFromScene(out string error))
                {
                    EditorUtility.SetDirty(graph);
                    EditorSceneManager.MarkSceneDirty(graph.gameObject.scene);
                    SceneView.RepaintAll();
                }
                else
                {
                    EditorUtility.DisplayDialog("Room Graph Bake 실패", error, "확인");
                }
            }

            if (GUILayout.Button("Clear Baked Graph"))
            {
                Undo.RecordObject(graph, "Clear Baked Room Graph");
                graph.ClearBakedGraph();
                EditorUtility.SetDirty(graph);
                EditorSceneManager.MarkSceneDirty(graph.gameObject.scene);
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                graph.IsBaked
                    ? "Bake된 방 그래프가 런타임 BFS에 사용됩니다. Scene 뷰에서 노드와 간선을 확인할 수 있습니다."
                    : "아직 Bake되지 않았습니다. Bake Room Graph 버튼을 눌러 씬의 Room과 Doorway 연결을 저장하세요.",
                graph.IsBaked ? MessageType.Info : MessageType.Warning);
        }

        /// <summary>Bake된 각 Room을 점과 이름으로, Doorway를 간선으로 Scene 뷰에 표시한다.</summary>
        private void OnSceneGUI()
        {
            RoomGraph graph = (RoomGraph)target;
            if (!graph.IsBaked)
            {
                return;
            }

            var drawnDoorways = new HashSet<Doorway>();
            foreach (RoomGraphNodeData node in graph.BakedNodes)
            {
                if (node == null || node.Room == null)
                {
                    continue;
                }

                Vector3 roomPosition = node.Room.transform.position + Vector3.up * 0.5f;
                Handles.color = new Color(0.2f, 0.8f, 1f, 1f);
                Handles.SphereHandleCap(0, roomPosition, Quaternion.identity, 0.35f, EventType.Repaint);
                Handles.Label(roomPosition + Vector3.up * 0.5f, node.Room.RoomId);

                foreach (RoomGraphEdgeData edge in node.Edges)
                {
                    if (edge.DestinationRoom == null || edge.Doorway == null || !drawnDoorways.Add(edge.Doorway))
                    {
                        continue;
                    }

                    Vector3 destinationPosition = edge.DestinationRoom.transform.position + Vector3.up * 0.5f;
                    Handles.color = new Color(1f, 0.75f, 0.2f, 0.9f);
                    Handles.DrawAAPolyLine(3f, roomPosition, destinationPosition);
                    Handles.Label((roomPosition + destinationPosition) * 0.5f, edge.Doorway.name);
                }
            }
        }
    }
}
