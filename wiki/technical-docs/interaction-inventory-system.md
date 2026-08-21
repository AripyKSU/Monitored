# 상호작용과 인벤토리 시스템 기술문서

> Sources: Haunted House 프로젝트 소스 코드, 2026-08-21; User, 2026-08-21
> Raw: [상호작용과 인벤토리 시스템 구현 기록](../../raw/technical-docs/2026-08-21-interaction-inventory-system.md); [시스템 개발 사양 합의](../../raw/game-design/2026-08-21-haunted-house-system-development-decisions.md)
> Updated: 2026-08-21
> Status: 현재 구현 기준

## 개요

상호작용 시스템은 `TargetCommand`가 접근을 끝낸 뒤 대상 유형별 결과를 `ITargetAction`으로 실행한다. 인벤토리는 고유 아이템의 보유 상태만 관리하고, 화면 열람과 대상에 사용할 아이템 선택은 별도 UI가 담당한다. 현재 구현은 아이템 획득, 메시지 조사, 잠긴 문에 아이템 사용, 문 통과를 지원하며 수량·중첩·조합·장비는 포함하지 않는다.

## 계층과 책임

```text
TargetCommandRunner
└─ ITargetAction.Execute(context)
   ├─ ItemPickupTarget ──→ PlayerInventory.TryAdd()
   ├─ MessageTarget ─────→ MessagePanel.Show()
   ├─ ItemUseTarget
   │  ├─ ItemSelectionPanel
   │  ├─ PlayerInventory.TryRemove()
   │  └─ IItemUseEffect.ApplyItemEffect()
   └─ DoorTarget ────────→ Doorway 및 방 전환
```

이 구조에서 `ITargetAction`은 시간 기반 행동의 공통 계약이고 `IItemUseEffect`는 올바른 아이템을 소비한 뒤 적용할 즉시 효과의 계약이다. 아이템 선택 UI는 정답을 모르고, 인벤토리는 사용 대상을 모른다.

## 아이템 데이터

`ItemDefinition`은 다음 표시·식별 데이터만 가진 `ScriptableObject`다.

| 필드 | 용도 |
|---|---|
| `itemId` | 저장과 비교에 사용하는 고유 식별자 |
| `displayName` | UI 표시 이름 |
| `description` | 인벤토리 설명 |
| `icon` | 목록 아이콘 |

`TryValidate()`는 최소한 `ItemId`와 표시 이름이 있는지 검사한다. 사용 효과, 수량, 월드 오브젝트 상태는 정의에 넣지 않는다.

## 보유 상태

`PlayerInventory`는 `List<ItemDefinition>`을 내부에 보관하고 읽기 전용 `Items`로 노출한다. 아이템 동일성은 `ScriptableObject` 참조가 아니라 `ItemId`의 `StringComparison.Ordinal` 비교로 결정한다.

- `TryAdd()`: 유효하고 같은 `ItemId`가 없는 아이템만 추가한다.
- `Contains()`: 같은 `ItemId`의 보유 여부를 반환한다.
- `TryRemove()`: 같은 `ItemId`의 첫 항목을 제거한다.
- `Changed`: 추가·제거 뒤 UI와 진행 시스템에 알린다.

모든 아이템이 고유하므로 수량 필드와 중첩 로직은 없다. 초기 아이템은 `initialItems`로 주입되며 `Awake()`에서 같은 검증을 거쳐 등록된다.

## 아이템 획득 흐름

`ItemPickupTarget.Execute()`는 다음 순서를 보장한다.

1. Target, 아이템, 인벤토리, 메시지 패널 참조를 검사한다.
2. `PlayerInventory.TryAdd(item)`을 호출한다.
3. 설정 메시지 또는 기본 획득 메시지를 표시하고 사용자 확인을 기다린다.
4. 같은 프레임 재선택을 막기 위해 `Target.SetSelectable(false)`를 먼저 호출한다.
5. 지정된 `worldObject`, 없으면 자기 GameObject를 비활성화한다.

아이템 추가가 실패한 경우 월드 오브젝트를 소비하지 않는다. 중복 아이템이 정상적으로 존재할 수 없는 현재 설계에서는 이를 설정 오류로 기록한다.

