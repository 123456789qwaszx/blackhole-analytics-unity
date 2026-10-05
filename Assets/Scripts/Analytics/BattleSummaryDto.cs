using System;
using System.Collections.Generic;

namespace BlackHole.Analytics
{
    // 전투 한 판의 요약: 시작 상태(성장도·노드), 집계(처치·Gold), 결과. 통계 서버와 주고받는 JSON 계약이다(예: battle-summary.sample.json).
    // 필드 이름이 곧 JSON 키라서 C# 관례와 달리 camelCase다.
    // JsonUtility는 null을 쓰지 못한다 — 문자열은 "", 목록은 []로 나간다. 값이 없으면 ""다.
    [Serializable]
    public sealed class BattleSummaryDto
    {
        // 지금 쓰는 계약 버전. 필드를 바꾸면 올린다.
        public const int CurrentSchemaVersion = 1;

        // 만들 때 지금 계약 버전으로 정해 둔다. 채우는 쪽이 빠뜨려도 0이 나가지 않는다.
        public int schemaVersion = CurrentSchemaVersion;
        // 판 하나의 ID(UUID). 판을 시작할 때 한 번 만들고, 다시 보내도 바뀌지 않는다. 서버는 이것으로 중복을 가린다.
        public string battleId;
        // 설치 하나의 ID(UUID). 첫 실행 때 만들어 기기에 보관한다. 같은 플레이어의 판을 잇는다 — 앱을 지우면 새로 생긴다.
        public string installId;
        // 이 설치에서 몇 번째 판인가(1부터). 판 순서는 기기 시각 대신 이것으로 본다.
        // 판을 시작할 때마다 오르므로, 끝나지 않은 판(포기·강제 종료)이 있으면 번호가 건너뛴다.
        public int battleIndex;

        // 앱 빌드 버전.
        public string buildVersion;
        // 콘텐츠(밸런스) 버전. 공급 방식을 정하기 전에는 ""다.
        public string contentVersion;

        // 판을 시작한 시각과 끝낸 시각(UTC, ISO 8601).
        public string startedAtUtc;
        public string endedAtUtc;
        // 판이 진행된 게임 시간(초). 두 시각의 차이와 다를 수 있다.
        public float playedSeconds;

        // 판을 시작할 때의 성장도와 산 노드(처음 산 순서). 노드는 전투 중에 바뀌지 않는다.
        public int startGrowthStage;
        public List<NodeRankDto> nodes = new();

        // 종류별 처치 수(처음 처치한 순서)와 그 합.
        public List<EnemyKillDto> kills = new();
        public int totalKills;

        // 이 판에서 번 Gold.
        public long earnedGold;
        // 결산이 진행 상태에 더한 Gold. 이정표로 끝났으면 이정표 보상, 아니면 earnedGold와 같다.
        public long settledGold;
        // 판이 끝났을 때 블랙홀의 Level.
        public int reachedLevel;
        // 이정표에 닿아 끝났는가.
        public bool reachedMilestone;
    }
}
