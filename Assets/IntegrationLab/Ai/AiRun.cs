using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IntegrationLab
{
    // 자동 루프(M6): 에디터가 이 PC의 Claude Code CLI(claude -p)를 백그라운드로 돌려 AI 초안을 쓰게 한다.
    // 여기는 Unity에 기대지 않는 부분이다: 설정, 명령줄, 실행 스크립트, 지시문, 결과 읽기, 판정, 규칙 검사, 기록.
    // 프로세스를 띄우고 지켜보는 일과 창은 에디터 AiRunner가 맡는다. 흐름과 파일: Docs/AiIntegration.md

    // 설정: <레포>/LabData/ai.json(git 무시, PC마다 따로). 없으면 기본값이다.
    internal sealed class AiRunSettings
    {
        public const string FileName = "ai.json";
        public const int DefaultMaxTurns = 30;
        public const double DefaultMaxBudgetUsd = 2;
        public const int DefaultTimeoutSeconds = 600;
        public const int MinTimeoutSeconds = 60;
        public const int MaxTimeoutSeconds = 3600;
        private static readonly Regex ModelPattern = new Regex("^[A-Za-z0-9._:\\[\\]-]*$");

        // Claude Code 실행 파일(전체 경로나 PATH의 이름). 비우면 찾는다(AiRun.Resolve).
        public string Command = string.Empty;
        // 메모를 저장하면 자동으로 맡긴다.
        public bool AutoOnNote;
        // 비우면 Claude Code 기본 모델.
        public string Model = string.Empty;
        public int MaxTurns = DefaultMaxTurns;
        public double MaxBudgetUsd = DefaultMaxBudgetUsd;
        public int TimeoutSeconds = DefaultTimeoutSeconds;
        // 초안이 검사를 통과하지 못하면 오류를 붙여 한 번 더 한다.
        public bool Retry = true;

        public static AiRunSettings Parse(string json, out string error)
        {
            var settings = new AiRunSettings();
            error = null;
            JsonObject obj;

            try
            {
                obj = PlaytestJson.Parse(json) as JsonObject;
            }
            catch (FormatException exception)
            {
                error = $"{FileName}을 읽지 못했다: {exception.Message}";
                return settings;
            }

            if (obj == null)
            {
                error = $"{FileName}이 JSON 객체가 아니다.";
                return settings;
            }

            settings.Command = Unquote((obj.Text("command") ?? string.Empty).Trim());
            settings.AutoOnNote = obj["autoOnNote"] is bool auto && auto;
            settings.Retry = !(obj["retry"] is bool retry) || retry;
            settings.MaxTurns = Math.Max(1, Math.Min(200, obj.Int("maxTurns") ?? DefaultMaxTurns));
            settings.TimeoutSeconds = Math.Max(MinTimeoutSeconds, Math.Min(MaxTimeoutSeconds, obj.Int("timeoutSeconds") ?? DefaultTimeoutSeconds));

            double budget = obj.Number("maxBudgetUsd") ?? DefaultMaxBudgetUsd;
            settings.MaxBudgetUsd = budget > 0 ? Math.Min(50, budget) : DefaultMaxBudgetUsd;

            string model = (obj.Text("model") ?? string.Empty).Trim();

            if (ModelPattern.IsMatch(model))
                settings.Model = model;
            else
                error = $"{FileName}의 model에 쓸 수 없는 글자가 있어 기본 모델을 쓴다: '{model}'";

            if (settings.Command.Length > 0 && !AiRun.IsCmdSafe(settings.Command))
            {
                error = $"{FileName}의 command에 쓸 수 없는 글자(\" % 줄바꿈)가 있어 무시한다.";
                settings.Command = string.Empty;
            }

            return settings;
        }

        public string ToJsonText() => PlaytestJson.Write(new JsonObject
        {
            { "command", Command },
            { "autoOnNote", AutoOnNote },
            { "model", Model },
            { "maxTurns", MaxTurns },
            { "maxBudgetUsd", MaxBudgetUsd },
            { "timeoutSeconds", TimeoutSeconds },
            { "retry", Retry },
        }, true) + "\n";

        private static string Unquote(string text) =>
            text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"' ? text.Substring(1, text.Length - 2).Trim() : text;
    }

    // 실행 하나(시도 하나). 시작할 때 실행 폴더의 run.json에 쓰고, 에디터가 다시 켜지면 여기서 이어 본다.
    internal sealed class AiRunRequest
    {
        public const int Schema = 1;

        public string Id;
        public DateTime AtUtc;
        // note(메모 저장) | button(창의 버튼) | retry(앞 시도 다시 하기)
        public string Trigger;
        public string SetupKey;
        // 이번에 볼 메모. 버튼으로 맡겼고 메모가 없으면 null.
        public string NoteId;
        public int Attempt = 1;
        public string RetryOf;
        public List<string> PreviousErrors = new List<string>();
        // Claude Code 실행 파일.
        public string Command;
        // 실행 스크립트를 돌리는 셸(cmd.exe·sh)의 프로세스 ID.
        public int Pid;
        // 시작 전 초안의 해시. 초안이 없었으면 null.
        public string DraftHashBefore;

        public JsonObject ToJson() => new JsonObject
        {
            { "schema", Schema },
            { "id", Id },
            { "atUtc", AiRun.Utc(AtUtc) },
            { "trigger", Trigger },
            { "setupKey", SetupKey },
            { "noteId", NoteId },
            { "attempt", Attempt },
            { "retryOf", RetryOf },
            { "previousErrors", new List<object>(PreviousErrors) },
            { "command", Command },
            { "pid", Pid },
            { "draftHashBefore", DraftHashBefore },
        };

        public static AiRunRequest FromJson(JsonObject obj)
        {
            if (obj == null || obj.Int("schema") != Schema || string.IsNullOrEmpty(obj.Text("id")))
                return null;

            var run = new AiRunRequest
            {
                Id = obj.Text("id"),
                AtUtc = AiRun.ParseUtc(obj.Text("atUtc")),
                Trigger = obj.Text("trigger"),
                SetupKey = obj.Text("setupKey"),
                NoteId = obj.Text("noteId"),
                Attempt = obj.Int("attempt") ?? 1,
                RetryOf = obj.Text("retryOf"),
                Command = obj.Text("command"),
                Pid = obj.Int("pid") ?? 0,
                DraftHashBefore = obj.Text("draftHashBefore"),
            };
            AiRun.AddTexts(obj.Array("previousErrors"), run.PreviousErrors);
            return run;
        }
    }

    // Claude Code의 --output-format json 결과(out.json).
    internal sealed class AiRunOutput
    {
        public bool Parsed;
        public string Result;
        public bool IsError;
        public string Subtype;
        public double? CostUsd;
        public int? Turns;
        public long? DurationMs;
        // 권한 규칙에 막힌 시도(도구와 파일). 지시를 어기려 했는지 사람이 보게 한다.
        public List<string> Denials = new List<string>();

        public static AiRunOutput Parse(string text)
        {
            var output = new AiRunOutput();

            if (string.IsNullOrWhiteSpace(text))
                return output;

            JsonObject obj = ResultOf(TryParse(text.Trim()));

            // 앞뒤에 다른 줄이 섞였으면 '{'로 시작하는 마지막 줄을 본다.
            if (obj == null)
            {
                string[] lines = text.Replace("\r\n", "\n").Split('\n');

                for (int i = lines.Length - 1; i >= 0 && obj == null; i--)
                {
                    if (lines[i].TrimStart().StartsWith("{", StringComparison.Ordinal))
                        obj = ResultOf(TryParse(lines[i].Trim()));
                }
            }

            if (obj == null)
                return output;

            output.Parsed = true;
            output.Result = obj.Text("result");
            output.IsError = obj["is_error"] is bool isError && isError;
            output.Subtype = obj.Text("subtype");
            output.CostUsd = obj.Number("total_cost_usd");
            output.Turns = obj.Int("num_turns");
            double? duration = obj.Number("duration_ms");
            output.DurationMs = duration.HasValue ? (long)Math.Round(duration.Value) : (long?)null;

            foreach (object item in obj.Array("permission_denials") ?? new List<object>())
            {
                if (!(item is JsonObject denial))
                    continue;

                JsonObject input = denial.Object("tool_input");
                string target = input?.Text("file_path") ?? input?.Text("path") ?? input?.Text("command") ?? input?.Text("pattern");
                output.Denials.Add((denial.Text("tool_name") ?? "?") + (target != null ? " " + RepoRelative(target) : string.Empty));
            }

            return output;
        }

        // 레포 안의 절대 경로를 레포 기준으로 줄인다(아는 맨 위 폴더부터).
        private static string RepoRelative(string path)
        {
            string normal = path.Replace('\\', '/');

            foreach (string top in new[] { "/Assets/", "/Docs/", "/LabData/", "/Packages/", "/ProjectSettings/" })
            {
                int at = normal.IndexOf(top, StringComparison.Ordinal);

                if (at >= 0)
                    return normal.Substring(at + 1);
            }

            return normal;
        }

        private static object TryParse(string text)
        {
            try
            {
                return PlaytestJson.Parse(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // 결과 객체. 배열(메시지 목록)이면 type이 result인 마지막 것.
        private static JsonObject ResultOf(object value)
        {
            if (value is JsonObject obj)
                return obj.Text("type") == null || obj.Text("type") == "result" ? obj : null;

            if (value is List<object> list)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is JsonObject item && item.Text("type") == "result")
                        return item;
                }
            }

            return null;
        }
    }

    // 실행 기록 한 줄(schema 1). <레포>/LabData/ai-runs.ndjson에 덧붙이고, 실행 폴더의 done.json에도 쓴다.
    internal sealed class AiRunRecord
    {
        public const int Schema = 1;

        public string Id;
        public DateTime StartedAtUtc;
        public DateTime FinishedAtUtc;
        public string Trigger;
        public string SetupKey;
        public string NoteId;
        public int Attempt = 1;
        public string RetryOf;
        public string Command;
        public int? ExitCode;
        // AiRun.Ok | NoDraft | InvalidDraft | Error | Timeout | Stopped | Lost
        public string Status;
        // AI의 마지막 답(앞 몇 줄).
        public string Summary;
        public double? CostUsd;
        public int? Turns;
        public long? DurationMs;
        public bool DraftWritten;
        public bool DraftValid;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        public double Seconds => Math.Max(0, (FinishedAtUtc - StartedAtUtc).TotalSeconds);

        public JsonObject ToJson() => new JsonObject
        {
            { "schema", Schema },
            { "id", Id },
            { "startedAtUtc", AiRun.Utc(StartedAtUtc) },
            { "finishedAtUtc", AiRun.Utc(FinishedAtUtc) },
            { "trigger", Trigger },
            { "setupKey", SetupKey },
            { "noteId", NoteId },
            { "attempt", Attempt },
            { "retryOf", RetryOf },
            { "command", Command },
            { "exitCode", ExitCode },
            { "status", Status },
            { "summary", Summary },
            { "costUsd", CostUsd },
            { "turns", Turns },
            { "durationMs", DurationMs },
            { "draftWritten", DraftWritten },
            { "draftValid", DraftValid },
            { "errors", new List<object>(Errors) },
            { "warnings", new List<object>(Warnings) },
        };

        public static AiRunRecord FromJson(JsonObject obj)
        {
            if (obj == null || obj.Int("schema") != Schema)
                return null;

            double? duration = obj.Number("durationMs");
            var record = new AiRunRecord
            {
                Id = obj.Text("id"),
                StartedAtUtc = AiRun.ParseUtc(obj.Text("startedAtUtc")),
                FinishedAtUtc = AiRun.ParseUtc(obj.Text("finishedAtUtc")),
                Trigger = obj.Text("trigger"),
                SetupKey = obj.Text("setupKey"),
                NoteId = obj.Text("noteId"),
                Attempt = obj.Int("attempt") ?? 1,
                RetryOf = obj.Text("retryOf"),
                Command = obj.Text("command"),
                ExitCode = obj.Int("exitCode"),
                Status = obj.Text("status"),
                Summary = obj.Text("summary"),
                CostUsd = obj.Number("costUsd"),
                Turns = obj.Int("turns"),
                DurationMs = duration.HasValue ? (long)Math.Round(duration.Value) : (long?)null,
                DraftWritten = obj["draftWritten"] is bool written && written,
                DraftValid = obj["draftValid"] is bool valid && valid,
            };
            AiRun.AddTexts(obj.Array("errors"), record.Errors);
            AiRun.AddTexts(obj.Array("warnings"), record.Warnings);
            return record;
        }
    }

    internal static class AiRun
    {
        // 결과 상태
        public const string Ok = "ok";
        public const string NoDraft = "no-draft";
        public const string InvalidDraft = "invalid-draft";
        public const string Error = "error";
        public const string Timeout = "timeout";
        public const string Stopped = "stopped";
        public const string Lost = "lost";

        // 계기
        public const string ByNote = "note";
        public const string ByButton = "button";
        public const string ByRetry = "retry";

        // 레포 기준 경로(/). AI가 쓸 수 있는 파일은 이것 하나다.
        public const string DraftPath = "LabData/ai-draft.json";
        public const string DataFolderName = "LabData";
        public const string RunsFolderName = "ai-runs";
        public const string LogFileName = "ai-runs.ndjson";

        // 실행 폴더의 파일
        public const string PromptFile = "prompt.md";
        public const string WindowsScriptFile = "run.cmd";
        public const string ShellScriptFile = "run.sh";
        public const string StateFile = "run.json";
        public const string OutFile = "out.json";
        public const string ErrFile = "err.txt";
        public const string ExitFile = "exit.txt";
        public const string DoneFile = "done.json";
        public const string DraftBeforeFile = "draft-before.json";
        public const string DraftAfterFile = "draft-after.json";

        // 지침 규칙(AI-GUIDE 4절)
        public const int MaxPatches = 3;
        public const double MinRatio = 0.5;
        public const double MaxRatio = 2;
        public const int MaxAttempts = 2;

        // --restricted가 들어온 Claude Code 버전.
        public static readonly Version MinVersion = new Version(2, 1, 248);

        // 명령줄의 짧은 지시. 본문(prompt.md)은 표준 입력으로 넘긴다(명령줄에 한글·따옴표를 넣지 않는다).
        public const string Query = "Follow the instructions given on standard input exactly. Work without asking questions and write your final answer in Korean.";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string NewId(DateTime atUtc, Random random) =>
            $"r-{atUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{random.Next(0x10000):x4}";

        // 실행 폴더(레포 기준, /).
        public static string RunFolder(string id) => DataFolderName + "/" + RunsFolderName + "/" + id;

        public static string RunDir(string repoRoot, string id) =>
            Path.Combine(repoRoot, DataFolderName, RunsFolderName, id);

        public static string LogPath(string repoRoot) => Path.Combine(repoRoot, DataFolderName, LogFileName);

        #region 명령줄과 스크립트

        // Claude Code 인자. 첫 인자 뒤가 지시(위치 인자)라, 여러 값을 받는 옵션은 그 뒤에 둔다.
        public static List<string> Arguments(AiRunSettings settings)
        {
            var arguments = new List<string>
            {
                "-p", Query,
                "--output-format", "json",
                // 묻지 않고 거절한다. 미리 허락한 것(초안 쓰기)과 작업 폴더 안 읽기만 된다.
                "--permission-mode", "dontAsk",
                // 읽기·찾기·고치기·쓰기만. 명령 실행·웹은 없다.
                "--tools", "Read,Edit,Write,Glob,Grep",
                // 쓰기 허락은 초안 한 파일(Edit 규칙이 Write도 다룬다).
                "--allowedTools", "Edit(" + DraftPath + ")",
                // 사용자 설정에 넓은 허락이 있어도 거절이 먼저다. 시트 토큰과 .env는 읽지도 않는다.
                "--disallowedTools", "mcp__*", "Edit(Assets/Data/**)", "Edit(Assets/Scripts/**)", "Edit(Docs/**)", "Edit(LabData/context/**)",
                "Read(LabData/sheet.json)", "Read(**/.env)",
                // 사용자·프로젝트 설정(넓은 허락 규칙, 훅)을 읽지 않고, 명령 실행 도구를 빼고, 파일 도구를 작업 폴더 안으로 묶는다.
                "--restricted",
                "--strict-mcp-config",
                "--no-session-persistence",
                "--max-turns", settings.MaxTurns.ToString(CultureInfo.InvariantCulture),
                "--max-budget-usd", settings.MaxBudgetUsd.ToString("0.##", CultureInfo.InvariantCulture),
            };

            if (!string.IsNullOrEmpty(settings.Model))
            {
                arguments.Add("--model");
                arguments.Add(settings.Model);
            }

            return arguments;
        }

        // cmd 배치 파일의 따옴표 안에 그대로 넣을 수 있는가: 따옴표, %, 줄바꿈이 없고 역슬래시로 끝나지 않는다(\"가 따옴표로 읽힌다).
        public static bool IsCmdSafe(string text) =>
            text != null && text.IndexOf('"') < 0 && text.IndexOf('%') < 0 && text.IndexOf('\r') < 0 && text.IndexOf('\n') < 0 && !text.EndsWith("\\", StringComparison.Ordinal);

        public static string QuoteCmd(string text)
        {
            if (!IsCmdSafe(text))
                throw new ArgumentException($"배치 파일에 넣을 수 없는 값이다(\" % 줄바꿈, 끝의 역슬래시): {text}");

            return "\"" + text + "\"";
        }

        public static string QuoteSh(string text) => "'" + (text ?? string.Empty).Replace("'", "'\\''") + "'";

        // ProcessStartInfo.Arguments 한 칸(.NET·Mono가 Windows 규칙으로 나눈다): 따옴표로 싸고, 따옴표와 그 앞 역슬래시를 이스케이프한다.
        public static string QuoteArg(string text)
        {
            var quoted = new StringBuilder("\"");
            int slashes = 0;

            foreach (char c in text ?? string.Empty)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }

                quoted.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                quoted.Append(c);
            }

            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        // Windows: cmd.exe /d /s /c ""<run.cmd>""로 창 없이 돌린다. 손으로 두 번 눌러 돌려도 같다.
        // - call: claude.cmd(npm 설치)도 끝난 뒤 돌아온다.
        // - 표준 입력 = prompt.md, 출력 = out.json, 오류 = err.txt. 끝나면 종료 코드를 exit.txt에 쓴다(임시 파일을 옮겨 반쯤 쓴 파일을 읽지 않게).
        public static string WindowsScript(string repoRoot, string command, IReadOnlyList<string> arguments, string runFolder)
        {
            string root = repoRoot.Replace('/', '\\');
            string run = runFolder.Replace('/', '\\');
            var line = new StringBuilder("call ").Append(QuoteCmd(command));

            foreach (string argument in arguments)
                line.Append(' ').Append(QuoteCmd(argument));

            line.Append(" < ").Append(QuoteCmd(run + "\\" + PromptFile))
                .Append(" > ").Append(QuoteCmd(run + "\\" + OutFile))
                .Append(" 2> ").Append(QuoteCmd(run + "\\" + ErrFile));

            string exitTemp = QuoteCmd(run + "\\" + ExitFile + ".tmp");
            return string.Join("\r\n",
                "@echo off",
                "rem Feed-A-Blackhole M6: AI draft run written by the Unity editor. See Docs/AiIntegration.md",
                "chcp 65001 >nul",
                "cd /d " + QuoteCmd(root),
                line.ToString(),
                "> " + exitTemp + " echo %ERRORLEVEL%",
                "move /y " + exitTemp + " " + QuoteCmd(run + "\\" + ExitFile) + " >nul",
                string.Empty);
        }

        // macOS·Linux: /bin/sh run.sh.
        public static string ShellScript(string repoRoot, string command, IReadOnlyList<string> arguments, string runFolder)
        {
            var line = new StringBuilder(QuoteSh(command));

            foreach (string argument in arguments)
                line.Append(' ').Append(QuoteSh(argument));

            line.Append(" < ").Append(QuoteSh(runFolder + "/" + PromptFile))
                .Append(" > ").Append(QuoteSh(runFolder + "/" + OutFile))
                .Append(" 2> ").Append(QuoteSh(runFolder + "/" + ErrFile));

            string exitTemp = QuoteSh(runFolder + "/" + ExitFile + ".tmp");
            return string.Join("\n",
                "#!/bin/sh",
                "# Feed-A-Blackhole M6: AI draft run written by the Unity editor. See Docs/AiIntegration.md",
                "cd " + QuoteSh(repoRoot) + " || exit 1",
                line.ToString(),
                "echo $? > " + exitTemp,
                "mv -f " + exitTemp + " " + QuoteSh(runFolder + "/" + ExitFile),
                string.Empty);
        }

        #endregion

        #region 실행 파일 찾기

        // 찾아볼 곳(순서대로). env는 환경 변수 읽기(PATH, USERPROFILE, APPDATA, LOCALAPPDATA, HOME).
        public static List<string> Candidates(bool windows, Func<string, string> env)
        {
            var candidates = new List<string>();
            char separator = windows ? ';' : ':';
            string[] names = windows ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };

            foreach (string dir in (env("PATH") ?? string.Empty).Split(separator))
            {
                string trimmed = dir.Trim().Trim('"');

                if (trimmed.Length == 0)
                    continue;

                foreach (string name in names)
                    Add(candidates, Join(trimmed, name, windows));
            }

            if (windows)
            {
                AddUnder(candidates, env("USERPROFILE"), ".local\\bin\\claude.exe", true);
                AddUnder(candidates, env("APPDATA"), "npm\\claude.cmd", true);
                AddUnder(candidates, env("LOCALAPPDATA"), "Microsoft\\WinGet\\Links\\claude.exe", true);
            }
            else
            {
                AddUnder(candidates, env("HOME"), ".local/bin/claude", false);
                AddUnder(candidates, env("HOME"), ".claude/local/claude", false);
                Add(candidates, "/opt/homebrew/bin/claude");
                Add(candidates, "/usr/local/bin/claude");
                Add(candidates, "/usr/bin/claude");
            }

            return candidates;
        }

        // 실행 파일. configured가 경로면 그것만, 이름이면 PATH에서, 비었으면 Candidates에서 처음 있는 것. 못 찾으면 null.
        public static string Resolve(string configured, bool windows, Func<string, string> env, Func<string, bool> exists)
        {
            configured = (configured ?? string.Empty).Trim();

            if (configured.Length > 0 && (configured.IndexOf('/') >= 0 || configured.IndexOf('\\') >= 0))
                return exists(configured) ? configured : null;

            List<string> candidates = Candidates(windows, env);

            if (configured.Length > 0)
            {
                char separator = windows ? ';' : ':';
                candidates.Clear();

                foreach (string dir in (env("PATH") ?? string.Empty).Split(separator))
                {
                    string trimmed = dir.Trim().Trim('"');

                    if (trimmed.Length == 0)
                        continue;

                    Add(candidates, Join(trimmed, configured, windows));

                    if (windows && Path.GetExtension(configured).Length == 0)
                    {
                        Add(candidates, Join(trimmed, configured + ".exe", true));
                        Add(candidates, Join(trimmed, configured + ".cmd", true));
                    }
                }
            }

            foreach (string candidate in candidates)
            {
                if (exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string Join(string dir, string name, bool windows)
        {
            char slash = windows ? '\\' : '/';
            return dir.TrimEnd('\\', '/') + slash + name;
        }

        private static void AddUnder(List<string> candidates, string dir, string rest, bool windows)
        {
            if (!string.IsNullOrEmpty(dir))
                Add(candidates, Join(dir, rest, windows));
        }

        private static void Add(List<string> candidates, string path)
        {
            if (!candidates.Contains(path))
                candidates.Add(path);
        }

        #endregion

        #region 지시문

        public static string BuildPrompt(AiRunRequest run)
        {
            string trigger = run.Trigger == ByNote ? "메모 저장"
                : run.Trigger == ByRetry ? "앞 시도 다시 하기"
                : "창의 버튼";
            var text = new StringBuilder();
            text.Append("# 밸런스 초안 자동 실행\n\n");
            text.Append($"이 실행은 Unity 에디터가 시작했다(M6 자동 루프, 계기: {trigger}, 실행 {run.Id}). ");
            text.Append("지금 폴더는 독립 AI 연계 실험 레포다. 사람은 이 대화에 없으니 묻지 말고 끝까지 한다.\n\n");

            text.Append("## 할 일\n\n");
            text.Append("1. `Docs/AI-GUIDE.md`를 읽고 그대로 따른다.\n");
            text.Append($"2. 묶음 `LabData/context/{run.SetupKey}.json`을 읽는다.\n");
            text.Append(run.NoteId != null
                ? $"   - 이번 메모: `{run.NoteId}`. 이 메모의 의도를 가장 먼저 본다. 같은 세팅의 다른 메모도 참고한다.\n"
                : "   - 이번 메모: 없음(사람이 창의 버튼으로 맡겼다). 묶음의 메모를 최근 것부터 본다.\n");
            text.Append($"3. 초안이 필요하면 `{DraftPath}` 하나만 쓴다(있으면 덮어쓴다). 형식과 제한은 AI-GUIDE 4절이다.\n");
            text.Append("4. 초안을 만들지 않는 게 맞으면(버그나 잘못 들어간 값이 의심됨, 메모가 이미 반영됨, 정보가 모자람) 파일을 쓰지 않는다.\n\n");

            text.Append("## 이 실행에서 다른 점\n\n");
            text.Append($"- 쓸 수 있는 파일은 `{DraftPath}` 하나다. 다른 파일 쓰기와 명령 실행은 거절된다. 시도하지 않는다.\n");
            text.Append("- 초안 검사는 실행이 끝난 뒤 에디터가 입력 묶음의 허용 경로·범위로 한다. 묶음의 draft 칸이 다시 써지기를 기다리지 않는다.\n");
            text.Append("- 입력의 values에 있는 경로만 사용하고 min/max/type을 지킨다. 게임이나 시트에 적용하지 않는다.\n\n");

            text.Append("## 마지막 답\n\n");
            text.Append("한국어로, 다른 말 없이 세 줄만 쓴다. 이 답이 에디터 창에 그대로 보인다.\n\n");
            text.Append("- 초안을 썼으면 AI-GUIDE 6절의 세 줄(무엇을 / 왜 / 예상).\n");
            text.Append("- 쓰지 않았으면 첫 줄을 `초안 없음: <이유>`로 하고, 다음 줄에 사람이 할 일을 쓴다.\n");

            if (run.Attempt > 1)
            {
                text.Append($"\n## 다시 하기 ({run.Attempt}번째 시도)\n\n");
                text.Append($"앞 시도(`{run.RetryOf}`)가 쓴 초안이 에디터 검사를 통과하지 못했다. 지금 `{DraftPath}`가 그 초안이다. ");
                text.Append("아래 오류를 고쳐 다시 쓴다. 고칠 수 없으면 `초안 없음: <이유>`로 답한다.\n\n");

                foreach (string error in run.PreviousErrors)
                    text.Append("- ").Append(OneLine(error)).Append('\n');
            }

            return text.ToString();
        }

        #endregion

        #region 실행 폴더

        // 실행 폴더를 만들고 지시문·스크립트·상태·이전 초안을 쓴다. 스크립트의 전체 경로를 돌려준다.
        public static string Prepare(string repoRoot, AiRunRequest run, AiRunSettings settings, bool windows, string draftBefore)
        {
            string dir = RunDir(repoRoot, run.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, PromptFile), BuildPrompt(run), Utf8);

            if (draftBefore != null)
                File.WriteAllText(Path.Combine(dir, DraftBeforeFile), draftBefore, Utf8);

            List<string> arguments = Arguments(settings);
            string folder = RunFolder(run.Id);
            string scriptPath = Path.Combine(dir, windows ? WindowsScriptFile : ShellScriptFile);
            File.WriteAllText(scriptPath,
                windows ? WindowsScript(repoRoot, run.Command, arguments, folder) : ShellScript(repoRoot, run.Command, arguments, folder),
                Utf8);
            SaveState(dir, run);
            return scriptPath;
        }

        public static void SaveState(string runDir, AiRunRequest run) =>
            File.WriteAllText(Path.Combine(runDir, StateFile), PlaytestJson.Write(run.ToJson(), true) + "\n", Utf8);

        public static AiRunRequest LoadState(string runDir)
        {
            string text = ReadText(Path.Combine(runDir, StateFile));

            try
            {
                return text != null ? AiRunRequest.FromJson(PlaytestJson.Parse(text) as JsonObject) : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public static bool IsDone(string runDir) => File.Exists(Path.Combine(runDir, DoneFile));

        public static bool HasExited(string runDir) => File.Exists(Path.Combine(runDir, ExitFile));

        public static int? ParseExit(string text) =>
            int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ? code : (int?)null;

        // 파일 전체. 없거나 읽지 못하면 null(다른 프로세스가 쓰는 중이어도 읽는다).
        public static string ReadText(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Utf8, true))
                    return reader.ReadToEnd();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // 끝난 실행을 기록한다: 실행 폴더의 done.json과 기록 파일 한 줄.
        public static void WriteDone(string repoRoot, AiRunRecord record)
        {
            string json = PlaytestJson.Write(record.ToJson());
            string dir = RunDir(repoRoot, record.Id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, DoneFile), PlaytestJson.Write(record.ToJson(), true) + "\n", Utf8);
            File.AppendAllText(LogPath(repoRoot), json + "\n", Utf8);
        }

        public static List<AiRunRecord> ReadLog(string repoRoot)
        {
            var records = new List<AiRunRecord>();
            string text = ReadText(LogPath(repoRoot));

            if (text == null)
                return records;

            foreach (JsonObject obj in PlaytestJson.ParseLines(text.Replace("\r\n", "\n").Split('\n'), AiRunRecord.Schema, out _))
            {
                AiRunRecord record = AiRunRecord.FromJson(obj);

                if (record != null)
                    records.Add(record);
            }

            return records;
        }

        #endregion

        #region 판정

        // 초안 내용의 해시(바뀌었나 보려고). null이면 null.
        public static string Hash(string text)
        {
            if (text == null)
                return null;

            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Utf8.GetBytes(text));
                var hex = new StringBuilder(16);

                for (int i = 0; i < 8; i++)
                    hex.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));

                return hex.ToString();
            }
        }

        // AI-GUIDE 4절 규칙. 오류(돌려준다)와 경고(warnings에 더한다). current: 경로의 지금 원본 값(모르면 null).
        public static List<string> CheckRules(BalanceProfile draft, Func<string, double?> current, bool requireNoteIds, List<string> warnings)
        {
            var errors = new List<string>();

            if (draft == null)
            {
                errors.Add("초안이 없다.");
                return errors;
            }

            if (draft.name != BalanceProfile.DraftName)
                errors.Add($"초안 name은 {BalanceProfile.DraftName}이어야 한다(받은 값 '{draft.name}').");

            if (draft.patches.Count > MaxPatches)
                errors.Add($"한 번에 값 {MaxPatches}개 이하로 바꾼다(지금 {draft.patches.Count}개).");

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (BalanceProfile.Patch patch in draft.patches)
            {
                if (patch == null || string.IsNullOrWhiteSpace(patch.path))
                {
                    errors.Add("경로가 빈 패치가 있다.");
                    continue;
                }

                if (!seen.Add(patch.path))
                    errors.Add($"{patch.path}: 같은 경로가 두 번 있다.");

                if (string.IsNullOrWhiteSpace(patch.reason))
                    errors.Add($"{patch.path}: reason이 없다.");

                if (requireNoteIds && (patch.noteIds == null || patch.noteIds.Count == 0))
                    errors.Add($"{patch.path}: noteIds가 없다.");

                double? before = current?.Invoke(patch.path);

                if (!before.HasValue)
                    continue;

                if (Math.Abs(patch.value - before.Value) <= 1e-9 * Math.Max(1, Math.Abs(before.Value)))
                {
                    warnings.Add($"{patch.path}: 값이 지금과 같다({Number(before.Value)}).");
                    continue;
                }

                if (before.Value == 0 || patch.value == 0 || Math.Sign(before.Value) != Math.Sign(patch.value))
                {
                    warnings.Add($"{patch.path}: {Number(before.Value)} → {Number(patch.value)}(배율로 잴 수 없다).");
                    continue;
                }

                double ratio = patch.value / before.Value;

                if (ratio < MinRatio - 1e-9 || ratio > MaxRatio + 1e-9)
                    warnings.Add($"{patch.path}: {Number(before.Value)} → {Number(patch.value)}(×{Number(ratio)})는 ×{Number(MinRatio)}~×{Number(MaxRatio)} 밖이다. 메모가 넓히라고 했는지 본다.");
            }

            return errors;
        }

        // 끝난 실행 하나를 판정한다. forced: Timeout·Stopped·Lost처럼 결과와 상관없이 정해진 상태(없으면 null).
        public static AiRunRecord Evaluate(AiRunRequest run, DateTime finishedUtc, int? exitCode, string outText, string errText,
            bool draftWritten, IReadOnlyList<string> draftErrors, IReadOnlyList<string> draftWarnings, string forced)
        {
            AiRunOutput output = AiRunOutput.Parse(outText);
            draftErrors = draftErrors ?? Array.Empty<string>();
            var record = new AiRunRecord
            {
                Id = run.Id,
                StartedAtUtc = run.AtUtc,
                FinishedAtUtc = finishedUtc,
                Trigger = run.Trigger,
                SetupKey = run.SetupKey,
                NoteId = run.NoteId,
                Attempt = run.Attempt,
                RetryOf = run.RetryOf,
                Command = run.Command,
                ExitCode = exitCode,
                Summary = output.Parsed ? Summarize(output.Result) : null,
                CostUsd = output.CostUsd,
                Turns = output.Turns,
                DurationMs = output.DurationMs,
                DraftWritten = draftWritten,
                DraftValid = draftWritten && draftErrors.Count == 0,
            };

            if (draftWarnings != null)
                record.Warnings.AddRange(draftWarnings);

            foreach (string denial in output.Denials)
                record.Warnings.Add("권한에 막힌 시도: " + denial);

            string err = Summarize(errText, 6, 600);

            if (forced != null)
            {
                record.Status = forced;

                if (err != null)
                    record.Errors.Add("오류 출력: " + err);

                AddDraftErrors(record, draftErrors);
                return record;
            }

            if (!output.Parsed || output.IsError || exitCode != 0)
            {
                record.Status = Error;

                if (exitCode == null)
                    record.Errors.Add("종료 코드를 읽지 못했다.");
                else if (exitCode != 0)
                    record.Errors.Add($"Claude Code 종료 코드 {exitCode}.");

                if (output.Parsed && output.IsError)
                    record.Errors.Add("Claude Code가 실패로 끝냈다" + (string.IsNullOrEmpty(output.Subtype) ? "." : $"({output.Subtype})."));

                if (!output.Parsed)
                    record.Errors.Add(string.IsNullOrWhiteSpace(outText)
                        ? "결과(out.json)가 비었다."
                        : "결과(out.json)를 JSON으로 읽지 못했다: " + Summarize(outText, 3, 300));

                if (err != null)
                    record.Errors.Add("오류 출력: " + err);

                AddDraftErrors(record, draftErrors);
                return record;
            }

            if (!draftWritten)
            {
                record.Status = NoDraft;
                return record;
            }

            if (draftErrors.Count > 0)
            {
                record.Status = InvalidDraft;
                record.Errors.AddRange(draftErrors);
                return record;
            }

            record.Status = Ok;
            return record;
        }

        private static void AddDraftErrors(AiRunRecord record, IReadOnlyList<string> draftErrors)
        {
            if (!record.DraftWritten)
                return;

            foreach (string error in draftErrors)
                record.Errors.Add("초안: " + error);
        }

        public static bool ShouldRetry(AiRunRecord record, AiRunSettings settings) =>
            settings.Retry && record.Status == InvalidDraft && record.Attempt < MaxAttempts;

        // 검사에 실패한 시도를 오류와 함께 다시 한다.
        public static AiRunRequest Retry(AiRunRequest run, AiRunRecord record, DateTime atUtc, Random random)
        {
            var next = new AiRunRequest
            {
                Id = NewId(atUtc, random),
                AtUtc = atUtc,
                Trigger = ByRetry,
                SetupKey = run.SetupKey,
                NoteId = run.NoteId,
                Attempt = run.Attempt + 1,
                RetryOf = run.Id,
                Command = run.Command,
            };

            for (int i = 0; i < record.Errors.Count && i < 10; i++)
                next.PreviousErrors.Add(record.Errors[i]);

            return next;
        }

        // "2.1.296 (Claude Code)" → 2.1.296. 읽지 못하면 null.
        public static Version ParseVersion(string text)
        {
            Match match = Regex.Match(text ?? string.Empty, @"(\d+)\.(\d+)\.(\d+)");
            return match.Success
                ? new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
                : null;
        }

        public static string StatusText(string status)
        {
            switch (status)
            {
                case Ok: return "초안 도착";
                case NoDraft: return "초안 없음";
                case InvalidDraft: return "초안 검사 실패";
                case Error: return "실패";
                case Timeout: return "시간 초과";
                case Stopped: return "멈춤";
                case Lost: return "결과 없음";
                default: return status ?? "?";
            }
        }

        public static string TriggerText(string trigger) =>
            trigger == ByNote ? "메모 저장" : trigger == ByRetry ? "다시 하기" : trigger == ByButton ? "버튼" : trigger ?? "?";

        // 앞의 빈 줄이 아닌 maxLines줄, maxChars자까지. 비었으면 null.
        public static string Summarize(string text, int maxLines = 4, int maxChars = 500)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lines = new List<string>();

            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim().Length == 0)
                    continue;

                lines.Add(line.TrimEnd());

                if (lines.Count == maxLines)
                    break;
            }

            string joined = string.Join("\n", lines);
            return joined.Length > maxChars ? joined.Substring(0, maxChars) + "…" : joined;
        }

        #endregion

        #region 작은 도구

        public static string Utc(DateTime at) =>
            at == default ? null : at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        public static DateTime ParseUtc(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at)
                ? at
                : default;

        public static void AddTexts(List<object> from, List<string> into)
        {
            if (from == null)
                return;

            foreach (object item in from)
            {
                if (item is string text)
                    into.Add(text);
            }
        }

        public static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string OneLine(string text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

        #endregion
    }
}
