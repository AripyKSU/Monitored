# Project logging guidelines

- Do not call `UnityEngine.Debug.Log`, `UnityEngine.Debug.LogWarning`, or `UnityEngine.Debug.LogError` directly in project C# code.
- Use `GameLog.Log(message, context)` for informational development logs. These calls are compiled only for the Unity Editor or Development Builds.
- Use `GameLog.LogWarning(message, context)` for development warnings. These calls are compiled only for the Unity Editor or Development Builds.
- Use `GameLog.LogError(message, context)` for genuine errors that should remain observable in release builds.
- Avoid logging every frame or inside hot loops. Never include credentials, tokens, personal information, or other sensitive data in logs.
- Keep `GameLog` as the single place for changing project-wide logging behavior. Add new logging behavior there instead of creating another wrapper.

# Code commenting guidelines

- 클래스와 메서드에는 해당 요소의 역할과 사용 의도를 알 수 있도록 XML 문서 주석을 한글로 작성한다.
- 메서드 내부의 핵심 로직에도 처리 목적과 흐름을 쉽게 이해할 수 있도록 꼼꼼한 한글 주석을 작성한다.
- 개발자가 코드를 편하게 읽고 유지보수할 수 있도록 명확하고 이해하기 쉬운 표현을 사용한다.

# 파트별 개발 진행 지침

- 파트별 개발을 시작하기 전에 `wiki/병원 맵 전체 게임플레이 개발 계획.md` 문서를 반드시 읽는다.
- 해당 문서의 `1번 문서 목적`, `2번 기본 개발 원칙`, `3번 전체 진행 구조`를 공통으로 확인한 뒤 개발에 들어간다.
- 개발하려는 파트에서 문서가 요구하는 번호 지침이 있다면 해당 번호의 지침도 반드시 읽고 준수한다.
- 위 문서의 지침과 현재 작업 범위를 먼저 대조하여, 계획과 다른 방식으로 구현해야 할 경우 그 차이와 이유를 작업 결과에 명시한다.

# Unity Play 모드 검증 지침

- 큰 기능 개발이 끝나면 Unity Play 모드에서 기능을 검증한다.
- 다른 에이전트가 이미 Play 모드를 사용 중인 것을 확인하면 해당 에이전트의 작업을 방해하지 않는다.
- Play 모드가 사용 중인 동안에는 가능한 다른 작업을 진행하며 대기한다.
- 일정 시간이 지난 후 Play 모드 사용 여부를 다시 확인하고, 다른 에이전트가 Play 모드를 종료하여 비어 있을 때 검증을 진행한다.
- 검증을 수행하지 못한 경우에는 그 사유와 검증이 보류된 상태를 작업 결과에 명시한다.
- 다른 에이전트가 Play 모드를 사용하고 있는 중에 수정한 내용은 해당 에이전트의 Play 모드가 종료된 후 Unity Hierarchy와 씬에 제대로 반영되었는지 확인한다. 필요에 따라 Editor 스크립트 작성으로 대체할 수도 있으며, 이 경우 사람이 직접 Editor 스크립트를 사용해야 하는 작업으로 `wiki/todolist.md`에 기록한다.
- Play 모드 대기가 지나치게 길어지면 검증용 Editor 스크립트로 대체할 수 있는지 검토하고, 대체가 가능하면 해당 Editor 스크립트를 작성하여 사용자에게 직접 실행하도록 요청한다.

# 다른 에이전트 작업 보호 지침

- 다른 에이전트가 진행 중인 작업의 변경사항을 덮어쓰거나 삭제하지 않도록 작업 전후 상태를 주의 깊게 확인한다.
- 다른 에이전트가 수정 중인 파일은 임의로 되돌리거나 전체 내용을 덮어쓰지 않는다.
- Scene 등 여러 작업이 동시에 반영될 수 있는 파일은 수정 전에 현재 변경사항과 사용 상태를 확인하고, 필요한 범위만 최소한으로 수정한다.
- 파일을 덮어쓰기, 삭제 또는 되돌려야 할 가능성이 있으면 먼저 대상 파일의 최신 상태와 변경 주체를 확인하고, 충돌 위험이 있는 경우 작업을 중지하거나 안전한 대안을 사용한다.

# 여러 Codex 대화·에이전트 동시 작업 지침

