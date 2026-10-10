using System;
using System.Collections.Generic;

namespace IntegrationLab
{
    // 기획 시트 연동 설정(M5). <레포>/PlaytestData/sheet.json(git 무시, PC마다 따로)에 둔다(읽고 쓰기는 에디터 SheetSync). 토큰이 들어 있어 커밋하지 않는다.
    // - endpoint·token: 시트의 Apps Script 웹 앱(Docs/BalanceLoop/sheet-sync/BlackholeSheetSync.gs). 읽기와 쓰기를 모두 한다.
    // - csv: 웹 앱이 없을 때의 읽기 전용 대체. 탭 이름 → CSV 주소("웹에 게시" CSV나 내보내기 주소).
    // - tabs: 레포 CSV 이름 → 시트 탭 이름(같으면 비워 둔다).
    // - autoPull·autoPullSeconds: 시트가 바뀌면 저절로 끌어온다(웹 앱이 있을 때만, Unity가 앞에 있을 때만 본다).
    internal sealed class SheetSyncConfig
    {
        public const string FileName = "sheet.json";
        public const int MinAutoPullSeconds = 10;
        public const int DefaultAutoPullSeconds = 30;

        public string Endpoint = string.Empty;
        public string Token = string.Empty;
        public readonly Dictionary<string, string> Csv = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Tabs = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool AutoPull;
        public int AutoPullSeconds = DefaultAutoPullSeconds;

        // 웹 앱으로 읽고 쓸 수 있다.
        public bool HasEndpoint => Endpoint.Length > 0 && Token.Length > 0;

        // 읽을 수 있다(웹 앱, 또는 탭 4개의 CSV 주소).
        public bool CanRead => HasEndpoint || Array.TrueForAll(SheetTabs.Names, tab => Csv.ContainsKey(tab));

        // 레포 CSV 이름 → 시트 탭 이름.
        public string TabOf(string name) => Tabs.TryGetValue(name, out string tab) && !string.IsNullOrEmpty(tab) ? tab : name;

        public static SheetSyncConfig Parse(string json, out string error)
        {
            var config = new SheetSyncConfig();
            error = null;
            JsonObject obj;

            try
            {
                obj = PlaytestJson.Parse(json) as JsonObject;
            }
            catch (FormatException exception)
            {
                error = $"{FileName}을 읽지 못했다: {exception.Message}";
                return config;
            }

            if (obj == null)
            {
                error = $"{FileName}이 JSON 객체가 아니다.";
                return config;
            }

            config.Endpoint = (obj.Text("endpoint") ?? string.Empty).Trim();
            config.Token = (obj.Text("token") ?? string.Empty).Trim();
            config.AutoPull = obj["autoPull"] is bool auto && auto;
            config.AutoPullSeconds = Math.Max(MinAutoPullSeconds, obj.Int("autoPullSeconds") ?? DefaultAutoPullSeconds);
            ReadMap(obj.Object("csv"), config.Csv);
            ReadMap(obj.Object("tabs"), config.Tabs);
            return config;
        }

        public string ToJsonText()
        {
            var csv = new JsonObject();
            foreach (KeyValuePair<string, string> pair in Csv)
                csv.Add(pair.Key, pair.Value);

            var tabs = new JsonObject();
            foreach (KeyValuePair<string, string> pair in Tabs)
                tabs.Add(pair.Key, pair.Value);

            return PlaytestJson.Write(new JsonObject
            {
                { "endpoint", Endpoint },
                { "token", Token },
                { "csv", csv },
                { "tabs", tabs },
                { "autoPull", AutoPull },
                { "autoPullSeconds", AutoPullSeconds },
            }, true) + "\n";
        }

        private static void ReadMap(JsonObject from, Dictionary<string, string> into)
        {
            if (from == null)
                return;

            foreach (KeyValuePair<string, object> pair in from)
            {
                if (pair.Value is string text && text.Trim().Length > 0)
                    into[pair.Key] = text.Trim();
            }
        }
    }
}
