using System.Collections.Generic;
using System.Text;

namespace IntegrationLab
{
    // 스프레드시트가 주고받는 CSV(RFC 4180): 쉼표로 칸을 나누고, 쉼표·큰따옴표·줄바꿈이 든 칸은 큰따옴표로 감싼다.
    // 실행 중의 노드 콘텐츠 읽기(NodeContentCsv)가 쓴다(노드 데이터는 시트에서 내려받은 CSV가 원본이다).
    public static class Csv
    {
        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            int start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];

                if (quoted)
                {
                    if (c != '"')
                        cell.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"')
                        cell.Append(text[++i]);
                    else
                        quoted = false;

                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        break;
                    case ',':
                        row.Add(cell.ToString());
                        cell.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(row.ToArray());
                        row.Clear();
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row.ToArray());
            }

            return rows;
        }

        public static string Write(IEnumerable<IReadOnlyList<string>> rows)
        {
            var text = new StringBuilder();

            foreach (IReadOnlyList<string> row in rows)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (i > 0)
                        text.Append(',');

                    text.Append(Escape(row[i] ?? string.Empty));
                }

                text.Append('\n');
            }

            return text.ToString();
        }

        private static string Escape(string cell) =>
            cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? cell : "\"" + cell.Replace("\"", "\"\"") + "\"";
    }
}
