using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BlackHole.Analytics.Transport
{
    // 통계를 큐에 넣고 서버로 보낸다. 큐에 남은 것은 다음 보낼 기회에 다시 보낸다.
    // 보낼 기회는 부르는 쪽이 정한다: 판이 끝났을 때(SendAsync), 앱을 켜거나 다시 돌아왔을 때(FlushAsync).
    //
    // 결과는 "이 통계 하나의 문제인가, 전송 환경 전체의 문제인가"로 나눈다(Classify):
    // - 2xx: 전달됐다(Delivered). 처음 받으면 201, 이미 받은 판이면 200이다. 큐에서 지운다.
    // - 400·413·422: 이 통계가 잘못됐다(Rejected). rejected/로 옮기고 다음 통계를 보낸다.
    // - 그 밖: 환경의 문제다(Halted). 뒤의 통계도 같은 이유로 실패하므로 큐에 두고 이번 보내기를 멈춘다.
    //   닿지 않음은 Log(오프라인이면 정상), 408·425·429·5xx는 Warning(기다리면 풀린다),
    //   401·403·404·405·415와 계약에 없는 코드는 Error(인증·주소·계약을 고쳐야 한다).
    // 클라이언트는 최소 한 번 보낸다(at-least-once). 같은 판이 두 번 가도 서버가 battleId로 가린다.
    public sealed class AnalyticsSender
    {
        public enum Outcome
        {
            Delivered,
            Rejected,
            Halted,
        }

        private readonly AnalyticsQueue _queue;
        private readonly IAnalyticsClient _client;
        // 진행 중인 보내기. 없으면 끝난 Task다.
        private Task _flushing = Task.CompletedTask;

        public AnalyticsSender(AnalyticsQueue queue, IAnalyticsClient client)
        {
            _queue = queue;
            _client = client;
        }

        // 큐에 넣고 보낸다. 넣지 못해도(저장 공간 부족 등) 이 판의 통계만 잃고, 쌓여 있던 통계는 보낸다.
        public Task SendAsync(BattleSummaryDto summary)
        {
            try
            {
                _queue.Enqueue(summary);
            }
            catch (Exception error)
            {
                Debug.LogError($"[통계] 큐에 넣지 못했다: {error}");
            }

            return FlushAsync();
        }

        // 큐가 빌 때까지, 또는 멈출 때까지 넣은 순서대로 보낸다.
        // 이미 보내는 중이면 그 보내기를 돌려준다 — 새로 넣은 통계도 멈추지 않는 한 그 보내기가 이어서 보낸다.
        // 돌려준 Task가 끝났다는 것은 이번 보내기가 끝났다는 뜻이지, 다 보냈다는 뜻이 아니다.
        // 예외를 던지지 않는다. 기다리지 않고 불러도(_ = FlushAsync()) 된다.
        public Task FlushAsync()
        {
            if (_flushing.IsCompleted)
                _flushing = FlushCoreAsync();

            return _flushing;
        }

        public static Outcome Classify(AnalyticsResponse response)
        {
            if (response.Ok)
                return Outcome.Delivered;

            if (!response.NetworkError && IsBadData(response.StatusCode))
                return Outcome.Rejected;

            return Outcome.Halted;
        }

        private async Task FlushCoreAsync()
        {
            try
            {
                while (_queue.TryPeek(out AnalyticsQueue.Item item))
                {
                    AnalyticsResponse response = await _client.PostBattleSummaryAsync(item.Json);

                    switch (Classify(response))
                    {
                        case Outcome.Delivered:
                            _queue.Remove(item);
                            break;

                        case Outcome.Rejected:
                            _queue.Reject(item);
                            Debug.LogError($"[통계] 서버가 받지 않아 rejected/로 옮겼다 - {Describe(response)} ({item.Name})\n{response.Body}");
                            break;

                        default:
                            LogHalted(response, item);
                            return;
                    }
                }
            }
            catch (Exception error)
            {
                // 예상하지 못한 예외(파일을 읽거나 옮기지 못함, 잘못된 주소 등). 통계는 큐에 남아 다음에 다시 보낸다.
                Debug.LogError($"[통계] 보내다 멈췄다: {error}");
            }
        }

        // 이 통계 하나가 잘못됐다. 다시 보내도 같고, 다른 통계와는 상관없다.
        private static bool IsBadData(long statusCode) =>
            statusCode == 400 || statusCode == 413 || statusCode == 422;

        // 잠깐의 제한이나 서버 오류. 기다리면 풀린다.
        private static bool IsTransient(long statusCode) =>
            statusCode == 408 || statusCode == 425 || statusCode == 429 || (statusCode >= 500 && statusCode < 600);

        private static void LogHalted(AnalyticsResponse response, AnalyticsQueue.Item item)
        {
            string message = $"[통계] 보내지 못해 큐에 두었다 - {Describe(response)} ({item.Name})";

            if (response.NetworkError)
                Debug.Log(message);
            else if (IsTransient(response.StatusCode))
                Debug.LogWarning(message);
            else
                Debug.LogError(message + "\n인증·주소·계약이 맞는지 확인한다.");
        }

        // 로그에 남길 결과 요약: "HTTP 400 INVALID_FIELD" 또는 "닿지 않음: <이유>".
        private static string Describe(AnalyticsResponse response)
        {
            if (response.NetworkError)
                return $"닿지 않음: {response.Body}";

            return response.ErrorCode == null
                ? $"HTTP {response.StatusCode}"
                : $"HTTP {response.StatusCode} {response.ErrorCode}";
        }
    }
}
