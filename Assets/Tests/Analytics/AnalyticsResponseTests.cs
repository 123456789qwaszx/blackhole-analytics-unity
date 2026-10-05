using BlackHole.Analytics.Transport;
using NUnit.Framework;

namespace BlackHole.Analytics.Tests
{
    public sealed class AnalyticsResponseTests
    {
        // 닿지 않으면 상태 코드가 없고, 이유는 Body에 있다.
        [Test]
        public void NetworkErrorIsNotOk()
        {
            AnalyticsResponse response = AnalyticsResponse.Network("Cannot connect to destination host");

            Assert.IsTrue(response.NetworkError);
            Assert.IsFalse(response.Ok);
            Assert.AreEqual("Cannot connect to destination host", response.Body);
            Assert.IsNull(response.ErrorCode);
        }

        // 2xx는 본문이 무엇이든 에러 코드를 읽지 않는다.
        [TestCase(200)]
        [TestCase(201)]
        public void SuccessIsOkWithoutErrorCode(int statusCode)
        {
            AnalyticsResponse response = AnalyticsResponse.FromHttp(statusCode, "{\"code\":\"INVALID_FIELD\"}");

            Assert.IsTrue(response.Ok);
            Assert.IsNull(response.ErrorCode);
        }

        // 그 밖의 상태 코드는 에러 본문에서 code를 읽는다.
        [Test]
        public void ErrorCodeIsReadFromBody()
        {
            AnalyticsResponse response = AnalyticsResponse.FromHttp(400, JsonSamples.Load(JsonSamples.ErrorResponse));

            Assert.IsFalse(response.Ok);
            Assert.AreEqual("INVALID_FIELD", response.ErrorCode);
        }

        // 우리 서버 형식이 아니면 에러 코드는 없다: 빈 본문, 프록시의 HTML, 다른 모양의 JSON.
        [TestCase(502, "")]
        [TestCase(502, "<html>Bad Gateway</html>")]
        [TestCase(400, "{\"status\":400,\"error\":\"Bad Request\"}")]
        public void UnreadableBodyHasNoErrorCode(int statusCode, string body)
        {
            AnalyticsResponse response = AnalyticsResponse.FromHttp(statusCode, body);

            Assert.IsFalse(response.Ok);
            Assert.IsNull(response.ErrorCode);
            Assert.AreEqual(body, response.Body);
        }
    }
}
