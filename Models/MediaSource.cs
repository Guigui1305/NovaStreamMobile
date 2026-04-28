using System;

namespace NovaStreamMobile.Models
{
    public enum SourceType { M3U, Xtream }

    public class MediaSource
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public SourceType Type { get; set; } = SourceType.M3U;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;

        // URL M3U generee pour les sources Xtream
        public string GetM3UUrl()
        {
            if (Type == SourceType.Xtream && !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                string baseUrl = Url.TrimEnd('/');
                return $"{baseUrl}/get.php?username={Username}&password={Password}&type=m3u_plus&output=mpegts";
            }
            return Url;
        }

        // URL EPG generee pour les sources Xtream
        public string GetEpgUrl()
        {
            if (Type == SourceType.Xtream && !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                string baseUrl = Url.TrimEnd('/');
                return $"{baseUrl}/xmltv.php?username={Username}&password={Password}";
            }
            return string.Empty;
        }
    }
}
