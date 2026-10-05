using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BlackHole.Analytics.Transport
{
    // 보낼 통계를 모아 두는 곳. 통계 하나를 파일 하나로 두어, 앱이 꺼져도 남고 다음 실행 때 다시 보낸다.
    // 파일에는 보낼 JSON을 그대로 적는다 — 앱이 업데이트돼도 그때 만든 요청을 그대로 보낸다.
    // 파일 이름은 넣은 시각(UTC)으로 시작해서, 이름 순서가 곧 넣은 순서다.
    // 서버가 잘못됐다고 한 통계는 지우지 않고 rejected/로 옮겨 둔다. 다시 꺼내지 않지만, 나중에 살펴보거나 다시 보낼 수 있다.
    public sealed class AnalyticsQueue
    {
        private const string Extension = ".json";
        private const string TempExtension = ".tmp";
        private const string RejectedFolder = "rejected";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly string _directory;
        private readonly int _capacity;
        // 이번 실행에서 마지막으로 넣은 시각. 같은 순간에 둘을 넣어도 이름 순서가 넣은 순서와 같게 한다.
        private long _lastTicks;

        // 큐와 rejected/ 각각 이 개수까지 둔다.
        public AnalyticsQueue(string directory, int capacity)
        {
            _directory = directory;
            _capacity = capacity;
            RejectedDirectory = Path.Combine(directory, RejectedFolder);
            Directory.CreateDirectory(directory);

            // 쓰다 만 파일(쓰는 도중 앱이 꺼졌다)은 버린다. 보낼 파일은 다 쓴 뒤에만 이름을 바꿔 넣는다.
            foreach (string temp in Directory.GetFiles(directory, "*" + TempExtension))
                File.Delete(temp);
        }

        public string RejectedDirectory { get; }

        public int Count => Files(_directory).Length;

        public void Enqueue(BattleSummaryDto summary)
        {
            _lastTicks = Math.Max(DateTime.UtcNow.Ticks, _lastTicks + 1);
            string stamp = new DateTime(_lastTicks, DateTimeKind.Utc).ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
            string path = Path.Combine(_directory, $"{stamp}-{summary.battleId}{Extension}");
            string temp = path + TempExtension;

            File.WriteAllText(temp, JsonUtility.ToJson(summary), Utf8);
            File.Move(temp, path);

            DropOverflow(_directory, "큐");
        }

        // 가장 먼저 넣은 통계. 비었으면 false.
        public bool TryPeek(out Item item)
        {
            string[] files = Files(_directory);

            if (files.Length == 0)
            {
                item = null;
                return false;
            }

            item = new Item(files[0], File.ReadAllText(files[0], Utf8));
            return true;
        }

        public void Remove(Item item) => File.Delete(item.FilePath);

        // 큐에서 빼서 rejected/로 옮긴다.
        public void Reject(Item item)
        {
            Directory.CreateDirectory(RejectedDirectory);
            File.Move(item.FilePath, Path.Combine(RejectedDirectory, item.Name));

            DropOverflow(RejectedDirectory, "격리 폴더");
        }

        // 가득 차면 가장 오래된 것부터 버린다. 오래 오프라인이어도 저장 공간을 끝없이 쓰지 않는다.
        private void DropOverflow(string directory, string what)
        {
            string[] files = Files(directory);

            for (int i = 0; i < files.Length - _capacity; i++)
            {
                File.Delete(files[i]);
                Debug.LogWarning($"[통계] {what}가 가득 차({_capacity}개) 가장 오래된 통계를 버렸다: {Path.GetFileName(files[i])}");
            }
        }

        private static string[] Files(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*" + Extension);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        // 큐에 든 통계 하나: 파일과 보낼 JSON.
        public sealed class Item
        {
            public string FilePath { get; }
            public string Json { get; }
            public string Name => Path.GetFileName(FilePath);

            internal Item(string filePath, string json)
            {
                FilePath = filePath;
                Json = json;
            }
        }
    }
}
