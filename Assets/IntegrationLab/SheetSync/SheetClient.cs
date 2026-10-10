using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationLab
{
    // 웹 앱 응답 하나. Ok가 아니면 Error(와 칸별 Errors)에 이유가 있다.
    internal sealed class SheetReply
    {
        public bool Ok;
        public string Error;
        public readonly List<string> Errors = new List<string>();
        public JsonObject Body;
        // read: 레포 CSV 이름 → CSV 글.
        public readonly Dictionary<string, string> Tabs = new Dictionary<string, string>(StringComparer.Ordinal);

        public static SheetReply Fail(string error) => new SheetReply { Error = error };

        public string Describe() => Ok ? "성공" : Errors.Count > 0 ? $"{Error}\n{string.Join("\n", Errors)}" : Error;
    }

    // 기획 시트 웹 앱(Docs/BalanceLoop/sheet-sync/BlackholeSheetSync.gs)과 주고받는다. 에디터 전용이다(M5, 개발 빌드에 네트워크·토큰 코드를 넣지 않는다).
    // - 요청은 모두 POST(JSON). Apps Script는 응답을 다른 주소로 넘기므로(302) 넘김을 직접 따라간다(POST → GET).
    // - 웹 앱이 없고 탭 CSV 주소만 있으면 읽기만 GET으로 한다.
    // - 비동기다. 부르는 쪽(에디터)은 Task가 끝났는지 매 프레임 보고 결과를 메인 스레드에서 쓴다.
    internal sealed class SheetClient
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private const int MaxRedirects = 5;
        private const int SnippetLength = 160;

        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout };

        private readonly SheetSyncConfig _config;

        public SheetClient(SheetSyncConfig config) => _config = config;

        public Task<SheetReply> Ping() =>
            _config.HasEndpoint
                ? PostJson(_config.Endpoint, new JsonObject { { "token", _config.Token }, { "action", "ping" } })
                : Task.FromResult(SheetReply.Fail("웹 앱 주소와 토큰이 없다."));

        // 탭 4개를 읽는다. 성공하면 Tabs에 레포 CSV 이름 → 글이 모두 있다. 끝의 빈 행은 뺀다(SheetDiff.TrimEmptyRows).
        public async Task<SheetReply> Read()
        {
            if (!_config.HasEndpoint)
                return await ReadCsvUrls().ConfigureAwait(false);

            var tabs = new List<object>();
            foreach (string name in SheetTabs.Names)
                tabs.Add(_config.TabOf(name));

            SheetReply reply = await PostJson(_config.Endpoint, new JsonObject
            {
                { "token", _config.Token },
                { "action", "read" },
                { "tabs", tabs },
            }).ConfigureAwait(false);

            if (!reply.Ok)
                return reply;

            JsonObject read = reply.Body.Object("tabs");

            foreach (string name in SheetTabs.Names)
            {
                if (read?[_config.TabOf(name)] is string csv)
                    reply.Tabs[name] = SheetDiff.TrimEmptyRows(csv);
                else
                    return SheetReply.Fail($"응답에 탭 '{_config.TabOf(name)}'이 없다.");
            }

            return reply;
        }

        // 칸들을 쓴다. dryRun이면 시트가 검사만 한다.
        public Task<SheetReply> Write(IEnumerable<SheetUpdate> updates, bool dryRun)
        {
            if (!_config.HasEndpoint)
                return Task.FromResult(SheetReply.Fail("시트에 쓰려면 웹 앱 주소와 토큰이 필요하다."));

            var list = new List<object>();
            foreach (SheetUpdate update in updates)
            {
                JsonObject request = update.ToRequest();
                request.Add("tab", _config.TabOf(update.Tab));
                list.Add(request);
            }

            return PostJson(_config.Endpoint, new JsonObject
            {
                { "token", _config.Token },
                { "action", "write" },
                { "dryRun", dryRun },
                { "updates", list },
            });
        }

        private async Task<SheetReply> ReadCsvUrls()
        {
            var reply = new SheetReply { Ok = true };

            foreach (string name in SheetTabs.Names)
            {
                if (!_config.Csv.TryGetValue(name, out string url))
                    return SheetReply.Fail($"탭 '{name}'의 CSV 주소가 없다(웹 앱도 없다).");

                (string text, string error) = await Send(HttpMethod.Get, url, null).ConfigureAwait(false);

                if (error != null)
                    return SheetReply.Fail($"{name}: {error}");

                if (text.TrimStart().StartsWith("<", StringComparison.Ordinal))
                    return SheetReply.Fail($"{name}: CSV가 아니라 웹 페이지가 왔다. 공개 범위나 주소를 본다. 앞부분: {Snippet(text)}");

                reply.Tabs[name] = SheetDiff.TrimEmptyRows(text);
            }

            return reply;
        }

        // JSON을 POST하고 JSON 응답을 읽는다.
        internal static async Task<SheetReply> PostJson(string url, JsonObject body)
        {
            (string text, string error) = await Send(HttpMethod.Post, url, PlaytestJson.Write(body)).ConfigureAwait(false);

            if (error != null)
                return SheetReply.Fail(error);

            JsonObject obj;

            try
            {
                obj = text.TrimStart().StartsWith("{", StringComparison.Ordinal) ? PlaytestJson.Parse(text) as JsonObject : null;
            }
            catch (FormatException)
            {
                obj = null;
            }

            if (obj == null)
                return SheetReply.Fail("JSON이 아닌 응답이 왔다. 웹 앱을 액세스 '모든 사용자'로 배포했는지, 주소가 /exec로 끝나는지 본다. 앞부분: " + Snippet(text));

            var reply = new SheetReply
            {
                Ok = obj["ok"] is bool ok && ok,
                Error = obj.Text("error"),
                Body = obj,
            };

            foreach (object item in obj.Array("errors") ?? new List<object>())
            {
                if (item is string message)
                    reply.Errors.Add(message);
            }

            if (!reply.Ok && reply.Error == null)
                reply.Error = "웹 앱이 실패를 알렸다(이유 없음).";

            return reply;
        }

        // 요청 하나를 보내고 넘김(3xx)을 따라간다. 302·303은 GET으로, 307·308은 같은 방법으로 다시 보낸다.
        internal static async Task<(string Text, string Error)> Send(HttpMethod method, string url, string json)
        {
            try
            {
                for (int hop = 0; hop <= MaxRedirects; hop++)
                {
                    using (var request = new HttpRequestMessage(method, url))
                    {
                        if (json != null && method == HttpMethod.Post)
                            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                        using (HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false))
                        {
                            int code = (int)response.StatusCode;

                            if (code >= 300 && code < 400 && response.Headers.Location != null)
                            {
                                Uri next = response.Headers.Location.IsAbsoluteUri
                                    ? response.Headers.Location
                                    : new Uri(new Uri(url), response.Headers.Location);
                                url = next.ToString();

                                if (code != (int)HttpStatusCode.TemporaryRedirect && code != 308)
                                {
                                    method = HttpMethod.Get;
                                    json = null;
                                }

                                continue;
                            }

                            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                            return response.IsSuccessStatusCode
                                ? (text, null)
                                : (null, $"HTTP {code}: {Snippet(text)}");
                        }
                    }
                }

                return (null, $"넘김이 {MaxRedirects}번을 넘었다.");
            }
            catch (TaskCanceledException)
            {
                return (null, $"시간 초과({Timeout.TotalSeconds:0}초).");
            }
            catch (HttpRequestException exception)
            {
                return (null, "연결하지 못했다: " + (exception.InnerException?.Message ?? exception.Message));
            }
            catch (UriFormatException exception)
            {
                return (null, "주소가 틀렸다: " + exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return (null, "주소가 틀렸다: " + exception.Message);
            }
        }

        private static string Snippet(string text)
        {
            string flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length > SnippetLength ? flat.Substring(0, SnippetLength) + "…" : flat;
        }
    }
}
