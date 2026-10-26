# 아이콘 선택 이름 페이드 안내 검증 — 2026-10-26

- 아이콘 선택 시 24pt 기능명을 본문 상단 중앙에 표시한다. 0.9초 유지 후 0.9초 동안 투명도를 낮춰 숨긴다.
- 모드/폭 전환은 변경된 상태의 이름을 표시한다. 연속 선택 시 마지막 이름으로 교체하고 표시 시간을 다시 시작한다.
- 별도 소유 팝업에 NOACTIVATE/TOOLWINDOW/TRANSPARENT 스타일을 사용하고, 네이티브 히트 테스트에서 HTTRANSPARENT를 반환한다. WebView 위에서도 표시하며 키보드 포커스를 빼앗지 않는다.
- 폭 메뉴를 열거나 메인 창을 최소화/숨김 처리하면 안내를 숨긴다. 종료 시 팝업·타이머·글꼴을 정리한다.
- 열기/저장/인쇄는 대화상자를 열기 전에 선택 이름을 표시한다. 안내는 기능 선택을 알려 주며 작업 성공을 의미하지 않는다.

## 최종 테스트

| 검사 | 결과 |
|---|---|
| 전체 Windows/WebView2 회귀 검사 | 148 통과 / 29 실패 / 0 차단 |
| 신규 TOOLBAR-07~09 | 모두 통과 |
| 웹 단위 테스트 `npm test` | 6 통과 |

TOOLBAR-07은 즉시 표시·큰 글꼴·유지 구간·중간 투명도·최종 숨김을 확인한다. TOOLBAR-08은 연속 클릭의 최신 상태 이름·시간 초기화와 비활성 버튼의 안내 억제를 확인한다. TOOLBAR-09는 포커스 비활성·클릭 통과 확장 스타일과 네이티브 메시지 반환값, 창 배치 보존, 최소화 시 숨김을 확인한다.

전체 실패 29개 ID를 `baseline-run.log`의 변경 전 원본 기준 실행과 비교해 모두 동일함을 확인했다. 전체 검사 종료 코드는 1이며 전체 회귀 검사가 모두 통과했다고 판정하지 않는다.

실제 팝업을 캡처한 `verification/artifacts/toolbar-notice.png`를 열어 큰 이름과 테마색을 확인했다.

로그: `verification/toolbar-notice-full-run.log`, `verification/toolbar-notice-release-run.log`.

## 릴리스

- `RELEASE.BAT` 종료 코드 0. Windows x64 self-contained 단일 실행 파일 생성 성공.
- `publish/MDviewer.exe`: 파일 버전 **1.3.7.0**, **76,744,858 bytes**.
- SHA256: `DB6861C40D793B036F09FAFB01D02BEE01033044B4DD9B81A9655881EE9E43CA`.
- 기존 MSB3277 WindowsBase 참조 충돌 경고는 남아 있다.
