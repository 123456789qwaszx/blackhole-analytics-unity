using System;

namespace BlackHole.Analytics
{
    // 한 종류의 처치 수.
    [Serializable]
    public sealed class EnemyKillDto
    {
        public string enemyId;
        public int count;
    }
}
