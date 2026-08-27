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

# 다른 에이전트 작업 보호 지침

- 다른 에이전트가 진행 중인 작업의 변경사항을 덮어쓰거나 삭제하지 않도록 작업 전후 상태를 주의 깊게 확인한다.
- 다른 에이전트가 수정 중인 파일은 임의로 되돌리거나 전체 내용을 덮어쓰지 않는다.
- Scene 등 여러 작업이 동시에 반영될 수 있는 파일은 수정 전에 현재 변경사항과 사용 상태를 확인하고, 필요한 범위만 최소한으로 수정한다.
- 파일을 덮어쓰기, 삭제 또는 되돌려야 할 가능성이 있으면 먼저 대상 파일의 최신 상태와 변경 주체를 확인하고, 충돌 위험이 있는 경우 작업을 중지하거나 안전한 대안을 사용한다.

# 남은 작업 기록 지침

- 추후 사람이 직접 수행해야 하거나 사람의 판단·확인이 필요한 작업만 `wiki/todolist.md` 파일에 기록한다.
- 에이전트가 이어서 처리할 수 있는 일반적인 후속 개발 작업이나 보완 사항은 `wiki/todolist.md`에 기록하지 않는다.
