# MDviewer 실행 테스트 결과 — 2026-09-18

**최종 결과: 80개 검사 중 54개 통과, 26개 실패. 전체 요건 충족 판정은 실패.**

> 이 보고서는 현재 줄 표시를 삼각형으로 변경하기 전의 실행 기록이다. 후속 변경과 35개 관련 검사 결과는 `CURSOR-RESULTS.md`를 참조한다.

검사 수는 요구사항 63개와 일대일 대응하지 않는다. 한 결함을 여러 계층에서 검사한 사례가 있고, 모든 요구사항·운영 환경을 실행한 것은 아니다. 아래 결과는 현재 작업 폴더의 미커밋 변경까지 포함한 소스 기준이다.

## 실행 방식과 빌드

- Windows 11 x64, .NET SDK 10.0.300, 대상 프레임워크 net8.0-windows.
- 실제 Program.cs와 WordExport.cs를 링크해 STA WinForms 테스트 호스트에서 컴파일·실행했다. 앱 로직을 별도로 재구현하지 않았다.
- 실제 RichTextBox, MainForm, FileTab과 WebView2를 사용했다. 키보드는 컨트롤의 KeyDown 이벤트를 호출했고 브라우저는 실제 DOM/JavaScript를 실행했다. OS 입력 주입에 의한 사용자 조작 테스트는 아니다.
- 코드 복사는 navigator.clipboard.writeText에 전달되는 문자열을 캡처했다. OS 클립보드 자체와 fallback 권한 동작은 검사하지 않았다.
- Word 내보내기는 파일 생성, 내용 확인, OpenXmlValidator 스키마 검증을 수행했다. Microsoft Word에서 직접 열지는 않았다.
- Debug 빌드와 Release win-x64 self-contained 단일 파일 publish가 성공했다. MSB3277 WindowsBase 4.0/5.0 참조 충돌 경고가 남아 있다.
- 배포 빌드 출력: obj/verification/publish/MDviewer.exe. 기존 publish 폴더는 교체하지 않았다.
- 테스트 실행기의 초기 탐색 이벤트 경합, 각주 클래스명 가정, 누락된 BuildDate 메타데이터를 보정한 뒤 최종 실행했다. 초기 실행의 오탐은 최종 수치에 포함하지 않았다.

## 우선 수정할 재현 결함

| 우선순위 | 검사 ID | 재현 방법 | 실제 결과 / 기대 결과 | 관련 코드 |
|---|---|---|---|---|
| P1 | APP-08 | 파일을 연 후 내용을 수정하고 저장하지 않은 상태에서 같은 파일을 다시 열기 | `unsaved local edit`가 디스크의 `alpha`로 교체되고 수정 상태가 사라짐 / 미저장 내용 보존 또는 확인 필요 | Program.cs:904, 912 |
| P1 | CODE-01, WEB-07, WEB-08 | 두 줄 코드 블록 또는 JSON 블록의 Copy 버튼 동작 실행 | 래퍼/줄 번호 중복, 줄 번호와 중복 본문이 복사됨. 줄 구분이 실제 개행 대신 리터럴 `\n`. JSON 재파싱 실패 / 번호 없는 원문과 실제 개행 필요 | Program.cs:2962, 2993, 3274 |
| P1 | WEB-15 | 본문 바로 다음에 표를 작성하고 그 아래 태스크를 둔 뒤 체크박스 클릭 | 화면은 체크되지만 소스 `[ ]`는 변경되지 않음 / 대응 소스 `[x]`로 변경 필요 | Program.cs:1691, 2135, 3175 |
| P2 | FIND-03, WEB-04 | `문서 문서들 문서`에서 `문서`를 단어 단위로 검색 | C# 소스 검색 2건, WebView2 표시 검색 0건 / 두 영역 일치 필요 | Program.cs:2411, 3447 |
| P2 | EDIT-08, EDIT-09 | 에디터 폭을 좁히고 긴 목록 항목의 마지막 시각적 줄에서 Enter 또는 Tab 처리 | 다음 목록 마커 생성 및 들여쓰기 실패 / 논리 줄 기준으로 동작 필요 | Program.cs:2198, 2237 |
| P2 | WORD-01 | 표가 있는 문서를 DOCX로 내보내고 스키마 검증 | 표 테두리의 `w:left` 자식 순서 오류 / 유효한 Open XML 필요. 실제 Word의 열기/복구 여부는 미검사 | WordExport.cs:122 |
| P2 | IMAGE-02, WEB-09 | `![A & B](assets/albatross.png)` 렌더링 | 캡션이 `A &amp; B`로 표시 / `A & B` 표시 필요 | Program.cs:2917 |
| P2 | IMAGE-03 | 문단 중간 `before ![inlinecaption](assets/albatross.png) after` 렌더링 | alt가 검색 가능한 캡션으로 나오지 않음 / 이미지 alt 검색 요건에 대한 범위 확인·보완 필요 | Program.cs:2912 |
| P2 | CALLOUT-02 | NOTE 콜아웃 본문에 빈 인용 줄과 목록 추가 | 콜아웃 카드 변환이 사라짐 / 중첩 목록 지원 필요 | Program.cs:2943 |
| P2 | WEB-17 | 검색 하이라이트를 만든 뒤 편집 미리보기 DOM 갱신 | 하이라이트 2개가 0개로 사라짐 / 검색 표시 재적용 필요 | Program.cs:3394 |
| 확인 필요 | WEB-11 | 코드가 있는 문서에서 숫자 `1`을 강조 키워드로 지정 | 본문뿐 아니라 줄 번호까지 강조됨. 요구 문서의 명시적 수용 기준보다는 추가 탐색 검사로 분류 | Program.cs:3511 |

