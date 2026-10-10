# AI 연계 독립 실험

`feat/ai-integration`은 `dev`에서 분기한다. 시트 브랜치, 게임 Core, 전투, 노드 시트가 없어도 실행한다.
원본 M6의 실행·취소·시간 제한·결과 기록·실패 재시도 코드를 이동했다.
게임 로더/승격 참조는 입력 계약의 경로·값 범위·정수·메모 검증으로 교체했다.

## 실행
1. PC에서 Claude Code 설치·로그인을 마친다. 이 실행기는 원본과 같은 CLI 옵션을 사용한다.
2. Unity 메뉴 Integration Lab → AI Draft → Claude Code 확인.
3. 샘플 메모를 저장한다. Samples/Ai/context.json + 입력 메모가 LabData/context/sample.json이 된다.
4. 저장한 메모로 초안 요청 → 완료 상태와 LabData/ai-draft.json을 확인한다.
5. 필요할 때 메모 저장 때 자동 요청을 켠다. 기본값은 꺼짐이다.
6. 실행 중 메모 입력은 잠긴다. 동일 파일 입력이 중간에 바뀌지 않게 한다. 취소·타임아웃·재시도·실행 기록은 AiRunner가 처리한다.

설정은 LabData/ai.json(command/model/maxTurns/maxBudgetUsd/timeoutSeconds/retry).
기록은 LabData/ai-runs와 ai-runs.ndjson. 실제 Claude 호출은 사용량이 든다.
게임 데이터에 반영하는 기능은 없다. 게임별 어댑터가 입력 묶음과 검사 기준을 제공하도록 분리했다.

## 검증
Tests/Ai는 게임·Unity·Claude 계정 없이 JSON 계약과 실행 계획·결과 판정을 검사한다.
실제 Claude 로그인과 Windows 프로세스/Unity 창은 사용자 PC에서 위 절차로 확인해야 한다.
