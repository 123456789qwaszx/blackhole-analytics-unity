using System;
using System.IO;
using System.Text.RegularExpressions;
using BlackHole.Analytics.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlackHole.Analytics.Tests
{
    public sealed class AnalyticsQueueTests
    {
        private string _directory;

        [SetUp]
        public void CreateDirectory() =>
            _directory = Path.Combine(Path.GetTempPath(), "analytics-queue-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        // 넣은 순서대로 나온다. 같은 순간에 넣어도 그렇다 — battleId 이름 순서와 반대로 넣는다.
        [Test]
        public void ItemsComeOutInEnqueueOrder()
        {
            var queue = new AnalyticsQueue(_directory, 10);
            queue.Enqueue(Summary("b"));
            queue.Enqueue(Summary("a"));

            Assert.AreEqual("b", BattleIdOf(Dequeue(queue)));
            Assert.AreEqual("a", BattleIdOf(Dequeue(queue)));
            Assert.AreEqual(0, queue.Count);
        }

        // 보낼 JSON을 그대로 둔다.
        [Test]
        public void ItemKeepsJsonAsWritten()
        {
            var queue = new AnalyticsQueue(_directory, 10);
            BattleSummaryDto summary = Summary("a");
            queue.Enqueue(summary);

            Assert.IsTrue(queue.TryPeek(out AnalyticsQueue.Item item));
            Assert.AreEqual(JsonUtility.ToJson(summary), item.Json);
        }

        // 앱을 다시 켜도(새 큐) 남아 있다. 쓰다 만 파일은 버린다.
        [Test]
        public void ItemsSurviveRestartAndPartialFilesAreDropped()
        {
            new AnalyticsQueue(_directory, 10).Enqueue(Summary("a"));
            File.WriteAllText(Path.Combine(_directory, "20260101000000000000000-x.json.tmp"), "{");

            var reopened = new AnalyticsQueue(_directory, 10);

            Assert.AreEqual(1, reopened.Count);
            Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
        }

        // 가득 차면 가장 오래된 것부터 버린다.
        [Test]
        public void OldestIsDroppedWhenFull()
        {
            var queue = new AnalyticsQueue(_directory, 2);
            queue.Enqueue(Summary("1"));
            queue.Enqueue(Summary("2"));

            LogAssert.Expect(LogType.Warning, new Regex("가장 오래된 통계를 버렸다: .*-1\\.json"));
            queue.Enqueue(Summary("3"));

            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual("2", BattleIdOf(Dequeue(queue)));
        }

        // rejected/로 옮긴 통계는 다시 꺼내지 않는다.
        [Test]
        public void RejectedItemIsMovedAside()
        {
            var queue = new AnalyticsQueue(_directory, 10);
            queue.Enqueue(Summary("a"));
            queue.Enqueue(Summary("b"));

            Assert.IsTrue(queue.TryPeek(out AnalyticsQueue.Item rejected));
            queue.Reject(rejected);

            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual("b", BattleIdOf(Dequeue(queue)));
            Assert.IsTrue(File.Exists(Path.Combine(queue.RejectedDirectory, rejected.Name)));
        }

        // rejected/도 가득 차면 가장 오래된 것부터 버린다.
        [Test]
        public void OldestRejectedIsDroppedWhenFull()
        {
            var queue = new AnalyticsQueue(_directory, 1);
            queue.Enqueue(Summary("1"));
            queue.Reject(Peek(queue));
            queue.Enqueue(Summary("2"));

            LogAssert.Expect(LogType.Warning, new Regex("격리 폴더가 가득 차\\(1개\\) 가장 오래된 통계를 버렸다: .*-1\\.json"));
            queue.Reject(Peek(queue));

            string[] rejected = Directory.GetFiles(queue.RejectedDirectory);
            Assert.AreEqual(1, rejected.Length);
            StringAssert.EndsWith("-2.json", rejected[0]);
        }

        internal static BattleSummaryDto Summary(string battleId) =>
            new BattleSummaryDto { battleId = battleId };

        private static AnalyticsQueue.Item Peek(AnalyticsQueue queue)
        {
            Assert.IsTrue(queue.TryPeek(out AnalyticsQueue.Item item));
            return item;
        }

        private static AnalyticsQueue.Item Dequeue(AnalyticsQueue queue)
        {
            AnalyticsQueue.Item item = Peek(queue);
            queue.Remove(item);
            return item;
        }

        private static string BattleIdOf(AnalyticsQueue.Item item) =>
            JsonUtility.FromJson<BattleSummaryDto>(item.Json).battleId;
    }
}
