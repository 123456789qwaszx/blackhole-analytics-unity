using System;
using System.Collections.Generic;

namespace BlackHole.Analytics
{
    // 판을 시작할 때 확정된, 이 판에 실제로 적용된 수치. 분석에 쓰는 것만 담는다 — 업그레이드 표 전체가 아니다.
    // 시트 기본값(UpgradeStatValues.ValueOf)이나 증가량(GainOf)이 아니라, 전투가 실제로 쓴 최종 값이다(콘텐츠 기본값에 노드를 반영한 값).
    // 기본값과 노드 표는 버전마다 바뀔 수 있으므로, 기본값과 같은 수치도 빼지 않고 적는다.
    [Serializable]
    public sealed class AppliedStatsDto
    {
        // 블랙홀: 이 판이 시작한 Level과 그 Level에 닿는 누적 EXP, 목표 Level(다음 이정표)과 그 누적 EXP.
        // 목표가 없으면(마지막 이정표 뒤) goalLevel·goalExp는 0이다.
        public int startLevel;
        public long startExp;
        public int goalLevel;
        public long goalExp;

        // 판을 시작할 때의 제한 시간(초)과 블랙홀 Level업마다 더하는 초. 판 중에 실제로 늘어난 시간은 stats.addedSeconds다.
        public float timeLimitSeconds;
        public float growthTimeSeconds;

        // Breaker: Tick 하나의 피해, 공격 주기(초), 공격 원의 반지름(달 버프 전), 치명타 확률(0 ~ 1), 치명타 때 더하는 피해 배율(1이면 2배).
        public float breakerDamage;
        public float breakerInterval;
        public float breakerRadius;
        public float breakerCritChance;
        public float breakerCritDamage;

        // 보통 종류(소행성·행성·별)의 성질이 붙을 확률. 종류의 성질마다 하나씩, 확률이 0이어도 적는다.
        // 픽업(혜성)은 성질이 언제나 붙으므로 넣지 않는다.
        public List<TraitChanceDto> traitChances = new();
        // 황금 소행성의 Gold 배율(노드 반영).
        public float goldenAsteroidMultiplier;
    }
}
