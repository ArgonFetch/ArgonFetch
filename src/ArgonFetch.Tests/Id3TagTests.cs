using ArgonFetch.Application.Services;
using ArgonFetch.Infrastructure.Services;
using System.Text;

namespace ArgonFetch.Tests
{
    public class Id3TagTests
    {
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

        [Fact]
        public void Build_DeclaresItsOwnSize_SoAPlayerFindsTheAudioAfterIt()
        {
            var tag = Id3Tag.Build(new MediaTags("Warteliste", "Waifu Jackson"), Jpeg)!;

            var declared = (tag[6] << 21) | (tag[7] << 14) | (tag[8] << 7) | tag[9];

            Assert.Equal("ID3", Encoding.ASCII.GetString(tag, 0, 3));
            Assert.Equal(3, tag[3]);
            Assert.Equal(tag.Length - 10, declared);
        }

        [Fact]
        public void Build_EmbedsAJpegCoverAsTheFrontCover()
        {
            var tag = Id3Tag.Build(new MediaTags("Warteliste", "Waifu Jackson"), Jpeg)!;
            var text = Encoding.Latin1.GetString(tag);

            Assert.Contains("APIC", text);
            Assert.Contains("image/jpeg\0\u0003\0", text);
            Assert.EndsWith(Encoding.Latin1.GetString(Jpeg), text);
        }

        [Fact]
        public void Build_LeavesOutACoverItCannotName()
        {
            byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8];

            var tag = Id3Tag.Build(new MediaTags("Warteliste", null), webp)!;

            Assert.DoesNotContain("APIC", Encoding.Latin1.GetString(tag));
        }

        [Fact]
        public void Build_WritesNothingWhenThereIsNothingToSay()
        {
            Assert.Null(Id3Tag.Build(MediaTags.None, null));
        }
    }
}
