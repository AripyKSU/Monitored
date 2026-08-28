using HauntedHouse.Threat;
using HauntedHouse.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>현재 씬의 눈동자 위험 경보 UI 참조와 화면 배치를 복구하는 Editor 설정 도구다.</summary>
public static class DangerAlertUiSetup
{
    private const string CautionSpritePath =
        "Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeClosed.png";
    private const string EmergencySpritePath =
        "Assets/_3DStealthGame/Art/Textures/UI/Markers/CommandMarker_EyeOpen.png";

    /// <summary>눈동자 UI를 활성화하고 Sprite·경보 시스템·RectTransform을 명시적으로 다시 연결한다.</summary>
    [MenuItem("Haunted House/Setup/Repair Danger Alert UI")]
    public static void RepairDangerAlertUi()
    {
        DangerAlertView view = Object.FindFirstObjectByType<DangerAlertView>(FindObjectsInactive.Include);
        DangerAlertSystem system = Object.FindFirstObjectByType<DangerAlertSystem>(FindObjectsInactive.Include);
        Sprite cautionSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CautionSpritePath);
        Sprite emergencySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EmergencySpritePath);

        if (view == null || system == null || cautionSprite == null || emergencySprite == null)
        {
            GameLog.LogError(
                "눈동자 UI 복구에 필요한 DangerAlertView, DangerAlertSystem 또는 Sprite를 찾지 못했습니다.");
            return;
        }

        Image image = view.GetComponent<Image>();
        Canvas canvas = view.GetComponentInParent<Canvas>(true);
        if (image == null || canvas == null)
        {
            GameLog.LogError("눈동자 UI의 Image 또는 부모 Canvas를 찾지 못했습니다.", view);
            return;
        }

        Undo.RecordObject(view, "Repair Danger Alert UI");
        Undo.RecordObject(image, "Repair Danger Alert UI");
        Undo.RecordObject(view.transform, "Repair Danger Alert UI");
        Undo.SetTransformParent(view.transform, canvas.transform, "Repair Danger Alert UI");

        SerializedObject serializedView = new SerializedObject(view);
        serializedView.FindProperty("alertImage").objectReferenceValue = image;
        serializedView.FindProperty("cautionSprite").objectReferenceValue = cautionSprite;
        serializedView.FindProperty("emergencySprite").objectReferenceValue = emergencySprite;
        serializedView.FindProperty("alertSystem").objectReferenceValue = system;
        serializedView.FindProperty("forceTopCenterLayout").boolValue = true;
        serializedView.FindProperty("topCenterOffset").vector2Value = new Vector2(0f, -48f);
        serializedView.FindProperty("alertSize").vector2Value = new Vector2(160f, 80f);
        serializedView.ApplyModifiedPropertiesWithoutUndo();

        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -48f);
        rect.sizeDelta = new Vector2(160f, 80f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.SetAsLastSibling();

        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = false;
        view.gameObject.SetActive(true);

        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(image);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        GameLog.Log("눈동자 위험 경보 UI의 참조와 상단 중앙 배치를 복구했습니다.", view);
    }
}
