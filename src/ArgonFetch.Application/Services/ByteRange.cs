namespace ArgonFetch.Application.Services
{
    /// <summary>A resolved byte range, inclusive at both ends as HTTP defines it.</summary>
    public readonly record struct ByteRange(long From, long To)
    {
        public long Length => To - From + 1;
    }

    /// <summary>What a Range header asked for, once weighed against the resource's length.</summary>
    public enum RangeRequest
    {
        /// <summary>No range asked for, or one this server does not honour. Serve the whole thing.</summary>
        None,

        /// <summary>A range that can be served: reply 206 with the resolved window.</summary>
        Satisfiable,

        /// <summary>A range that starts past the end of the resource: reply 416.</summary>
        Unsatisfiable
    }

    /// <summary>
    /// Reads the Range request header.
    /// <para>
    /// Only single ranges are honoured. Multipart responses exist in the specification and
    /// almost nothing asks for them - players and download managers ask for one window at a
    /// time - so a multi-range request is served whole rather than half-answered.
    /// </para>
    /// </summary>
    public static class RangeHeader
    {
        public static RangeRequest Parse(string? header, long totalLength, out ByteRange range)
        {
            range = default;

            if (string.IsNullOrWhiteSpace(header) || totalLength <= 0)
                return RangeRequest.None;

            const string unitPrefix = "bytes=";

            // Surrounding whitespace is the transport's, not the client's. Anything else out
            // of shape - including a space around the "=" - is left unhonoured rather than
            // guessed at.
            var value = header.Trim();

            if (!value.StartsWith(unitPrefix, StringComparison.OrdinalIgnoreCase))
                return RangeRequest.None;

            var spec = value[unitPrefix.Length..].Trim();

            // More than one range asked for; serving the first would be a lie by omission.
            if (spec.Contains(','))
                return RangeRequest.None;

            var separator = spec.IndexOf('-');

            if (separator < 0)
                return RangeRequest.None;

            var fromText = spec[..separator].Trim();
            var toText = spec[(separator + 1)..].Trim();

            // "bytes=-500" asks for the last 500 bytes rather than a range starting at zero.
            if (fromText.Length == 0)
            {
                if (!long.TryParse(toText, out var suffixLength) || suffixLength <= 0)
                    return RangeRequest.None;

                var start = Math.Max(0, totalLength - suffixLength);
                range = new ByteRange(start, totalLength - 1);

                return RangeRequest.Satisfiable;
            }

            if (!long.TryParse(fromText, out var from) || from < 0)
                return RangeRequest.None;

            if (from >= totalLength)
                return RangeRequest.Unsatisfiable;

            // "bytes=500-" runs to the end.
            if (toText.Length == 0)
            {
                range = new ByteRange(from, totalLength - 1);

                return RangeRequest.Satisfiable;
            }

            if (!long.TryParse(toText, out var to) || to < from)
                return RangeRequest.None;

            // An end past the resource is clamped rather than refused, which is what the
            // specification asks for and what every client expects.
            range = new ByteRange(from, Math.Min(to, totalLength - 1));

            return RangeRequest.Satisfiable;
        }
    }
}