표 뒤 체크박스 재현 입력:

```markdown
intro
|A|B|
|-|-|
|1|2|

- [ ] todo
```

`NormalizeMd`가 표 앞에 빈 줄을 삽입한 뒤 만들어진 DOM 줄 번호가 원래 소스 줄로 역변환되지 않고 체크박스 처리에 전달된다.

## 추가 명세에 대한 실행 실패

이 항목은 기존 기능의 회귀와 구분해야 한다. 추가MD문법.md에 명시된 동작을 입력해 실제 출력이 충족되는지 검사했으며, 구현되지 않았거나 명세와 다른 동작이 관찰됐다.

- SPEC-01~08: 위키링크, 해시태그, 문서 임베드, 블록 참조, Mermaid SVG 다이어그램, 인라인 필드, `%%...%%` 주석 숨김, dataview 쿼리 결과 표가 기대대로 생성되지 않았다.
- CALLOUT-03: 접힌 콜아웃 `> [!warning]-`에 펼침/접기 UI가 없다.
- EDIT-10: `Ctrl+Shift+H`가 선택 텍스트를 `==...==`로 감싸지 않는다.
- WEB-16: 완료 태스크에 취소선이 적용되지 않는다.

## 통과한 주요 범위

- 논리 줄 계산, CRLF/LF 경계, 표 정규화의 줄 번호 변환 함수.
- 기본 Markdown, 표, 프론트매터 숨김, 각주 링크/역링크 생성, 기본 5종 콜아웃.
- JSON/JSONC 파싱, 토큰 강조, 두 칸 들여쓰기, 오류 JSON의 오류·줄 번호·원문 표시.
- Ctrl+B/I/K, 짧은 목록의 Enter·번호 증가·빈 항목 탈출·Tab/Shift+Tab.
- 일반/정규식 특수문자 검색, 영어 단어 단위 검색, 다중 탭 정방향·역방향 순환 및 검색 바 전체 닫기.
- 기본 태스크 클릭의 소스 반영과 DOM 유지, 키워드 추가/삭제, 한글 저장·재읽기.
- 4종 수식 구분자와 오류 수식의 붉은 글씨/원문 보존.
- 3종 테마 체크박스 색상, 개별/동시 줌, 배율 OSD 표시·자동 숨김.
- 문서 다중 탭 열기, F8 편집 전환, 새 문서·현재/다른/전체 문서 닫기.
- 기존 test-sample.md의 9개 절과 의도적으로 잘못된 JSON 1개 표시.

## 미실행 및 판정 한계

