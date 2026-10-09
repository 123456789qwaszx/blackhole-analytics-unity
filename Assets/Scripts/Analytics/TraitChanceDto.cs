using System;

namespace BlackHole.Analytics
{
    // 한 종류에 한 성질이 붙을 확률.
    // enemyId·traitId는 EnemyType·EnemyTraitType 이름의 첫 글자를 소문자로 쓴다(asteroid, golden …).
    [Serializable]
    public sealed class TraitChanceDto
    {
        public string enemyId;
        public string traitId;
        // 0 ~ 1.
        public float chance;
    }
}
