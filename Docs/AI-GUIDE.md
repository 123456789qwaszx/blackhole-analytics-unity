# 독립 AI 초안 지침

입력 묶음의 values는 허용된 경로와 value/min/max/type, notes는 메모 id/intent다.
사용자의 intent에 직접 관련된 값만 최소로 바꾼다. Unity나 전투를 실행하지 않는다.

## 4. 출력 계약
`LabData/ai-draft.json` 하나만 쓴다. 다른 파일은 읽기 전용이다.
형식: {"name":"ai-draft","note":"요약","patches":[{"path":"spawn/count","value":80,"reason":"밀도 감소","noteIds":["입력의 실제 메모 ID"]}]}
값은 최대 3개, 경로 중복 금지, reason과 noteIds 필수. 입력의 범위·정수 여부를 지킨다.
가능하면 현재 값의 0.5~2배 이내로 제안한다. 정보가 부족하면 초안을 쓰지 않는다.

## 6. 응답
무엇을 / 왜 / 예상의 세 줄로 보고한다. 적용이나 게임 검증을 했다고 말하지 않는다.
