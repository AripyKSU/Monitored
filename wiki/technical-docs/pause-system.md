# 공용 일시정지 시스템 기술문서

> Sources: Haunted House 프로젝트 소스 코드, 2026-08-21; User, 2026-08-21
> Raw: [공용 일시정지 시스템 구현 기록](../../raw/technical-docs/2026-08-21-pause-system.md); [시스템 개발 사양 합의](../../raw/game-design/2026-08-21-haunted-house-system-development-decisions.md)
> Updated: 2026-08-21
> Status: 현재 구현 기준

## 개요

`GamePauseService`는 인벤토리, 아이템 선택, 향후 대화와 퍼즐이 공유하는 참조 계수 방식의 일시정지 서비스다. 각 요청자는 `AcquirePause()`로 자기 `PauseHandle`을 받고, 작업이 끝나면 `Dispose()`한다. 요청이 하나라도 남아 있으면 게임 시간을 멈추고 마지막 요청이 해제될 때만 이전 `Time.timeScale`을 복원하므로 여러 모달 흐름이 겹쳐도 한 요청이 다른 요청의 정지를 임의로 풀지 않는다.

## 공개 계약

| 구성 | 의미 |
|---|---|
| `IsPaused` | 해제되지 않은 요청이 하나 이상인지 반환한다. |
| `ActiveRequestCount` | 현재 요청 수를 반환한다. |
| `PauseStateChanged` | 전체 상태가 실행↔정지로 실제 변경될 때 발생한다. |
| `AcquirePause(owner)` | 요청을 등록하고 전용 `PauseHandle`을 반환한다. |
| `PauseHandle.Dispose()` | 해당 요청 하나만 해제한다. |

`owner`는 현재 진단을 위한 `UnityEngine.Object` 참조로 저장된다. 해제 권한은 발급된 Handle에 있으며 외부에 전체 강제 해제 메서드를 제공하지 않는다.

## 상태 전환

```text
요청 0개
  │ 첫 AcquirePause(owner)
  ▼
timeScaleBeforePause = Time.timeScale
Time.timeScale = 0
PauseStateChanged(true)
  │
  ├─ 추가 AcquirePause: 요청만 추가, 상태 이벤트 없음
  ├─ 일부 Dispose: 요청만 제거, 여전히 정지
  │
  └─ 마지막 Dispose
       Time.timeScale = timeScaleBeforePause
       PauseStateChanged(false)
```

`PauseHandle`은 내부 서비스 참조를 첫 `Dispose()`에서 null로 바꾸므로 중복 호출은 안전하게 무시된다. 요청 번호는 증가하는 정수이며 서비스의 Dictionary 키로 사용된다.

## 소비자별 생명주기

### `InventoryPanel`

`pauseWhileOpen`이 참일 때 `Open()`에서 Handle을 획득하고 `Close()`에서 해제한다. 패널이 비활성화되는 예외 경로에서도 `OnDisable()`이 해제하므로 UI가 닫혔는데 게임이 계속 멈춘 상태를 방지한다.

### `ItemUseTarget`

아이템 선택 직전에 Handle을 획득한다. 선택, 취소, 오답 메시지, 정답 효과 중 어느 경로로 끝나도 `finally`에서 패널과 이벤트를 정리한 뒤 Handle을 해제한다. 컴포넌트 비활성화에도 `OnDisable()`이 보조 정리를 수행한다.

### `Player`와 `PlayerMover`

`Player`는 `GamePauseService.IsPaused`를 조회하는 함수를 `PlayerMover`에 전달한다. 이동 중 일시정지가 시작되면 `NavMeshAgent.isStopped`를 참으로 만들고 경로 계산 및 이동 제한 시간의 `unscaledDeltaTime` 누적도 보류한다. 해제되면 Agent 이동을 다시 허용한다.

이는 `Time.timeScale = 0`만 믿는 것보다 이동 계약이 명시적이다. 특히 제한 시간은 unscaled time을 사용하므로 별도 보류가 없으면 UI를 오래 열어 둔 것만으로 이동 실패가 발생할 수 있다.

## 일시정지 중 계속 동작해야 하는 UI

일시정지는 게임 시간과 월드 진행을 멈추지만 UI 입력과 코루틴 자체를 모두 중지하지 않는다. 현재 `MessagePanel.Show()`와 아이템 선택 대기는 `yield return null`로 사용자 입력을 기다려 정지 중에도 완료할 수 있다. 반대로 `WaitForSeconds`, `Time.deltaTime`, Animator의 기본 Update Mode처럼 scaled time에 의존하는 연출은 정지 중 진행하지 않는다.

새 UI는 다음 원칙을 따라야 한다.

- 닫기·확인 입력은 `Time.timeScale == 0`에서도 동작해야 한다.
- 반드시 `try/finally`, `OnDisable()` 또는 둘 다에서 Handle을 해제한다.
- 다른 시스템이 정지 중일 수 있으므로 `Time.timeScale = 1`을 직접 설정하지 않는다.
- 정지 여부가 필요하면 `Time.timeScale` 비교 대신 `GamePauseService.IsPaused`를 사용한다.

## 서비스 비활성화와 씬 종료

`GamePauseService.OnDisable()`은 요청이 남아 있으면 Dictionary를 비우고 저장해 둔 시간 배율을 복원한 뒤 `PauseStateChanged(false)`를 발생시킨다. 이는 씬 전환이나 `GameSession` 비활성화 시 정지 상태가 다음 흐름에 누출되는 것을 막는 최종 안전장치다.

## 확장 시 주의점

- 현재 구현은 서비스 외부에서 일시정지 중 `Time.timeScale`을 변경하지 않는다는 전제를 가진다.
- `nextRequestId`가 매우 긴 실행에서 넘치는 상황은 현재 범위에서 처리하지 않는다.
- 소유자 파괴를 자동 감지해 요청을 회수하지 않으므로 각 소비자가 Handle 수명을 책임져야 한다.
- 오디오, 입력 맵, Animator, 파티클 등은 `timeScale`만으로 원하는 정지 정책이 되지 않을 수 있다. 새 동작은 `PauseStateChanged` 구독 또는 서비스의 중앙 정책 확장으로 처리해야 한다.

## 함께 보기

- [`CoreSandbox` 씬 구조와 검증 구성](coresandbox-scene-structure.md)
- [`TargetCommand` 시스템](target-command-system.md)
- [상호작용과 인벤토리 시스템](interaction-inventory-system.md)
- [폐건물 탈출 공포 게임 — 시스템 개발 사양](../game-design/haunted-house-system-development-spec.md)

