using System;

namespace BlackHole.Analytics
{
    // 판의 스킬별 피해와 수집 통계(게임 Core의 BattleStats와 같은 칸).
    // 분석할 때 지킬 규칙:
    // - 피해는 피격 기록의 피해량 합이다 — 적의 남은 체력을 넘은 몫도 센다.
    // - breakerCriticalDamage는 breakerDamage에 이미 들어 있다. 둘을 더하지 않는다.
    // - goldenAsteroidGold는 BattleSummaryDto.earnedGold에 이미 들어 있다.
    // - addedSeconds는 블랙홀 성장(Level업) 몫과 적 파괴 때 시간 추가 몫의 합이다.
    //   성장 몫은 (reachedLevel − appliedStats.startLevel) × appliedStats.growthTimeSeconds, 나머지가 파괴 몫이다.
    [Serializable]
    public sealed class BattleStatsDto
    {
        // Breaker가 준 피해 전부(치명타 포함)와 그중 치명타 Tick의 몫, Tick 수(맞힌 적이 없는 Tick도 센다).
        public double breakerDamage;
        public double breakerCriticalDamage;
        public int breakerTicks;

        // 사망 효과의 피해: 전기 소행성·전기 별의 번개, 레이저 별의 레이저, 초신성 별의 폭발.
        public double electricAsteroidDamage;
        public double electricStarDamage;
        public double laserDamage;
        public double supernovaDamage;

        // 황금 소행성의 처치 보상 합(황금 치명타 보너스 포함).
        public long goldenAsteroidGold;

        // 처치해 Breaker 버프로 받은 달·혜성 수.
        public int collectedMoons;
        public int collectedComets;

        // 이 판의 제한 시간에 더해진 초.
        public float addedSeconds;
    }
}
