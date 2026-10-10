# 시트 통신 독립 실험

`feat/sheet-sync`는 `dev`에서 분기한다. 게임 Core, 노드 구매, AI 실행·변경 기록 의존성이 없다.
원본 M5의 HTTP 클라이언트, CSV 비교, Apps Script를 이동했고 게임 에셋 쓰기와 AI 승격 기록 의존성을 제거했다.
4개 탭 이름/행 키는 블랙홀 노드 시트 계약을 실험하는 샘플이다. 범용 임의 시트 편집기는 아니다.

## 실행
1. 별도의 Google Sheets를 만들고 Samples/SheetSync의 CSV 4개를 같은 이름의 탭으로 가져온다.
2. 확장 프로그램 → Apps Script에 AppsScript/BlackholeSheetSync.gs를 붙인다.
3. bhSyncSetupToken을 한 번 실행한다. 토큰은 개인 설정에만 입력한다.
4. 웹 앱으로 배포(실행 사용자 나, 액세스 모든 사용자)하고 /exec 주소를 복사한다.
5. Unity에서 Integration Lab → Sheet Sync를 연다. 주소와 토큰 저장 → 연결 확인 → 4개 탭 읽기.
6. 기본 샘플 demo / Rank 1 / Cost 100 → 120을 검사만으로 실행한 뒤 실제 쓰기를 실행한다.
7. 다시 읽어 120인지 확인한다. 기대값 999로 130을 쓰면 충돌로 거절되어야 한다.

캐시는 LabData/sheet-cache, 토큰은 LabData/sheet.json이다. 둘 다 git 무시한다.
시트의 수동 편집은 Apps Script lock의 대상이 아니며, 여러 셀의 setValue는 DB 트랜잭션이 아니다.
실험창은 한 칸씩 쓰며 실패 시 다시 읽어 실제 상태를 확인한다. 자동 끌어오기와 게임 CSV 반영은 제공하지 않는다.

## 검증
Tests/SheetSync의 실행 테스트는 실제 Google 계정 없이 통신 응답·CSV·Apps Script 규칙을 검증한다.
Unity 창, Google 배포/권한/리다이렉트는 위 절차로 사용자 PC에서 확인해야 한다.
