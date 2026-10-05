namespace BlackHole.Analytics.Transport
{
    // 요청 하나의 응답. 응답을 받지 못했으면(연결 실패, 시간 초과) StatusCode가 0이고 Error에 이유가 있다.
    public sealed class AnalyticsResponse
    {
        public long StatusCode { get; }
        public string Body { get; }
        public string Error { get; }

        public AnalyticsResponse(long statusCode, string body, string error)
        {
            StatusCode = statusCode;
            Body = body;
            Error = error;
        }
    }
}
