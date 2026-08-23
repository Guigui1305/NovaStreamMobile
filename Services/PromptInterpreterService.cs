using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    /// <summary>
    /// Traduit un prompt en langage naturel (FR/EN) en parametres de rendu.
    /// Tout est local : simple analyse lexicale, aucun appel reseau.
    /// </summary>
    public class PromptInterpreterService
    {
        private sealed class Palette
        {
            public string Name = string.Empty;
            public int[] Colors = Array.Empty<int>();
            public string[] Keywords = Array.Empty<string>();
        }

        private static int C(uint argb) => unchecked((int)argb);

        private static readonly Palette[] Palettes =
        {
            new Palette
            {
                Name = "Coucher de soleil",
                Colors = new[] { C(0xFF1B1033), C(0xFF6D2361), C(0xFFE0525A), C(0xFFFF9E4F), C(0xFFFFD98E) },
                Keywords = new[] { "coucher de soleil", "sunset", "crepuscule", "aube", "sunrise", "lever de soleil", "orange", "dore soir" }
            },
            new Palette
            {
                Name = "Ocean",
                Colors = new[] { C(0xFF021B33), C(0xFF06395C), C(0xFF0E7C9B), C(0xFF35C6D6), C(0xFFAFF3F0) },
                Keywords = new[] { "ocean", "mer", "vague", "wave", "eau", "water", "plage", "beach", "bleu", "blue", "aqua", "marin", "sea" }
            },
            new Palette
            {
                Name = "Foret",
                Colors = new[] { C(0xFF06180F), C(0xFF10391F), C(0xFF1F7A3F), C(0xFF5FBF6B), C(0xFFD3F0A7) },
                Keywords = new[] { "foret", "forest", "nature", "jungle", "vert", "green", "arbre", "montagne", "prairie", "bambou" }
            },
            new Palette
            {
                Name = "Feu",
                Colors = new[] { C(0xFF1A0402), C(0xFF631206), C(0xFFC02A0C), C(0xFFF2701A), C(0xFFFFC94A) },
                Keywords = new[] { "feu", "fire", "lave", "lava", "volcan", "flamme", "flame", "rouge", "red", "braise", "chaud" }
            },
            new Palette
            {
                Name = "Neon",
                Colors = new[] { C(0xFF0B021A), C(0xFF3D0B63), C(0xFFB026FF), C(0xFFFF2E88), C(0xFF00F0FF) },
                Keywords = new[] { "neon", "cyberpunk", "synthwave", "retro", "vaporwave", "violet", "purple", "rose", "pink", "magenta", "futuriste", "arcade", "club" }
            },
            new Palette
            {
                Name = "Nuit etoilee",
                Colors = new[] { C(0xFF01030F), C(0xFF091238), C(0xFF1E3A8A), C(0xFF6D8FE8), C(0xFFE6ECFF) },
                Keywords = new[] { "nuit", "night", "espace", "space", "galaxie", "galaxy", "etoile", "star", "cosmos", "univers", "lune", "moon", "ciel", "sombre", "dark" }
            },
            new Palette
            {
                Name = "Glace",
                Colors = new[] { C(0xFF071726), C(0xFF16405C), C(0xFF4E9BC4), C(0xFFAEE0F5), C(0xFFFFFFFF) },
                Keywords = new[] { "neige", "snow", "hiver", "winter", "glace", "ice", "givre", "froid", "blanc", "white", "polaire", "cristal" }
            },
            new Palette
            {
                Name = "Or & noir",
                Colors = new[] { C(0xFF0A0A0A), C(0xFF2B2109), C(0xFF8A6A15), C(0xFFD9A824), C(0xFFFFE9A8) },
                Keywords = new[] { "or", "gold", "dore", "luxe", "luxury", "premium", "elegant", "champagne", "jaune", "business" }
            },
            new Palette
            {
                Name = "NovaStream",
                Colors = new[] { C(0xFF0A0A14), C(0xFF2A0A2E), C(0xFF8B5CF6), C(0xFFE50914), C(0xFF00D4AA) },
                Keywords = new[] { "novastream", "nova" }
            }
        };

        private static readonly (VideoScene Scene, string Name, string[] Keywords)[] Scenes =
        {
            (VideoScene.Waves, "Vagues", new[] { "vague", "wave", "ocean", "mer", "eau", "water", "liquide", "fluide", "houle", "surf", "riviere", "sea" }),
            (VideoScene.Particles, "Particules", new[] { "particule", "particle", "etoile", "star", "neige", "snow", "pluie", "rain", "poussiere", "dust", "bulle", "bubble", "galaxie", "espace", "space", "confetti", "feu d artifice", "lucioles" }),
            (VideoScene.NeonGrid, "Grille neon", new[] { "neon", "grille", "grid", "synthwave", "retro", "cyberpunk", "vaporwave", "tunnel", "80s", "arcade", "futuriste", "route" }),
            (VideoScene.Pulse, "Pulsations", new[] { "pulse", "pulsation", "battement", "beat", "rythme", "musique", "music", "son", "audio", "coeur", "heart", "energie", "radar", "onde", "sonar" }),
            (VideoScene.Aurora, "Aurore", new[] { "aurore", "aurora", "nuage", "cloud", "fumee", "smoke", "brume", "fog", "abstrait", "abstract", "degrade", "gradient", "soie", "reve", "dream", "calme", "zen", "meditation" })
        };

        private static readonly string[] FastWords =
            { "rapide", "fast", "dynamique", "energique", "intense", "speed", "vitesse", "nerveux", "action", "explosif", "frenetique" };

        private static readonly string[] SlowWords =
            { "lent", "slow", "calme", "doux", "zen", "meditation", "relaxant", "paisible", "tranquille", "ambient", "sommeil", "chill" };

        public VideoPromptSpec Interpret(string prompt)
        {
            string raw = (prompt ?? string.Empty).Trim();
            string norm = Normalize(raw);

            var spec = new VideoPromptSpec
            {
                Prompt = raw,
                Seed = StableSeed(norm)
            };

            // ----- Scene -----
            var scene = MatchScene(norm);
            spec.Scene = scene.Scene;
            spec.SceneName = scene.Name;

            // ----- Palette -----
            var palette = MatchPalette(norm);
            spec.Palette = palette.Colors;
            spec.PaletteName = palette.Name;

            // ----- Vitesse -----
            spec.Speed = 1.0;
            if (ContainsAny(norm, FastWords)) spec.Speed = 1.9;
            else if (ContainsAny(norm, SlowWords)) spec.Speed = 0.5;

            // ----- Duree -----
            double? duration = ParseDuration(norm);
            if (duration.HasValue)
            {
                spec.DurationSeconds = Math.Clamp(duration.Value, 2, 60);
                spec.HasExplicitDuration = true;
            }

            // ----- Orientation -----
            if (ContainsAny(norm, new[] { "paysage", "landscape", "horizontal", "16:9", "16/9", "youtube", "tv", "cinema" }))
                spec.Orientation = VideoOrientation.Landscape;
            else if (ContainsAny(norm, new[] { "carre", "square", "1:1", "1/1", "instagram post" }))
                spec.Orientation = VideoOrientation.Square;
            else
                spec.Orientation = VideoOrientation.Portrait;

            // ----- Texte incruste -----
            spec.OverlayText = ExtractOverlayText(raw);

            return spec;
        }

        // ==================== Analyse ====================

        private static (VideoScene Scene, string Name) MatchScene(string norm)
        {
            var best = (Scene: VideoScene.Aurora, Name: "Aurore", Score: 0);
            foreach (var entry in Scenes)
            {
                int score = entry.Keywords.Count(k => norm.Contains(k, StringComparison.Ordinal));
                if (score > best.Score)
                    best = (entry.Scene, entry.Name, score);
            }
            return (best.Scene, best.Name);
        }

        private static Palette MatchPalette(string norm)
        {
            Palette? best = null;
            int bestScore = 0;
            foreach (var palette in Palettes)
            {
                int score = palette.Keywords.Count(k => norm.Contains(k, StringComparison.Ordinal));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = palette;
                }
            }
            return best ?? Palettes[Palettes.Length - 1]; // NovaStream par defaut
        }

        private static double? ParseDuration(string norm)
        {
            var match = Regex.Match(norm, @"(\d+(?:[.,]\d+)?)\s*(secondes|seconde|seconds|second|sec|s\b|minutes|minute|mins|min\b|m\b)");
            if (!match.Success) return null;

            string number = match.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return null;

            string unit = match.Groups[2].Value;
            if (unit.StartsWith("min", StringComparison.Ordinal) || unit == "m")
                value *= 60;

            return value;
        }

        /// <summary>
        /// Texte a incruster : ce qui est entre guillemets si present,
        /// sinon rien (le prompt complet serait illisible a l'ecran).
        /// </summary>
        private static string ExtractOverlayText(string raw)
        {
            var patterns = new[]
            {
                "\"([^\"]{1,60})\"",
                "«\\s*([^»]{1,60})\\s*»",
                "'([^']{2,60})'"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(raw, pattern);
                if (match.Success)
                {
                    string text = match.Groups[1].Value.Trim();
                    if (text.Length > 0) return text;
                }
            }

            var explicitText = Regex.Match(raw, @"(?:texte|text|titre|title)\s*[:=]\s*(.+)$",
                RegexOptions.IgnoreCase);
            if (explicitText.Success)
                return explicitText.Groups[1].Value.Trim().Trim('"', '\'').Trim();

            return string.Empty;
        }

        private static bool ContainsAny(string haystack, string[] needles)
            => needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

        /// <summary>Minuscules sans accents, pour comparer les mots-cles simplement.</summary>
        private static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string lowered = input.ToLowerInvariant()
                .Replace('\'', ' ')
                .Replace('\u2019', ' ');
            string decomposed = lowered.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Hash stable (independant du runtime) : meme prompt = meme video.</summary>
        private static int StableSeed(string text)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in text) hash = hash * 31 + c;
                return hash == 0 ? 1 : Math.Abs(hash);
            }
        }

        /// <summary>Exemples proposes dans l'interface.</summary>
        public static IReadOnlyList<string> Examples { get; } = new[]
        {
            "Coucher de soleil sur l'ocean, vagues lentes, 10 secondes, texte \"Vacances\"",
            "Nuit etoilee dans l'espace, particules qui derivent, calme, 12 secondes",
            "Neon cyberpunk retro, grille synthwave, rapide, 8 secondes, texte \"NOVA\"",
            "Foret verte, brume douce, ambiance zen, 15 secondes",
            "Pulsations rouges au rythme de la musique, intense, 6 secondes",
            "Fond dore et noir elegant, paysage 16:9, 10 secondes, titre : Premium"
        };
    }
}
