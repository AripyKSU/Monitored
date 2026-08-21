# `TargetCommand` 시스템 기술문서

> Sources: Haunted House 프로젝트 소스 코드, 2026-08-21; User, 2026-08-21
> Raw: [`TargetCommand` 시스템 구현 기록](../../raw/technical-docs/2026-08-21-target-command-system.md); [시스템 개발 사양 합의](../../raw/game-design/2026-08-21-haunted-house-system-development-decisions.md)
> Updated: 2026-08-21
> Status: 현재 구현 기준

## 개요

`TargetCommand` 시스템은 화면의 대상 선택을 “접근 지점으로 이동한 뒤 고유 행동 실행”이라는 하나의 명령으로 처리한다. 입력과 마커는 `TargetPresenter`, 대상 규칙은 `Target`, 실행 순서는 `TargetCommandRunner`, 이동은 `Player`와 `PlayerMover`, 도착 후 행동은 `ITargetAction` 구현체가 담당한다. 일반 플레이에서는 명령 예약·취소·목표 변경을 지원하지 않으며 한 명령이 끝날 때까지 추가 선택을 차단한다.

## 구성 요소와 책임

| 구성 요소 | 책임 | 소유하지 않는 것 |
|---|---|---|
| `Target` | `ApproachPoint`, `OwnerRoom`, 선택 가능 상태, `ITargetAction` 참조 | 클릭 입력, 마커 위치, 이동 |
| `TargetPresenter` | 월드 Collider와 UI 마커 클릭, 화면 좌표 갱신 | 명령 실행 여부, 게임 규칙 |
| `TargetCommandRunner` | 단일 명령 상태, 이동→행동 순서, 입력 잠금 | NavMesh 세부 제어, 대상별 결과 |
| `Player` | 외부 이동 진입점과 최근 결과 제공 | 명령 상태, UI |
| `PlayerMover` | NavMesh 경로·도착·제한 시간 판정 | Unity 컴포넌트 생명주기, 상호작용 |
| `ITargetAction` | 도착 후 대상별 코루틴 계약 | 접근 이동 |
| `TargetActionContext` | 현재 `Target`과 `Player` 전달 | 서비스 탐색이나 가변 명령 상태 |

## 상태 모형

```text
          TryExecute 성공
Idle ─────────────────────→ Moving
 ↑                            │
 │                            │ MoveTo 성공
 │                            ▼
 └──────────────────────── Executing
       행동 완료 또는 실패 정리
```

`TargetCommandState`는 `Idle`, `Moving`, `Executing` 세 상태만 가진다. `IsBusy`는 `Idle`이 아닐 때 참이다. 상태 변경은 `StateChanged` 이벤트로 외부에 알릴 수 있지만, 현재 구현에서 상태 소유권은 `TargetCommandRunner` 한 곳에 있다.

## 선택과 표시

`TargetPresenter.LateUpdate()`는 다음 조건을 모두 만족할 때만 마커와 클릭 Collider를 활성화한다.

1. Runner가 표현을 허용한 상태다.
2. `Target.IsSelectableIn(CurrentRoom)`이 참이다.
3. `UIAnchor`가 있고 활성 Main Camera의 화면 안에 있다.

이 조건 덕분에 CCTV에 다른 방 일부가 보이더라도 `OwnerRoom`이 현재 방과 다른 Target은 선택되지 않는다. 월드 Collider와 UI Button은 모두 `SelectionRequested`로 합쳐지고, 실제 접수 여부는 Runner가 다시 검사한다.

## 명령 실행 순서

