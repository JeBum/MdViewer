# MDviewer Diagram Studio

기존 Windows MDviewer와 별도로 실행하는 Markdown 웹 뷰어입니다. 왼쪽에서 편집하면 300ms 뒤 오른쪽 미리보기가 갱신됩니다.

## 설치와 실행

Node.js 20.19+ 또는 22.12+가 필요합니다.

```powershell
cd web
npm install
npm run dev
```

표시된 로컬 주소(기본 http://127.0.0.1:5173)를 엽니다. `npm test`는 저장소 루트의 `diagram-sample.md`를 이용한 회귀 테스트이고, `npm run test:e2e`는 설치된 Microsoft Edge에서 실제 SVG 렌더링을 검사합니다. `npm run build`는 `dist/` 배포 파일을 만듭니다. 앱의 “샘플 불러오기” 버튼으로 같은 Markdown 파일을 열 수 있습니다.

## 지원 문법

| 펜스 | 렌더러 |
| --- | --- |
| `mermaid` | 내장 Mermaid.js |
| `sequence` | 내장 Mermaid sequenceDiagram |
| `dot` / `graphviz` | 내장 Graphviz WASM |
| `plantuml` / `zenuml` / `kroki` | 일반 코드 블록 |

외부 서버가 필요한 PlantUML·ZenUML·Kroki 펜스는 원문 코드 블록으로 표시합니다. 렌더러 등록은 `src/renderers.js`의 `renderers` 객체에서 앱 내부 렌더러를 추가하는 방식으로 확장할 수 있습니다.

## 구성

- `src/markdown.js`: Markdown 파싱, 코드 펜스 분류, HTML 정화
- `src/renderers.js`: 앱 내부 다이어그램 렌더러 레지스트리와 SVG 정화
- `src/main.js`: 파일 열기, 편집 디바운스, 미리보기 갱신, 테마
- `src/style.css`: 분할 화면, 반응형/인쇄 스타일
- `../diagram-sample.md`: Windows 앱과 웹 앱이 함께 사용하는 샘플 겸 테스트 입력

다이어그램 문법 오류나 서버 오류가 나면 해당 영역에 메시지와 원문을 표시합니다. 인쇄 버튼으로 브라우저의 PDF 저장 기능을 이용할 수 있습니다.
