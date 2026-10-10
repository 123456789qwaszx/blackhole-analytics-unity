using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace IntegrationLab
{
    // 통신 실험: 게임 에셋/AI 변경 기록 없이 샘플 시트의 키 한 칸을 읽고 쓴다.
    internal sealed class SheetSyncLabWindow : EditorWindow
    {
        private SheetSyncConfig _config = new SheetSyncConfig();
        private Task<SheetReply> _request;
        private string _result = "샘플 시트 사본으로 시험하세요. 읽기는 LabData/sheet-cache/에만 저장합니다.";
        private string _node = "demo", _stat = "damage";
        private int _rank = 1, _tab;
        private double _before = 100, _after = 120;
        private bool _dryRun = true, _reading;
        private Vector2 _scroll;
        private const string Settings = "LabData/sheet.json";
        [MenuItem("Integration Lab/Sheet Sync")]
        private static void Open() => GetWindow<SheetSyncLabWindow>("Sheet Sync Lab");
        private void OnEnable()
        {
            if (File.Exists(Settings)) _config = SheetSyncConfig.Parse(File.ReadAllText(Settings), out _result);
        }
        private void OnGUI()
        {
            _config.Endpoint = EditorGUILayout.TextField("웹 앱 /exec 주소", _config.Endpoint);
            _config.Token = EditorGUILayout.PasswordField("토큰", _config.Token);
            if (GUILayout.Button("설정 저장 (이 PC만)"))
            {
                Directory.CreateDirectory("LabData");
                File.WriteAllText(Settings, _config.ToJsonText());
            }
            using (new EditorGUI.DisabledScope(_request != null))
            {
                if (GUILayout.Button("연결 확인")) Start(new SheetClient(_config).Ping(), false);
                if (GUILayout.Button("4개 탭 읽기 → 로컬 캐시")) Start(new SheetClient(_config).Read(), true);
                _tab = EditorGUILayout.Popup("쓰기 대상", _tab, new[] { "NodeCost.Cost", "NodeEffects.Value" });
                _node = EditorGUILayout.TextField("NodeId", _node);
                _rank = EditorGUILayout.IntField("Rank", _rank);
                if (_tab == 1) _stat = EditorGUILayout.TextField("StatId", _stat);
                _before = EditorGUILayout.DoubleField("기대하는 현재 값", _before);
                _after = EditorGUILayout.DoubleField("바꿀 값", _after);
                _dryRun = EditorGUILayout.Toggle("검사만 (dryRun)", _dryRun);
                if (GUILayout.Button(_dryRun ? "쓰기 검사" : "시트에 한 칸 쓰기"))
                {
                    var update = new SheetUpdate { Tab = _tab == 0 ? "NodeCost" : "NodeEffects",
                        Column = _tab == 0 ? "Cost" : "Value", NodeId = _node, Rank = _rank,
                        StatId = _tab == 1 ? _stat : null, Before = _before, After = _after };
                    if (_dryRun || EditorUtility.DisplayDialog("시트 변경", update.Describe() + $"\n{_before} → {_after}", "쓰기", "취소"))
                        Start(new SheetClient(_config).Write(new[] { update }, _dryRun), false);
                }
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_result ?? "", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        private void Start(Task<SheetReply> request, bool reading)
        { _request = request; _reading = reading; _result = "요청 중…"; }
        private void Update()
        {
            if (_request == null || !_request.IsCompleted) return;
            try
            {
                SheetReply reply = _request.GetAwaiter().GetResult();
                _result = reply.Describe();
                if (reply.Body != null) _result += "\n" + PlaytestJson.Write(reply.Body, true);
                if (_reading && reply.Ok)
                {
                    // 모두 검사한 뒤 캐시에 저장한다. 게임 데이터로 가져오는 것은 별도의 어댑터 책임이다.
                    foreach (var pair in reply.Tabs)
                    {
                        string path = "LabData/sheet-cache/" + pair.Key + ".csv";
                        var diff = SheetDiff.Compare(pair.Key, File.Exists(path) ? File.ReadAllText(path) : pair.Value, pair.Value);
                        if (diff.Error != null) throw new FormatException(diff.Error);
                        _result += "\n" + diff.Summary();
                    }
                    Directory.CreateDirectory("LabData/sheet-cache");
                    foreach (var pair in reply.Tabs) File.WriteAllText("LabData/sheet-cache/" + pair.Key + ".csv", pair.Value);
                }
            }
            catch (Exception error) { _result = "실패: " + error.Message; }
            finally { _request = null; Repaint(); }
        }
    }
}
