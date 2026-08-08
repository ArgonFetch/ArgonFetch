namespace ArgonFetch.Domain
{
    /// <summary>
    /// Running total of media requests this installation has served.
    /// <para>
    /// A single row, updated in place. The count is only ever incremented and read as a
    /// total, so there is no reason to store one row per request - that would grow without
    /// bound and turn a counter read into an aggregate over the whole table.
    /// </para>
    /// </summary>
    public class RequestCounter
    {
        public int Id { get; set; }

        public long TotalRequests { get; set; }

        public DateTime LastRequestAtUtc { get; set; }
    }
}
