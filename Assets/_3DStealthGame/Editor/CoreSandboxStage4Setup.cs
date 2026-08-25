using System;
using System.Linq;
using HauntedHouse.Commands;
using HauntedHouse.Core;
using HauntedHouse.Interaction;
using HauntedHouse.Inventory;
using HauntedHouse.UI;
using HauntedHouse.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CoreSandbox에 4단계 상호작용과 인벤토리 검증 구성을 반복 적용할 수 있는 Editor 도구다.
/// 기존 방, 문, Target, Presenter와 NavMesh 구성은 보존하고 필요한 컴포넌트와 UI만 교체·추가한다.
/// </summary>
public static class CoreSandboxStage4Setup
{
    private const string ScenePath = "Assets/_3DStealthGame/Scenes/CoreSandbox.unity";
    private const string DataFolder = "Assets/_3DStealthGame/Data";
    private const string ItemFolder = DataFolder + "/Items";
    private const string MaterialFolder = DataFolder + "/Materials";
    private const string KeyAssetPath = ItemFolder + "/Key.asset";
    private const string KeyIconPath = ItemFolder + "/KeyIcon.asset";
    private const string KeyMaterialPath = MaterialFolder + "/KeyPickupMaterial.mat";

    [MenuItem("Haunted House/Setup CoreSandbox Stage 4")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GameLog.LogError("CoreSandbox Stage 4 설정은 Play Mode에서 실행할 수 없습니다.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            GameLog.LogError($"활성 씬이 '{ScenePath}'가 아닙니다.");
            return;
        }

        EnsureAssetFolders();
        Sprite keyIcon = CreateOrUpdateKeyIcon();
        ItemDefinition keyItem = CreateOrUpdateKeyItem(keyIcon);
        Material keyMaterial = CreateOrUpdateKeyMaterial();

        GameObject gameSession = RequireObject("GameSession");
        GameObject playerSystems = GameObject.Find("PlayerSystems") ?? gameSession;
        GameObject playerObject = RequireObject("Player");
        GameObject canvasObject = RequireObject("Target UI");
        GameObject keyObject = FindEither("Room_A/KeyPickup", "Room_A/ItemTarget");
        GameObject messageObject = FindEither("Room_B/MessageObject", "Room_B/ItemUseTarget");
        Doorway doorway = RequireObject("Door_A_B").GetComponent<Doorway>();

        if (doorway == null)
        {
            throw new InvalidOperationException("Door_A_B에 Doorway가 없습니다.");
        }

        GamePauseService pauseService = GetOrAdd<GamePauseService>(gameSession);
        PlayerInventory inventory = GetOrAdd<PlayerInventory>(playerSystems);
        ConfigurePlayer(playerObject, pauseService);

        Stage4Ui ui = BuildStage4Ui(canvasObject, inventory, pauseService, keyIcon);
        ConfigureKeyPickup(keyObject, keyItem, keyMaterial, inventory, ui.MessagePanel);
        ConfigureMessageTarget(messageObject, ui.MessagePanel);
        ConfigureLockedDoor(doorway, keyItem, inventory, pauseService, ui);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (ValidateScene(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    [MenuItem("Haunted House/Validate CoreSandbox Stage 4")]
    public static void Validate()
    {
        if (ValidateScene(out string report))
        {
            GameLog.Log(report);
        }
        else
        {
            GameLog.LogError(report);
        }
    }

    /// <summary>
    /// 필수 에셋, 시스템, UI, 열쇠 Target과 잠긴 문 연결 상태를 검사한다.
    /// </summary>
    public static bool ValidateScene(out string report)
    {
        var problems = new System.Collections.Generic.List<string>();
        ItemDefinition keyItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(KeyAssetPath);
        GamePauseService pauseService = UnityEngine.Object.FindFirstObjectByType<GamePauseService>();
        PlayerInventory inventory = UnityEngine.Object.FindFirstObjectByType<PlayerInventory>();
        InventoryPanel inventoryPanel = UnityEngine.Object.FindFirstObjectByType<InventoryPanel>(FindObjectsInactive.Include);
        ItemSelectionPanel selectionPanel = UnityEngine.Object.FindFirstObjectByType<ItemSelectionPanel>(FindObjectsInactive.Include);
        MessagePanel messagePanel = UnityEngine.Object.FindFirstObjectByType<MessagePanel>(FindObjectsInactive.Include);
        ItemPickupTarget pickup = UnityEngine.Object.FindFirstObjectByType<ItemPickupTarget>(FindObjectsInactive.Include);
        MessageTarget messageTarget = UnityEngine.Object.FindFirstObjectByType<MessageTarget>(FindObjectsInactive.Include);
        Doorway doorway = RequireObject("Door_A_B").GetComponent<Doorway>();
        DoorTarget[] doorTargets = UnityEngine.Object.FindObjectsByType<DoorTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        ItemUseTarget[] itemUseTargets = UnityEngine.Object.FindObjectsByType<ItemUseTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (keyItem == null || !keyItem.TryValidate(out _)) problems.Add("유효한 Key ItemDefinition이 없습니다.");
        if (pauseService == null) problems.Add("GamePauseService가 없습니다.");
        if (inventory == null) problems.Add("PlayerInventory가 없습니다.");
        if (inventoryPanel == null) problems.Add("InventoryPanel이 없습니다.");
        if (selectionPanel == null) problems.Add("ItemSelectionPanel이 없습니다.");
        if (messagePanel == null) problems.Add("MessagePanel이 없습니다.");
        if (pickup == null) problems.Add("ItemPickupTarget이 없습니다.");
        if (messageTarget == null) problems.Add("MessageTarget이 없습니다.");
        if (doorway == null || !doorway.IsLocked) problems.Add("Door_A_B가 잠긴 상태로 저장되지 않았습니다.");
        if (doorTargets.Length != 2) problems.Add($"DoorTarget 개수가 2가 아닙니다: {doorTargets.Length}");
        if (itemUseTargets.Length != 2) problems.Add($"문용 ItemUseTarget 개수가 2가 아닙니다: {itemUseTargets.Length}");

        foreach (ItemUseTarget itemUseTarget in itemUseTargets)
        {
            if (!itemUseTarget.TryValidateConfiguration(out string error))
            {
                problems.Add(error);
            }
        }

        if (problems.Count > 0)
        {
            report = "CoreSandbox Stage 4 검증 실패:\n- " + string.Join("\n- ", problems);
            return false;
        }

        report = "CoreSandbox Stage 4 검증 성공: 열쇠 획득, 인벤토리, 아이템 선택 UI와 잠긴 문 참조가 모두 연결됐습니다.";
        return true;
    }

    private static void ConfigurePlayer(GameObject playerObject, GamePauseService pauseService)
    {
        Player player = playerObject.GetComponent<Player>();
        if (player == null)
        {
            throw new InvalidOperationException("Player 오브젝트에 Player 컴포넌트가 없습니다.");
        }

        SetObjectReference(player, "pauseService", pauseService);
    }

    private static void ConfigureKeyPickup(
        GameObject keyObject,
        ItemDefinition keyItem,
        Material keyMaterial,
        PlayerInventory inventory,
        MessagePanel messagePanel)
    {
        keyObject.name = "KeyPickup";
        RemoveComponent<DummyMessageTargetAction>(keyObject);
        ItemPickupTarget pickup = GetOrAdd<ItemPickupTarget>(keyObject);
        Target target = keyObject.GetComponent<Target>();

        if (target == null)
        {
            throw new InvalidOperationException("KeyPickup에 Target이 없습니다.");
        }

        SetObjectReference(pickup, "item", keyItem);
        SetObjectReference(pickup, "inventory", inventory);
        SetObjectReference(pickup, "messagePanel", messagePanel);
        SetObjectReference(pickup, "worldObject", keyObject);
        SetString(pickup, "pickupMessage", "낡은 열쇠를 획득했습니다.");
        SetObjectReference(target, "actionComponent", pickup);
        target.SetSelectable(true);

        BuildKeyVisual(keyObject, keyMaterial);
        SetMarkerLabel("Target UI/Markers/ItemTarget Marker/Label", "KEY");
    }

    private static void ConfigureMessageTarget(GameObject messageObject, MessagePanel messagePanel)
    {
        messageObject.name = "MessageObject";
        RemoveComponent<DummyMessageTargetAction>(messageObject);
        MessageTarget messageTarget = GetOrAdd<MessageTarget>(messageObject);
        Target target = messageObject.GetComponent<Target>();

        if (target == null)
        {
            throw new InvalidOperationException("MessageObject에 Target이 없습니다.");
        }

        SetObjectReference(messageTarget, "messagePanel", messagePanel);
        SetString(messageTarget, "message", "문 너머에서 차가운 바람이 불어온다.");
        SetObjectReference(target, "actionComponent", messageTarget);
        SetMarkerLabel("Target UI/Markers/ItemUseTarget Marker/Label", "NOTE");
    }

    private static void ConfigureLockedDoor(
        Doorway doorway,
        ItemDefinition keyItem,
        PlayerInventory inventory,
        GamePauseService pauseService,
        Stage4Ui ui)
    {
        SetBool(doorway, "locked", true);
        doorway.SetLocked(true);

        foreach (string path in new[] { "Door_A_B/From_A_Target", "Door_A_B/From_B_Target" })
        {
            GameObject targetObject = RequireObject(path);
            DoorTarget doorTarget = targetObject.GetComponent<DoorTarget>();
            if (doorTarget == null)
            {
                throw new InvalidOperationException($"{path}에 DoorTarget이 없습니다.");
            }

            ItemUseTarget itemUseTarget = GetOrAdd<ItemUseTarget>(targetObject);
            SetObjectReference(itemUseTarget, "requiredItem", keyItem);
            SetObjectReference(itemUseTarget, "effectComponent", doorTarget);
            SetObjectReference(itemUseTarget, "inventory", inventory);
            SetObjectReference(itemUseTarget, "pauseService", pauseService);
            SetObjectReference(itemUseTarget, "selectionPanel", ui.SelectionPanel);
            SetObjectReference(itemUseTarget, "messagePanel", ui.MessagePanel);
            SetString(itemUseTarget, "successMessage", "열쇠가 맞았다. 문이 잠금 해제됐다.");
            SetString(itemUseTarget, "failureMessage", "이 물건으로는 문을 열 수 없다.");
            SetObjectReference(doorTarget, "lockedItemUseTarget", itemUseTarget);
        }
    }

    private static Stage4Ui BuildStage4Ui(
        GameObject canvasObject,
        PlayerInventory inventory,
        GamePauseService pauseService,
        Sprite keyIcon)
    {
        Transform oldRoot = canvasObject.transform.Find("Stage4 UI");
        if (oldRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(oldRoot.gameObject);
        }

        GameObject controller = CreateUiObject("Stage4 UI", canvasObject.transform);
        Stretch(controller.GetComponent<RectTransform>());
        InventoryPanel inventoryPanel = controller.AddComponent<InventoryPanel>();
        ItemSelectionPanel selectionPanel = controller.AddComponent<ItemSelectionPanel>();
        MessagePanel messagePanel = controller.AddComponent<MessagePanel>();

        GameObject templates = CreateUiObject("Templates", controller.transform);
        templates.SetActive(false);
        InventoryItemView itemTemplate = CreateItemTemplate(templates.transform, keyIcon);

        GameObject inventoryRoot = CreatePanel("Inventory Panel", controller.transform, new Vector2(680f, 440f));
        Text inventoryTitle = CreateText("Title", inventoryRoot.transform, "INVENTORY", 30, TextAnchor.MiddleCenter);
        SetRect(inventoryTitle.rectTransform, new Vector2(0f, 175f), new Vector2(620f, 50f));
        Transform inventoryContainer = CreateContainer("Items", inventoryRoot.transform, new Vector2(0f, 70f), new Vector2(600f, 130f));
        Text description = CreateText("Description", inventoryRoot.transform, string.Empty, 22, TextAnchor.UpperLeft);
        SetRect(description.rectTransform, new Vector2(0f, -65f), new Vector2(580f, 120f));
        Text inventoryEmpty = CreateText("Empty", inventoryRoot.transform, "No items", 22, TextAnchor.MiddleCenter);
        SetRect(inventoryEmpty.rectTransform, new Vector2(0f, 70f), new Vector2(580f, 80f));
        Button inventoryClose = CreateButton("Close", inventoryRoot.transform, "CLOSE", new Vector2(0f, -180f), new Vector2(180f, 52f));

        GameObject selectionRoot = CreatePanel("Item Selection Panel", controller.transform, new Vector2(620f, 360f));
        Text selectionTitle = CreateText("Title", selectionRoot.transform, "SELECT AN ITEM", 28, TextAnchor.MiddleCenter);
        SetRect(selectionTitle.rectTransform, new Vector2(0f, 130f), new Vector2(560f, 50f));
        Transform selectionContainer = CreateContainer("Items", selectionRoot.transform, new Vector2(0f, 35f), new Vector2(540f, 130f));
        Text selectionEmpty = CreateText("Empty", selectionRoot.transform, "No items available", 22, TextAnchor.MiddleCenter);
        SetRect(selectionEmpty.rectTransform, new Vector2(0f, 35f), new Vector2(520f, 80f));
        Button selectionCancel = CreateButton("Cancel", selectionRoot.transform, "CANCEL", new Vector2(0f, -135f), new Vector2(180f, 52f));

        GameObject messageRoot = CreatePanel("Message Panel", controller.transform, new Vector2(760f, 220f));
        Text messageLabel = CreateText("Message", messageRoot.transform, string.Empty, 24, TextAnchor.MiddleCenter);
        SetRect(messageLabel.rectTransform, new Vector2(0f, 28f), new Vector2(690f, 100f));
        Button confirmButton = CreateButton("Confirm", messageRoot.transform, "OK", new Vector2(0f, -72f), new Vector2(160f, 50f));

        Button inventoryButton = CreateButton("Inventory Button", controller.transform, "INVENTORY", new Vector2(-32f, -32f), new Vector2(190f, 56f));
        RectTransform inventoryButtonRect = inventoryButton.GetComponent<RectTransform>();
        inventoryButtonRect.anchorMin = Vector2.one;
        inventoryButtonRect.anchorMax = Vector2.one;
        inventoryButtonRect.pivot = Vector2.one;

        SetObjectReference(inventoryPanel, "inventory", inventory);
        SetObjectReference(inventoryPanel, "pauseService", pauseService);
        SetBool(inventoryPanel, "pauseWhileOpen", true);
        SetObjectReference(inventoryPanel, "panelRoot", inventoryRoot);
        SetObjectReference(inventoryPanel, "itemContainer", inventoryContainer);
        SetObjectReference(inventoryPanel, "itemViewPrefab", itemTemplate);
        SetObjectReference(inventoryPanel, "descriptionLabel", description);
        SetObjectReference(inventoryPanel, "emptyLabel", inventoryEmpty);

        SetObjectReference(selectionPanel, "panelRoot", selectionRoot);
        SetObjectReference(selectionPanel, "itemContainer", selectionContainer);
        SetObjectReference(selectionPanel, "itemViewPrefab", itemTemplate);
        SetObjectReference(selectionPanel, "emptyLabel", selectionEmpty);

        SetObjectReference(messagePanel, "panelRoot", messageRoot);
        SetObjectReference(messagePanel, "messageLabel", messageLabel);
        SetObjectReference(messagePanel, "confirmButton", confirmButton);

        UnityEventTools.AddPersistentListener(inventoryButton.onClick, inventoryPanel.Toggle);
        UnityEventTools.AddPersistentListener(inventoryClose.onClick, inventoryPanel.Close);
        UnityEventTools.AddPersistentListener(selectionCancel.onClick, selectionPanel.Cancel);

        inventoryRoot.SetActive(false);
        selectionRoot.SetActive(false);
        messageRoot.SetActive(false);
        SetLayerRecursively(controller, LayerMask.NameToLayer("UI"));

        return new Stage4Ui(inventoryPanel, selectionPanel, messagePanel);
    }

    private static InventoryItemView CreateItemTemplate(Transform parent, Sprite keyIcon)
    {
        GameObject root = CreateUiObject("Item Entry Template", parent);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(180f, 86f);
        Image background = root.AddComponent<Image>();
        background.sprite = GetUiSprite();
        background.type = Image.Type.Sliced;
        background.color = new Color(0.18f, 0.2f, 0.24f, 1f);
        Button button = root.AddComponent<Button>();

        GameObject iconObject = CreateUiObject("Icon", root.transform);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(12f, 0f);
        iconRect.sizeDelta = new Vector2(56f, 56f);
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = keyIcon;
        icon.preserveAspect = true;

        Text nameLabel = CreateText("Name", root.transform, "Item", 20, TextAnchor.MiddleLeft);
        RectTransform nameRect = nameLabel.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(80f, 8f);
        nameRect.offsetMax = new Vector2(-10f, -8f);

        InventoryItemView view = root.AddComponent<InventoryItemView>();
        SetObjectReference(view, "iconImage", icon);
        SetObjectReference(view, "nameLabel", nameLabel);
        SetObjectReference(view, "button", button);
        return view;
    }

    private static void BuildKeyVisual(GameObject keyObject, Material keyMaterial)
    {
        Transform oldVisual = keyObject.transform.Find("Key Visual");
        if (oldVisual != null)
        {
            UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
        }

        MeshRenderer rootRenderer = keyObject.GetComponent<MeshRenderer>();
        if (rootRenderer != null)
        {
            rootRenderer.enabled = false;
        }

        keyObject.transform.localScale = Vector3.one;
        BoxCollider collider = keyObject.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.center = Vector3.zero;
            collider.size = new Vector3(0.9f, 0.45f, 0.9f);
        }

        GameObject visual = new("Key Visual");
        visual.transform.SetParent(keyObject.transform, false);
        visual.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);

        CreatePrimitivePart("Shaft", PrimitiveType.Cube, visual.transform, new Vector3(0f, 0f, 0.08f), new Vector3(0.12f, 0.10f, 0.62f), keyMaterial);
        CreatePrimitivePart("Bow", PrimitiveType.Sphere, visual.transform, new Vector3(0f, 0f, -0.30f), new Vector3(0.34f, 0.10f, 0.34f), keyMaterial);
        CreatePrimitivePart("Tooth A", PrimitiveType.Cube, visual.transform, new Vector3(0.11f, 0f, 0.27f), new Vector3(0.20f, 0.10f, 0.12f), keyMaterial);
        CreatePrimitivePart("Tooth B", PrimitiveType.Cube, visual.transform, new Vector3(-0.08f, 0f, 0.38f), new Vector3(0.14f, 0.10f, 0.10f), keyMaterial);
    }

    private static void CreatePrimitivePart(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static ItemDefinition CreateOrUpdateKeyItem(Sprite icon)
    {
        ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(KeyAssetPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, KeyAssetPath);
        }

        SetString(item, "itemId", "stage1_key");
        SetString(item, "displayName", "Old Key");
        SetString(item, "description", "A cold, tarnished key. It looks like it belongs to the nearby door.");
        SetObjectReference(item, "icon", icon);
        EditorUtility.SetDirty(item);
        return item;
    }

    private static Sprite CreateOrUpdateKeyIcon()
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(KeyIconPath);
        if (texture == null)
        {
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "KeyIconTexture",
                filterMode = FilterMode.Point
            };
            AssetDatabase.CreateAsset(texture, KeyIconPath);
        }

