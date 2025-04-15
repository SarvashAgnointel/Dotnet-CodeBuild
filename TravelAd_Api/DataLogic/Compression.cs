using System.IO.Compression;
using System.Text;

namespace TravelAd_Api.DataLogic
{
    public class Compression
    {
        public static string CompressString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            using (var outputStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
                {
                    gzipStream.Write(inputBytes, 0, inputBytes.Length);
                }
                return Convert.ToBase64String(outputStream.ToArray());
            }
        }

        // Decompress a string
        public static string DecompressString(string compressedInput)
        {
            if (string.IsNullOrEmpty(compressedInput))
                return compressedInput;

            byte[] compressedBytes = Convert.FromBase64String(compressedInput);
            using (var inputStream = new MemoryStream(compressedBytes))
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                gzipStream.CopyTo(outputStream);
                return Encoding.UTF8.GetString(outputStream.ToArray());
            }
        }
    }
}
