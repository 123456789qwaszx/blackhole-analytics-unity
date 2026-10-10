using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntegrationLab
{
    // 노드 시트 탭(= 레포 Assets/Data/NodeTable의 CSV 이름)과 행을 가르는 키 칸.
    internal static class SheetTabs
    {
        public const string UpgradeStats = "UpgradeStats";
        public const string Nodes = "Nodes";
        public const string NodeCost = "NodeCost";
        public const string NodeEffects = "NodeEffects";

        public static readonly string[] Names = { UpgradeStats, Nodes, NodeCost, NodeEffects };

        public static string[] KeysOf(string tab) => tab switch
        {
            UpgradeStats => new[] { "StatId" },
            Nodes => new[] { "NodeId" },
            NodeCost => new[] { "NodeId", "Rank" },
            NodeEffects => new[] { "NodeId", "Rank", "StatId" },
            _ => Array.Empty<string>(),
        };
    }

    // 행 하나의 차이. Cells는 바뀐 칸(칸 이름, 이전, 이후). 새로 생긴 행·없어진 행은 Cells가 비어 있다.
    internal sealed class SheetRowChange
    {
        public string Key;
        public readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<(string Column, string Before, string After)> Cells = new List<(string Column, string Before, string After)>();
    }

    // 탭 하나의 차이(레포 CSV → 시트). 글자가 아니라 읽은 행(키 → 칸 값)으로 비교한다.
    internal sealed class SheetTabDiff
    {
        public string Tab;
        // 비교하지 못한 이유(머리칸에 키가 없음, 같은 키가 두 번 등). 있으면 덮어쓰지 않는다.
        public string Error;
        public readonly List<string> AddedColumns = new List<string>();
        public readonly List<string> RemovedColumns = new List<string>();
        public readonly List<SheetRowChange> Added = new List<SheetRowChange>();
        public readonly List<SheetRowChange> Removed = new List<SheetRowChange>();
        public readonly List<SheetRowChange> Changed = new List<SheetRowChange>();
        // 값은 같은데 글자가 다르다(행 순서·따옴표·줄 끝 등). 덮어쓰면 글자만 바뀐다.
        public bool TextOnly;

        public bool HasChanges => AddedColumns.Count + RemovedColumns.Count + Added.Count + Removed.Count + Changed.Count > 0;

        public string Summary()
        {
            if (Error != null)
                return $"{Tab}: 비교 못 함 — {Error}";

            if (!HasChanges)
                return TextOnly ? $"{Tab}: 값은 같고 글자 모양만 다르다" : $"{Tab}: 같다";

            var parts = new List<string>();
            if (Changed.Count > 0) parts.Add($"바뀐 행 {Changed.Count}");
            if (Added.Count > 0) parts.Add($"새 행 {Added.Count}");
            if (Removed.Count > 0) parts.Add($"없어진 행 {Removed.Count}");
            if (AddedColumns.Count > 0) parts.Add($"새 칸 {string.Join("·", AddedColumns)}");
            if (RemovedColumns.Count > 0) parts.Add($"없어진 칸 {string.Join("·", RemovedColumns)}");
            return $"{Tab}: {string.Join(", ", parts)}";
        }
    }

    // 레포 CSV와 시트에서 읽은 CSV의 행 단위 비교, 그리고 끌어온 글을 레포 파일 모양(줄 끝)으로 맞추기.
    internal static class SheetDiff
    {
        public static SheetTabDiff Compare(string tab, string local, string remote)
        {
            var diff = new SheetTabDiff { Tab = tab };
            string[] keys = SheetTabs.KeysOf(tab);

            if (!TryRows(local, keys, out List<string> localHeader, out Dictionary<string, (Dictionary<string, string> Row, int Order)> localRows, out string error) ||
                !TryRows(remote, keys, out List<string> remoteHeader, out Dictionary<string, (Dictionary<string, string> Row, int Order)> remoteRows, out error))
            {
                diff.Error = error;
                return diff;
            }

            foreach (string column in remoteHeader)
            {
                if (!localHeader.Contains(column))
                    diff.AddedColumns.Add(column);
            }

            foreach (string column in localHeader)
            {
                if (!remoteHeader.Contains(column))
                    diff.RemovedColumns.Add(column);
            }

            foreach (KeyValuePair<string, (Dictionary<string, string> Row, int Order)> pair in remoteRows)
            {
                if (!localRows.TryGetValue(pair.Key, out (Dictionary<string, string> Row, int Order) old))
                {
                    diff.Added.Add(Change(pair.Key, keys, pair.Value.Row));
                    continue;
                }

                SheetRowChange change = null;

                foreach (string column in remoteHeader)
                {
                    if (!localHeader.Contains(column))
                        continue;

                    string before = old.Row.TryGetValue(column, out string b) ? b : string.Empty;
                    string after = pair.Value.Row.TryGetValue(column, out string a) ? a : string.Empty;

                    if (before == after)
                        continue;

                    change ??= Change(pair.Key, keys, pair.Value.Row);
                    change.Cells.Add((column, before, after));
                }

                if (change != null)
                    diff.Changed.Add(change);
            }

            foreach (KeyValuePair<string, (Dictionary<string, string> Row, int Order)> pair in localRows)
            {
                if (!remoteRows.ContainsKey(pair.Key))
                    diff.Removed.Add(Change(pair.Key, keys, pair.Value.Row));
            }

            diff.Added.Sort((x, y) => remoteRows[x.Key].Order.CompareTo(remoteRows[y.Key].Order));
            diff.Changed.Sort((x, y) => remoteRows[x.Key].Order.CompareTo(remoteRows[y.Key].Order));
            diff.Removed.Sort((x, y) => localRows[x.Key].Order.CompareTo(localRows[y.Key].Order));
            diff.TextOnly = !diff.HasChanges && Normalize(local) != Normalize(remote);
            return diff;
        }

        // 시트 끝의 빈 행(칸이 모두 빈 줄, 예: ",,,,,")을 뺀다. 시트는 수식이나 서식이 아래로 늘어 있으면 그 행까지 읽히지만,
        // 시트의 CSV 다운로드는 끝의 빈 행을 넣지 않는다. 중간의 빈 행은 그대로 둔다.
        // 뺄 것이 없으면 글을 그대로 돌려주고, 뺐으면 원래 줄 끝(CRLF/LF)으로 잇되 마지막 줄바꿈은 두지 않는다(다운로드 모양).
        public static string TrimEmptyRows(string csv)
        {
            string original = csv ?? string.Empty;
            string eol = original.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = original.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int count = lines.Length;

            while (count > 1 && IsEmptyRow(lines[count - 1]))
                count--;

            // 마지막 줄바꿈 하나(빈 줄 하나)만 있었으면 빈 행이 아니다.
            if (count == lines.Length || (count == lines.Length - 1 && lines[lines.Length - 1].Length == 0))
                return original;

            return string.Join(eol, lines, 0, count);
        }

        private static bool IsEmptyRow(string line)
        {
            foreach (char c in line)
            {
                if (c != ',' && c != '"' && !char.IsWhiteSpace(c))
                    return false;
            }

            return true;
        }

        // 끌어온 글을 레포 파일의 줄 끝(CRLF/LF)과 마지막 줄바꿈 여부에 맞춘다. 시트 CSV 다운로드는 CRLF, 마지막 줄바꿈 없음이다.
        public static string MatchLineEndings(string remote, string local)
        {
            bool crlf = local.Contains("\r\n") || !local.Contains("\n");
            bool trailing = local.EndsWith("\n", StringComparison.Ordinal);
            string text = remote.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');

            if (crlf)
                text = text.Replace("\n", "\r\n");

            return trailing ? text + (crlf ? "\r\n" : "\n") : text;
        }

        public static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        private static SheetRowChange Change(string key, string[] keys, Dictionary<string, string> row)
        {
            var change = new SheetRowChange { Key = key };
            foreach (string column in keys)
                change.Keys[column] = row.TryGetValue(column, out string value) ? value : string.Empty;

            return change;
        }

        // CSV → 키 → (칸 이름 → 값, 순서). 키 칸이 모두 빈 행은 건너뛴다(시트의 빈 줄).
        private static bool TryRows(
            string csv,
            string[] keys,
            out List<string> header,
            out Dictionary<string, (Dictionary<string, string> Row, int Order)> rows,
            out string error)
        {
            header = new List<string>();
            rows = new Dictionary<string, (Dictionary<string, string> Row, int Order)>(StringComparer.Ordinal);
            error = null;
            List<string[]> table = Csv.Parse(csv ?? string.Empty);

            if (table.Count == 0)
            {
                error = "비어 있다.";
                return false;
            }

            foreach (string name in table[0])
                header.Add(name.Trim());

            foreach (string key in keys)
            {
                if (!header.Contains(key))
                {
                    error = $"머리칸에 '{key}'이 없다.";
                    return false;
                }
            }

            for (int r = 1; r < table.Count; r++)
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int c = 0; c < header.Count; c++)
                {
                    if (header[c].Length > 0 && !row.ContainsKey(header[c]))
                        row[header[c]] = c < table[r].Length ? table[r][c].Trim() : string.Empty;
                }

                var keyText = new StringBuilder();
                bool empty = true;

                foreach (string key in keys)
                {
                    string value = row[key];
                    empty &= value.Length == 0;
                    keyText.Append(keyText.Length > 0 ? "\n" : string.Empty).Append(value);
                }

                if (empty)
                    continue;

                string id = keyText.ToString();

                if (rows.ContainsKey(id))
                {
                    error = $"같은 키의 행이 두 번 있다: {id.Replace('\n', ' ')}.";
                    return false;
                }

                rows.Add(id, (row, r));
            }

            return true;
        }

        private static string Normalize(string text) => (text ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n');
    }

}