        var pixels = new Color[32 * 32];
        Color transparent = new(0f, 0f, 0f, 0f);
        Color gold = new(0.95f, 0.72f, 0.18f, 1f);
        for (int index = 0; index < pixels.Length; index++) pixels[index] = transparent;
        PaintRect(pixels, 32, 11, 14, 29, 18, gold);
        PaintRect(pixels, 32, 6, 8, 16, 23, gold);
        PaintRect(pixels, 32, 18, 12, 22, 16, gold);
        PaintRect(pixels, 32, 23, 12, 27, 20, gold);
        texture.SetPixels(pixels);
        texture.Apply();
        EditorUtility.SetDirty(texture);

        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(KeyIconPath).OfType<Sprite>().FirstOrDefault();
        if (sprite == null)
        {
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
            sprite.name = "KeyIcon";
            AssetDatabase.AddObjectToAsset(sprite, texture);
        }

        EditorUtility.SetDirty(sprite);
        return sprite;
    }

    private static void PaintRect(Color[] pixels, int width, int minX, int minY, int maxX, int maxY, Color color)
    {
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                pixels[y * width + x] = color;
            }
        }
    }

    private static Material CreateOrUpdateKeyMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(KeyMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "KeyPickupMaterial" };
            AssetDatabase.CreateAsset(material, KeyMaterialPath);
        }

        material.color = new Color(0.88f, 0.58f, 0.08f, 1f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.75f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.6f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject CreatePanel(string name, Transform parent, Vector2 size)
    {
        GameObject panel = CreateUiObject(name, parent);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        Image image = panel.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.type = Image.Type.Sliced;
        image.color = new Color(0.035f, 0.045f, 0.06f, 0.96f);
        return panel;
    }

    private static Transform CreateContainer(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject container = CreateUiObject(name, parent);
        SetRect(container.GetComponent<RectTransform>(), position, size);
        HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return container.transform;
    }

    private static Button CreateButton(string name, Transform parent, string label, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        SetRect(buttonObject.GetComponent<RectTransform>(), position, size);
        Image image = buttonObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.type = Image.Type.Sliced;
        image.color = new Color(0.28f, 0.34f, 0.42f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        Text text = CreateText("Label", buttonObject.transform, label, 20, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        return button;
    }

    private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = CreateUiObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Sprite GetUiSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    private static void EnsureAssetFolders()
    {
        EnsureFolder(DataFolder);
        EnsureFolder(ItemFolder);
        EnsureFolder(MaterialFolder);
    }

    private static void EnsureFolder(string path)
    {
        string[] segments = path.Split('/');
        string current = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }

            current = next;
        }
    }

    private static GameObject FindEither(string primaryPath, string fallbackPath)
    {
        GameObject gameObject = GameObject.Find(primaryPath) ?? GameObject.Find(fallbackPath);
        if (gameObject == null)
        {
            throw new InvalidOperationException($"씬 오브젝트 '{primaryPath}' 또는 '{fallbackPath}'를 찾을 수 없습니다.");
        }

        return gameObject;
    }

    private static GameObject RequireObject(string path)
    {
        GameObject gameObject = GameObject.Find(path);
        if (gameObject == null)
        {
            throw new InvalidOperationException($"씬 오브젝트 '{path}'를 찾을 수 없습니다.");
        }

        return gameObject;
    }

    private static T GetOrAdd<T>(GameObject gameObject) where T : Component
    {
        return gameObject.GetComponent<T>() ?? gameObject.AddComponent<T>();
    }

    private static void RemoveComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component != null)
        {
            UnityEngine.Object.DestroyImmediate(component);
        }
    }

    private static void SetMarkerLabel(string path, string value)
    {
        GameObject labelObject = GameObject.Find(path);
        Text label = labelObject != null ? labelObject.GetComponent<Text>() : null;
        if (label != null)
        {
            label.text = value;
            EditorUtility.SetDirty(label);
        }
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
        if (layer >= 0)
        {
            gameObject.layer = layer;
        }

        foreach (Transform child in gameObject.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serializedObject = new(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없습니다.");
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serializedObject = new(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없습니다.");
        }

        property.stringValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serializedObject = new(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없습니다.");
        }

        property.boolValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private readonly struct Stage4Ui
    {
        public InventoryPanel InventoryPanel { get; }
        public ItemSelectionPanel SelectionPanel { get; }
        public MessagePanel MessagePanel { get; }

        public Stage4Ui(
            InventoryPanel inventoryPanel,
            ItemSelectionPanel selectionPanel,
            MessagePanel messagePanel)
        {
            InventoryPanel = inventoryPanel;
            SelectionPanel = selectionPanel;
            MessagePanel = messagePanel;
        }
    }
}
