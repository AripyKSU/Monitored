# 방과 CCTV 전환 시스템 구현 기록

> Source: Haunted House 프로젝트 `World` 및 `Interaction/DoorTarget.cs` 런타임 코드
> Collected: 2026-08-21
> Published: Unknown

## 원천 파일

- `Scripts/World/Room.cs`
- `Scripts/World/RoomCamera.cs`
- `Scripts/World/CameraDirector.cs`
- `Scripts/World/PlayerRoomTracker.cs`
- `Scripts/World/Doorway.cs`
- `Scripts/World/DoorTraversal.cs`
- `Scripts/World/DoorwaySensor.cs`
- `Scripts/World/DoorController.cs`
- `Scripts/Interaction/DoorTarget.cs`

## 데이터와 공개 계약

`Room`은 `roomId`, `roomCamera`, `playerSpawnPoint`, `adjacentRooms`를 가진다. `PlayerRoomTracker`가 `CurrentRoom`의 단일 기준이며 `EnterRoom(Room)` 성공 후 `RoomEntered`를 발생시킨다.

`RoomCamera`는 `CinemachineCamera`와 우선순위 변경만 감싼다. `CameraDirector.CutTo(Room)`는 현재 가상 카메라 우선순위를 0, 목적지 우선순위를 100으로 설정한다. `CinemachineBrain.DefaultBlend`는 `Cut`, 0초다.

`Doorway`는 두 방, 양방향 `PassageStart`, 양방향 `EntryPoint`, 두 `DoorwaySensor`, `DoorController`, 공용 `locked`를 가진다. `TryGetTraversal(Room, out DoorTraversal)`은 출발 방향에 맞는 통과 데이터를 만든다.

## 문 통과 순서

```text
DoorTarget.Execute()
→ 현재 방과 인접 관계 검증
→ Doorway.Open()
→ Player.MoveTo(PassageStart)
→ DoorwaySensor.OnTriggerEnter()
→ Doorway.PassageStarted
→ CameraDirector.CutTo(DestinationRoom)
→ PlayerRoomTracker.EnterRoom(DestinationRoom)
→ Player.MoveTo(EntryPoint)
→ Doorway.Close()
```

카메라 전환과 논리 방 변경은 동일한 Trigger 콜백에서 확정한다. 카메라 전환 뒤 논리 방 변경이 실패하면 출발 방 CCTV로 되돌린다. 통과 도중 실패하면 `finally`에서 이벤트 구독을 해제하고 `Doorway.ForceClosed()`로 문짝을 복구한다.

