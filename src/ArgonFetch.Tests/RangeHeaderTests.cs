using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class RangeHeaderTests
    {
        private const long Total = 1000;

        [Theory]
        [InlineData("bytes=0-499", 0, 499)]
        [InlineData("bytes=500-", 500, 999)]
        [InlineData("bytes=-200", 800, 999)]
        [InlineData("bytes=0-", 0, 999)]
        [InlineData("bytes=999-999", 999, 999)]
        // An end past the resource is clamped, which is what clients expect.
        [InlineData("bytes=900-5000", 900, 999)]
        // A suffix longer than the resource is the whole resource.
        [InlineData("bytes=-5000", 0, 999)]
        [InlineData(" bytes=10-20 ", 10, 20)]
        [InlineData("BYTES=10-20", 10, 20)]
        public void Parse_ResolvesSatisfiableRanges(string header, long from, long to)
        {
            Assert.Equal(RangeRequest.Satisfiable, RangeHeader.Parse(header, Total, out var range));
            Assert.Equal(new ByteRange(from, to), range);
            Assert.Equal(to - from + 1, range.Length);
        }

        [Fact]
        public void Parse_ReportsAStartPastTheEndAsUnsatisfiable()
        {
            Assert.Equal(RangeRequest.Unsatisfiable, RangeHeader.Parse("bytes=1000-1200", Total, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("items=0-10")]      // not a byte range
        [InlineData("bytes=abc-def")]
        [InlineData("bytes=500")]       // no separator
        [InlineData("bytes=300-200")]   // end before start
        [InlineData("bytes=-0")]        // a zero-length suffix asks for nothing
        [InlineData("bytes = 10-20")]   // a space around the separator is not the defined syntax
        [InlineData("bytes=0-99,200-299")] // multipart, which this server does not serve
        public void Parse_IgnoresWhatItCannotHonour(string? header)
        {
            // Ignored means the whole resource is served, which is always a valid answer.
            Assert.Equal(RangeRequest.None, RangeHeader.Parse(header, Total, out _));
        }

        [Fact]
        public void Parse_IgnoresRangesWhenTheLengthIsUnknown()
        {
            // Without a length there is nothing to resolve "bytes=-200" or an open end against.
            Assert.Equal(RangeRequest.None, RangeHeader.Parse("bytes=0-99", 0, out _));
        }
    }
}
