using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Core.SaveSystem
{
    /// <summary>
    /// for saving any .bin files, but firstly created for the texture saving
    /// sits in the save together with save.json
    /// </summary>
    internal static class BlobFile
    {
        // "CBLB". lets us reject a file that isn't ours before we try to parse it
        private const uint Magic = 0x424C4243;

        public static void Write(string path, Dictionary<string, byte[]> blobs, int version)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            
            writer.Write(Magic);
            writer.Write(version);
            writer.Write(blobs.Count);

            foreach (var pair in blobs)
            {
                byte[] packed = Deflate(pair.Value);

                writer.Write(pair.Key);
                writer.Write(pair.Value.Length);
                writer.Write(packed.Length);
                writer.Write(packed);
            }
        }

        public static Dictionary<string, byte[]> Read(string path, int expectedVersion)
        {
            var result = new Dictionary<string, byte[]>();
            if(!File.Exists(path)) return result;
            
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            
            if (reader.ReadUInt32() != Magic) 
                throw new InvalidDataException("[Save] not a blob file");
            
            int version = reader.ReadInt32();
            if (version != expectedVersion)
                throw new InvalidDataException($"[Save] blob version {version}, expected version {expectedVersion}");
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                int rawLength = reader.ReadInt32();
                int packedLength = reader.ReadInt32();
                byte[] packed = reader.ReadBytes(packedLength);
                result[key] = Inflate(packed, rawLength);   
            }

            return result;
        }

        private static byte[] Inflate(byte[] packed, int rawLength)
        {
            var raw = new byte[rawLength];
            using var input =  new MemoryStream(packed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);

            int read = 0;
            while (read < rawLength)
            {
                int got = deflate.Read(raw, read, raw.Length - read);
                if (got == 0) break;
                read += got; 
            }
            return raw;
        }

        private static byte[] Deflate(byte[] raw)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream
                       (output, CompressionLevel.Optimal, true)) 
                deflate.Write(raw, 0, raw.Length);
            return output.ToArray();
        }
    }
}