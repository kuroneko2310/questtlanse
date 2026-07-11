using Xunit;

namespace QuestTranslationAddon.Tests
{
    public class ProtectedTextPayloadTests
    {
        [Fact]
        public void MixedJapaneseAndEnglishRestoresProtectedContentExactly()
        {
            const string original = "Deliver 10 units of SteelDef_01 to 日本語拠点. Signal Quest123.Added.";

            ProtectedTextPayload payload;
            Assert.True(ProtectedTextPayload.TryCreate(original, "Japanese", out payload));

            var translatedPayload = payload.ProtectedText
                .Replace("Deliver", "届ける")
                .Replace(" units of ", " 個の ")
                .Replace(" to ", " を ")
                .Replace("Signal ", "信号 ");

            string restored;
            Assert.True(payload.TryRestore(translatedPayload, out restored));
            Assert.Contains("10", restored);
            Assert.Contains("SteelDef_01", restored);
            Assert.Contains("日本語拠点", restored);
            Assert.Contains("Quest123.Added", restored);
            Assert.DoesNotContain("QTSHIELD_", restored);
            Assert.DoesNotContain("__PH", restored);
        }

        [Fact]
        public void DigitsTouchingJapaneseAndHyphenatedIdsAreShielded()
        {
            const string original = "Recover beacon abc-123-def from 第123号区域.";

            ProtectedTextPayload payload;
            Assert.True(ProtectedTextPayload.TryCreate(original, "Japanese", out payload));
            Assert.DoesNotContain("abc-123-def", payload.ProtectedText);
            Assert.DoesNotContain("123", payload.ProtectedText);

            string restored;
            Assert.True(payload.TryRestore(payload.ProtectedText.Replace("Recover beacon", "ビーコンを回収"), out restored));
            Assert.Contains("abc-123-def", restored);
            Assert.Contains("第123号区域", restored);
        }

        [Fact]
        public void MissingTemporaryMarkerRejectsEntireTranslation()
        {
            const string original = "Bring 25 medicine to 日本語拠点.";

            ProtectedTextPayload payload;
            Assert.True(ProtectedTextPayload.TryCreate(original, "Japanese", out payload));
            Assert.NotEmpty(payload.Tokens);

            var damaged = payload.ProtectedText.Replace(payload.Tokens[0], string.Empty);
            string restored;

            Assert.False(payload.TryRestore(damaged, out restored));
            Assert.Equal(original, restored);
        }

        [Fact]
        public void DuplicatedTemporaryMarkerRejectsEntireTranslation()
        {
            const string original = "Escort 3 colonists to 日本語拠点.";

            ProtectedTextPayload payload;
            Assert.True(ProtectedTextPayload.TryCreate(original, "Japanese", out payload));
            Assert.NotEmpty(payload.Tokens);

            var damaged = payload.ProtectedText + payload.Tokens[0];
            string restored;

            Assert.False(payload.TryRestore(damaged, out restored));
            Assert.Equal(original, restored);
        }

        [Fact]
        public void AddedNumberRejectsTranslation()
        {
            const string original = "Deliver 10 meals.";

            ProtectedTextPayload payload;
            Assert.True(ProtectedTextPayload.TryCreate(original, "Japanese", out payload));

            string restored;
            Assert.False(payload.TryRestore(payload.ProtectedText + " 999", out restored));
            Assert.Equal(original, restored);
        }

        [Fact]
        public void StandaloneQuestSignalIsNeverSentForTranslation()
        {
            ProtectedTextPayload payload;
            Assert.False(ProtectedTextPayload.TryCreate("Quest123.Added", "Japanese", out payload));
            Assert.Null(payload);
        }

        [Fact]
        public void LeakedParentPlaceholderRejectsTranslation()
        {
            const string original = "Defend 日本語拠点 for 2 days.";

            ProtectedTextPayload payload;
            Assert.True(ProtectedTextPayload.TryCreate(original, "Japanese", out payload));

            string restored;
            Assert.False(payload.TryRestore(payload.ProtectedText + " __PH99__", out restored));
            Assert.Equal(original, restored);
        }
    }
}
