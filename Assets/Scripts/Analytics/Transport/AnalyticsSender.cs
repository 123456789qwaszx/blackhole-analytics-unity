using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace BlackHole.Analytics.Transport
{
    // 통계를 큐에 넣고 서버로 보낸다. 큐에 남은 것은 다음 보낼 기회에 다시 보낸다.
    // 보낼 기회는 부르는 쪽이 정한다: 판이 끝났을 때(SendAsync), 앱을 켜거나 다시 돌아왔을 때(FlushAsync).
    // 응답에 따라(Classify):
    // - 2xx: 보냈다. 처음 받으면 201, 이미 받은 판이면 200이다. 큐에서 지운다.
    // - 400·413·422: 요청 내용 탓이라 다시 보내도 같다. 에러를 남기고 버린다.
    // - 그 밖(응답 없음, 404, 429, 5xx 등): 큐에 두고 이번 보내기를 멈춘다. 서버 주소가 틀려도 통계를 잃지 않는다.
    public sealed class AnalyticsSender
    {
        public enum Outcome
        {
            Sent,
            Rejected,
            Retry,
        }

        private readonly AnalyticsQueue _queue;
        private readonly IAnalyticsClient _client;
        private bool _flushing;

        public AnalyticsSender(AnalyticsQueue queue, IAnalyticsClient client)
        {
            _queue = queue;
            _client = client;
        }

        public Task SendAsync(BattleStatsDto stats)
        {
            try
            {
                _queue.Enqueue(stats);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // 저장 공간 부족 등. 이 판의 통계는 잃고, 게임은 그대로 이어 간다.
                Debug.LogError($"[통계] 큐에 넣지 못했다: {error.Message}");
            }

            return FlushAsync();
        }

        // 큐가 빌 때까지 넣은 순서대로 보낸다. 이미 보내는 중이면 그쪽이 새로 넣은 것까지 보낸다.
        public async Task FlushAsync()
        {
            if (_flushing)
                return;

            _flushing = true;

            try
            {
                while (_queue.TryPeek(out AnalyticsQueue.Item item))
                {
                    AnalyticsResponse response = await _client.PostBattleStatsAsync(item.Json);
                    Outcome outcome = Classify(response.StatusCode);

                    if (outcome == Outcome.Retry)
                    {
                        Debug.LogWarning($"[통계] 보내지 못해 큐에 두었다({Describe(response)}): {item.Name}");
                        return;
                    }

                    if (outcome == Outcome.Rejected)
                        Debug.LogError($"[통계] 서버가 받지 않아 버렸다({Describe(response)}): {item.Name}");

                    _queue.Remove(item);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogError($"[통계] 큐를 읽거나 지우지 못했다: {error.Message}");
            }
            finally
            {
                _flushing = false;
            }
        }

        // 응답 코드로 큐에서 지울지 정한다. 0은 응답을 받지 못한 것이다.
        public static Outcome Classify(long statusCode)
        {
            if (statusCode >= 200 && statusCode < 300)
                return Outcome.Sent;

            if (statusCode == 400 || statusCode == 413 || statusCode == 422)
                return Outcome.Rejected;

            return Outcome.Retry;
        }

        // 로그에 남길 응답 요약.
        private static string Describe(AnalyticsResponse response)
        {
            if (response.StatusCode == 0)
                return $"응답 없음: {response.Error}";

            ErrorResponseDto error = ParseError(response.Body);

            if (error == null || string.IsNullOrEmpty(error.code))
                return response.StatusCode.ToString();

            string summary = $"{response.StatusCode} {error.code}: {error.message}";

            if (error.errors.Count > 0)
                summary += " (" + string.Join(", ", error.errors.Select(field => $"{field.field}: {field.reason}")) + ")";

            return summary;
        }

        private static ErrorResponseDto ParseError(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                return JsonUtility.FromJson<ErrorResponseDto>(body);
            }
            catch (ArgumentException)
            {
                // JSON이 아니다(프록시의 HTML 오류 페이지 등).
                return null;
            }
        }
    }
}