1. `TargetPresenter`가 `SelectionRequested`를 발생시킨다.
2. `TargetCommandRunner.TryExecute()`가 Busy 상태, `Player`, `PlayerRoomTracker.CurrentRoom`, Target 선택 가능 여부와 설정을 검사한다.
3. 코루틴을 시작하기 전에 상태를 `Moving`으로 바꾸고 모든 Presenter를 비활성화한다. 같은 프레임의 연속 클릭도 차단된다.
4. `Player.MoveTo(Target.ApproachPoint)`가 끝날 때까지 기다린다.
5. `LastMoveSucceeded`와 이동 중 Target 상태를 다시 검사한다.
6. 상태를 `Executing`으로 바꾸고 `TargetActionContext`를 생성한다.
7. `ITargetAction.Execute()`가 반환한 코루틴이 있으면 끝까지 기다린다.
8. `finally`에서 상태를 `Idle`로 되돌리고 Presenter를 복원한다.

행동 코루틴이 UI 입력을 기다리거나 문 통과처럼 여러 이동을 수행해도 명령 전체는 `Executing`에 머문다. 따라서 대상별 구현은 명령 잠금 방식을 별도로 만들 필요가 없다.

## 이동 구현

`Player`는 `NavMeshAgent`를 직접 외부에 노출하지 않는다. `Awake()`에서 `PlayerMover`를 생성하고 설정값과 일시정지 조회 함수를 전달한다. `PlayerMover`는 다음 실패를 정상 플레이 결과가 아닌 구성 오류로 처리한다.

- `ApproachPoint` 누락
- 비활성 또는 NavMesh 밖의 `NavMeshAgent`
- 목적지 설정 실패나 무효·부분 경로
- 경로 계산 제한 시간 초과
- 이동 제한 시간 초과
- 이동 도중 Agent 또는 목적지 무효화

도착은 남은 거리뿐 아니라 경로 계산 완료, 허용 거리, 실질적인 정지 상태를 함께 확인한다. 성공하면 목적지 Transform의 회전을 사용해 최종 방향도 맞춘다.

## 중단과 복구 규칙

- 이동 실패 시 행동을 실행하지 않고 마지막 정리 구간으로 이동한다.
- 이동 중 Target이 사라지거나 선택 불가능해지면 스테이지 오류로 기록한다.
- Runner가 비활성화되면 활성 코루틴을 중지하고 `Idle`과 Presenter 표시를 복구한다.
- 대상 행동이 정상 종료, 조기 종료, 오류 경로 중 어느 쪽이든 Runner의 `finally`가 입력 잠금을 해제한다.

## 새 Target 추가 방법

1. 월드 오브젝트에 `Target`과 대상 유형의 `ITargetAction` 구현체를 추가한다.
2. `ApproachPoint`를 자식 Transform으로 만들고 도착 위치와 최종 시선을 지정한다.
3. `OwnerRoom`과 `actionComponent`를 연결한다.
4. `TargetPresenter`에 Target, 클릭 Collider, `UIAnchor`, 마커를 연결한다.
5. 필요하면 `TargetCommandRunner.presenters`에 명시적으로 추가한다. 비어 있으면 `Awake()`에서 한 번만 검색한다.
6. `TryValidateConfiguration()`과 실제 NavMesh 도달 가능성을 검증한다.

## 확장 시 주의점

추격 중 목표 변경이나 예약을 추가하려면 `TargetCommandState`만 늘리는 것으로 충분하지 않다. 현재 명령의 취소 권한, `PlayerMover` 목적지 교체, 실행 중 Target 행동의 중단 계약, Presenter 재활성화 범위를 함께 정의해야 한다. 일반 명령의 “한 번 선택하면 완료까지 잠금” 계약은 유지하고 추격 전용 실행기를 분리하는 방식도 고려할 수 있다.

## 함께 보기

- [`CoreSandbox` 씬 구조와 검증 구성](coresandbox-scene-structure.md)
- [방과 CCTV 전환 시스템](cctv-room-transition-system.md)
- [상호작용과 인벤토리 시스템](interaction-inventory-system.md)
- [공용 일시정지 시스템](pause-system.md)
- [폐건물 탈출 공포 게임 — 시스템 개발 사양](../game-design/haunted-house-system-development-spec.md)

