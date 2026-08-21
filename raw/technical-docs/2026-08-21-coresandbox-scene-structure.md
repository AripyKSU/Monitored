# CoreSandbox 씬 구조 구현 기록

> Source: Haunted House 프로젝트의 `CoreSandbox.unity`, `CoreSandboxStage4Setup.cs` 및 연결된 런타임 코드
> Collected: 2026-08-21
> Published: Unknown

## 원천 파일

- `Assets/_3DStealthGame/Scenes/CoreSandbox.unity`
- `Assets/_3DStealthGame/Editor/CoreSandboxStage4Setup.cs`

## 구현에서 확인한 씬 경로

`CoreSandboxStage4Setup.Setup()`은 아래 오브젝트를 필수 경로로 조회한다.

```text
GameSession
Player
Target UI
Room_A/KeyPickup
Room_B/MessageObject
Door_A_B
Door_A_B/From_A_Target
Door_A_B/From_B_Target
```

이전 단계 이름과 호환하기 위해 `Room_A/ItemTarget`, `Room_B/ItemUseTarget`도 대체 경로로 조회한 뒤 각각 `KeyPickup`, `MessageObject`로 이름을 정규화한다.

## 공용 오브젝트에 연결되는 컴포넌트

- `GameSession`: `GamePauseService`, `PlayerInventory`
- `Player`: `Player`, `NavMeshAgent`; `Player.pauseService`에 `GamePauseService` 연결
- `Target UI`: 기존 Target 마커와 `Stage4 UI`를 포함하는 Canvas 루트
- `Door_A_B`: `Doorway`; 양방향 `DoorTarget`과 공용 잠금 상태를 연결

## 4단계 화면 계층

`BuildStage4Ui()`가 `Target UI` 아래에 다음 계층을 생성한다.

```text
Target UI
└─ Stage4 UI
   ├─ Templates
   │  └─ Item Entry Template
   ├─ Inventory Panel
   │  ├─ Title
   │  ├─ Items
   │  ├─ Description
   │  ├─ Empty
   │  └─ Close
   ├─ Item Selection Panel
   │  ├─ Title
   │  ├─ Items
   │  ├─ Empty
   │  └─ Cancel
   ├─ Message Panel
   │  ├─ Message
   │  └─ Confirm
   └─ Inventory Button
```

`Stage4 UI`에는 `InventoryPanel`, `ItemSelectionPanel`, `MessagePanel`이 연결된다. 세 패널 루트는 저장 시 비활성 상태이며, `Inventory Button`, `Close`, `Cancel`은 각 공개 메서드에 영구 리스너로 연결된다.

## 검증 계약

`CoreSandboxStage4Setup.ValidateScene()`은 유효한 `Key ItemDefinition`, `GamePauseService`, `PlayerInventory`, 세 UI 패널, `ItemPickupTarget`, `MessageTarget`, 잠긴 `Door_A_B`, 두 개의 `DoorTarget`, 두 개의 문용 `ItemUseTarget`을 요구한다. 각 `ItemUseTarget.TryValidateConfiguration()`도 통과해야 한다.

