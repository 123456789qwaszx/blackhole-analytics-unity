using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BlackHole.Analytics.Tests
{
    // 계약 예시 JSON을 읽고 견주는 도구.
    internal static class JsonSamples
    {
        public const string BattleStats = "Assets/Scripts/Analytics/battle-stats.sample.json";
        public const string ErrorResponse = "Assets/Scripts/Analytics/Transport/error-response.sample.json";

        public static string Load(string path)
        {
            var sample = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.IsNotNull(sample, $"계약 예시가 없다: {path}");
            return sample.text;
        }

        // 문자열 밖의 공백을 지운다. JsonUtility.ToJson(prettyPrint 없음)과 견줄 수 있게.
        public static string Minify(string json)
        {
            var builder = new StringBuilder(json.Length);
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    builder.Append(c);

                    if (c == '\\')
                        builder.Append(json[++i]);
                    else if (c == '"')
                        inString = false;
                }
                else if (c == '"')
                {
                    inString = true;
                    builder.Append(c);
                }
                else if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
