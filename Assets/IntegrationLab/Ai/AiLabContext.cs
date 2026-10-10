using System;
using System.Collections.Generic;
using System.IO;

namespace IntegrationLab
{
    // 도메인 어댑터의 최소 계약: 허용된 경로·현재값·범위 + 메모. 게임 Core를 참조하지 않는다.
    internal static class AiLabContext
    {
        public static string DataFolder => "LabData";
        public static string PathOf(string key) => Path.Combine(DataFolder, "context", key + ".json");
        public static string SaveSample(string intent)
        {
            var context = (JsonObject)PlaytestJson.Parse(File.ReadAllText("Samples/Ai/context.json"));
            string noteId = "note-" + Guid.NewGuid().ToString("N");
            context.Add("notes", new List<object> { new JsonObject { { "id", noteId }, { "intent", intent } } });
            Directory.CreateDirectory(Path.Combine(DataFolder, "context"));
            File.WriteAllText(PathOf("sample"), PlaytestJson.Write(context, true));
            File.AppendAllText(Path.Combine(DataFolder, "notes.ndjson"), PlaytestJson.Write(new JsonObject {
                { "id", noteId }, { "atUtc", DateTime.UtcNow.ToString("o") }, { "intent", intent } }) + "\n");
            return noteId;
        }
        public static void Validate(string key, string noteId, string draftPath, List<string> errors, List<string> warnings)
        {
            try
            {
                BalanceProfile draft = BalanceProfile.Parse(File.ReadAllText(draftPath), out string error);
                if (draft == null) { errors.Add(error); return; }
                var context = (JsonObject)PlaytestJson.Parse(File.ReadAllText(PathOf(key)));
                JsonObject values = context.Object("values");
                var ids = new HashSet<string>();
                foreach (object item in context.Array("notes") ?? new List<object>())
                    if (item is JsonObject note && note.Text("id") is string id) ids.Add(id);
                errors.AddRange(AiRun.CheckRules(draft, path => values?.Object(path)?.Number("value"), noteId != null, warnings));
                foreach (var patch in draft.patches)
                {
                    if (patch == null || patch.path == null) continue;
                    JsonObject slot = values?.Object(patch.path);
                    if (slot == null) { errors.Add("허용하지 않은 경로: " + patch.path); continue; }
                    if (double.IsNaN(patch.value) || double.IsInfinity(patch.value)
                        || patch.value < (slot.Number("min") ?? double.MinValue)
                        || patch.value > (slot.Number("max") ?? double.MaxValue)) errors.Add("범위를 벗어난 값: " + patch.path);
                    if (slot.Text("type") == "integer" && patch.value != Math.Truncate(patch.value)) errors.Add("정수가 필요하다: " + patch.path);
                    foreach (string id in patch.noteIds)
                        if (!ids.Contains(id)) errors.Add("없는 메모 ID: " + id);
                }
            }
            catch (Exception e) when (e is IOException || e is FormatException || e is ArgumentException || e is UnauthorizedAccessException)
            { errors.Add(e.Message); }
        }
    }
}
