# MDviewer

서강대학교 MOT대학원 데이터 엔지니어링 프로그래밍 수업용 Windows Markdown 뷰어.

https://github.com/JeBum/MDviewer

## 요구 사항

- Windows 10/11 x64
- 개발: .NET 8 SDK
- 실행: WebView2 (Windows 10/11에 보통 포함)

## 빌드

```bat
build.bat
```

결과: `publish\MDviewer.exe`

설치본:

```bat
make_setup.bat
```

결과: `installer\MDviewerSetup.exe`

## 단축키

| 키 | 동작 |
|---|---|
| Ctrl+N | 새 문서 |
| Ctrl+O | 열기 |
| Ctrl+S | 저장 |
| Ctrl+Shift+S | Word로 저장 |
| Ctrl+W | 탭 닫기 |
| F5 | 다시 읽기 |
| Ctrl+휠 / Ctrl+Shift+휠 | 확대/축소 |

`.md` 파일을 창에 끌어다 놓으면 새 탭으로 엽니다. 오른쪽 클릭으로 편집/분할/테마.

## 테마

- 서강 테마
- 알바트로스 테마
