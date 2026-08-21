# `CoreSandbox` 씬 구조와 검증 구성

> Sources: Haunted House 프로젝트 소스 코드, 2026-08-21
> Raw: [`CoreSandbox` 씬 구조 구현 기록](../../raw/technical-docs/2026-08-21-coresandbox-scene-structure.md)
> Updated: 2026-08-21
> Status: 현재 구현 기준

## 개요

`CoreSandbox`는 방·CCTV 전환, `TargetCommand`, 상호작용, 인벤토리, 공용 일시정지를 한 씬에서 통합 검증하는 개발용 샌드박스다. 씬의 핵심은 `GameSession`의 공용 상태, `Player`의 이동 주체, `Room_A`와 `Room_B`의 월드 데이터, 두 방을 잇는 `Door_A_B`, 그리고 월드 Target 마커와 모달 UI를 담은 `Target UI`로 나뉜다. `CoreSandboxStage4Setup`은 4단계 구성을 반복 적용하고 필수 참조를 검사하는 Editor 도구다.

## 최상위 책임 구조

```text
CoreSandbox
├─ GameSession                 공용 런타임 서비스와 상태
│  ├─ GamePauseService
│  └─ PlayerInventory
├─ Player                      명령 실행 주체와 NavMesh 이동
├─ Room_A                      시작 방, 열쇠 획득 대상
│  └─ KeyPickup
├─ Room_B                      목적지 방, 조사 메시지 대상
│  └─ MessageObject
├─ Door_A_B                    양방향 방 연결과 잠긴 문
│  ├─ From_A_Target
│  └─ From_B_Target
└─ Target UI                  Target 마커와 4단계 UI
   └─ Stage4 UI
```

이 트리는 `CoreSandboxStage4Setup`이 이름으로 직접 조회하는 경로를 중심으로 정리한 구조다. `Room`, 각 방의 `RoomCamera`, `PlayerRoomTracker`, `CameraDirector` 같은 참조는 이 오브젝트 계층에 배치된 컴포넌트를 통해 연결된다.

## 공용 런타임 영역

### `GameSession`

씬 전체에서 한 개만 필요한 상태와 서비스를 둔다.

| 컴포넌트 | 역할 |
|---|---|
| `GamePauseService` | 여러 UI와 연출의 중첩 일시정지 요청을 관리한다. |
| `PlayerInventory` | 현재 세션에서 보유한 고유 아이템 목록을 관리한다. |

두 컴포넌트는 월드 오브젝트 생명주기와 분리되어 있으므로 아이템이 사라지거나 문 Target이 전환되어도 공용 상태를 유지한다.

### `Player`

`Player`에는 `NavMeshAgent`와 `Player` 컴포넌트가 필요하다. `Player`는 외부에 `MoveTo()`와 최근 이동 결과만 공개하고, NavMesh 세부 제어는 내부 `PlayerMover`에 위임한다. `GamePauseService`도 연결되어 모달 UI가 열린 동안 진행 중인 이동을 안전하게 보류한다.

## 방과 문 영역

`Room_A`와 `Room_B`는 각자의 `Room`, `RoomCamera`, Target을 소유한다. Target의 `OwnerRoom`은 표시와 선택 가능 범위를 제한하므로 다른 방 일부가 CCTV 화면에 보이더라도 현재 논리 방의 대상만 선택된다.

`Door_A_B`는 하나의 `Doorway`로 양방향 연결을 표현한다. 두 자식 Target은 같은 문을 서로 다른 방에서 선택하기 위한 방향별 진입점이다. 잠금 상태와 문짝 연출은 공용 `Doorway`에 있으므로 어느 방향에서 보더라도 상태가 일치한다.

## 상호작용 검증 대상

| 경로 | 핵심 컴포넌트 | 검증 목적 |
|---|---|---|
| `Room_A/KeyPickup` | `Target`, `ItemPickupTarget` | 접근, 고유 아이템 획득, 월드 오브젝트 소모 |
| `Room_B/MessageObject` | `Target`, `MessageTarget` | 접근 후 메시지 표시와 확인 대기 |
| `Door_A_B/From_A_Target` | `Target`, `DoorTarget`, `ItemUseTarget` | 잠긴 문, 열쇠 선택, A→B 전환 |
| `Door_A_B/From_B_Target` | `Target`, `DoorTarget`, `ItemUseTarget` | 공용 잠금 상태, B→A 전환 |

잠긴 문에 열쇠를 사용하면 그 명령에서는 잠금만 해제된다. 실제 통과는 문을 다시 선택했을 때 시작되므로 아이템 사용과 방 전환을 각각 독립적으로 확인할 수 있다.

## 화면 계층

`Target UI`는 월드 Target 마커와 `Stage4 UI`를 함께 담는다. `Stage4 UI`에는 다음 세 종류의 모달 패널과 공용 아이템 칸 템플릿이 있다.

- `Inventory Panel`: 현재 보유 목록과 설명을 열람한다.
- `Item Selection Panel`: 상호작용 대상에 사용할 아이템을 선택한다.
- `Message Panel`: 획득·실패·조사 메시지를 표시하고 확인을 기다린다.
- `Templates/Item Entry Template`: `InventoryItemView`를 두 아이템 목록에서 재사용한다.

패널 루트는 저장 상태에서 비활성화한다. `Inventory Button`, `Close`, `Cancel`, `Confirm` 입력은 각 패널의 공개 동작으로 연결한다.

## 설정과 검증 절차

1. Unity에서 `CoreSandbox`를 활성 씬으로 연다.
2. `Haunted House/Setup CoreSandbox Stage 4`를 실행해 에셋, 컴포넌트, UI, 참조를 정규화한다.
3. `Haunted House/Validate CoreSandbox Stage 4`를 실행한다.
4. Play Mode에서 열쇠 획득, 인벤토리 열람, 잠긴 문 아이템 선택, 잠금 해제, 재선택 후 방 전환을 확인한다.

검증기는 `Key ItemDefinition`, 공용 서비스, 세 패널, 상호작용 컴포넌트, 잠긴 문 상태, 양방향 Target 개수와 각 `ItemUseTarget`의 설정을 검사한다. 이는 런타임 오류를 정상 게임 분기로 숨기지 않고 씬 설정 단계에서 발견하기 위한 계약이다.

## 변경 시 주의점

- 필수 오브젝트 이름을 바꾸면 `CoreSandboxStage4Setup`의 경로도 함께 갱신해야 한다.
- `Stage4 UI`는 설정 도구 실행 시 다시 생성되므로 수동 UI 수정은 도구 코드에 반영해야 유지된다.
- 문 양쪽 Target은 하나의 `Doorway`를 공유해야 잠금과 열림 상태가 어긋나지 않는다.
- Target을 추가하면 `OwnerRoom`, `ApproachPoint`, `ITargetAction`, `TargetPresenter` 연결을 모두 검증해야 한다.

## 함께 보기

- [`TargetCommand` 시스템](target-command-system.md)
- [방과 CCTV 전환 시스템](cctv-room-transition-system.md)
- [상호작용과 인벤토리 시스템](interaction-inventory-system.md)
- [공용 일시정지 시스템](pause-system.md)
- [폐건물 탈출 공포 게임 — 시스템 개발 사양](../game-design/haunted-house-system-development-spec.md)