- 컴퓨터 사용 도구는 `helper_unknown_error: setup refresh had errors`로 초기화에 실패했다. 화면을 보고 실제 키보드·마우스로 조작하는 테스트는 수행하지 못했다.
- 화면 잘림, DPI별 배치, 실제 거터 모양, 깜빡임·체감 지연, 커서 스크롤 위치의 시각적 정확성은 미검증이다.
- Explorer 더블클릭/드래그앤드롭, 실제 단일 인스턴스 IPC·최소화 복원, 시스템 기본 앱 등록, 설치 프로그램, 외부 브라우저/mailto 실행은 미실행이다.
- 테마의 디스크 저장·프로그램 재시작 후 복구, 저장 실패 및 취소 확인 대화상자, OS 클립보드 권한/fallback은 미실행이다.
- 자동완성, 백링크 인덱스, YAML 속성 폼 등 모든 추가 명세의 세부 동작까지 검사한 것은 아니다.
- 기존 앱 소스와 사용자 문서는 수정하지 않았다. 이번 작업의 추가 파일은 verification 아래 테스트 실행기·로그·보고서·임시 문서다.

## 재실행

프로젝트 루트의 PowerShell에서:

```powershell
& .\verification\Run-Checks.ps1
```

실행기는 obj/verification 아래 임시 프로젝트를 만들고 현재 앱 소스를 링크해 실행한다. Checks.cs.txt 확장자는 앱 프로젝트의 기본 `*.cs` 포함 규칙에 테스트 코드가 섞이지 않게 하기 위한 것이다. 테스트 실패 시 종료 코드는 1이다. verification/artifacts 아래 테스트용 문서와 결과를 덮어쓴다.

- `verification/artifacts/results.json`: 검사별 상태와 실패 상세.
- `verification/artifacts/copied-code.txt`, `copied-json.txt`: 실제 복사 함수 출력.
- `verification/artifacts/export.docx`: 검사한 Word 내보내기 결과.
- `verification/run.log`: 최종 실행 로그.
- `verification/publish.log`: 배포 빌드 로그.

## 전체 검사 결과

