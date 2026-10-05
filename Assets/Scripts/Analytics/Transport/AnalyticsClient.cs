using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BlackHole.Analytics.Transport
{
    // 통계 서버에 HTTP로 보낸다. 요청 하나를 보내고 응답을 그대로 돌려준다 — 큐에서 지울지는 AnalyticsSender가 정한다.
    public sealed class AnalyticsClient : IAnalyticsClient
    {
        public const string BattleSummariesPath = "/api/v1/battle-summaries";

        private readonly string _battleSummariesUrl;
        private readonly int _timeoutSeconds;

        // baseUrl 예: http://localhost:8080
        public AnalyticsClient(string baseUrl, int timeoutSeconds)
        {
            _battleSummariesUrl = baseUrl.TrimEnd('/') + BattleSummariesPath;
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task<AnalyticsResponse> PostBattleSummaryAsync(string json)
        {
            using var request = new UnityWebRequest(_battleSummariesUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds,
            };

            request.SetRequestHeader("Content-Type", "application/json");
            await request.SendWebRequest();

            // 4xx·5xx도 서버가 답한 것이다. 연결 실패·시간 초과만 닿지 않은 것이다.
            if (request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return AnalyticsResponse.Network(request.error);
            }

            return AnalyticsResponse.FromHttp(request.responseCode, request.downloadHandler.text);
        }
    }
}
