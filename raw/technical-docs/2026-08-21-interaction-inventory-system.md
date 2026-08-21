# 상호작용과 인벤토리 시스템 구현 기록

> Source: Haunted House 프로젝트 `Interaction`, `Inventory`, `UI` 런타임 코드
> Collected: 2026-08-21
> Published: Unknown

## 원천 파일

- `Scripts/Interaction/ItemPickupTarget.cs`
- `Scripts/Interaction/ItemUseTarget.cs`
- `Scripts/Interaction/MessageTarget.cs`
- `Scripts/Interaction/DoorTarget.cs`
- `Scripts/Interaction/IItemUseEffect.cs`
- `Scripts/Inventory/ItemDefinition.cs`
- `Scripts/Inventory/PlayerInventory.cs`
- `Scripts/UI/InventoryPanel.cs`
- `Scripts/UI/InventoryItemView.cs`
- `Scripts/UI/ItemSelectionPanel.cs`
- `Scripts/UI/MessagePanel.cs`

## 데이터와 이벤트

`ItemDefinition`은 `itemId`, `displayName`, `description`, `icon`을 가진 정적 `ScriptableObject`다. `PlayerInventory`는 `ItemId`의 서수 비교로 고유성을 판정하며 `TryAdd()`, `Contains()`, `TryRemove()`와 `Changed` 이벤트를 제공한다.

`InventoryItemView`는 인벤토리 열람과 아이템 선택에서 공유한다. `InventoryPanel`은 보유 목록과 설명을 표시하고, `ItemSelectionPanel`은 `ItemSelected` 또는 `Cancelled` 결과만 전달한다. `MessagePanel.Show()`는 확인 버튼 입력까지 코루틴을 유지한다.

## 상호작용 흐름

`ItemPickupTarget.Execute()`는 아이템 추가, 획득 메시지 확인, `Target.SetSelectable(false)`, 월드 오브젝트 비활성화를 순서대로 실행한다.

`ItemUseTarget.Execute()`는 공용 일시정지를 획득하고 보유 아이템 선택 창을 연다. 선택한 `ItemId`가 `requiredItem.ItemId`와 다르면 실패 메시지를 표시하고 유지한다. 일치하면 인벤토리에서 먼저 제거한 뒤 `IItemUseEffect.ApplyItemEffect()`를 호출한다. `finally`에서 이벤트, 패널, 일시정지 Handle을 정리한다.

`DoorTarget`은 `IItemUseEffect`도 구현한다. 올바른 열쇠의 효과는 `Doorway.SetLocked(false)`만 수행하며, 실제 문 통과는 다음 `DoorTarget` 명령에서 실행한다.

