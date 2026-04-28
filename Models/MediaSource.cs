using System;

namespace NovaStreamMobile.Models
{
    public enum SourceType { M3U, Xtream }

    public class MediaSource
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty; // For M3U or Server URL for Xtream
        public SourceType Type { get; set; } = SourceType.M3U;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }
}
