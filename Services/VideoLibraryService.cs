using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    /// <summary>
    /// Bibliotheque des videos generees : dossier de sortie + index JSON local.
    /// Les fichiers vont dans le dossier prive de l'app sur le stockage externe,
    /// accessible depuis un explorateur de fichiers et partageable, sans permission.
    /// </summary>
    public class VideoLibraryService
    {
        private readonly string _folder;
        private readonly string _indexPath;

        public VideoLibraryService()
        {
            _folder = ResolveFolder();
            if (!Directory.Exists(_folder))
                Directory.CreateDirectory(_folder);

            _indexPath = Path.Combine(_folder, "index.json");
        }

        public string Folder => _folder;

        private static string ResolveFolder()
        {
#if ANDROID
            try
            {
                var external = Android.App.Application.Context.GetExternalFilesDir(
                    Android.OS.Environment.DirectoryMovies);
                if (external != null && !string.IsNullOrEmpty(external.AbsolutePath))
                    return Path.Combine(external.AbsolutePath, "NovaStudio");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoLibrary] {ex.Message}");
            }
#endif
            return Path.Combine(FileSystem.AppDataDirectory, "NovaStudio");
        }

        public string BuildOutputPath(VideoPromptSpec spec)
        {
            string slug = Slugify(string.IsNullOrWhiteSpace(spec.OverlayText) ? spec.SceneName : spec.OverlayText);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(_folder, $"nova-{slug}-{stamp}.mp4");
        }

        public List<GeneratedVideo> Load()
        {
            try
            {
                if (!File.Exists(_indexPath)) return new List<GeneratedVideo>();

                string json = File.ReadAllText(_indexPath);
                var items = JsonSerializer.Deserialize<List<GeneratedVideo>>(json) ?? new List<GeneratedVideo>();

                // On ne garde que ce qui existe encore sur le disque.
                var existing = items.Where(v => File.Exists(v.FilePath))
                                    .OrderByDescending(v => v.CreatedAt)
                                    .ToList();

                if (existing.Count != items.Count)
                    Save(existing);

                return existing;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoLibrary] Load: {ex.Message}");
                return new List<GeneratedVideo>();
            }
        }

        public void Save(List<GeneratedVideo> videos)
        {
            try
            {
                string json = JsonSerializer.Serialize(videos);
                File.WriteAllText(_indexPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoLibrary] Save: {ex.Message}");
            }
        }

        public GeneratedVideo Add(VideoPromptSpec spec, string filePath)
        {
            var video = new GeneratedVideo
            {
                FilePath = filePath,
                Prompt = spec.Prompt,
                SceneName = spec.SceneName,
                PaletteName = spec.PaletteName,
                DurationSeconds = spec.DurationSeconds,
                Width = spec.Width,
                Height = spec.Height,
                Fps = spec.Fps,
                CreatedAt = DateTime.Now,
                SizeBytes = SafeLength(filePath)
            };

            var videos = Load();
            videos.Insert(0, video);
            Save(videos);
            return video;
        }

        public void Delete(GeneratedVideo video)
        {
            try
            {
                if (File.Exists(video.FilePath)) File.Delete(video.FilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoLibrary] Delete: {ex.Message}");
            }

            var videos = Load().Where(v => v.Id != video.Id).ToList();
            Save(videos);
        }

        private static long SafeLength(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        private static string Slugify(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "video";

            var chars = input.ToLowerInvariant()
                             .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')
                             .ToArray();

            string slug = new string(chars);
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            slug = slug.Trim('-');

            if (slug.Length > 24) slug = slug.Substring(0, 24).Trim('-');
            return string.IsNullOrEmpty(slug) ? "video" : slug;
        }
    }
}
