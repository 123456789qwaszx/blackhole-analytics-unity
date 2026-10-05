using System.Threading.Tasks;

namespace BlackHole.Analytics.Transport
{
    // 통계 서버에 요청 하나를 보낸다. AnalyticsSender는 이것으로만 서버와 이야기한다 — 테스트에서는 가짜로 바꾼다.
    public interface IAnalyticsClient
    {
        Task<AnalyticsResponse> PostBattleSummaryAsync(string json);
    }
}
