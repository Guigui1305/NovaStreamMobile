namespace NovaStreamMobile.Models
{
    public class Channel
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string Group { get; set; } = "General";
        public bool IsFavorite { get; set; }
        public string EpgId { get; set; } = string.Empty;
        public Program? CurrentProgram { get; set; }
    }
}
