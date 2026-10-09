using System;
using System.IO;
using System.Threading.Tasks;
using BlackHole.Analytics;
using BlackHole.Analytics.Transport;
using UnityEngine;

namespace BlackHole.Dev
{
    // 로컬 통계 서버(bootRun)에 실제로 보내 보는 확인용 도구. Play 모드에서 화면의 패널 버튼으로 쓴다(제목 줄을 끌어 옮긴다).
    // 게임 레포로 옮기지 않는다 — 그래서 Analytics 폴더 밖에 둔다.
    // 성공(2xx)은 AnalyticsSender가 로그를 남기지 않으므로, 응답 코드를 [확인] 로그로 따로 찍는다.
    public sealed class AnalyticsServerCheck : MonoBehaviour
    {
        // IMGUI 창 번호. 화면에 이 창 하나뿐이라 겹치지 않는 아무 숫자면 된다.
        private const int WindowId = 7301;

        [SerializeField] private string _baseUrl = "http://localhost:8080";
        [SerializeField] private int _timeoutSeconds = 10;

        // 화면 패널. Play 중에도 Inspector에서 바로 바뀐다(Play 중에 바꾼 값은 Play를 끄면 되돌아간다).
        [Header("Panel")]
        [SerializeField, Range(0.5f, 4f)] private float _scale = 2f;
        [SerializeField] private Vector2 _position = new Vector2(10f, 10f);
        [SerializeField] private float _width = 360f;

        private AnalyticsQueue _queue;
        private AnalyticsSender _sender;
        private string _queueDirectory;
        private string _installId;
        private int _battleIndex;
        private BattleSummaryDto _lastSummary;

        private void Awake()
        {
            _queueDirectory = Path.Combine(Application.persistentDataPath, "analytics-check");
            _queue = new AnalyticsQueue(_queueDirectory, 100);
            _sender = new AnalyticsSender(_queue, new LoggingClient(new AnalyticsClient(_baseUrl, _timeoutSeconds)));
            _installId = Guid.NewGuid().ToString();
            Debug.Log($"[확인] 큐 폴더: {_queueDirectory}");
        }

        private void OnGUI()
        {
            // 패널 전체(글자·버튼)를 _scale배로 그린다. _position과 _width는 확대하기 전 크기 기준이다.
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

            Rect start = new Rect(_position.x, _position.y, _width, 0f);
            Rect window = GUILayout.Window(WindowId, start, DrawWindow, "통계 서버 확인", GUILayout.Width(_width));
            _position = KeepOnScreen(window);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label($"서버: {_baseUrl}");
            GUILayout.Label($"큐 {_queue.Count}개 · rejected {RejectedCount()}개");

            if (GUILayout.Button("새 판 보내기"))
            {
                _lastSummary = NewSummary();
                _ = _sender.SendAsync(_lastSummary);
            }

            if (GUILayout.Button("같은 판 다시 보내기") && _lastSummary != null)
                _ = _sender.SendAsync(_lastSummary);

            if (GUILayout.Button("잘못된 판 보내기 (buildVersion 빈 값)"))
            {
                BattleSummaryDto summary = NewSummary();
                summary.buildVersion = "";
                _ = _sender.SendAsync(summary);
            }

            if (GUILayout.Button("남은 것 보내기 (Flush)"))
                _ = _sender.FlushAsync();

            if (GUILayout.Button("큐 폴더 열기"))
                Application.OpenURL(new Uri(_queueDirectory).AbsoluteUri);

            // 제목 줄을 끌어 옮긴다.
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        // 끌어 옮기다 화면 밖으로 나가 안 보이게 되지 않도록 화면 안에 붙잡아 둔다.
        private Vector2 KeepOnScreen(Rect window)
        {
            float maxX = Mathf.Max(0f, Screen.width / _scale - window.width);
            float maxY = Mathf.Max(0f, Screen.height / _scale - window.height);
            return new Vector2(Mathf.Clamp(window.x, 0f, maxX), Mathf.Clamp(window.y, 0f, maxY));
        }

        // 계약(v2)을 지키는 판 하나. 판마다 battleId·seed가 새로 생기고 battleIndex가 오른다. 값은 예시 JSON과 같은 모양의 가짜다.
        private BattleSummaryDto NewSummary()
        {
            DateTime now = DateTime.UtcNow;

            return new BattleSummaryDto
            {
                battleId = Guid.NewGuid().ToString(),
                installId = _installId,
                battleIndex = ++_battleIndex,
                buildVersion = Application.version,
                contentVersion = "",
                platform = Application.platform.ToString(),
                startedAtUtc = now.AddSeconds(-30).ToString("o"),
                endedAtUtc = now.ToString("o"),
                playedSeconds = 28f,
                seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                startGrowthStage = 1,
                nodes =
                {
                    new NodeRankDto { nodeId = "timer-01", rank = 1 },
                    new NodeRankDto { nodeId = "growth.time-01", rank = 1 },
                },
                appliedStats =
                {
                    startLevel = 10,
                    startExp = 250000,
                    goalLevel = 20,
                    goalExp = 18000000,
                    timeLimitSeconds = 14f,
                    growthTimeSeconds = 3f,
                    breakerDamage = 1f,
                    breakerInterval = 1f,
                    breakerRadius = 0.26f,
                    breakerCritChance = 0f,
                    breakerCritDamage = 1f,
                    traitChances =
                    {
                        new TraitChanceDto { enemyId = "asteroid", traitId = "golden", chance = 0f },
                        new TraitChanceDto { enemyId = "asteroid", traitId = "electric", chance = 0f },
                    },
                    goldenAsteroidMultiplier = 50f,
                },
                kills = { new EnemyKillDto { enemyId = "asteroid", count = 10 } },
                totalKills = 10,
                earnedGold = 100,
                settledGold = 100,
                reachedLevel = 12,
                exp = 340000,
                reachedMilestone = false,
                stats =
                {
                    breakerDamage = 120.5,
                    breakerTicks = 28,
                    addedSeconds = 14f,
                },
            };
        }

        private int RejectedCount() =>
            Directory.Exists(_queue.RejectedDirectory) ? Directory.GetFiles(_queue.RejectedDirectory, "*.json").Length : 0;

        // 실제 클라이언트를 감싸 응답 코드를 로그로 남긴다.
        private sealed class LoggingClient : IAnalyticsClient
        {
            private readonly IAnalyticsClient _inner;

            public LoggingClient(IAnalyticsClient inner)
            {
                _inner = inner;
            }

            public async Task<AnalyticsResponse> PostBattleSummaryAsync(string json)
            {
                AnalyticsResponse response = await _inner.PostBattleSummaryAsync(json);
                Debug.Log(response.NetworkError
                    ? $"[확인] 응답 없음: {response.Body}"
                    : $"[확인] HTTP {response.StatusCode}");
                return response;
            }
        }
    }
}
