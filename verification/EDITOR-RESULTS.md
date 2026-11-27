# 편집 커서 보호 — 2026-09-18

## 원인과 수정

VIEW의 scroll 이벤트는 80ms 잠금이 풀린 뒤 현재 문서 위치를 호스트에 전달했다. 호스트는 이를 RichTextBox.SelectionStart에 반영하면서 선택 범위를 해제하고 ScrollToCaret을 호출했다. 이미지가 늦게 로드되거나 미리보기 레이아웃이 변하면 브라우저가 추가 scroll 이벤트를 발생시켜, 사용자가 편집 중인 위치를 변경했다.

자동 동기화는 편집기 → VIEW 방향으로만 수행한다. VIEW의 스크롤에서 편집 위치를 보내던 코드와 호스트의 자동 역방향 이동을 제거했으며, 사용자 클릭 표식이 없는 이전 line/clickLine/heading 메시지도 무시한다. 현재 줄 삼각형의 스크롤·리사이즈·이미지 로딩 위치 갱신은 VIEW 내부에서 계속 동작한다.

사용자가 오른쪽 본문을 직접 왼쪽 클릭한 경우에만 해당 원문 줄로 커서와 포커스를 이동한다. isTrusted 클릭을 확인하며 링크·버튼·입력 컨트롤·드래그 선택과 조합키 클릭은 제외한다. 자동 줄바꿈으로 인한 화면 줄 수 대신 원문 논리 줄 번호를 사용하고, 표 렌더링을 위해 삽입한 빈 줄도 원문 위치로 환산한다. 클릭한 VIEW는 다시 스크롤하지 않는다.

## 검증

- 수정 전: `editor-isolation-before.log` — 4개 중 3개 실패, 1개 통과.
- 수정 후: `editor-isolation-after.log` — 4개 모두 통과.
- 클릭 복원 전 관련 회귀 검사: `editor-regression-run.log` — 42개 통과, 실패 0개.
- 클릭 복원 및 커서 보호 검사: `preview-click-run.log` — 7개 통과, 실패 0개.
- 최종 관련 회귀 검사: `preview-click-regression-run.log` — **45개 통과, 실패 0개**.

실제 WinForms RichTextBox와 WebView2를 사용했다. 미리보기의 늦은 위치 메시지, 실제 브라우저 스크롤과 합성 DOM click(), 220ms 뒤 12px에서 1800px로 바뀌는 이미지 로드, 반복 입력/갱신/리사이즈 상황에서 커서·선택 영역·편집기 스크롤·키보드 포커스를 검사했다. 이미지 로딩 뒤 이어지는 입력이 원래 선택 위치에 들어가는지도 확인했다. 입력은 RichTextBox.SelectedText를 통한 실제 컨트롤 편집이며 OS 키보드 입력 주입 검사는 아니다.

명시적 클릭 검사는 WebView2 DevTools Input.dispatchMouseEvent로 브라우저 입력을 전달하고 isTrusted=true를 확인했다. 긴 자동 줄바꿈 문단 뒤 제목, 표 셀, 버튼, VIEW 모드의 클릭을 검사했다.

기존 커서 표시·분할·테마·Word 변환·홈 샘플 열기 검사도 함께 통과했다. 이전 전체 기능 검사에서 기록한 별도 실패 항목을 모두 수정했다는 의미는 아니다.

## 샘플 보존

`test-sample.md`는 사용자 수정본을 그대로 유지했다. 작업 전후 SHA-256:

`951A64BE4C53530E5DE967ADED92E368340467EE10AE6D2A6EBD70333CB04B36`

앞으로 기능 검증 절차는 `TEST-CASES.md`와 `Checks.cs.txt`에 기록한다. 기존 샘플 파일에 테스트 케이스를 추가하거나 내용을 정리하지 않았다.

## 2026-11-26 문법 예제와 마지막 줄·목록 편집

상단 문법 예제 버튼, F1, 메뉴에서 읽기 전용 문법 안내를 연다. 긴 목록의 Enter와 Tab은 원본 줄을 기준으로 처리하며 Shift+Tab은 공백 두 칸을 제거한다. 마지막 빈 줄과 마지막 문단의 VIEW 스크롤을 문서 끝까지 이동하고, 편집 진입 직후 실제 입력이 시작되면 진입 전 스크롤 보호를 해제한다.

- 최종 기능 검사 `obj/verification/editing-fixes.log` — 7개 통과, 실패 0개.
- 최종 툴바 검사 `obj/verification/syntax-toolbar.log` — 16개 통과, 실패 0개. 작은 창에서 문법 버튼의 표시와 메뉴·F1·툴바 버튼의 대화상자 열기를 확인했다.
- 확장 커서 검사 `obj/verification/editing-cursor-regression.log` — 90개 통과, 4개 실패. 실패 항목은 SCROLL-01, HOME-04, GEOMETRY-02, VIEW-CLICK-01이다. 상단 문법 버튼을 추가하기 전 실행한 검사다.
- 실제 WinForms 컨트롤과 WebView2에서 검증했다. 목록 키 입력은 컨트롤 KeyDown 이벤트 호출로 검사했다.
- `RELEASE.BAT` 종료 코드 0. 결과는 `publish/MDviewer.exe`, 76,766,264 bytes, 파일 버전 1.3.10.0.
- SHA256 `54792D2927AC292D412A8036E86078A3A5A6204E6B3AAA4B6F53F7E9EE6A775D`.
- 빌드에는 WindowsBase 참조 충돌 경고 MSB3277이 남아 있다.
