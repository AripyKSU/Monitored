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
