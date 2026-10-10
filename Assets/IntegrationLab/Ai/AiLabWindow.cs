using System.IO;
using UnityEditor;
using UnityEngine;

namespace IntegrationLab
{
    internal sealed class AiLabWindow : EditorWindow
    {
        [SerializeField] private string _intent = "적 밀도를 낮추고 싶다. spawn/count를 조금 줄여줘.";
        private string _noteId, _status;
        private Vector2 _scroll;
        [MenuItem("Integration Lab/AI Draft")]
        private static void Open() => GetWindow<AiLabWindow>("AI Draft Lab");
        private void OnEnable() => AiRunner.Changed += Repaint;
        private void OnDisable() => AiRunner.Changed -= Repaint;
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("샘플 수치와 메모 → Claude Code → 초안 검사. 게임/시트 원본을 수정하지 않습니다.", MessageType.Info);
            if (GUILayout.Button("Claude Code 확인")) AiRunner.CheckClaude();
            EditorGUILayout.LabelField(AiRunner.CheckText ?? "", EditorStyles.wordWrappedLabel);
            var settings = AiRunner.LoadSettings(out string settingsError);
            using (new EditorGUI.DisabledScope(AiRunner.Busy))
            {
                bool auto = EditorGUILayout.Toggle("메모 저장 때 자동 요청", settings.AutoOnNote);
                if (auto != settings.AutoOnNote) AiRunner.SetAutoOnNote(auto, out _status);
                _intent = EditorGUILayout.TextArea(_intent, GUILayout.Height(65));
                if (GUILayout.Button("샘플 메모 저장"))
                {
                    try { _noteId = AiLabContext.SaveSample(_intent); _status = "메모 저장: " + _noteId; AiRunner.OnNewNote("sample", _noteId); }
                    catch (System.Exception e) { _status = e.Message; }
                }
                if (GUILayout.Button("저장한 메모로 초안 요청"))
                    _status = AiRunner.Start(AiRun.ByButton, "sample", _noteId, out string error) ? "실행 중" : error;
            }
            if (AiRunner.Busy && GUILayout.Button("멈추기")) AiRunner.Stop();
            EditorGUILayout.LabelField(settingsError ?? _status ?? "", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField(AiRunner.Busy ? $"실행 중 {AiRunner.ActiveSeconds:0}초" : "대기");
            var records = AiRunner.ReadRecords();
            if (records.Count > 0)
            {
                var last = records[records.Count - 1];
                EditorGUILayout.LabelField(AiRun.StatusText(last.Status) + "\n" + last.Summary + "\n" + string.Join("\n", last.Errors), EditorStyles.wordWrappedLabel);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (File.Exists(AiRun.DraftPath)) EditorGUILayout.TextArea(File.ReadAllText(AiRun.DraftPath), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        private void Update() { if (AiRunner.Busy) Repaint(); }
    }
}
