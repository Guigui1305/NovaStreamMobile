using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    public class StorageService
    {
        private readonly string _baseFolder;
        private readonly string _profilesPath;
        private readonly string _sourcesPath;
        private UserProfile? _currentProfile;

        public StorageService()
        {
            _baseFolder = Path.Combine(FileSystem.AppDataDirectory, "NovaStreamData");
            if (!Directory.Exists(_baseFolder)) Directory.CreateDirectory(_baseFolder);

            _profilesPath = Path.Combine(_baseFolder, "profiles.json");
            _sourcesPath = Path.Combine(_baseFolder, "sources.json");
        }

        public void SetCurrentProfile(UserProfile profile)
        {
            _currentProfile = profile;
            string profileFolder = GetProfileFolder();
            if (!Directory.Exists(profileFolder)) Directory.CreateDirectory(profileFolder);
        }

        private string GetProfileFolder()
        {
            if (_currentProfile == null) return _baseFolder;
            return Path.Combine(_baseFolder, "Profiles", _currentProfile.Id.ToString());
        }

        // Profiles Management
        public List<UserProfile> LoadProfiles()
        {
            try
            {
                if (!File.Exists(_profilesPath)) return new List<UserProfile>();
                string json = File.ReadAllText(_profilesPath);
                return JsonSerializer.Deserialize<List<UserProfile>>(json) ?? new List<UserProfile>();
            }
            catch { return new List<UserProfile>(); }
        }

        public void SaveProfiles(List<UserProfile> profiles)
        {
            try
            {
                string json = JsonSerializer.Serialize(profiles);
                File.WriteAllText(_profilesPath, json);
            }
            catch { }
        }

        // Global Sources (Shared across profiles)
        public List<MediaSource> LoadSources()
        {
            try
            {
                if (!File.Exists(_sourcesPath)) return new List<MediaSource>();
                string json = File.ReadAllText(_sourcesPath);
                return JsonSerializer.Deserialize<List<MediaSource>>(json) ?? new List<MediaSource>();
            }
            catch { return new List<MediaSource>(); }
        }

        public void SaveSources(List<MediaSource> sources)
        {
            try
            {
                string json = JsonSerializer.Serialize(sources, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_sourcesPath, json);
            }
            catch { }
        }

        // Profile-specific Data (Favorites)
        public List<string> LoadFavorites()
        {
            try
            {
                string path = Path.Combine(GetProfileFolder(), "favorites.json");
                if (!File.Exists(path)) return new List<string>();
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public void SaveFavorites(List<string> favoriteUrls)
        {
            try
            {
                string path = Path.Combine(GetProfileFolder(), "favorites.json");
                string json = JsonSerializer.Serialize(favoriteUrls);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        // Profile-specific Data (History)
        public List<string> LoadHistory()
        {
            try
            {
                string path = Path.Combine(GetProfileFolder(), "history.json");
                if (!File.Exists(path)) return new List<string>();
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public void SaveHistory(List<string> history)
        {
            try
            {
                string path = Path.Combine(GetProfileFolder(), "history.json");
                string json = JsonSerializer.Serialize(history.Take(10).ToList());
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
