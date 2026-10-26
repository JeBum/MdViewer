# 기능명 안내·툴팁 겹침 수정 검증 — 2026-10-26

- 큰 기능명 안내를 본문 하단 중앙, 푸터 위 24px(DPI 보정)에 배치해 상단 툴바 마우스오버 툴팁과 분리했다.
- 화면 작업 영역을 벗어나지 않도록 위치를 제한하며 창 이동/크기 변경 시 위치를 다시 계산한다.
- 기존 24pt 안내 글꼴, 0.9초 유지+0.9초 페이드, 연속 선택 갱신, 클릭 통과·포커스 보존 동작을 유지했다.

## 관련 검사

`verification/Run-Checks.ps1 -ToolbarOnly`: **15 통과 / 0 실패 / 0 차단**, 종료 코드 0.

추가한 TOOLBAR-10에서 일반 크기(1100×780), 최소 크기(520×340), 변경 크기(900×650)의 안내 위치를 실제 클라이언트 좌표로 검증했다. 푸터 위 여백, 수평 중앙 정렬, 상단 툴팁 영역과의 분리, 창 이동 시 위치 추적이 모두 통과했다. 기존 페이드·연속 선택·클릭 통과·최소화 검사도 통과했다.

로그: `verification/toolbar-notice-position-run.log`, `verification/toolbar-notice-position-full-run.log`, `verification/toolbar-notice-position-release-run.log`.

## 전체 회귀 검사

최종 전체 검사: **149 통과 / 29 실패 / 0 차단**. 실패 ID 29개를 Git 원본 기준 실행과 비교해 모두 동일함을 확인했다. 전체 검사 종료 코드는 1이며, 전체 회귀 검사가 모두 통과했다고 판정하지 않는다.

## 릴리스

- `RELEASE.BAT` 종료 코드 0. 단일 실행 파일 생성 성공.
- `publish/MDviewer.exe`: 파일 버전 **1.3.8.0**, **76,744,888 bytes**.
- SHA256: `A522887F80E633AEF05EE8F3FE41D3F0EE1C9FC440D8E52DBF89C561E962FCAD`.
- 기존 MSB3277 WindowsBase 참조 충돌 경고는 남아 있다.
