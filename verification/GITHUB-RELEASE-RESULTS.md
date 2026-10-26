# GitHub Release v1.3.8 게시 검증 — 2026-10-26

- 저장소: `JeBum/MdViewer` (기존 origin 주소 `JeBum/MDviewer`와 같은 GitHub 저장소).
- `origin/main`에 소스·README·History·검증 기록 커밋 `cc3a94386e4b08715c61acdb09746e95cb707549` 푸시 완료.
- 위 커밋에서 `RELEASE.BAT` 재실행, 종료 코드 0. 기능 코드는 앞서 검사한 1.3.8과 동일하다. 관련 검사 15개 통과, 전체 149 통과/29 기존 실패 기록은 유지한다.
- [공개 Release](https://github.com/JeBum/MdViewer/releases/tag/v1.3.8): draft=false, prerelease=false.
- [다운로드 — MDviewer.exe](https://github.com/JeBum/MdViewer/releases/download/v1.3.8/MDviewer.exe).

| 확인 항목 | 결과 |
|---|---|
| Release 태그 | `v1.3.8` |
| 태그가 가리키는 커밋 | `cc3a94386e4b08715c61acdb09746e95cb707549` |
| 프로젝트 버전 | `1.3.8` |
| EXE 파일 버전 | `1.3.8.0` |
| EXE 제품 버전 | `1.3.8+cc3a94386e4b08715c61acdb09746e95cb707549` |
| 파일 크기 | 76,744,888 bytes |
| SHA256 | `4B6F9EBDA4451F45AF2462EA2D9875626025CD9676D4E31871C3990B8E89F321` |

GitHub 업로드 자산의 이름·크기와 서버에서 계산한 `digest`를 로컬 파일과 비교하여 일치를 확인한 뒤 Release를 공개했다. 인증 없는 공개 API로 게시 상태와 다운로드 주소를 다시 조회했다. 원격 태그를 받아 태그의 소스 커밋도 확인했다.

배포 파일은 Git 등록 후 소스 커밋 메타데이터를 반영해 재빌드했으므로 사전 로컬 검증 기록의 파일 SHA256과 다르다. 공개 배포 파일의 검증에는 위 SHA256을 사용한다. 이 게시 결과와 다운로드 링크 문서 갱신은 소스 태그 이후의 문서 커밋에 기록한다.

생성 로그: `verification/github-release-run.log` (Git 제외). 실행 파일은 Git 소스 이력 대신 Release의 Asset으로 게시했다. 이번 작업과 무관한 `TEMPWEB/`는 등록하지 않았다.
