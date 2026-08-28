using System.Net;
using System.Net.Http.Headers;
using ArgonFetch.Application.Services;
using ArgonFetch.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArgonFetch.Tests
{
    public class AcceleratedDownloadServiceTests
    {
        // Larger than the 2 MB minimum chunk, so a download of it is split and reassembled
        // rather than arriving in one piece.
        private const int FileSize = 5 * 1024 * 1024;

        [Fact]
        public async Task StreamWithAcceleration_ReassemblesTheFileInOrder()
        {
            var source = Bytes(FileSize);
            var output = new MemoryStream();

            await Service(source).StreamWithAccelerationAsync("https://example.test/file", output);

            Assert.Equal(source, output.ToArray());
        }

        [Fact]
        public async Task StreamWithAcceleration_WritesOnlyTheRequestedWindow()
        {
            // What a client seeking into a file asks for. Writing anything else makes the body
            // disagree with the Content-Range already announced.
            var source = Bytes(FileSize);
            var output = new MemoryStream();
            var window = new ByteRange(1_000_000, 3_500_000);

            await Service(source).StreamWithAccelerationAsync("https://example.test/file", output, range: window);

            Assert.Equal(source[(int)window.From..((int)window.To + 1)], output.ToArray());
        }

        [Fact]
        public async Task StreamWithAcceleration_AsksForOffsetsIntoTheFileNotIntoTheWindow()
        {
            // A window near the end must fetch the end of the resource. Asking from zero would
            // return the right number of bytes from entirely the wrong place.
            var source = Bytes(FileSize);
            var output = new MemoryStream();
            var window = new ByteRange(FileSize - 3_000_000, FileSize - 1);

            var handler = new RangeServer(source);
            await Service(source, handler).StreamWithAccelerationAsync("https://example.test/file", output, range: window);

            Assert.Equal(source[(int)window.From..], output.ToArray());
            Assert.All(handler.RangesRequested.Skip(1), r => Assert.True(r.From >= window.From));
        }

        [Fact]
        public async Task StreamWithAcceleration_FallsBackToOneConnectionWhenRangesAreRefused()
        {
            var source = Bytes(FileSize);
            var output = new MemoryStream();

            await Service(source, new RangeServer(source) { AcceptsRanges = false })
                .StreamWithAccelerationAsync("https://example.test/file", output);

            Assert.Equal(source, output.ToArray());
        }

        [Fact]
        public async Task StreamWithAcceleration_FallsBackWhenTheProbeFails()
        {
            var source = Bytes(FileSize);
            var output = new MemoryStream();

            await Service(source, new RangeServer(source) { FailProbe = true })
                .StreamWithAccelerationAsync("https://example.test/file", output);

            Assert.Equal(source, output.ToArray());
        }

        [Fact]
        public async Task StreamWithAcceleration_DoesNotRetryOnceBytesHaveBeenWritten()
        {
            // Retrying here would append a second copy to a response that already holds part of
            // one, which is worse than the failure it is trying to hide.
            var source = Bytes(FileSize);
            var output = new MemoryStream();
            var handler = new RangeServer(source) { FailAfterChunks = 1 };

            await Assert.ThrowsAnyAsync<Exception>(() =>
                Service(source, handler).StreamWithAccelerationAsync("https://example.test/file", output));

            Assert.True(output.Length > 0, "the first chunk should have reached the output");
            Assert.True(output.Length < source.Length, "the download should not have completed");
        }

        [Fact]
        public async Task GetContentLength_ReportsWhatTheSourceDeclares()
        {
            var source = Bytes(FileSize);

            Assert.Equal(FileSize, await Service(source).GetContentLengthAsync("https://example.test/file"));
        }

        [Fact]
        public async Task GetContentLength_IsNullWhenTheSourceCannotBeReached()
        {
            // An answer rather than a fault: the caller declares no length and sends chunked.
            var service = Service(Bytes(1024), new RangeServer(Bytes(1024)) { FailProbe = true });

            Assert.Null(await service.GetContentLengthAsync("https://example.test/file"));
        }

        private static AcceleratedDownloadService Service(byte[] source, RangeServer? handler = null) =>
            new(new StubClients(handler ?? new RangeServer(source)),
                NullLogger<AcceleratedDownloadService>.Instance);

        /// <summary>Deterministic content, so a misplaced chunk shows up as a difference.</summary>
        private static byte[] Bytes(int count)
        {
            var bytes = new byte[count];

            for (var i = 0; i < count; i++)
                bytes[i] = (byte)(i % 251);

            return bytes;
        }

        private sealed class StubClients(HttpMessageHandler handler) : IMediaHttpClients
        {
            private readonly HttpClient _client = new(handler);

            public HttpClient For(string? proxy) => _client;
        }

        /// <summary>A source that serves byte ranges, and can be made to misbehave.</summary>
        private sealed class RangeServer(byte[] content) : HttpMessageHandler
        {
            private int _chunksServed;

            public bool AcceptsRanges { get; init; } = true;
            public bool FailProbe { get; init; }
            public int? FailAfterChunks { get; init; }

            public List<ByteRange> RangesRequested { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var range = request.Headers.Range?.Ranges.FirstOrDefault();
                var isProbe = range?.From == 0 && range?.To == 0;

                if (isProbe && FailProbe)
                    throw new HttpRequestException("probe refused");

                if (!AcceptsRanges)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(content)
                    });
                }

                if (!isProbe && FailAfterChunks is { } limit && Interlocked.Increment(ref _chunksServed) > limit)
                    throw new HttpRequestException("source gave up");

                var from = range?.From ?? 0;
                var to = range?.To ?? content.Length - 1;

                if (!isProbe)
                    RangesRequested.Add(new ByteRange(from, to));

                var slice = content[(int)from..(int)(to + 1)];

                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(slice)
                };

                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, content.Length);

                return Task.FromResult(response);
            }
        }
    }
}
