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
- [ ] `Dialogue UI/BubbleArea/DialogueText`의 여백·위치·크기·정렬·색상과 한글 표시용 TMP Font Asset/Material을 최종 확정, 현재 mabinogi 글씨가 제대로 적용이 안됬다고 함

## Story Dialogue 문구

각 항목의 실제 문장과 화자(Radio / Player)를 작성한다. Sector1에서 이미 생성된 Dialogue asset은 괄호로 표시한다.

### Sector1

- [ ] `[게임 시작 무전기 대화]` 실제 문구 작성 및 화자 확정 (`S1_IntroRadioDialogue.asset`)
- [ ] `[복도 끝 문이 닫히는 것을 본 주인공]` 실제 문구 작성 및 화자 확정 (`S1_HallwayDoorScareDialogue.asset`)
- [ ] `[일기장 1번]` 실제 문구 작성 및 화자 확정 (`S1_Diary1Dialogue.asset`)

### NursingRoom

- [ ] `[너무 어둡다]` 문구 확정 (`NursingRoomStoryController`의 `darkMessage`)

### Sector2

- [ ] `[중앙 복도 철창 발견]` 실제 문구 작성 및 화자 확정
- [ ] `[빨리 숨어야 한다]` 실제 문구 작성 및 화자 확정
- [ ] `[살인마가 돌아다니고 있으니 조심해야 한다]` 실제 문구 작성 및 화자 확정

### Sector3

- [ ] `[이 문이 출구이며 마스터키가 필요하다]` 실제 문구 작성 및 화자 확정
- [ ] `[식당 힌트보드]` 실제 문구 작성 및 화자 확정
- [ ] `[일기장 2번]` 실제 문구 작성 및 화자 확정

### Sector4

- [ ] `[살인마 일기 3번]` 실제 문구 작성 및 화자 확정
- [ ] `[생존자의 메시지]` 실제 문구 작성 및 화자 확정
- [ ] `[마스터키 획득 후 무전기]` 실제 문구 작성 및 화자 확정

### Sector5 및 엔딩

- [ ] `[도망쳐]` 실제 문구 작성 및 화자 확정
- [ ] `[왼쪽 방]` 실제 문구 작성 및 화자 확정
- [ ] `[Bad Ending 텍스트 대체본]` 실제 문구 작성 및 화자 확정
- [ ] `[Happy Ending 텍스트 대체본]` 실제 문구 작성 및 화자 확정

## Sector2 GeneratorRoom - 작업 15

- [ ] `Generator_Boiler/ApproachPoint`를 실제 플레이 이동으로 확인하고 필요 시 최종 위치/회전 미세조정
- [ ] GeneratorRoom에서 실제 UI 입력으로 `WrongItem` / `Cancel` / `Fuse` 선택을 각각 수행해 Inventory 감소와 두 Story Flag를 엔드투엔드 확인
- [ ] Save3 CheckpointDefinition이 실제 배치되는 작업에서 `FuseInstalled`, `GeneratorPowered`를 ProgressFlags에 포함하고 Restart 복원을 최종 확인

## Sector2 MachineRoom - 작업 16

- [ ] 철창 해제용 SFX 에셋을 선정하고 `MachineRoomTarget.gateActivationSound`에 연결
- [ ] 철창 최종 모델 에셋을 선정하고 `GateController`의 Gate Root 표현에 연결
- [ ] 선정한 철창의 최종 Open 상태를 확인하고 필요 시 Animation 적용 여부를 결정해 연결

## 별도 항목

현재 개발 범위 이후 진행할 후속 폴리싱 및 연출 작업이다.

### 조명 폴리싱

- [ ] 각 방의 기본 조명과 병원 전체의 어두운 분위기를 최종 조정
- [ ] Sector별 Light를 최종 튜닝

### Decal

- [ ] 병원 전체 오염 Decal을 배치하고 최종 조정
- [ ] Sector4의 피 Decal과 Workshop / Morgue 집중 연출용 Decal을 배치

### 엔딩 실제 연출

- [ ] MonitoringRoom 카메라 연출과 문 닫힘 연출 구성
- [ ] Cafe Map 및 주인공 Animation 연출 구성
- [ ] CCTV Zoom 연출 구성
- [ ] Bad Ending / Happy Ending의 실제 Ending Timeline을 구성하고 연결
