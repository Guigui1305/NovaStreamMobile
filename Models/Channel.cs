using System;

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

        // Proprietes calculees pour le binding XAML
        public string CurrentProgramTitle => CurrentProgram?.Title ?? string.Empty;

        public double CurrentProgramProgress
        {
            get
            {
                if (CurrentProgram == null) return 0;
                return CurrentProgram.Progress / 100.0;
            }
        }

        public bool HasCurrentProgram => CurrentProgram != null && !string.IsNullOrEmpty(CurrentProgram.Title);
    }
}
