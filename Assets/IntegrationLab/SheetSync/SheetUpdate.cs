using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntegrationLab
{
    // 기획 시트에 쓸 칸 하나: 노드 비용(NodeCost.Cost) 또는 효과(NodeEffects.Value).
    // Before는 시트에 있어야 할 지금 값(처음 변경의 이전 값), After는 쓸 값(마지막 변경의 이후 값).
    internal sealed class SheetUpdate
    {
        public string Path;
        public string Tab;
        public string NodeId;
        public int Rank;
        public string StatId;
        public string Column;
        public double Before;
        public double After;
        public readonly List<string> RecordIds = new List<string>();

        public string Describe() => $"{Tab} {NodeId} Rank {Rank}{(StatId != null ? " " + StatId : string.Empty)} · {Column}";

        // 웹 앱 write 요청의 한 칸.
        public JsonObject ToRequest()
        {
            var keys = new JsonObject { { "NodeId", NodeId }, { "Rank", Rank.ToString(CultureInfo.InvariantCulture) } };
            if (StatId != null)
                keys.Add("StatId", StatId);

            return new JsonObject
            {
                { "tab", Tab },
                { "keys", keys },
                { "column", Column },
                { "value", After },
                { "expected", Before },
            };
        }
    }

}
