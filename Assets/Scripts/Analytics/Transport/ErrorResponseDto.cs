using System;
using System.Collections.Generic;

namespace BlackHole.Analytics.Transport
{
    // 서버가 요청을 받지 않을 때 보내는 본문(예: error-response.sample.json).
    // 서버와의 JSON 계약이라 필드 이름이 곧 JSON 키다(camelCase).
    [Serializable]
    public sealed class ErrorResponseDto
    {
        // 이유를 나타내는 코드(예: INVALID_FIELD). 분기는 이것으로 한다.
        public string code;
        // 사람이 읽는 설명.
        public string message;
        // 필드마다의 이유. 특정 필드 탓이 아니면 []다.
        public List<FieldErrorDto> errors = new();
    }
}
