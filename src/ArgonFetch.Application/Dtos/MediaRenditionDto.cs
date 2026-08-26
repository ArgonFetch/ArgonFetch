using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    public class MediaRenditionDto
    {
        public required string Key { get; set; }

        public required string Label { get; set; }

        public string? Description { get; set; }

        public required string FileExtension { get; set; }

        public required string MimeType { get; set; }

        public UrlType UrlType { get; set; }

        public long? FileSizeBytes { get; set; }

        public int? Height { get; set; }

        public double? Bitrate { get; set; }

        public string? ConvertTo { get; set; }
    }
}
