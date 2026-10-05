using System;

namespace BlackHole.Analytics
{
    // 산 노드 하나와 그 Rank.
    // 진행 저장의 NodeRankSaveData와 모양은 같지만 따로 둔다 — 저장 형식과 서버 계약은 바뀌는 이유가 다르다.
    [Serializable]
    public sealed class NodeRankDto
    {
        public string nodeId;
        public int rank;
    }
}
