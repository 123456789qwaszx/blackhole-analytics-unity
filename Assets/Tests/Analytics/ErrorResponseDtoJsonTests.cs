using BlackHole.Analytics.Transport;
using NUnit.Framework;
using UnityEngine;

namespace BlackHole.Analytics.Tests
{
    // 에러 응답 예시(error-response.sample.json)를 실제 JsonUtility로 확인한다. 서버도 이 예시대로 답해야 한다.
    public sealed class ErrorResponseDtoJsonTests
    {
        [Test]
        public void SampleRoundTripsUnchanged()
        {
            string sample = JsonSamples.Load(JsonSamples.ErrorResponse);
            ErrorResponseDto dto = JsonUtility.FromJson<ErrorResponseDto>(sample);

            Assert.AreEqual(JsonSamples.Minify(sample), JsonUtility.ToJson(dto));
            Assert.AreEqual("INVALID_FIELD", dto.code);
            Assert.AreEqual("startGrowthStage", dto.errors[0].field);
        }
    }
}
