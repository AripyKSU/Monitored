# Knowledge Base Index

## game-design

게임의 핵심 규칙, 시스템, 경험 목표를 정리합니다.

| Article | Summary | Updated |
|---------|---------|---------|
| [폐건물 탈출 공포 게임 — 리부트 컨셉 기획](game-design/haunted-house-reboot-concept.md) | CCTV 시점, 클릭 이동, 단계형 위험 경보, 은신, 적, 추격, 퍼즐과 스토리 컨셉. | 2026-08-21 |
| [폐건물 탈출 공포 게임 — 시스템 개발 사양](game-design/haunted-house-system-development-spec.md) | 단일 TargetCommand, 고정 방 데이터, 적 순찰, 위험 경보, 은신, 인벤토리와 메모리 체크포인트 구현 기준. | 2026-08-21 |
| [폐병원 스테이지 구성안 — 5개 세이브 구간](game-design/abandoned-hospital-stage-design.md) | [Archived] 사용자가 정리한 5개 구간의 맵, 아이템, 적 이벤트, 추격과 두 가지 엔딩 구성. | 2026-08-25 |

## technical-docs

현재 구현된 게임 시스템의 구조, 책임, 실행 흐름과 확장 시 주의점을 정리합니다.

| Article | Summary | Updated |
|---------|---------|---------|
| [`CoreSandbox` 씬 구조와 검증 구성](technical-docs/coresandbox-scene-structure.md) | 통합 검증 씬의 오브젝트 계층, 공용 서비스, 상호작용 대상, UI와 검증 절차. | 2026-08-21 |
| [`TargetCommand` 시스템 기술문서](technical-docs/target-command-system.md) | 대상 선택부터 NavMesh 이동, 행동 실행, 입력 잠금과 실패 복구까지의 단일 명령 구조. | 2026-08-21 |
| [방과 CCTV 전환 시스템 기술문서](technical-docs/cctv-room-transition-system.md) | 고정 방 데이터, Cinemachine CCTV, 양방향 문 통과와 논리 방 상태의 동기화. | 2026-08-21 |
| [상호작용과 인벤토리 시스템 기술문서](technical-docs/interaction-inventory-system.md) | 고유 아이템 데이터, 획득·선택·사용·소비 흐름과 UI 책임 분리. | 2026-08-21 |
| [공용 일시정지 시스템 기술문서](technical-docs/pause-system.md) | 중첩 요청을 안전하게 처리하는 PauseHandle 기반 일시정지와 소비자 생명주기. | 2026-08-21 |
