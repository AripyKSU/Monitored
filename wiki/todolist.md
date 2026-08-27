# Development TODO

개발 중 현재 단계에서 확정하거나 적용할 수 없어 추후 보완해야 하는 항목을 기록한다. 여러 에이전트가 동시에 작업할 수 있으므로 기존 항목 수정 시에는 text.read 후 SHA 기반 text.patch를 사용한다.

## 아이템

- [ ] Flashlight 실제 인벤토리 아이콘을 찾거나 제작하고 `Assets/Data/Items/Flashlight.asset`의 icon에 연결
- [ ] WardHallKey 실제 인벤토리 아이콘을 찾거나 제작하고 `Assets/Data/Items/WardHallKey.asset`의 icon에 연결
- [ ] BasementKey 실제 인벤토리 아이콘을 찾거나 제작하고 `Assets/Data/Items/BasementKey.asset`의 icon에 연결
- [ ] MasterKey 실제 인벤토리 아이콘을 찾거나 제작하고 `Assets/Data/Items/MasterKey.asset`의 icon에 연결
- [ ] Fuse 실제 인벤토리 아이콘을 찾거나 제작하고 `Assets/Data/Items/Fuse.asset`의 icon에 연결

## NursingRoom 조명

- [ ] `Player/NursingRoomFlashlightAnchor`의 Local Position / Rotation을 Scene 뷰에서 직접 조정해 손전등 빛의 세부 위치와 방향을 확정
- [ ] `NursingRoomStorySpotLight`의 Intensity / Range / Spot Angle을 실제 플레이 화면에서 튜닝. 현재 임시값은 Intensity 2.5, Range 12, Spot Angle 55

## NursingRoom 상호작용

- [ ] `Story_WardHallKeyDrawerTarget/ApproachPoint`의 위치를 실제 플레이 동선에서 확인하고, 캐비닛 앞 자동 이동 지점이 어색하면 Local Position을 조정

## 카메라

## Dialogue UI

- [ ] `CoreSandbox`의 `Dialogue UI/Radio`를 화면 왼쪽 아래 기준으로 배치하고 무전기 Sprite의 최종 위치·크기를 조정
- [ ] `Dialogue UI/BubbleArea/Bubble`의 위치·크기를 조정해 무전기 오른쪽에 자연스럽게 이어지도록 배치. Player 화자에서 말풍선 좌우반전 후의 위치도 함께 확인
- [ ] `Dialogue UI/BubbleArea/DialogueText`의 여백·위치·크기·정렬·색상과 한글 표시용 TMP Font Asset/Material을 최종 확정
