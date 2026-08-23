using System;
using System.Collections.Generic;

namespace NovaStreamMobile.Models
{
    /// <summary>Type d'animation genere localement.</summary>
    public enum VideoScene
    {
        Aurora,     // nappes de lumiere / degrades mouvants
        Waves,      // vagues sinusoidales
        Particles,  // particules, etoiles, neige, pluie
        NeonGrid,   // grille retro / synthwave
        Pulse       // ondes concentriques rythmees
    }

    public enum VideoOrientation { Portrait, Landscape, Square }

    /// <summary>
    /// Resultat de l'interpretation d'un prompt texte : tout ce dont
    /// le moteur de rendu a besoin pour dessiner la video.
    /// </summary>
    public class VideoPromptSpec
    {
        public string Prompt { get; set; } = string.Empty;

        /// <summary>Texte incruste dans la video (vide = aucun).</summary>
        public string OverlayText { get; set; } = string.Empty;

        public VideoScene Scene { get; set; } = VideoScene.Aurora;
        public VideoOrientation Orientation { get; set; } = VideoOrientation.Portrait;

        public string SceneName { get; set; } = "Aurore";
        public string PaletteName { get; set; } = "NovaStream";

        /// <summary>Couleurs ARGB (0xFFRRGGBB) utilisees par la scene.</summary>
        public int[] Palette { get; set; } = Array.Empty<int>();

        /// <summary>Multiplicateur de vitesse d'animation (0.4 = lent, 2.0 = rapide).</summary>
        public double Speed { get; set; } = 1.0;

        public double DurationSeconds { get; set; } = 8;

        /// <summary>True si la duree a ete lue explicitement dans le prompt.</summary>
        public bool HasExplicitDuration { get; set; }

        public int Fps { get; set; } = 24;

        /// <summary>Petit cote de l'image en pixels (540 ou 720).</summary>
        public int ShortSide { get; set; } = 540;

        /// <summary>Graine pseudo-aleatoire derivee du prompt : meme prompt = meme video.</summary>
        public int Seed { get; set; }

        public int Width => Orientation switch
        {
            VideoOrientation.Portrait => Align16(ShortSide),
            VideoOrientation.Landscape => Align16(ShortSide * 16 / 9),
            _ => Align16(ShortSide)
        };

        public int Height => Orientation switch
        {
            VideoOrientation.Portrait => Align16(ShortSide * 16 / 9),
            VideoOrientation.Landscape => Align16(ShortSide),
            _ => Align16(ShortSide)
        };

        public int TotalFrames => Math.Max(1, (int)Math.Round(DurationSeconds * Fps));

        /// <summary>Les encodeurs materiels exigent des dimensions multiples de 16.</summary>
        private static int Align16(int value) => Math.Max(16, ((value + 8) / 16) * 16);

        public string OrientationLabel => Orientation switch
        {
            VideoOrientation.Portrait => "Portrait 9:16",
            VideoOrientation.Landscape => "Paysage 16:9",
            _ => "Carre 1:1"
        };

        public string Describe()
            => $"{SceneName} • {PaletteName} • {DurationSeconds:0.#}s • {Width}x{Height} @ {Fps} fps";
    }

    /// <summary>Une video generee, telle que stockee dans la bibliotheque locale.</summary>
    public class GeneratedVideo
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FilePath { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string SceneName { get; set; } = string.Empty;
        public string PaletteName { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Fps { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string FileName => System.IO.Path.GetFileName(FilePath);

        public string Summary
            => $"{SceneName} • {DurationSeconds:0.#}s • {Width}x{Height} • {SizeBytes / 1024.0 / 1024.0:0.0} Mo";

        public string PromptPreview
            => string.IsNullOrWhiteSpace(Prompt) ? "(sans prompt)"
               : (Prompt.Length > 70 ? Prompt.Substring(0, 70) + "..." : Prompt);
    }
}
