using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BlackHole.Analytics.Transport
{
    // 통계 서버에 HTTP로 보낸다. 요청 하나를 보내고 응답을 그대로 돌려준다 — 큐에서 지울지는 AnalyticsSender가 정한다.
    public sealed class AnalyticsClient : IAnalyticsClient
    {
        public const string BattleStatsPath = "/api/v1/battle-stats";

        private readonly string _battleStatsUrl;
        private readonly int _timeoutSeconds;

        // baseUrl 예: http://localhost:8080
        public AnalyticsClient(string baseUrl, int timeoutSeconds)
        {
            _battleStatsUrl = baseUrl.TrimEnd('/') + BattleStatsPath;
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task<AnalyticsResponse> PostBattleStatsAsync(string json)
        {
            using var request = new UnityWebRequest(_battleStatsUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds,
            };

            request.SetRequestHeader("Content-Type", "application/json");
            await request.SendWebRequest();

            // 4xx·5xx도 서버가 답한 것이다. 연결 실패·시간 초과만 응답이 없다.
            bool answered = request.result == UnityWebRequest.Result.Success
                || request.result == UnityWebRequest.Result.ProtocolError;

            return answered
                ? new AnalyticsResponse(request.responseCode, request.downloadHandler.text, null)
                : new AnalyticsResponse(0, null, request.error);
        }
    }
}
