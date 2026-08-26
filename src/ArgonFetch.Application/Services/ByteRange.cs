namespace ArgonFetch.Application.Services
{
    public readonly record struct ByteRange(long From, long To)
    {
        public long Length => To - From + 1;
    }

    public enum RangeRequest
    {
        None,

        /// <summary>A range that can be served: reply 206 with the resolved window.</summary>
        Satisfiable,

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

            var value = header.Trim();

            if (!value.StartsWith(unitPrefix, StringComparison.OrdinalIgnoreCase))
                return RangeRequest.None;

            var spec = value[unitPrefix.Length..].Trim();

            if (spec.Contains(','))
                return RangeRequest.None;

            var separator = spec.IndexOf('-');

            if (separator < 0)
                return RangeRequest.None;

            var fromText = spec[..separator].Trim();
            var toText = spec[(separator + 1)..].Trim();

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

            if (toText.Length == 0)
            {
                range = new ByteRange(from, totalLength - 1);

                return RangeRequest.Satisfiable;
            }

            if (!long.TryParse(toText, out var to) || to < from)
                return RangeRequest.None;

            range = new ByteRange(from, Math.Min(to, totalLength - 1));

            return RangeRequest.Satisfiable;
        }
    }
}
