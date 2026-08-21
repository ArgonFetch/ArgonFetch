namespace ArgonFetch.Application.Dtos
{
    public class AppInfoDto
    {
        public required string Version { get; set; }
        public required bool IsHealthy { get; set; }
        public required string Environment { get; set; }

        /// <summary>
        /// What the server is busy with, or null when it is serving normally. Clients show it
        /// as a maintenance screen instead of letting fetches fail in confusing ways.
        /// </summary>
        public string? Maintenance { get; set; }
    }
}
