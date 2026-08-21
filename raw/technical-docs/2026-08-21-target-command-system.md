# TargetCommand 시스템 구현 기록

> Source: Haunted House 프로젝트 `Commands`, `UI/TargetPresenter.cs` 런타임 코드
> Collected: 2026-08-21
> Published: Unknown

## 원천 파일

- `Scripts/Commands/Target.cs`
- `Scripts/Commands/TargetCommandRunner.cs`
- `Scripts/Commands/TargetCommandState.cs`
- `Scripts/Commands/TargetActionContext.cs`
- `Scripts/Commands/ITargetAction.cs`
- `Scripts/Commands/Player.cs`
- `Scripts/Commands/PlayerMover.cs`
- `Scripts/UI/TargetPresenter.cs`

## 공개 계약

```csharp
public enum TargetCommandState { Idle, Moving, Executing }
public bool TargetCommandRunner.TryExecute(Target target)
public IEnumerator Player.MoveTo(Transform approachPoint)
IEnumerator ITargetAction.Execute(TargetActionContext context)
```

`Target`은 `approachPoint`, `ownerRoom`, `selectable`, `actionComponent`를 직렬화한다. `Action`은 `actionComponent as ITargetAction`으로 제공한다. `TargetPresenter`는 화면 마커 및 클릭만 담당하고 `SelectionRequested`를 발생시킨다.

## 실행 순서

```text
TargetPresenter 선택 요청
→ TargetCommandRunner.TryExecute()
→ Moving, 모든 Presenter 비활성
→ Player.MoveTo(Target.ApproachPoint)
→ Executing
→ ITargetAction.Execute(TargetActionContext)
→ Idle, 모든 Presenter 복원
```

`TargetCommandRunner`는 대기열을 갖지 않으며 `IsBusy`일 때 추가 요청을 거부한다. `finally`에서 `activeCommand`, 상태, Presenter 표시를 정리한다.

## 이동과 실패 판정

`Player`는 `NavMeshAgent` 세부 처리를 내부 일반 C# 클래스인 `PlayerMover`에 위임한다. 가장 최근 결과는 `LastMoveSucceeded`, `LastMoveFailureReason`으로 노출한다. 잘못된 `ApproachPoint`, NavMesh 미배치, 경로 계산 실패, 부분 경로, 제한 시간 초과는 설정 오류로 기록한다.

공용 일시정지 중 `PlayerMover`는 `NavMeshAgent.isStopped`를 사용해 이동을 멈추고 경로 계산 및 이동 제한 시간 누적도 보류한다.

