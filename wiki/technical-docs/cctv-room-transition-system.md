# 방과 CCTV 전환 시스템 기술문서

> Sources: Haunted House 프로젝트 소스 코드, 2026-08-21; User, 2026-08-21
> Raw: [방과 CCTV 전환 시스템 구현 기록](../../raw/technical-docs/2026-08-21-cctv-room-transition-system.md); [시스템 개발 사양 합의](../../raw/game-design/2026-08-21-haunted-house-system-development-decisions.md)
> Updated: 2026-08-21
> Status: 현재 구현 기준

## 개요

방과 CCTV 전환 시스템은 고정된 방 연결 데이터, 플레이어의 논리적 현재 방, 방별 Cinemachine CCTV, 양방향 문 통과를 일관된 시점에 갱신한다. `Room`은 정적 맵 데이터, `PlayerRoomTracker`는 현재 방의 단일 기준, `RoomCamera`와 `CameraDirector`는 화면 시점, `Doorway` 계열은 경계와 문 상태, `DoorTarget`은 이들을 하나의 상호작용 순서로 조율한다.

## 데이터 소유권

| 데이터 | 소유자 | 설명 |
|---|---|---|
| 방 ID와 인접 방 | `Room` | 스테이지가 정한 고정 연결 정보 |
| 방별 가상 카메라 | `RoomCamera` | 하나의 `CinemachineCamera` 시점 프리셋 |
| 플레이어 현재 방 | `PlayerRoomTracker` | 논리 방 상태의 단일 기준 |
| 현재 화면의 방 | `CameraDirector` | 활성 CCTV 우선순위 상태 |
| 두 방과 통과 지점 | `Doorway` | 양방향 `DoorTraversal` 생성 |
| 문 잠금과 열림 | `Doorway`, `DoorController` | 규칙 상태와 문짝 표현을 분리 |

`CameraDirector.CurrentRoom`과 `PlayerRoomTracker.CurrentRoom`은 서로 다른 책임을 표현한다. 정상 흐름에서는 함께 바뀌지만, 카메라 전환 실패를 먼저 확인하고 논리 방을 확정하기 위해 별도 상태로 둔다.

## 방 구성

`Room`의 핵심 직렬화 필드는 `roomId`, `roomCamera`, `playerSpawnPoint`, `adjacentRooms`다. `IsAdjacentTo()`는 문 하나로 직접 연결된 방인지 판정한다. `TryValidateConfiguration()`은 빈 ID, 유효하지 않은 CCTV, null·자기 참조·중복 인접 방을 거부한다.

인접 관계는 런타임 탐색으로 자동 생성하지 않는다. 각 방의 배열에 명시적으로 저장하며 `Doorway.TryValidateConfiguration()`은 양쪽 방이 서로를 인접 방으로 등록했는지 검사한다.

## CCTV 구성과 초기화

실제 렌더링은 Main Camera 한 대와 그 위의 `CinemachineBrain`이 담당한다. 각 방의 `RoomCamera`는 `CinemachineCamera` 참조와 우선순위 변경 진입점만 제공한다.

`CameraDirector.Awake()`는 Brain의 기본 블렌드를 `Cut`, 0초로 고정한다. `Start()`에서는 별도의 초기 방 필드를 사용하지 않고 `PlayerRoomTracker.CurrentRoom`을 읽어 초기 CCTV를 선택한다. `CutTo()`는 이전 카메라 우선순위를 0, 새 카메라 우선순위를 100으로 바꾸며 설정이 잘못된 방에는 전환하지 않는다.

## 문 구조

`Doorway` 하나가 다음 데이터를 공유한다.

- `roomA`, `roomB`
- A→B와 B→A 방향별 `PassageStart`
- 목적지 방 내부의 방향별 `EntryPoint`
- A 진입용·B 진입용 `DoorwaySensor`
- 문짝 연출용 `DoorController`
- 양방향 공용 `locked`

`DoorTarget`은 각 방에서 클릭할 방향별 상호작용 대상이다. `Doorway`는 연결과 상태를 공유하고, `DoorTarget`은 현재 방에 맞는 `DoorTraversal`을 받아 실행한다.

## 방 전환 실행 순서

```text
문 Target 접근 완료
→ 현재 방, Doorway 설정, 인접 관계 확인
→ 잠금 상태 확인
→ 문 열기 완료 대기
→ PassageStart로 이동
→ 목적지 쪽 DoorwaySensor 진입
→ PassageStarted 콜백
   ├─ 목적지 CCTV로 Cut
   └─ 논리 CurrentRoom 변경
→ 목적지 EntryPoint로 이동
→ 문 닫기 완료 대기
```

핵심 전환 시점은 `DoorwaySensor.OnTriggerEnter()`다. `PassageStart`를 목적지 Sensor 안쪽에 배치해 플레이어가 문 경계를 지나기 시작하는 순간 Trigger가 발생하도록 한다. 콜백에서 CCTV와 논리 방을 같은 프레임에 갱신한 뒤, 새 방 화면 상태에서 내부 진입 이동을 계속한다.

## 원자성에 가까운 복구 규칙

방과 CCTV는 별도 상태이므로 전환 순서와 되돌리기가 중요하다.

1. 먼저 `CameraDirector.CutTo(DestinationRoom)`을 호출한다. 실패하면 논리 방을 바꾸지 않는다.
2. 이어서 `PlayerRoomTracker.EnterRoom()`을 호출한다.
3. 논리 방 변경이 예외적으로 실패하면 출발 방 CCTV로 즉시 되돌린다.
4. 두 작업이 성공해야 `passageCommitted`가 참이 된다.

이동 실패, Trigger 누락, 컴포넌트 중단 등으로 명령이 끝나면 `finally`에서 `PassageStarted` 구독을 제거한다. 정상 닫기가 끝나지 않았다면 `ForceClosed()`로 문짝을 초기 회전에 되돌린다.

## 잠긴 문과 아이템 사용

`Doorway.IsLocked`가 참이면 `DoorTarget`은 통과를 시작하지 않고 연결된 `ItemUseTarget.Execute()`를 실행한다. 올바른 아이템 효과로 호출되는 `DoorTarget.ApplyItemEffect()`는 `Doorway.SetLocked(false)`만 수행한다. 따라서 잠금 해제 직후 자동 통과하지 않고, 사용자가 문을 다시 선택해야 기존 통과 흐름이 시작된다.

## 새 방과 문 추가 점검표

- 각 `Room`에 고유 `RoomId`, 유효한 `RoomCamera`, `PlayerSpawnPoint`를 지정한다.
- 연결된 두 방의 `adjacentRooms`에 서로를 한 번씩 등록한다.
- 한 `Doorway`에 양쪽 방과 네 통과 지점, 두 Sensor, `DoorController`를 연결한다.
- 각 방향 Target의 `OwnerRoom`과 `ApproachPoint`를 올바르게 지정한다.
- `PassageStart`가 목적지 Sensor Trigger를 실제로 통과하는지 NavMesh 경로로 시험한다.
- 초기 방은 `PlayerRoomTracker.initialRoom` 한 곳에서 지정한다.

## 함께 보기

- [`CoreSandbox` 씬 구조와 검증 구성](coresandbox-scene-structure.md)
- [`TargetCommand` 시스템](target-command-system.md)
- [상호작용과 인벤토리 시스템](interaction-inventory-system.md)
- [폐건물 탈출 공포 게임 — 시스템 개발 사양](../game-design/haunted-house-system-development-spec.md)

