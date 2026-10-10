# Unity 연동 실험실

기존 Analytics 전송·큐 실험은 그대로 유지한다. `dev`는 독립 실험들의 공통 기준이다.

| 브랜치 | 실험 | 메뉴 |
| --- | --- | --- |
| dev | 기존 서버 통신 + 공통 JSON | 기존 Analytics 씬 |
| feat/sheet-sync | Apps Script 시트 읽기·쓰기·충돌 검사 | Integration Lab / Sheet Sync |
| feat/ai-integration | 로컬 Claude Code 호출·메모·초안·검사 | Integration Lab / AI Draft |

두 브랜치는 각각 dev에서 출발한다. 서로를 병합하거나 게임 레포를 설치할 필요가 없다.
게임 상태 생성, 노드 구매 규칙, 전투 시뮬레이션, 게임 원본 반영은 이 레포의 책임이 아니다.
실험 데이터는 `LabData/`에 저장하며 git에서 제외한다. 팀 공유용 샘플은 각 브랜치의 `Samples/`에 둔다.

분리 원본: Anyway-WeMadeIt/Feed-A-Blackhole-Client `feat/autoLoop`의 f044ea6.