## 아이템 사용 흐름

`ItemUseTarget`은 대상별 `requiredItem`과 `IItemUseEffect` 구현체를 연결한다.

```text
Target 도착
→ GamePauseService.AcquirePause()
→ ItemSelectionPanel.Open(inventory.Items)
→ 선택 또는 취소 대기
├─ 취소: 정리 후 명령 종료
├─ 오답: 실패 메시지, 아이템 유지
└─ 정답: TryRemove() → ApplyItemEffect() → 성공 메시지
→ 이벤트·패널·PauseHandle 정리
```

정답 비교는 선택 아이템과 요구 아이템의 `ItemId`로 한다. 효과 적용보다 제거를 먼저 수행하므로 효과가 실행됐는데 아이템이 남는 상태를 방지한다. `try/finally`는 선택 패널 열기 실패나 코루틴 조기 종료에도 이벤트 구독과 일시정지를 회수한다.

## 현재 상호작용 유형

| 구현체 | 결과 |
|---|---|
| `ItemPickupTarget` | 고유 아이템 획득 후 월드 표현 소모 |
| `MessageTarget` | 공용 메시지를 표시하고 확인까지 대기 |
| `ItemUseTarget` | 보유 아이템 선택, 정답 검사, 소비와 효과 적용 |
| `DoorTarget` | 잠금 상태에 따라 아이템 사용 또는 양방향 문 통과 |
| `DummyMessageTargetAction` | `CoreSandbox` 단계 검증용 지연 메시지 |

`DoorTarget`은 `ITargetAction`과 `IItemUseEffect`를 함께 구현한다. 잠긴 문에서 정답 열쇠를 사용하면 `Doorway`의 공용 잠금 상태만 해제한다. 문 통과는 다음 선택에서 별도의 명령으로 실행된다.

## UI 구성

### `InventoryPanel`

보유 아이템의 아이콘, 이름과 설명을 열람한다. `Changed`를 구독해 내용을 갱신하며, `pauseWhileOpen`이 참이면 열려 있는 동안 공용 일시정지를 보유한다. 아이템 소비나 사용 가능 여부는 판단하지 않는다.

### `ItemSelectionPanel`

전달받은 보유 목록으로 선택 가능한 `InventoryItemView`를 만든다. 결과는 `ItemSelected` 또는 `Cancelled` 이벤트로만 알리며 정답과 효과를 알지 못한다.

### `InventoryItemView`

두 패널이 재사용하는 한 칸의 표현이다. 선택 콜백이 있으면 Button을 활성화하고, 없으면 읽기 전용 설명 선택 칸으로 동작한다. 재바인딩과 파괴 시 이전 리스너를 제거한다.

### `MessagePanel`

획득·실패·조사 메시지를 한 화면에서 처리한다. `Show()`는 확인 버튼이 눌릴 때까지 매 프레임 기다리며 scaled time을 사용하지 않으므로 `Time.timeScale == 0`에서도 닫을 수 있다.

## 새 상호작용 추가 원칙

- 접근 후 시간이 걸리는 행동은 `ITargetAction.Execute()` 코루틴으로 구현한다.
- 아이템 소비 뒤 즉시 적용할 효과는 `IItemUseEffect`로 분리한다.
- UI는 결과 선택만 전달하고 게임 규칙은 Target 행동 구현체가 소유한다.
- 인벤토리 변경은 반드시 `PlayerInventory`의 공개 메서드를 통해 `Changed` 이벤트를 발생시킨다.
- 소모 대상은 재선택을 막은 뒤 비활성화한다.
- 퍼즐처럼 일시정지가 필요한 새 UI는 자체 Boolean 대신 공용 `PauseHandle`을 소유한다.

## 함께 보기

- [`CoreSandbox` 씬 구조와 검증 구성](coresandbox-scene-structure.md)
- [`TargetCommand` 시스템](target-command-system.md)
- [방과 CCTV 전환 시스템](cctv-room-transition-system.md)
- [공용 일시정지 시스템](pause-system.md)
- [폐건물 탈출 공포 게임 — 시스템 개발 사양](../game-design/haunted-house-system-development-spec.md)

