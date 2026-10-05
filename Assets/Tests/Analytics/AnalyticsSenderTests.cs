using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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

        [TestCase(200, AnalyticsSender.Outcome.Sent)]
        [TestCase(201, AnalyticsSender.Outcome.Sent)]
        [TestCase(400, AnalyticsSender.Outcome.Rejected)]
        [TestCase(413, AnalyticsSender.Outcome.Rejected)]
        [TestCase(422, AnalyticsSender.Outcome.Rejected)]
        [TestCase(0, AnalyticsSender.Outcome.Retry)]
        [TestCase(401, AnalyticsSender.Outcome.Retry)]
        [TestCase(404, AnalyticsSender.Outcome.Retry)]
        [TestCase(408, AnalyticsSender.Outcome.Retry)]
        [TestCase(429, AnalyticsSender.Outcome.Retry)]
        [TestCase(500, AnalyticsSender.Outcome.Retry)]
        [TestCase(503, AnalyticsSender.Outcome.Retry)]
        public void ClassifiesStatusCode(int statusCode, AnalyticsSender.Outcome expected)
        {
            Assert.AreEqual(expected, AnalyticsSender.Classify(statusCode));
        }

        // 처음 받은 판(201)도, 이미 받은 판(200)도 보낸 것이다.
        [Test]
        public void SentAndAlreadyReceivedAreRemoved()
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

        // 요청 내용 탓이면 에러를 남기고 버린다. 에러 본문을 읽어 로그에 남긴다.
        [Test]
        public void RejectedIsDroppedWithError()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new FakeClient(Answer(400, JsonSamples.Load(JsonSamples.ErrorResponse)));

            LogAssert.Expect(LogType.Error, new Regex("버렸다\\(400 INVALID_FIELD: .*startGrowthStage: "));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(0, _queue.Count);
        }

        // 에러 본문이 JSON이 아니어도 버리고 코드만 남긴다.
        [Test]
        public void RejectedWithUnreadableBodyIsDropped()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new FakeClient(Answer(400, "<html>Bad Request</html>"));

            LogAssert.Expect(LogType.Error, new Regex("버렸다\\(400\\)"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(0, _queue.Count);
        }

        // 다시 보내야 하면 큐에 두고 멈춘다. 뒤의 것도 보내지 않는다.
        [Test]
        public void RetryKeepsItemsAndStops()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            _queue.Enqueue(AnalyticsQueueTests.Stats("b"));
            var client = new FakeClient(Answer(503));

            LogAssert.Expect(LogType.Warning, new Regex("큐에 두었다\\(503\\)"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(1, client.Posted.Count);
            Assert.AreEqual(2, _queue.Count);
        }

        // 응답이 없으면(연결 실패) 이유를 남기고 큐에 둔다.
        [Test]
        public void NoResponseKeepsItem()
        {
            _queue.Enqueue(AnalyticsQueueTests.Stats("a"));
            var client = new FakeClient(new AnalyticsResponse(0, null, "Cannot connect to destination host"));

            LogAssert.Expect(LogType.Warning, new Regex("응답 없음: Cannot connect"));
            RunToEnd(new AnalyticsSender(_queue, client).FlushAsync());

            Assert.AreEqual(1, _queue.Count);
        }

        private static AnalyticsResponse Answer(long statusCode, string body = "") =>
            new AnalyticsResponse(statusCode, body, null);

        // 가짜 클라이언트는 바로 답하므로 보내기도 바로 끝난다.
        private static void RunToEnd(Task task)
        {
            Assert.IsTrue(task.IsCompleted, "가짜 클라이언트로는 보내기가 바로 끝나야 한다.");
            task.GetAwaiter().GetResult();
        }

        // 정해 둔 응답을 차례로 돌려준다.
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
    }
}
