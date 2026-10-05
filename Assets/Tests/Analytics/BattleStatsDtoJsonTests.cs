using System;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BlackHole.Analytics.Tests
{
    // 계약 예시(battle-stats.sample.json)를 실제 JsonUtility로 확인한다.
    // 서버와 전송 계층이 이 예시를 테스트 데이터로 쓰므로, DTO가 예시와 다른 JSON을 내면 계약이 어긋난 것이다.
    public sealed class BattleStatsDtoJsonTests
    {
        private const string SamplePath = "Assets/Scripts/Analytics/battle-stats.sample.json";

        // 예시를 읽어 다시 쓰면 공백만 빼고 예시와 같다. 키 이름·순서와 숫자 형식이 계약대로다.
        [Test]
        public void SampleRoundTripsUnchanged()
        {
            string sample = LoadSample();
            BattleStatsDto dto = JsonUtility.FromJson<BattleStatsDto>(sample);

            Assert.AreEqual(Minify(sample), JsonUtility.ToJson(dto));
        }

        // 값이 없으면 null이 아니라 ""와 []로 나간다.
        [Test]
        public void EmptyValuesAreWrittenAsEmptyStringsAndLists()
        {
            string json = JsonUtility.ToJson(new BattleStatsDto());

            StringAssert.Contains("\"battleId\":\"\"", json);
            StringAssert.Contains("\"contentVersion\":\"\"", json);
            StringAssert.Contains("\"nodes\":[]", json);
            StringAssert.Contains("\"kills\":[]", json);
            StringAssert.DoesNotContain("null", json);
        }

        // 예시가 계약의 값 규칙을 지킨다.
        [Test]
        public void SampleFollowsContractRules()
        {
            BattleStatsDto dto = JsonUtility.FromJson<BattleStatsDto>(LoadSample());

            Assert.AreEqual(BattleStatsDto.CurrentSchemaVersion, dto.schemaVersion);
            Assert.IsTrue(Guid.TryParse(dto.battleId, out _), "battleId는 UUID다.");
            Assert.IsTrue(ParseUtc(dto.startedAtUtc) <= ParseUtc(dto.endedAtUtc), "끝낸 시각은 시작한 시각보다 앞서지 않는다.");
            Assert.AreEqual(dto.kills.Sum(kill => kill.count), dto.totalKills, "totalKills는 kills의 합이다.");

            if (!dto.reachedMilestone)
                Assert.AreEqual(dto.earnedGold, dto.settledGold, "이정표 없이 끝나면 settledGold는 earnedGold와 같다.");
        }

        private static string LoadSample()
        {
            var sample = AssetDatabase.LoadAssetAtPath<TextAsset>(SamplePath);
            Assert.IsNotNull(sample, $"계약 예시가 없다: {SamplePath}");
            return sample.text;
        }

        // UTC ISO 8601("o" 형식)만 받는다.
        private static DateTime ParseUtc(string value)
        {
            DateTime time = DateTime.ParseExact(value, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Assert.AreEqual(DateTimeKind.Utc, time.Kind, $"UTC가 아니다: {value}");
            return time;
        }

        // 문자열 밖의 공백을 지운다. JsonUtility.ToJson(prettyPrint 없음)과 견줄 수 있게.
        private static string Minify(string json)
        {
            var builder = new StringBuilder(json.Length);
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    builder.Append(c);

                    if (c == '\\')
                        builder.Append(json[++i]);
                    else if (c == '"')
                        inString = false;
                }
                else if (c == '"')
                {
                    inString = true;
                    builder.Append(c);
                }
                else if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
