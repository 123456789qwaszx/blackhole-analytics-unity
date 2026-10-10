using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace IntegrationLab
{
    // 자동 루프 실행기(M6). 이 PC의 Claude Code CLI(claude -p)를 백그라운드로 돌려 AI 초안(ai-draft.json)을 쓰게 한다.
    // - 계기: 메모 저장(LabData/ai.json의 autoOnNote가 켜져 있을 때, AiLabContext가 알린다), 실험창의 버튼.
    // - 실행마다 LabData/ai-runs/<runId>/에 지시문·스크립트를 쓰고 창 없이 돌린다. 스크립트가 결과를 파일로 남기므로
    //   스크립트 다시 컴파일(도메인 리로드)이나 에디터 종료 뒤에도 다시 켜질 때 마저 처리한다.
    // - 끝나면 입력 계약과 AI 지침 규칙으로 초안을 검사하고, 기록(LabData/ai-runs.ndjson)을 남기고, 창에 알린다.
    //   검사에 실패하면 오류를 붙여 한 번 더 한다. 한 번에 하나만 돌고, 도는 동안 들어온 메모는 가장 최근 것 하나만 기다린다.
    // - 승격·되돌리기·시트 반영은 여기서 하지 않는다(사람 버튼).
    // 실행 계획(명령줄·스크립트·판정)은 AiRun.cs, 흐름은 Docs/AiIntegration.md.
    [InitializeOnLoad]
    internal static class AiRunner
    {
        private const double TickSeconds = 1;
        private const int RecoverScan = 5;
        private const double LostGraceSeconds = 5;
        private const int CheckTimeoutMs = 20000;

        // 상태가 바뀌었다(창이 다시 그린다).
        public static event Action Changed;
        // 실행(시도) 하나가 끝났다.
        public static event Action<AiRunRecord> Finished;

        private static AiRunRequest _active;
        private static int _activeTimeout = AiRunSettings.DefaultTimeoutSeconds;
        private static string _pendingKey;
        private static string _pendingNote;
        private static double _nextTick;
        private static bool _recovered;
        private static double _goneSince = -1;
        private static readonly System.Random Random = new System.Random();

        // Claude Code 확인(백그라운드 스레드가 채운다).
        private static volatile string _checkText;
        private static volatile bool _checking;
        private static volatile bool _checkDone;

        static AiRunner()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += () => EditorApplication.update -= Tick;
        }

        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string RunsFolder => Path.Combine(AiLabContext.DataFolder, AiRun.RunsFolderName);
        public static string SettingsPath => Path.Combine(AiLabContext.DataFolder, AiRunSettings.FileName);
        private static string DraftFullPath => Path.Combine(RepoRoot, AiRun.DraftPath);
        private static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        public static AiRunRequest Active => _active;
        public static bool Busy => _active != null;
        public static double ActiveSeconds => _active == null ? 0 : Math.Max(0, (DateTime.UtcNow - _active.AtUtc).TotalSeconds);
        public static string PendingNoteId => _pendingKey != null ? _pendingNote ?? "(메모 없음)" : null;
        public static string CheckText => _checkText;
        public static bool Checking => _checking;

        #region 설정

        public static AiRunSettings LoadSettings(out string error)
        {
            error = null;

            if (!File.Exists(SettingsPath))
                return new AiRunSettings();

            string text = AiRun.ReadText(SettingsPath);

            if (text == null)
            {
                error = $"{AiRunSettings.FileName}을 읽지 못했다.";
                return new AiRunSettings();
            }

            return AiRunSettings.Parse(text, out error);
        }

        public static bool SetAutoOnNote(bool on, out string error)
        {
            AiRunSettings settings = LoadSettings(out error);

            // 사람이 손으로 고친 파일이 JSON으로 읽히지 않으면 덮어쓰지 않는다.
            if (File.Exists(SettingsPath) && !IsJsonObject(AiRun.ReadText(SettingsPath)))
            {
                error = $"{SettingsPath}가 JSON 객체가 아니어서 고치지 않았다. 파일을 고치거나 지운다.";
                return false;
            }

            settings.AutoOnNote = on;

            try
            {
                Directory.CreateDirectory(AiLabContext.DataFolder);
                File.WriteAllText(SettingsPath, settings.ToJsonText());
                error = null;
                Changed?.Invoke();
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = exception.Message;
                return false;
            }
        }

        public static string ResolveCommand(AiRunSettings settings) =>
            AiRun.Resolve(settings.Command, IsWindows, Environment.GetEnvironmentVariable, File.Exists);

        public static List<AiRunRecord> ReadRecords() => AiRun.ReadLog(RepoRoot);

        private static bool IsJsonObject(string text)
        {
            try
            {
                return text != null && PlaytestJson.Parse(text) is JsonObject;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        #endregion

        #region 시작·멈추기

        // 메모가 새로 저장됐다(AiLabContext가 그 세팅의 묶음을 쓴 뒤 부른다).
        public static void OnNewNote(string setupKey, string noteId)
        {
            AiRunSettings settings = LoadSettings(out _);

            if (!settings.AutoOnNote || string.IsNullOrEmpty(setupKey))
                return;

            if (_active != null)
            {
                _pendingKey = setupKey;
                _pendingNote = noteId;
                Debug.Log($"[AI 실행] 지금 실행이 끝나면 메모 {noteId}를 맡긴다.");
                Changed?.Invoke();
                return;
            }

            if (!Start(AiRun.ByNote, setupKey, noteId, out string error))
                Debug.LogWarning($"[AI 실행] 메모 {noteId}를 맡기지 못했다: {error}");
        }

        // 이 세팅의 묶음으로 AI에게 초안을 맡긴다. noteId가 있으면 그 메모를 먼저 본다.
        public static bool Start(string trigger, string setupKey, string noteId, out string error)
        {
            if (_active != null)
            {
                error = "이미 실행 중이다. 끝나거나 멈춘 뒤에 다시 한다.";
                return false;
            }

            if (string.IsNullOrEmpty(setupKey) || !System.Text.RegularExpressions.Regex.IsMatch(setupKey, "^[A-Za-z0-9_-]+$"))
            {
                error = "세팅 키가 없다.";
                return false;
            }

            if (!File.Exists(AiLabContext.PathOf(setupKey)))
            {
                error = $"이 세팅의 AI 묶음(LabData/context/{setupKey}.json)이 없다. 먼저 \"AI 묶음 만들기\"를 누른다.";
                return false;
            }

            DateTime now = DateTime.UtcNow;
            var run = new AiRunRequest
            {
                Id = AiRun.NewId(now, Random),
                AtUtc = now,
                Trigger = trigger,
                SetupKey = setupKey,
                NoteId = noteId,
            };
            return Launch(run, out error);
        }

        // 지금 실행을 멈춘다(프로세스 트리를 끝낸다). 기다리던 메모도 버린다.
        public static void Stop()
        {
            if (_active == null)
                return;

            _pendingKey = null;
            _pendingNote = null;
            Kill(_active.Pid);
            Finish(_active, AiRun.Stopped, allowRetry: false);
        }

        private static bool Launch(AiRunRequest run, out string error)
        {
            AiRunSettings settings = LoadSettings(out string settingsError);

            if (settingsError != null)
                Debug.LogWarning($"[AI 실행] {settingsError}");

            run.Command = ResolveCommand(settings);

            if (run.Command == null)
            {
                error = "Claude Code를 찾지 못했다. 설치하고 로그인한 뒤(Docs/AiIntegration.md \"손 확인\") 다시 한다. "
                        + $"다른 곳에 깔았으면 {AiRunSettings.FileName}의 command에 실행 파일 경로를 적는다.";
                return false;
            }

            string draftBefore = AiRun.ReadText(DraftFullPath);
            run.DraftHashBefore = AiRun.Hash(draftBefore);
            string script;

            try
            {
                script = AiRun.Prepare(RepoRoot, run, settings, IsWindows, draftBefore);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                error = $"실행 폴더를 만들지 못했다: {exception.Message}";
                return false;
            }

            try
            {
                ProcessStartInfo info = IsWindows
                    ? new ProcessStartInfo(ComSpec(), "/d /s /c \"" + AiRun.QuoteArg(script) + "\"")
                    : new ProcessStartInfo("/bin/sh", AiRun.QuoteArg(script));
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WorkingDirectory = RepoRoot;

                using (Process process = Process.Start(info))
                    run.Pid = process != null ? process.Id : 0;

                AiRun.SaveState(AiRun.RunDir(RepoRoot, run.Id), run);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException
                                              || exception is IOException || exception is UnauthorizedAccessException)
            {
                error = $"실행하지 못했다: {exception.Message}";
                AiRunRecord failed = AiRun.Evaluate(run, DateTime.UtcNow, null, null, null, false, null, null, AiRun.Error);
                failed.Errors.Add(error);
                Record(failed);
                return false;
            }

            _active = run;
            _activeTimeout = settings.TimeoutSeconds;
            _goneSince = -1;
            Debug.Log($"[AI 실행] 시작 {run.Id} · {AiRun.TriggerText(run.Trigger)} · 세팅 {run.SetupKey} · 메모 {run.NoteId ?? "없음"}"
                      + (run.Attempt > 1 ? $" · {run.Attempt}번째 시도" : string.Empty) + $"\n{run.Command}");
            error = null;
            Changed?.Invoke();
            return true;
        }

        private static string ComSpec()
        {
            string comspec = Environment.GetEnvironmentVariable("ComSpec");
            return string.IsNullOrEmpty(comspec) ? "cmd.exe" : comspec;
        }

        #endregion

        #region 지켜보기·마무리

        private static void Tick()
        {
            if (_checkDone)
            {
                _checkDone = false;
                Changed?.Invoke();
            }

            if (EditorApplication.timeSinceStartup < _nextTick)
                return;

            _nextTick = EditorApplication.timeSinceStartup + TickSeconds;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (!_recovered)
            {
                _recovered = true;
                Recover();
                return;
            }

            if (_active == null)
            {
                if (_pendingKey != null)
                {
                    string key = _pendingKey;
                    string note = _pendingNote;
                    _pendingKey = null;
                    _pendingNote = null;

                    if (!Start(AiRun.ByNote, key, note, out string error))
                        Debug.LogWarning($"[AI 실행] 기다리던 메모 {note}를 맡기지 못했다: {error}");
                }

                return;
            }

            AiRunRequest run = _active;

            if (AiRun.HasExited(AiRun.RunDir(RepoRoot, run.Id)))
            {
                Finish(run, null, allowRetry: true);
                return;
            }

            if (ActiveSeconds > _activeTimeout)
            {
                Kill(run.Pid);
                Finish(run, AiRun.Timeout, allowRetry: false);
                return;
            }

            // 결과 없이 프로세스가 사라졌다. exit.txt를 막 쓰는 중일 수 있어 몇 초 기다린다.
            if (!Alive(run.Pid))
            {
                if (_goneSince < 0)
                    _goneSince = EditorApplication.timeSinceStartup;
                else if (EditorApplication.timeSinceStartup - _goneSince > LostGraceSeconds)
                    Finish(run, AiRun.Lost, allowRetry: false);
            }
            else
            {
                _goneSince = -1;
            }
        }

        // 에디터를 켰거나 스크립트를 다시 컴파일한 뒤: 최근 실행 가운데 마무리하지 않은 것을 찾는다.
        // 아직 도는 것은 이어서 지켜보고, 그 사이 끝난 것은 지금 마무리한다(다시 하기는 하지 않는다).
        private static void Recover()
        {
            if (!Directory.Exists(RunsFolder))
                return;

            string[] dirs;

            try
            {
                dirs = Directory.GetDirectories(RunsFolder);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return;
            }

            Array.Sort(dirs, StringComparer.Ordinal);
            int timeout = LoadSettings(out _).TimeoutSeconds;

            for (int i = Math.Max(0, dirs.Length - RecoverScan); i < dirs.Length; i++)
            {
                if (AiRun.IsDone(dirs[i]))
                    continue;

                AiRunRequest run = AiRun.LoadState(dirs[i]);

                if (run == null)
                    continue;

                bool exited = AiRun.HasExited(dirs[i]);
                bool alive = !exited && Alive(run.Pid);
                bool late = (DateTime.UtcNow - run.AtUtc).TotalSeconds > timeout;

                if (alive && !late && _active == null)
                {
                    _active = run;
                    _activeTimeout = timeout;
                    _goneSince = -1;
                    Debug.Log($"[AI 실행] {run.Id}가 아직 돈다. 이어서 지켜본다.");
                    Changed?.Invoke();
                    continue;
                }

                if (alive)
                    Kill(run.Pid);

                Finish(run, exited ? null : alive ? AiRun.Timeout : AiRun.Lost, allowRetry: false);
            }
        }

        private static void Finish(AiRunRequest run, string forced, bool allowRetry)
        {
            if (_active == run)
            {
                _active = null;
                _goneSince = -1;
            }

            string dir = AiRun.RunDir(RepoRoot, run.Id);
            string draftText = AiRun.ReadText(DraftFullPath);
            bool written = draftText != null && AiRun.Hash(draftText) != run.DraftHashBefore;
            var errors = new List<string>();
            var warnings = new List<string>();

            if (written)
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, AiRun.DraftAfterFile), draftText);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[AI 실행] 초안 사본을 쓰지 못했다: {exception.Message}");
                }

                CheckDraft(run, errors, warnings);
            }

            AiRunRecord record = AiRun.Evaluate(run, DateTime.UtcNow,
                AiRun.ParseExit(AiRun.ReadText(Path.Combine(dir, AiRun.ExitFile))),
                AiRun.ReadText(Path.Combine(dir, AiRun.OutFile)),
                AiRun.ReadText(Path.Combine(dir, AiRun.ErrFile)),
                written, errors, warnings, forced);
            Record(record);

            if (allowRetry && _active == null && AiRun.ShouldRetry(record, LoadSettings(out _)))
            {
                AiRunRequest next = AiRun.Retry(run, record, DateTime.UtcNow, Random);

                if (!Launch(next, out string error))
                    Debug.LogWarning($"[AI 실행] 다시 하지 못했다: {error}");
            }
        }

        // 입력 묶음의 허용 경로·범위로 검사하고(AiLabContext), AI 지침 규칙(값 개수·이유·메모·배율)을 본다.
        private static void CheckDraft(AiRunRequest run, List<string> errors, List<string> warnings)
        {
            AiLabContext.Validate(run.SetupKey, run.NoteId, DraftFullPath, errors, warnings);
        }

        private static void Record(AiRunRecord record)
        {
            try
            {
                AiRun.WriteDone(RepoRoot, record);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[AI 실행] 기록을 쓰지 못했다: {exception.Message}");
            }

            string cost = record.CostUsd.HasValue ? $" · ${record.CostUsd.Value:0.00}" : string.Empty;
            string head = $"[AI 실행] {AiRun.StatusText(record.Status)} · {record.Id} · {record.Seconds:0}초{cost}";
            string summary = record.Summary != null ? "\n" + record.Summary : string.Empty;

            if (record.Status == AiRun.Ok)
                Debug.Log(head + summary);
            else
                Debug.LogWarning(head + summary + (record.Errors.Count > 0 ? "\n" + string.Join("\n", record.Errors) : string.Empty));

            Finished?.Invoke(record);
            Changed?.Invoke();
        }

        #endregion

        #region 프로세스

        private static bool Alive(int pid)
        {
            if (pid <= 0)
                return false;

            try
            {
                using (Process process = Process.GetProcessById(pid))
                    return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 볼 권한이 없다: 살아 있다고 보고 시간 제한에 맡긴다.
                return true;
            }
        }

        // 스크립트 셸과 그 아래(claude) 모두 끝낸다.
        private static void Kill(int pid)
        {
            if (pid <= 0)
                return;

            try
            {
                ProcessStartInfo info = IsWindows
                    ? new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
                    : new ProcessStartInfo("/bin/sh", $"-c \"pkill -TERM -P {pid}; kill -TERM {pid}\"");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;

                using (Process process = Process.Start(info))
                    process?.WaitForExit(5000);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                Debug.LogWarning($"[AI 실행] 프로세스 {pid}를 끝내지 못했다: {exception.Message}");
            }
        }

        #endregion

        #region Claude Code 확인

        // 실행 파일 위치, 버전(claude --version), 로그인(claude auth status)을 백그라운드로 본다. 결과는 CheckText.
        public static void CheckClaude()
        {
            if (_checking)
                return;

            string command = ResolveCommand(LoadSettings(out _));

            if (command == null)
            {
                _checkText = "Claude Code를 찾지 못했다. 설치: PowerShell에서 irm https://claude.ai/install.ps1 | iex → 새 터미널에서 claude를 한 번 실행해 로그인. "
                             + $"다른 곳에 깔았으면 LabData/{AiRunSettings.FileName}의 command에 경로를 적는다.";
                Changed?.Invoke();
                return;
            }

            _checking = true;
            _checkText = "Claude Code 확인 중…";
            Changed?.Invoke();
            bool windows = IsWindows;
            string root = RepoRoot;
            string comspec = ComSpec();

            var thread = new Thread(() =>
            {
                string text;

                try
                {
                    string version = RunShort(command, "--version", windows, root, comspec, out int versionCode);
                    string auth = RunShort(command, "auth status", windows, root, comspec, out int authCode);
                    Version parsed = AiRun.ParseVersion(version);
                    string versionText = versionCode != 0 ? $"버전 확인 실패(종료 코드 {versionCode}): {FirstLine(version)}"
                        : parsed != null && parsed < AiRun.MinVersion ? $"버전 {FirstLine(version)} — {AiRun.MinVersion} 이상이 필요하다. 터미널에서 claude update를 실행한다."
                        : "버전 " + FirstLine(version);
                    text = $"Claude Code: {command}\n{versionText}\n{AuthText(auth, authCode)}";
                }
                catch (Exception exception)
                {
                    text = $"Claude Code를 실행하지 못했다({command}): {exception.Message}";
                }

                _checkText = text;
                _checking = false;
                _checkDone = true;
            })
            {
                IsBackground = true,
            };
            thread.Start();
        }

        private static string RunShort(string command, string arguments, bool windows, string root, string comspec, out int exitCode)
        {
            ProcessStartInfo info = windows
                ? new ProcessStartInfo(comspec, "/d /s /c \"" + AiRun.QuoteArg(command) + " " + arguments + "\"")
                : new ProcessStartInfo(command, arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WorkingDirectory = root;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            using (Process process = Process.Start(info))
            {
                if (process == null)
                {
                    exitCode = -1;
                    return "실행하지 못했다.";
                }

                process.StandardInput.Close();
                var output = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(CheckTimeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    exitCode = -1;
                    return $"{CheckTimeoutMs / 1000}초 안에 끝나지 않았다.";
                }

                process.WaitForExit();
                exitCode = process.ExitCode;

                lock (output)
                    return output.ToString().Trim();
            }
        }

        // auth status는 JSON을 낸다(loggedIn, authMethod 등). 이메일 같은 개인 정보는 보이지 않는다.
        private static string AuthText(string output, int exitCode)
        {
            JsonObject obj = null;

            try
            {
                obj = PlaytestJson.Parse(output) as JsonObject;
            }
            catch (FormatException)
            {
            }

            bool loggedIn = obj != null ? obj["loggedIn"] is bool b && b : exitCode == 0;

            if (!loggedIn)
                return "로그인 안 됨: 터미널에서 claude를 한 번 실행해 로그인한다." + (obj == null ? $" ({FirstLine(output)})" : string.Empty);

            string method = obj?.Text("authMethod") ?? obj?.Text("apiProvider");
            string plan = obj?.Text("subscriptionType");
            return "로그인됨" + (method != null ? $" · {method}" : string.Empty) + (plan != null ? $" · {plan}" : string.Empty);
        }

        private static string FirstLine(string text) =>
            AiRun.Summarize(text, 1, 200) ?? "(출력 없음)";

        #endregion
    }
}
