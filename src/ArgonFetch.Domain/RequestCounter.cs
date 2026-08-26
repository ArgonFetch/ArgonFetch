namespace ArgonFetch.Domain
{
    /// <summary>
    /// Running total of media requests this installation has served.
    /// <para>
    /// The count is only ever incremented and read as a total, so there is no reason to keep
    /// one record per request - that would grow without bound and turn a counter read into an
    /// aggregate over the whole history.
    /// </para>
    /// </summary>
    public class RequestCounter
    {
        public long TotalRequests { get; set; }

        public DateTime LastRequestAtUtc { get; set; }
    }
}
