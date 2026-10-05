using System;
using UnityEngine;

namespace BlackHole.Analytics.Transport
{
    // 요청 하나의 결과. 세 갈래다:
    // - 닿지 않음(NetworkError): 연결 실패·시간 초과. Body에 이유가 있다. 오프라인이 정상 경로라 예외가 아니다.
    // - 2xx(Ok)
    // - 그 밖의 상태 코드: ErrorCode는 에러 본문의 code다. 우리 서버 형식이 아니면 null.
    public sealed class AnalyticsResponse
    {
        public bool NetworkError { get; }
        public long StatusCode { get; }
        public string Body { get; }
        public string ErrorCode { get; }
        public bool Ok => !NetworkError && IsSuccess(StatusCode);

        private AnalyticsResponse(bool networkError, long statusCode, string body, string errorCode)
        {
            NetworkError = networkError;
            StatusCode = statusCode;
            Body = body;
            ErrorCode = errorCode;
        }

        public static AnalyticsResponse Network(string error) =>
            new AnalyticsResponse(true, 0, error, null);

        public static AnalyticsResponse FromHttp(long statusCode, string body) =>
            new AnalyticsResponse(false, statusCode, body, IsSuccess(statusCode) ? null : ReadErrorCode(body));

        private static bool IsSuccess(long statusCode) => statusCode >= 200 && statusCode < 300;

        // 에러 본문의 code. 비었거나 우리 서버 형식이 아니면(프록시의 HTML 오류 페이지 등) null이다.
        private static string ReadErrorCode(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                string code = JsonUtility.FromJson<ErrorResponseDto>(body)?.code;
                return string.IsNullOrEmpty(code) ? null : code;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
