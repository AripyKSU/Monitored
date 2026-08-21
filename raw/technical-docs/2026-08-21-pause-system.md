# 공용 일시정지 시스템 구현 기록

> Source: Haunted House 프로젝트 `GamePauseService.cs` 및 소비자 런타임 코드
> Collected: 2026-08-21
> Published: Unknown

## 원천 파일

- `Scripts/Core/GamePauseService.cs`
- `Scripts/UI/InventoryPanel.cs`
- `Scripts/Interaction/ItemUseTarget.cs`
- `Scripts/Commands/Player.cs`
- `Scripts/Commands/PlayerMover.cs`

## 공개 계약

```csharp
public bool IsPaused { get; }
public int ActiveRequestCount { get; }
public event Action<bool> PauseStateChanged;
public PauseHandle AcquirePause(UnityEngine.Object owner);
public sealed class PauseHandle : IDisposable;
```

`GamePauseService`는 요청 번호와 소유자를 `Dictionary<int, UnityEngine.Object>`에 저장한다. 첫 요청에서 현재 `Time.timeScale`을 보존하고 0으로 설정한다. 각 `PauseHandle.Dispose()`는 자기 요청만 제거하며 마지막 요청이 해제될 때 보존한 배율을 복원한다. 중복 `Dispose()`는 무시한다.

## 현재 소비자

- `InventoryPanel`: `pauseWhileOpen`이 참이면 열 때 획득하고 닫거나 비활성화될 때 해제한다.
- `ItemUseTarget`: 아이템 선택을 시작할 때 획득하고 `finally` 또는 `OnDisable()`에서 해제한다.
- `Player`/`PlayerMover`: `IsPaused`를 읽어 NavMesh 이동과 제한 시간 누적을 보류한다.

`GamePauseService.OnDisable()`은 남은 요청을 비우고 보존한 `Time.timeScale`을 복원한다. 서비스는 요청을 발급하고 전체 정지 상태만 관리하며 개별 UI의 닫기 입력이나 코루틴 종료 조건은 소유하지 않는다.

