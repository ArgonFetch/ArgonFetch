using ArgonFetch.Application.Queries;

namespace ArgonFetch.Tests
{
    public class SubtitleConversionTests
    {
        [Fact]
        public void AsWebVtt_LeavesWebVttAlone()
        {
            const string vtt = "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nhello\n";

            Assert.Equal(vtt, StreamSubtitleQueryHandler.AsWebVtt(vtt));
        }

        [Fact]
        public void AsWebVtt_LeavesWebVttAloneBehindAByteOrderMark()
        {
            const string vtt = "﻿WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nhello\n";

            Assert.Equal(vtt, StreamSubtitleQueryHandler.AsWebVtt(vtt));
        }

        [Fact]
        public void AsWebVtt_ConvertsJson3()
        {
            const string json = """
                {"events":[
                  {"tStartMs":1360,"dDurationMs":1680,"segs":[{"utf8":"hello "},{"utf8":"world"}]},
                  {"tStartMs":18640,"dDurationMs":3240,"segs":[{"utf8":"second line"}]}
                ]}
                """;

            var vtt = StreamSubtitleQueryHandler.AsWebVtt(json);

            Assert.StartsWith("WEBVTT", vtt);
            Assert.Contains("00:00:01.360 --> 00:00:03.040", vtt);
            Assert.Contains("hello world", vtt);
            Assert.Contains("00:00:18.640 --> 00:00:21.880", vtt);
        }

        [Fact]
        public void AsWebVtt_SkipsJson3EventsThatOnlyCarryTiming()
        {
            const string json = """
                {"events":[
                  {"tStartMs":0,"dDurationMs":1000},
                  {"tStartMs":2000,"dDurationMs":1000,"segs":[{"utf8":"\n"}]},
                  {"tStartMs":4000,"dDurationMs":1000,"segs":[{"utf8":"kept"}]}
                ]}
                """;

            var vtt = StreamSubtitleQueryHandler.AsWebVtt(json);

            Assert.Single(vtt.Split("-->").Skip(1));
            Assert.Contains("kept", vtt);
        }

        [Fact]
        public void AsWebVtt_ConvertsTranscriptXml()
        {
            const string xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <transcript>
                  <text start="1.36" dur="1.68">hello &amp;amp; goodbye</text>
                  <text start="18.64" dur="3.24">second line</text>
                </transcript>
                """;

            var vtt = StreamSubtitleQueryHandler.AsWebVtt(xml);

            Assert.StartsWith("WEBVTT", vtt);
            Assert.Contains("00:00:01.360 --> 00:00:03.040", vtt);
            Assert.Contains("hello & goodbye", vtt);
            Assert.Contains("00:00:18.640 --> 00:00:21.880", vtt);
        }

        [Fact]
        public void AsWebVtt_ReadsDecimalsTheSameWhateverTheServerLocale()
        {
            var german = new System.Globalization.CultureInfo("de-DE");
            var before = System.Globalization.CultureInfo.CurrentCulture;

            System.Globalization.CultureInfo.CurrentCulture = german;

            try
            {
                var vtt = StreamSubtitleQueryHandler.AsWebVtt(
                    """<transcript><text start="1.5" dur="2.5">hallo</text></transcript>""");

                Assert.Contains("00:00:01.500 --> 00:00:04.000", vtt);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = before;
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a subtitle file at all")]
        [InlineData("{\"nothing\":true}")]
        [InlineData("<transcript></transcript>")]
        public void AsWebVtt_HandsBackWhatItCannotRead(string body)
        {
            // Better the client sees the source's own answer than an empty WEBVTT header that
            // looks like a working track with nothing to say.
            Assert.Equal(body, StreamSubtitleQueryHandler.AsWebVtt(body));
        }
    }
}
