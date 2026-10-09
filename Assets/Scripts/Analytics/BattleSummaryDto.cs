using System;
using System.Collections.Generic;

namespace BlackHole.Analytics
{
    // 전투 한 판의 요약: 시작 조건(성장도·노드·적용된 수치), 집계(처치·Gold·스킬 통계), 결과. 통계 서버와 주고받는 JSON 계약이다(예: battle-summary.sample.json).
    // 필드 이름이 곧 JSON 키라서 C# 관례와 달리 camelCase다. 필드 순서가 곧 JSON 키 순서다.
    // JsonUtility는 null을 쓰지 못한다 — 문자열은 "", 목록은 []로 나간다. 값이 없으면 ""다.
    [Serializable]
    public sealed class BattleSummaryDto
    {
        // 지금 쓰는 계약 버전. 필드를 바꾸면 올린다.
        // 2: platform·seed·appliedStats·exp·stats 추가.
        public const int CurrentSchemaVersion = 2;

        // 만들 때 지금 계약 버전으로 정해 둔다. 채우는 쪽이 빠뜨려도 0이 나가지 않는다.
        public int schemaVersion = CurrentSchemaVersion;
        // 판 하나의 ID(UUID). 판을 시작할 때 한 번 만들고, 다시 보내도 바뀌지 않는다. 서버는 이것으로 중복을 가린다.
        public string battleId;
        // 설치 하나의 ID(UUID). 첫 실행 때 만들어 기기에 보관한다. 같은 플레이어의 판을 잇는다 — 앱을 지우면 새로 생긴다.
        public string installId;
        // 이 설치에서 몇 번째 판인가(1부터). 판 순서는 기기 시각 대신 이것으로 본다.
        // 판을 시작할 때마다 오르므로, 끝나지 않은 판(포기·강제 종료)이 있으면 번호가 건너뛴다.
        public int battleIndex;

        // 앱 빌드 버전. 수집용 빌드마다 바꾼다 — contentVersion이 생기기 전까지 밸런스가 다른 판을 가르는 기준이다.
        public string buildVersion;
        // 콘텐츠(밸런스) 버전. 정하는 방식이 생기기 전에는 ""다. 밸런스 패치를 시작하기 전에 도입한다.
        public string contentVersion;
        // 실행 플랫폼(Application.platform의 이름: Android, WindowsEditor …). 에디터에서 낸 판을 분석에서 거른다.
        public string platform;

        // 판을 시작한 시각과 끝낸 시각(UTC, ISO 8601).
        public string startedAtUtc;
        public string endedAtUtc;
        // 판이 진행된 게임 시간(초). 두 시각의 차이와 다를 수 있다.
        public float playedSeconds;
        // 판의 난수 시드. 같은 판을 다시 돌리려면 같은 콘텐츠 수치와 전투 로직(같은 빌드)도 필요하다 — 시드만으로는 재현되지 않는다.
        public int seed;

        // 판을 시작할 때의 성장도와 산 노드(처음 산 순서). 노드는 전투 중에 바뀌지 않는다.
        public int startGrowthStage;
        public List<NodeRankDto> nodes = new();
        // 판을 시작할 때 이 판에 실제로 적용된 수치(분석에 쓰는 것만). 과거 판의 조건은 노드 목록으로 다시 계산하지 않고 이것으로 본다.
        public AppliedStatsDto appliedStats = new();

        // 종류별 처치 수(처음 처치한 순서)와 그 합.
        public List<EnemyKillDto> kills = new();
        public int totalKills;

        // 이 판에서 번 Gold. 황금 소행성 Gold(stats.goldenAsteroidGold)도 들어 있다.
        public long earnedGold;
        // 결산이 진행 상태에 더한 Gold. 이정표로 끝났으면 이정표 보상, 아니면 earnedGold와 같다.
        public long settledGold;
        // 판이 끝났을 때 블랙홀의 Level과 누적 EXP. EXP는 시작 Level에 닿는 누적 EXP(appliedStats.startExp)부터 센다.
        public int reachedLevel;
        public long exp;
        // 이정표에 닿아 끝났는가.
        public bool reachedMilestone;

        // 스킬별 피해와 수집 통계. 해석 규칙은 BattleStatsDto에 있다.
        public BattleStatsDto stats = new();
    }
}
