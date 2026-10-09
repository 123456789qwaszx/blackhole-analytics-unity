using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BlackHole.Analytics.Tests
{
    // 계약 예시(battle-summary.sample.json)를 실제 JsonUtility로 확인한다.
    // 서버와 전송 계층이 이 예시를 테스트 데이터로 쓰므로, DTO가 예시와 다른 JSON을 내면 계약이 어긋난 것이다.
    public sealed class BattleSummaryDtoJsonTests
    {
        // 예시를 읽어 다시 쓰면 공백만 빼고 예시와 같다. 키 이름·순서와 숫자 형식이 계약대로다.
        [Test]
        public void SampleRoundTripsUnchanged()
        {
            string sample = JsonSamples.Load(JsonSamples.BattleSummary);
            BattleSummaryDto dto = JsonUtility.FromJson<BattleSummaryDto>(sample);

            Assert.AreEqual(JsonSamples.Minify(sample), JsonUtility.ToJson(dto));
        }

        // 값이 없으면 null이 아니라 ""와 []로 나간다.
        [Test]
        public void EmptyValuesAreWrittenAsEmptyStringsAndLists()
        {
            string json = JsonUtility.ToJson(new BattleSummaryDto());

            StringAssert.Contains("\"battleId\":\"\"", json);
            StringAssert.Contains("\"installId\":\"\"", json);
            StringAssert.Contains("\"contentVersion\":\"\"", json);
            StringAssert.Contains("\"platform\":\"\"", json);
            StringAssert.Contains("\"nodes\":[]", json);
            StringAssert.Contains("\"kills\":[]", json);
            StringAssert.Contains("\"traitChances\":[]", json);
            StringAssert.Contains("\"appliedStats\":{", json);
            StringAssert.Contains("\"stats\":{", json);
            StringAssert.DoesNotContain("null", json);
        }

        // 새로 만들면 지금 계약 버전이다. 채우는 쪽이 빠뜨려도 0이 나가지 않는다.
        [Test]
        public void NewDtoHasCurrentSchemaVersion()
        {
            Assert.AreEqual(BattleSummaryDto.CurrentSchemaVersion, new BattleSummaryDto().schemaVersion);
        }

        // 예시가 계약의 값 규칙을 지킨다.
        [Test]
        public void SampleFollowsContractRules()
        {
            BattleSummaryDto dto = JsonUtility.FromJson<BattleSummaryDto>(JsonSamples.Load(JsonSamples.BattleSummary));

            Assert.AreEqual(BattleSummaryDto.CurrentSchemaVersion, dto.schemaVersion);
            Assert.IsTrue(Guid.TryParse(dto.battleId, out _), "battleId는 UUID다.");
            Assert.IsTrue(Guid.TryParse(dto.installId, out _), "installId는 UUID다.");
            Assert.IsTrue(dto.battleIndex >= 1, "battleIndex는 1부터다.");
            Assert.IsTrue(ParseUtc(dto.startedAtUtc) <= ParseUtc(dto.endedAtUtc), "끝낸 시각은 시작한 시각보다 앞서지 않는다.");
            Assert.AreEqual(dto.kills.Sum(kill => kill.count), dto.totalKills, "totalKills는 kills의 합이다.");

            if (!dto.reachedMilestone)
                Assert.AreEqual(dto.earnedGold, dto.settledGold, "이정표 없이 끝나면 settledGold는 earnedGold와 같다.");

            Assert.IsNotEmpty(dto.platform, "platform은 채운다.");
            Assert.That(dto.kills.Select(kill => kill.enemyId), Has.All.Match(IdPattern), "enemyId는 소문자로 시작하는 이름이다.");

            AppliedStatsDto applied = dto.appliedStats;
            Assert.That(applied.breakerCritChance, Is.InRange(0f, 1f), "치명타 확률은 0 ~ 1이다.");
            Assert.That(applied.traitChances.Select(trait => trait.chance), Has.All.InRange(0f, 1f), "성질 확률은 0 ~ 1이다.");
            Assert.That(applied.traitChances.Select(trait => trait.enemyId), Has.All.Match(IdPattern), "enemyId는 소문자로 시작하는 이름이다.");
            Assert.That(applied.traitChances.Select(trait => trait.traitId), Has.All.Match(IdPattern), "traitId는 소문자로 시작하는 이름이다.");
            Assert.IsTrue(dto.exp >= applied.startExp, "누적 EXP는 시작 Level의 EXP부터 센다.");

            if (applied.goalExp > 0)
                Assert.AreEqual(dto.reachedMilestone, dto.exp >= applied.goalExp, "목표 EXP에 닿은 판만 이정표에 닿는다.");

            BattleStatsDto stats = dto.stats;
            Assert.IsTrue(stats.breakerCriticalDamage <= stats.breakerDamage, "치명타 피해는 Breaker 피해에 들어 있다.");
            Assert.IsTrue(stats.goldenAsteroidGold <= dto.earnedGold, "황금 소행성 Gold는 earnedGold에 들어 있다.");
            Assert.IsTrue(dto.playedSeconds <= applied.timeLimitSeconds + stats.addedSeconds, "판 시간은 제한 시간과 늘어난 시간의 합을 넘지 않는다.");

            float growthSeconds = (dto.reachedLevel - applied.startLevel) * applied.growthTimeSeconds;
            Assert.IsTrue(growthSeconds <= stats.addedSeconds, "늘어난 시간에는 Level업마다의 성장 시간이 들어 있다.");
        }

        // enemyId·traitId: EnemyType·EnemyTraitType 이름의 첫 글자를 소문자로 쓴 것.
        private const string IdPattern = "^[a-z][A-Za-z]*$";

        // UTC ISO 8601("o" 형식)만 받는다.
        private static DateTime ParseUtc(string value)
        {
            DateTime time = DateTime.ParseExact(value, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Assert.AreEqual(DateTimeKind.Utc, time.Kind, $"UTC가 아니다: {value}");
            return time;
        }
    }
}
