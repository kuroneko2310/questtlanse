using AutoTranslation.Utilities;
using Xunit;

namespace AutoTranslation.Tests
{
    public class HelpersFormatTests
    {
        [Fact]
        public void ExtraOutOfRangePlaceholderIsRemovedWithoutThrowing()
        {
            var result = "Bring {0} to {1}.".FitFormat(1);

            Assert.Equal("Bring {0} to .", result);
        }

        [Fact]
        public void MissingPlaceholdersAreNotSynthesized()
        {
            var result = "Bring the medicine.".FitFormat(3);

            Assert.Equal("Bring the medicine.", result);
            Assert.DoesNotContain("|{", result);
        }

        [Fact]
        public void DuplicateValidPlaceholderIsPreserved()
        {
            var result = "{0} and {0}".FitFormat(1);

            Assert.Equal("{0} and {0}", result);
        }

        [Fact]
        public void ZeroArgumentsRemovesOnlyIndexedFormatPlaceholders()
        {
            var result = "Quest123.Added 第456号 {0} [PawnName]".FitFormat(0);

            Assert.Equal("Quest123.Added 第456号  [PawnName]", result);
        }

        [Fact]
        public void NullAndEmptyInputsRemainSafe()
        {
            string nullText = null;

            Assert.Null(nullText.FitFormat(1));
            Assert.Equal(string.Empty, string.Empty.FitFormat(1));
        }

        [Fact]
        public void NegativeArgumentCountIsTreatedAsZero()
        {
            var result = "Keep ID-77 but remove {0}.".FitFormat(-1);

            Assert.Equal("Keep ID-77 but remove .", result);
        }
    }
}
