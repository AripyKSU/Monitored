#if UNITY_EDITOR
using System;
using HauntedHouse.Dialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 기존 Dialogue UI 계층에 무전기와 말풍선 Sprite를 연결하고 화자별 표시 기준을 구성한다.
/// 새로운 대화 Canvas를 생성하지 않고 현재 DialogueView와 그 자식 Image만 사용한다.
/// </summary>
public static class DialogueSpriteSetup
{
    private const string ScenePath = "Assets/Scenes/CoreSandbox.unity";
    private const string SetupMenuPath = "Haunted House/Setup/Dialogue Sprites";
    private const string ValidateMenuPath = "Haunted House/Validate/Dialogue Sprites";
    private const string RadioSpritePath = "Assets/Arts/Sprites/무전기.png";
    private const string BubbleSpritePath = "Assets/Arts/Sprites/말풍선.png";

    /// <summary>기존 DialogueView의 Radio/Bubble Image에 Sprite와 화자별 반전 값을 연결한다.</summary>
    [MenuItem(SetupMenuPath)]
    public static void Configure()
    {
        EnsureScene();

        DialogueView view = UnityEngine.Object.FindFirstObjectByType<DialogueView>(
            FindObjectsInactive.Include);
        if (view == null)
        {
            throw new InvalidOperationException("DialogueView를 찾지 못했습니다.");
        }

        Transform dialogueRoot = view.transform.Find("Dialogue UI");
        Transform radioTransform = view.transform.Find("Dialogue UI/Radio");
        Transform bubbleTransform = view.transform.Find("Dialogue UI/BubbleArea/Bubble");
        Transform textTransform = view.transform.Find("Dialogue UI/BubbleArea/DialogueText");
        if (dialogueRoot == null || radioTransform == null || bubbleTransform == null || textTransform == null)
        {
            throw new InvalidOperationException("기존 Dialogue UI의 Radio, Bubble 또는 DialogueText 계층이 누락됐습니다.");
        }

        Image radioImage = radioTransform.GetComponent<Image>();
        Image bubbleImage = bubbleTransform.GetComponent<Image>();
        if (radioImage == null || bubbleImage == null)
        {
            throw new InvalidOperationException("Radio 또는 Bubble 오브젝트에 Image 컴포넌트가 없습니다.");
        }

        Sprite radioSprite = LoadSingleSprite(RadioSpritePath);
        Sprite bubbleSprite = LoadSingleSprite(BubbleSpritePath);
        radioImage.sprite = radioSprite;
        bubbleImage.sprite = bubbleSprite;

        // Radio 원본은 세로형이므로 비율을 보존하고, 말풍선은 기존 가로형 UI 영역에 맞춰 늘린다.
        radioImage.type = Image.Type.Simple;
        radioImage.preserveAspect = true;
        bubbleImage.type = Image.Type.Simple;
        bubbleImage.preserveAspect = false;
        bubbleImage.raycastTarget = false;

        SerializedObject serializedView = new SerializedObject(view);
        SetObject(serializedView, "root", dialogueRoot.gameObject);
        SetObject(serializedView, "radioRoot", radioTransform.gameObject);
        SetObject(serializedView, "bubbleImage", bubbleImage);
        SetObject(serializedView, "dialogueLabel", textTransform.GetComponent<TMPro.TMP_Text>());
        SetFloat(serializedView, "radioBubbleScaleX", 1f);
        SetFloat(serializedView, "playerBubbleScaleX", -1f);
        serializedView.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(radioImage);
        EditorUtility.SetDirty(bubbleImage);
        EditorUtility.SetDirty(view);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        GameLog.Log("기존 Dialogue UI에 무전기·말풍선 Sprite 연결을 완료했습니다.", view);
    }

    /// <summary>기존 Dialogue UI의 Sprite와 Text 계층 및 반전 기준을 검사한다.</summary>
    [MenuItem(ValidateMenuPath)]
    public static void Validate()
    {
        EnsureScene();

        DialogueView view = UnityEngine.Object.FindFirstObjectByType<DialogueView>(
            FindObjectsInactive.Include);
        if (view == null)
        {
            throw new InvalidOperationException("DialogueView를 찾지 못했습니다.");
        }

        Image radioImage = view.transform.Find("Dialogue UI/Radio")?.GetComponent<Image>();
        Image bubbleImage = view.transform.Find("Dialogue UI/BubbleArea/Bubble")?.GetComponent<Image>();
        Transform textTransform = view.transform.Find("Dialogue UI/BubbleArea/DialogueText");
        ValidateCondition(
            radioImage != null && radioImage.sprite != null,
            "Radio Image에 무전기 Sprite가 연결되지 않았습니다.");
        ValidateCondition(
            bubbleImage != null && bubbleImage.sprite != null,
            "Bubble Image에 말풍선 Sprite가 연결되지 않았습니다.");
        ValidateCondition(
            textTransform != null && textTransform.parent == bubbleImage?.transform.parent,
            "DialogueText가 Bubble Image의 자식이 아닌 BubbleArea 형제로 배치되어야 합니다.");

        GameLog.Log(
            $"Dialogue Sprite 구성이 유효합니다. Radio={radioImage.sprite.name}, Bubble={bubbleImage.sprite.name}, TextFlip=false.",
            view);
    }

    /// <summary>현재 씬만 Sprite 설정 대상으로 허용한다.</summary>
    private static void EnsureScene()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Play 모드에서는 Dialogue Sprite 설정을 실행할 수 없습니다.");
        }

        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            throw new InvalidOperationException("Dialogue Sprite 설정은 CoreSandbox 씬에서만 실행할 수 있습니다.");
        }
    }

    /// <summary>PNG에서 단일 Sprite를 읽고 Import 설정 오류를 명확히 알린다.</summary>
    private static Sprite LoadSingleSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            throw new InvalidOperationException(
                $"'{path}'에서 Sprite를 읽지 못했습니다. Sprite Mode가 Single인지 확인해야 합니다.");
        }

        return sprite;
    }

    /// <summary>SerializedObject에 Unity Object 참조를 설정한다.</summary>
    private static void SetObject(SerializedObject serialized, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"DialogueView 필드 '{propertyName}'를 찾지 못했습니다.");
        }

        property.objectReferenceValue = value;
    }

    /// <summary>SerializedObject에 실수 값을 설정한다.</summary>
    private static void SetFloat(SerializedObject serialized, string propertyName, float value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"DialogueView 필드 '{propertyName}'를 찾지 못했습니다.");
        }

        property.floatValue = value;
    }

    /// <summary>검증 조건이 false이면 예외를 발생시킨다.</summary>
    private static void ValidateCondition(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
#endif
