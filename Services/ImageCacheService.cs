using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NovaStreamMobile.Services
{
    public class ImageCacheService
    {
        private readonly string _cacheDirectory;
        private readonly HttpClient _httpClient;

        public ImageCacheService()
        {
            _cacheDirectory = Path.Combine(FileSystem.CacheDirectory, "Logos");
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
            _httpClient = new HttpClient();
        }

        public async Task<string> GetCachedImagePathAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            // Create a unique filename based on the URL hash
            string fileName = GetHash(url) + Path.GetExtension(url);
            if (string.IsNullOrEmpty(Path.GetExtension(url))) fileName += ".png";
            
            string filePath = Path.Combine(_cacheDirectory, fileName);

            // If file exists, return local path
            if (File.Exists(filePath))
            {
                return filePath;
            }

            // Otherwise download and cache
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
                return filePath;
            }
            catch
            {
                return url; // Fallback to original URL if download fails
            }
        }

        public void ClearCache()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }

        private string GetHash(string input)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] data = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }
    }
}
