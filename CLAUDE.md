# MdViewer 작업 규칙 (프로젝트를 열면 자동으로 읽힘)

@../AGENTS.md

## 요구사항 처리 절차 (모든 요구사항에 필수)
1. **소스 반영**: 요구사항을 관련 소스(`Program.cs` 등) 최상단 요건 주석에 `요건번호 / 날짜 / 한 줄 요건정의`로 추가하고 구현한다.
2. **테스트케이스 추가**: `verification/TEST-CASES.md`(표)와 `verification/Checks.cs.txt`(자동 검사)에 기록한다. `test-sample.md`는 사용자의 MD 샘플이므로 수정하지 않는다.
3. **변경 이력**: `History.md` 상단에 날짜별로 기록한다.
4. **검증**: 개발이 끝나면 모든 테스트케이스를 확인한다 (`verification/Run-Checks.ps1`).
5. **릴리스 빌드**: 마지막에 항상 `release.bat`을 실행해 컴파일 오류가 없는지, `publish\MDviewer.exe`가 생성되는지 확인한다.
