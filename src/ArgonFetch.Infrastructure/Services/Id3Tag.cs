using ArgonFetch.Application.Services;
using System.Buffers.Binary;
using System.Text;

namespace ArgonFetch.Infrastructure.Services
{
    // ID3v2.3, not 2.4: many players and Explorer only read the older revision.
    public static class Id3Tag
    {
        public static byte[]? Build(MediaTags? tags, byte[]? cover)
        {
            var frames = new MemoryStream();

            WriteText(frames, "TIT2", tags?.Title);
            WriteText(frames, "TPE1", tags?.Artist);
            WriteText(frames, "TPE2", tags?.Artist);
            WritePicture(frames, cover);

            if (frames.Length == 0)
                return null;

            var tag = new MemoryStream();
            tag.Write("ID3"u8);
            tag.Write([3, 0, 0]);
            tag.Write(SyncSafe((int)frames.Length));
            frames.WriteTo(tag);

            return tag.ToArray();
        }

        // UTF-16 with a BOM, the one Unicode encoding 2.3 has: titles like 言って。 need it.
        private static void WriteText(Stream frames, string id, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            byte[] body = [1, .. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text.Trim())];

            WriteFrame(frames, id, body);
        }

        // ponytail: JPEG and PNG only, the two formats APIC readers agree on. A WebP thumbnail
        // is left out rather than converted; add an FFmpeg pass if those turn out common.
        private static void WritePicture(Stream frames, byte[]? cover)
        {
            var mime = cover switch
            {
                [0xFF, 0xD8, ..] => "image/jpeg",
                [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => "image/png",
                _ => null,
            };

            if (mime is null)
                return;

            // Latin-1 text, the MIME type, picture type 3 (front cover), an empty description.
            byte[] body = [0, .. Encoding.ASCII.GetBytes(mime), 0, 3, 0, .. cover!];

            WriteFrame(frames, "APIC", body);
        }

        private static void WriteFrame(Stream frames, string id, byte[] body)
        {
            var size = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, body.Length);

            frames.Write(Encoding.ASCII.GetBytes(id));
            frames.Write(size);
            frames.Write([0, 0]);
            frames.Write(body);
        }

        private static byte[] SyncSafe(int size) =>
        [
            (byte)((size >> 21) & 0x7F),
            (byte)((size >> 14) & 0x7F),
            (byte)((size >> 7) & 0x7F),
            (byte)(size & 0x7F),
        ];
    }
}
