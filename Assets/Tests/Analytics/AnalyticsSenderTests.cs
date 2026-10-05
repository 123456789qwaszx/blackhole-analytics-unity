using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlackHole.Analytics.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlackHole.Analytics.Tests
{
    public sealed class AnalyticsSenderTests
    {
        private string _directory;
        private AnalyticsQueue _queue;

        [SetUp]
        public void CreateQueue()
        {
            _directory = Path.Combine(Path.GetTempPath(), "analytics-sender-" + Guid.NewGuid().ToString("N"));
            _queue = new AnalyticsQueue(_directory, 10);
        }

        [TearDown]
        public void DeleteQueue()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [TestCase(200, AnalyticsSender.Outcome.Delivered)]
        [TestCase(201, AnalyticsSender.Outcome.Delivered)]
        [TestCase(204, AnalyticsSender.Outcome.Delivered)]
        [TestCase(400, AnalyticsSender.Outcome.Rejected)]
        [TestCase(413, AnalyticsSender.Outcome.Rejected)]
        [TestCase(422, AnalyticsSender.Outcome.Rejected)]
        [TestCase(302, AnalyticsSender.Outcome.Halted)]
        [TestCase(401, AnalyticsSender.Outcome.Halted)]
        [TestCase(403, AnalyticsSender.Outcome.Halted)]
        [TestCase(404, AnalyticsSender.Outcome.Halted)]
        [TestCase(405, AnalyticsSender.Outcome.Halted)]
        [TestCase(408, AnalyticsSender.Outcome.Halted)]
        [TestCase(409, AnalyticsSender.Outcome.Halted)]
        [TestCase(415, AnalyticsSender.Outcome.Halted)]
        [TestCase(425, AnalyticsSender.Outcome.Halted)]
        [TestCase(429, AnalyticsSender.Outcome.Halted)]
        [TestCase(500, AnalyticsSender.Outcome.Halted)]
        [TestCase(503, AnalyticsSender.Outcome.Halted)]
        public void ClassifiesStatusCode(int statusCode, AnalyticsSender.Outcome expected)
        {
            Assert.AreEqual(expected, AnalyticsSender.Classify(Answer(statusCode)));
        }

        [Test]
        public void ClassifiesNetworkErrorAsHalted()
        {
            Assert.AreEqual(AnalyticsSender.Outcome.Halted, AnalyticsSender.Classify(AnalyticsResponse.Network("timeout")));
        }

        // 처음 받은 판(201)도, 이미 받은 판(200)도 전달된 것이다.
        [Test]
        public void DeliveredIsRemoved()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            _queue.Enqueue(AnalyticsQueueTests.Stats("b"));
            var client = new FakeClient(Answer(201), Answer(200));

            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(2, client.Posted.Count);
            Assert.AreEqual(0, _queue.Count);
        }

        // 넣고 바로 보낸다. 보낸 본문은 큐에 넣은 JSON 그대로다.
        [Test]
        public void SendAsyncPostsQueuedJson()
        {
            BattleStatsDto stats = AnalyticsQueueTests.Stats("a");
            var client = new FakeClient(Answer(201));

            RunToEnd(new AnalyticsSender(_queue, client).SendAsync(stats));

            Assert.AreEqual(JsonUtility.ToJson(stats), client.Posted[0]);
            Assert.AreEqual(0, _queue.Count);
        }

        // 잘못된 통계 하나는 rejected/로 옮기고, 뒤의 통계는 그대로 보낸다.
        [Test]
        public void RejectedIsMovedAsideAndNextIsSent()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            _queue.Enqueue(AnalyticsQueueTests.Stats("b"));
            var client = new FakeClient(Answer(422, JsonSamples.Load(JsonSamples.ErrorResponse)), Answer(201));

            LogAssert.Expect(LogType.Error, new Regex("rejected/로 옮겼다 - HTTP 422 INVALID_FIELD \\(.*-a\\.json\\)\n\\{"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(2, client.Posted.Count);
            Assert.AreEqual(0, _queue.Count);
            Assert.AreEqual(1, Directory.GetFiles(_queue.RejectedDirectory).Length);
        }

        // 에러 본문이 우리 형식이 아니어도 옮기고, 상태 코드와 본문 원문을 남긴다.
        [Test]
        public void RejectedWithUnreadableBodyIsMovedAside()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new FakeClient(Answer(400, "<html>Bad Request</html>"));

            LogAssert.Expect(LogType.Error, new Regex("rejected/로 옮겼다 - HTTP 400 \\(.*\\)\n<html>"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(0, _queue.Count);
            Assert.AreEqual(1, Directory.GetFiles(_queue.RejectedDirectory).Length);
        }

        // 환경의 문제면 큐에 두고 멈춘다. 뒤의 통계도 보내지 않고, 아무것도 옮기지 않는다.
        // 로그 수준: 기다리면 풀리는 것은 Warning, 설정을 고쳐야 하는 것은 Error.
        [TestCase(408, LogType.Warning)]
        [TestCase(425, LogType.Warning)]
        [TestCase(429, LogType.Warning)]
        [TestCase(503, LogType.Warning)]
        [TestCase(401, LogType.Error)]
        [TestCase(404, LogType.Error)]
        [TestCase(415, LogType.Error)]
        [TestCase(302, LogType.Error)]
        public void HaltedKeepsEveryItem(int statusCode, LogType logType)
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            _queue.Enqueue(AnalyticsQueueTests.Stats("b"));
            var client = new FakeClient(Answer(statusCode));

            LogAssert.Expect(logType, new Regex($"큐에 두었다 - HTTP {statusCode} \\("));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(1, client.Posted.Count);
            Assert.AreEqual(2, _queue.Count);
            Assert.IsFalse(Directory.Exists(_queue.RejectedDirectory));
        }

        // 닿지 않으면(오프라인) 이유를 Log로 남기고 큐에 둔다.
        [Test]
        public void NetworkErrorKeepsItem()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new FakeClient(AnalyticsResponse.Network("Cannot connect to destination host"));

            LogAssert.Expect(LogType.Log, new Regex("큐에 두었다 - 닿지 않음: Cannot connect"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(1, _queue.Count);
        }

        // 보내는 중에 다시 부르면 진행 중인 보내기를 돌려준다. 그 사이에 넣은 통계도 그 보내기가 이어서 보낸다.
        [Test]
        public void CallDuringFlushSharesIt()
        {
            // Unity의 동기화 컨텍스트에서는 응답 뒤의 코드가 다음 업데이트로 미뤄질 수 있다. 응답을 주는 즉시 이어지게 비운다.
            SynchronizationContext unityContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
                var client = new PendingClient();
                var sender = new AnalyticsSender(_queue, client);

                Task first = sender.FlushAsync();
                Task second = sender.SendAsync(AnalyticsQueueTests.Stats("b"));

                Assert.AreSame(first, second);
                Assert.IsFalse(first.IsCompleted);

                client.Respond(Answer(201));
                Assert.AreEqual(2, client.Posted.Count, "a를 보낸 뒤 이어서 b를 보낸다.");

                client.Respond(Answer(201));
                RunToEnd(first);

                Assert.AreEqual(0, _queue.Count);
                Assert.AreNotSame(first, sender.FlushAsync(), "끝난 뒤 부르면 새로 보낸다.");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(unityContext);
            }
        }

        // 예상하지 못한 예외는 로그로 남기고, 통계는 큐에 둔다. 예외를 던지지 않는다.
        [Test]
        public void UnexpectedExceptionIsLoggedAndItemKept()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new ThrowingClient(new InvalidOperationException("잘못된 주소"));

            LogAssert.Expect(LogType.Error, new Regex("보내다 멈췄다: System.InvalidOperationException: 잘못된 주소"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(1, _queue.Count);
        }

        // 큐에 넣지 못해도 쌓여 있던 통계는 보낸다.
        [Test]
        public void EnqueueFailureStillFlushes()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new FakeClient(Answer(201));

            LogAssert.Expect(LogType.Error, new Regex("큐에 넣지 못했다"));
            RunToEnd(new AnalyticsSender(_queue, client).SendAsync(null));

            Assert.AreEqual(1, client.Posted.Count);
            Assert.AreEqual(0, _queue.Count);
        }

        private static AnalyticsResponse Answer(long statusCode, string body = "") =>
            AnalyticsResponse.FromHttp(statusCode, body);

        // 가짜 클라이언트가 바로 답하면 보내기도 바로 끝난다.
        private static void RunToEnd(Task task)
        {
            Assert.IsTrue(task.IsCompleted, "가짜 클라이언트로는 보내기가 바로 끝나야 한다.");
            task.GetAwaiter().GetResult();
        }

        // 정해 둔 응답을 차례로 바로 돌려준다.
        private sealed class FakeClient : IAnalyticsClient
        {
            private readonly Queue<AnalyticsResponse> _responses;

            public List<string> Posted { get; } = new();

            public FakeClient(params AnalyticsResponse[] responses)
            {
                _responses = new Queue<AnalyticsResponse>(responses);
            }

            public Task<AnalyticsResponse> PostBattleStatsAsync(string json)
            {
                Posted.Add(json);
                return Task.FromResult(_responses.Dequeue());
            }
        }

        // Respond를 부를 때까지 답하지 않는다.
        private sealed class PendingClient : IAnalyticsClient
        {
            private TaskCompletionSource<AnalyticsResponse> _pending;

            public List<string> Posted { get; } = new();

            public Task<AnalyticsResponse> PostBattleStatsAsync(string json)
            {
                Posted.Add(json);
                _pending = new TaskCompletionSource<AnalyticsResponse>();
                return _pending.Task;
            }

            public void Respond(AnalyticsResponse response) => _pending.SetResult(response);
        }

        private sealed class ThrowingClient : IAnalyticsClient
        {
            private readonly Exception _error;

            public ThrowingClient(Exception error)
            {
                _error = error;
            }

            public Task<AnalyticsResponse> PostBattleStatsAsync(string json) => throw _error;
        }
    }
}
