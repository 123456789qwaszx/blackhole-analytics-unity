using System;

namespace BlackHole.Analytics.Transport
{
    // 필드 하나가 받아들여지지 않은 이유.
    [Serializable]
    public sealed class FieldErrorDto
    {
        public string field;
        public string reason;
    }
}