| ID | 결과 | 검사 |
|---|---|---|
| LINE-01 | PASS | Logical line ignores long wrapped text |
| LINE-02 | PASS | Logical line LF and CRLF boundaries |
| LINE-03 | PASS | Logical line count empty and trailing newline |
| LINE-04 | PASS | Line-to-character mapping |
| MD-01 | PASS | Headings bold italic strike and mark |
| MD-02 | PASS | Table without preceding blank line |
| MD-03 | PASS | Source-line mapping survives table normalization |
| MD-04 | PASS | Frontmatter excluded from preview |
| MD-05 | PASS | Footnote reference definition and backlink |
| JSON-01 | PASS | All JSON token types |
| JSON-02 | PASS | JSONC sample accepts comments and trailing comma |
| JSON-03 | PASS | Invalid JSON shows error with line and source |
| JSON-04 | PASS | Minified JSON gets two-space indentation |
| JSON-05 | PASS | Markdown JSON blocks are formatted |
| CODE-01 | FAIL | Code headers and line numbers |
| CODE-02 | PASS | HTML inside code stays text |
| CALLOUT-01 | PASS | Five documented GitHub callout types |
| CALLOUT-02 | FAIL | Callout containing list remains callout |
| CALLOUT-03 | FAIL | Collapsed callout exposes a toggle |
| IMAGE-01 | PASS | Standalone image gets searchable caption |
| IMAGE-02 | FAIL | Caption displays special characters once |
| IMAGE-03 | FAIL | Inline image caption is searchable |
| EDIT-01 | PASS | Ctrl+B wraps selected text |
| EDIT-02 | PASS | Ctrl+I inserts placeholder without selection |
| EDIT-03 | PASS | Ctrl+K inserts link and selects URL |
| EDIT-04 | PASS | Bullet list continues on Enter |
| EDIT-05 | PASS | Ordered list increments number |
| EDIT-06 | PASS | Empty list exits on Enter |
| EDIT-07 | PASS | Tab and Shift+Tab preserve list marker |
| EDIT-08 | FAIL | Wrapped list continues at logical end |
| EDIT-09 | FAIL | Wrapped list indents logical item |
| EDIT-10 | FAIL | Ctrl+Shift+H wraps highlight markup |
| FIND-01 | PASS | Search is literal and case insensitive |
| FIND-02 | PASS | English whole-word source search |
| FIND-03 | PASS | Korean whole-word source search |
| HL-01 | PASS | Keyword toggles add and remove |
| TASK-01 | PASS | Checkbox changes source and dirty state |
| FILE-01 | PASS | Save and reload preserve Korean text |
| WORD-01 | FAIL | DOCX contains heading table and Korean text |
| SPEC-01 | FAIL | Additional syntax: [[문서명]] |
| SPEC-02 | FAIL | Additional syntax: #중첩/태그 |
| SPEC-03 | FAIL | Additional syntax: ![[문서명]] |
| SPEC-04 | FAIL | Additional syntax: ((abc123)) |
| SPEC-06 | FAIL | Additional syntax: 상태:: 진행중 |
| SPEC-07 | FAIL | Additional syntax: visible %%hiddenmemo%% |
| SPEC-08 | FAIL | Additional syntax: ```dataview |
| WEB-01 | PASS | Preview JavaScript loads |
| WEB-02 | PASS | Substring find highlights three matches |
| WEB-03 | PASS | Whole-word preview matches source count |
| WEB-04 | FAIL | Korean whole-word preview matches source count |
| WEB-05 | PASS | Find literal punctuation |
| WEB-06 | PASS | Clear find removes all markers |
| WEB-07 | FAIL | Code copy preserves real line breaks and omits numbers |
| WEB-08 | FAIL | JSON copy produces parseable JSON |
| WEB-09 | FAIL | Caption punctuation displays correctly |
| WEB-10 | PASS | Image caption can be searched |
| WEB-11 | FAIL | Keyword highlighting excludes code line numbers |
| WEB-12 | PASS | Math dollar and parenthesis delimiters |
| WEB-13 | PASS | Invalid math preserves visible error source |
| WEB-14 | PASS | Checkbox DOM click updates source without replacing DOM |
| WEB-15 | FAIL | Checkbox after normalized table updates correct source line |
| WEB-16 | FAIL | Task completion applies strike-through |
| WEB-17 | FAIL | Edit refresh retains active search highlights |
| WEB-18 | PASS | Three theme checkbox accents |
| WEB-19 | PASS | Standalone test sample renders all nine sections |
| SPEC-05 | FAIL | Mermaid produces a diagram in WebView2 |
| ZOOM-01 | PASS | Editor-only zoom leaves preview scale unchanged |
| ZOOM-02 | PASS | Combined zoom applies to editor and preview |
| ZOOM-03 | PASS | Zoom OSD appears and expires |
| APP-01 | PASS | Open multiple files as separate tabs |
| APP-02 | PASS | F8 enters split editor and returns to viewer |
| APP-03 | PASS | All-tabs search skips nonmatching tab |
| APP-04 | PASS | All-tabs search wraps last to first |
| APP-05 | PASS | All-tabs reverse search wraps first to last |
| APP-06 | PASS | Hide search closes bars across all tabs |
| APP-07 | PASS | Opening same file does not duplicate tab |
| APP-08 | FAIL | Reopening already-open file preserves unsaved edits |
| APP-09 | PASS | Ctrl+N and Ctrl+W create and close blank tab |
| APP-10 | PASS | Close other documents keeps selected tab |
| APP-11 | PASS | Close all returns to empty tabs |

## 검사 대상 SHA-256

- Program.cs: 0B8329CAE91395F50FFFFAFA98FE7DF4131A5F86E262502FB6F1036838A2953A
- WordExport.cs: 42ECC80C3FF9A9887016D76A7B5564EFCCCD055147070B69F3E9051C785C3101
- test-sample.md: 7FCF639F67A35D592B60DE2A847569A67C34002BB3F223B48E07DA20E30B72D6
- test-sample.jsonc: 0C7A9391D3849881E2937F97AA73759D421950DEE72CEAB0D3CEC6B0573BD440
