# 화면·인쇄 레이아웃 분리 검증 — 2026-10-26

## 구현

- 새 기본 화면 폭은 창 맞춤. 문서 위 `읽기 편한 폭 / 창 맞춤` 버튼으로 바로 전환하며 마지막 선택을 기억한다.
- 화면 본문 상하좌우 여백 12px. 일반 본문은 줄바꿈, 넓은 표·도표는 가로 스크롤을 지원한다.
- 인쇄용 HTML 스냅샷과 별도 WebView/PDF 미리보기를 사용해 화면의 폭·스크롤·편집 상태를 유지한다.
- 인쇄 설정: A4/A3/Letter, 세로/가로, 좁게 8mm/보통 15mm/4방향 직접 지정, 너비 맞춤/10~200% 배율, 프린터 선택.
- 설정을 바꾸면 미리보기 PDF를 자동 갱신하고 설정을 저장한다. PDF 저장은 미리보기 파일을 그대로 사용한다. 프린터 출력도 같은 용지·방향·여백·배율·테마 배경 설정을 사용한다.

## 테스트

| 검사 | 결과 |
|---|---|
| 관련 검사 `Run-Checks.ps1 -PrintOnly` | 17 통과 / 0 실패 / 0 차단 |
| 최종 전체 검사 `Run-Checks.ps1` | 140 통과 / 28 실패 / 0 차단 |
| 웹 `npm test` | 6 통과 |

- 최종 전체 검사에서 LAYOUT-01~02, PRINT-01~06, WIDTH-01, TABLE-01~02 및 창 크기 유지 검사가 모두 통과했다.
- 실제 PDF MediaBox를 읽어 세로/가로 페이지 크기를 검증하고, 인쇄 설정 변경 전후 원본 DOM·스크롤·수정 상태가 동일함을 확인했다.
- 사용자 지정 여백·85% 배율의 UI 적용, 설정 저장/복원 및 손상된 설정의 기본값 복원을 확인했다.
- 기존 전체 검사 실패 목록(29개)과 비교했을 때 추가 실패는 없으며 VIEW-CLICK-01은 이번 전체 검사에서 통과했다. 나머지 28개 기존 실패는 남아 있어 전체 회귀 검사 종료 코드는 1이다.
- 첫 전체 실행에서는 실행 중인 MDviewer가 잠근 공유 임시 글꼴 파일 때문에 글꼴 관련 검사가 추가 실패했다. 파일 잠금을 확인한 뒤 테스트 TMP/TEMP를 실행별 폴더로 격리해 재실행했고 글꼴·테마·Word 검사가 통과했다.
- 화면 캡처 `verification/artifacts/forest-preview.png`를 확인해 창 너비와 축소 여백을 검토했다.
- 실제 프린터 용지 출력은 수행하지 않았다.

생성 로그: `verification/layout-run.log`, `verification/layout-full-run.log`, `verification/layout-release-run.log`. 관련 PDF: `verification/artifacts/layout-portrait.pdf`, `verification/artifacts/layout-landscape.pdf`.

## 릴리스 확인

- `RELEASE.BAT` 종료 코드 0. 첫 시도는 앱 종료 직후 실행 파일 잠금으로 중단됐으며 잠금 해제 확인 후 재실행해 성공했다.
- 산출물 `publish/MDviewer.exe`: 파일 버전 **1.3.5.0**, **76,739,248 bytes**.
- SHA256: `A2B33F3E1B4ABD764DCF4C1C78A8E3D2789D409BDB4EA4E3A15D79433C7C1706`.
- 기존 MSB3277 WindowsBase 참조 충돌 경고는 남아 있다.
