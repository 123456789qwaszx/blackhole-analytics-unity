using System;

namespace BlackHole.Analytics
{
    // 한 종류의 처치 수.
    // enemyId는 EnemyType 이름의 첫 글자를 소문자로 쓴다(asteroid, planet, star, comet).
    [Serializable]
    public sealed class EnemyKillDto
    {
        public string enemyId;
        public int count;
    }
}
