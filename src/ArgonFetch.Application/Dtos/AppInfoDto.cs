namespace ArgonFetch.Application.Dtos
{
    public class AppInfoDto
    {
        public required string Version { get; set; }
        public required bool IsHealthy { get; set; }
        public required string Environment { get; set; }
    }
}