- 대화 간에는 작업 맥락과 수정 의도가 자동으로 공유되지 않는다고 가정한다. 작업을 시작할 때 현재 브랜치, 작업 폴더가 공용 로컬 체크아웃인지 별도 worktree인지, 그리고 다른 에이전트의 작업이 진행 중인지 먼저 확인한다.
- 가능하면 에이전트별 별도 worktree와 브랜치를 사용한다. 같은 로컬 체크아웃을 함께 사용할 때는 동시에 파일을 수정하지 말고, 작업 범위를 파일 단위로 분리한다.
- 작업 시작 전과 실제 수정 직전에 `git status --short`와 대상 파일의 최신 내용을 다시 확인한다. 이미 수정되었거나 미추적 상태인 파일은 다른 에이전트 또는 사용자의 소유로 간주하며, 소유자와 목적을 확인하기 전에는 수정·삭제·이동·이름 변경하지 않는다.
- 한 에이전트가 수정 중인 파일은 다른 에이전트가 포맷팅, 자동 변환, 일괄 치환, 전체 파일 재생성으로 덮어쓰지 않는다. 수정이 필요하면 최신 내용에 대해 작은 범위의 패치만 적용한다.
- 같은 파일에서 변경이 겹치거나 파일의 변경 주체를 판단할 수 없으면 즉시 해당 파일에 대한 쓰기 작업을 중지한다. 충돌한 파일 경로, 발견한 변경 상태, 진행을 위해 필요한 조치를 작업 결과에 명시하고, 충돌하지 않는 읽기·분석 작업만 계속한다.
- 다른 에이전트의 변경사항을 보존하기 위해 `git reset`, `git checkout`을 통한 되돌리기, `git clean`, 무차별 삭제, 전체 폴더 복사·동기화를 사용하지 않는다. 되돌리기나 삭제가 꼭 필요하면 대상과 복구 가능성을 먼저 확인하고 사용자 승인을 받는다.
- 커밋·스테이징·병합 시에는 다른 에이전트의 변경사항을 포함하지 않는다. 작업 전후 `git status`와 `git diff -- <내가 수정한 경로>`로 자기 변경만 확인한 뒤 필요한 경로만 스테이징한다. 사용자가 요청하지 않은 커밋, rebase, merge, pull, push는 수행하지 않는다.
- Unity 직렬화 파일인 `.unity`, `.prefab`, `.asset`, `.mat`, `ProjectSettings` 및 대응 `.meta` 파일은 충돌 위험이 높은 공유 자원으로 취급한다. 한 번에 한 에이전트만 수정하며, 수정 전후 YAML 차이를 확인하고 관련 없는 오브젝트·컴포넌트·참조를 재저장하거나 정리하지 않는다.
- Unity Editor, Play 모드, import, build를 같은 프로젝트 폴더에서 여러 에이전트가 동시에 실행하지 않는다. 다른 에이전트나 사용자가 사용 중이면 프로세스를 종료하거나 파일을 강제로 잠금 해제하지 말고, 충돌하지 않는 코드 검토·문서 작업을 먼저 수행한다.
- `Library`, `Temp`, `Logs`, `obj`, `UserSettings`와 같은 Unity 생성·캐시 폴더 및 공용 빌드 출력물은 다른 에이전트의 작업 산출물로 취급한다. 원인 확인 없이 삭제·재생성·대량 갱신하지 않으며, 테스트나 빌드가 생성하는 출력 경로가 다른 작업과 겹치지 않는지 확인한다.
- 한 대화에서 다른 대화의 변경사항을 전제로 구현하지 않는다. 필요한 변경이 이미 반영되었는지 현재 파일과 diff에서 확인하고, 확인되지 않은 내용은 추측하지 않는다.
- 작업을 마칠 때 수정한 파일 목록, 다른 에이전트의 변경을 보존했는지, 실행한 검증과 미실행 사유를 요약한다. 통합이 필요한 경우 충돌 가능성이 있는 파일과 통합 순서를 함께 명시한다.

# 남은 작업 기록 지침

- 추후 사람이 직접 수행해야 하거나 사람의 판단·확인이 필요한 작업만 `wiki/todolist.md` 파일에 기록한다.
- 에이전트가 이어서 처리할 수 있는 일반적인 후속 개발 작업이나 보완 사항은 `wiki/todolist.md`에 기록하지 않는다.
