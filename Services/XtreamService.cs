using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    public class XtreamCategory
    {
        public string CategoryId { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public int ChannelCount { get; set; }
    }

    public class VodItem
    {
        public string Name { get; set; } = "";
        public int StreamId { get; set; }
        public string StreamIcon { get; set; } = "";
        public string ContainerExtension { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Url { get; set; } = "";
        public string Added { get; set; } = "";
        public string Genre { get; set; } = "";
    }

    public class SeriesItem
    {
        public string Name { get; set; } = "";
        public int SeriesId { get; set; }
        public string Cover { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Genre { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string Cast { get; set; } = "";
    }

    public class SeriesEpisode
    {
        public int Id { get; set; }
        public int EpisodeNum { get; set; }
        public string Title { get; set; } = "";
        public string ContainerExtension { get; set; } = "";
        public string Url { get; set; } = "";
        public string Info { get; set; } = "";
    }

    public class SeriesSeason
    {
        public int SeasonNumber { get; set; }
        public string Name { get; set; } = "";
        public List<SeriesEpisode> Episodes { get; set; } = new();
    }

    public class SeriesInfo
    {
        public string Name { get; set; } = "";
        public string Cover { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Cast { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Rating { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public List<SeriesSeason> Seasons { get; set; } = new();
    }

    public class XtreamAccountInfo
    {
        public string Username { get; set; } = "";
        public string Status { get; set; } = "";
        public string ExpDate { get; set; } = "";
        public int ActiveConnections { get; set; }
        public int MaxConnections { get; set; }
        public bool IsAuthenticated { get; set; }
    }

    /// <summary>
    /// Service Xtream Codes optimise pour la production.
    /// - Timeout HTTP 120 secondes
    /// - Cache memoire avec TTL configurable
    /// - Chargement par categorie (pas tout d'un coup)
    /// - Retry automatique (3 tentatives)
    /// - Gestion d'erreurs detaillee
    /// </summary>
    public class XtreamService
    {
        private static readonly HttpClient _httpClient;
        private static readonly Dictionary<string, CacheEntry> _cache = new();
        private static readonly SemaphoreSlim _cacheLock = new(1, 1);

        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1500;
        private const int CacheTtlMinutes = 15;

        static XtreamService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(
                new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(
                new System.Net.Http.Headers.StringWithQualityHeaderValue("deflate"));
        }

        private class CacheEntry
        {
            public string Data { get; set; } = "";
            public DateTime ExpiresAt { get; set; }
            public bool IsValid => DateTime.UtcNow < ExpiresAt;
        }

        private string BuildApiUrl(MediaSource source, string action, string extraParams = "")
        {
            string baseUrl = source.Url.TrimEnd('/');
            string url = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(source.Username)}&password={Uri.EscapeDataString(source.Password)}&action={action}";
            if (!string.IsNullOrEmpty(extraParams))
                url += "&" + extraParams;
            return url;
        }

        private async Task<string> FetchWithRetryAsync(string url, CancellationToken ct = default)
        {
            string cacheKey = url;

            await _cacheLock.WaitAsync(ct);
            try
            {
                if (_cache.TryGetValue(cacheKey, out var entry) && entry.IsValid)
                    return entry.Data;
            }
            finally
            {
                _cacheLock.Release();
            }

            Exception? lastException = null;
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    string response = await _httpClient.GetStringAsync(url, ct);

                    await _cacheLock.WaitAsync(ct);
                    try
                    {
                        _cache[cacheKey] = new CacheEntry
                        {
                            Data = response,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(CacheTtlMinutes)
                        };
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }

                    return response;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine(
                        $"[XtreamService] Tentative {attempt}/{MaxRetries} echouee pour {url}: {ex.Message}");
                    if (attempt < MaxRetries)
                        await Task.Delay(RetryDelayMs * attempt, ct);
                }
            }

            throw new Exception($"Echec apres {MaxRetries} tentatives: {lastException?.Message}", lastException);
        }

        // ==================== ACCOUNT ====================

        public async Task<XtreamAccountInfo> GetAccountInfoAsync(MediaSource source, CancellationToken ct = default)
        {
            var info = new XtreamAccountInfo();
            try
            {
                string baseUrl = source.Url.TrimEnd('/');
                string url = $"{baseUrl}/player_api.php?username={Uri.EscapeDataString(source.Username)}&password={Uri.EscapeDataString(source.Password)}";
                string response = await FetchWithRetryAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("user_info", out var userInfo))
                {
                    info.IsAuthenticated = userInfo.TryGetProperty("auth", out var auth) && auth.ToString() == "1";
                    info.Username = userInfo.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
                    info.Status = userInfo.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    info.ExpDate = userInfo.TryGetProperty("exp_date", out var e) ? e.ToString() : "";
                    if (userInfo.TryGetProperty("active_cons", out var ac))
                        int.TryParse(ac.ToString(), out int acVal);
                    if (userInfo.TryGetProperty("max_connections", out var mc))
                        int.TryParse(mc.ToString(), out int mcVal);
                }
            }
            catch { }
            return info;
        }

        public async Task<bool> ValidateLoginAsync(MediaSource source, CancellationToken ct = default)
        {
            try
            {
                var info = await GetAccountInfoAsync(source, ct);
                return info.IsAuthenticated;
            }
            catch { return false; }
        }

        // ==================== CATEGORIES ====================

        public async Task<List<XtreamCategory>> GetLiveCategoriesAsync(MediaSource source, CancellationToken ct = default)
        {
            return await GetCategoriesAsync(source, "get_live_categories", ct);
        }

        public async Task<List<XtreamCategory>> GetVodCategoriesAsync(MediaSource source, CancellationToken ct = default)
        {
            return await GetCategoriesAsync(source, "get_vod_categories", ct);
        }

        public async Task<List<XtreamCategory>> GetSeriesCategoriesAsync(MediaSource source, CancellationToken ct = default)
        {
            return await GetCategoriesAsync(source, "get_series_categories", ct);
        }

        private async Task<List<XtreamCategory>> GetCategoriesAsync(MediaSource source, string action, CancellationToken ct = default)
        {
            var categories = new List<XtreamCategory>();
            string url = BuildApiUrl(source, action);
            string response = await FetchWithRetryAsync(url, ct);

            using var doc = JsonDocument.Parse(response);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                categories.Add(new XtreamCategory
                {
                    CategoryId = item.TryGetProperty("category_id", out var id) ? id.ToString() : "",
                    CategoryName = item.TryGetProperty("category_name", out var name) ? name.GetString() ?? "" : ""
                });
            }
            return categories;
        }

        /// <summary>
        /// Retourne les categories FR pertinentes, triees par priorite.
        /// </summary>
        public async Task<List<XtreamCategory>> GetFrenchLiveCategoriesAsync(MediaSource source, CancellationToken ct = default)
        {
            var all = await GetLiveCategoriesAsync(source, ct);
            var frKeywords = new[] { "|FR|", "|CH|", "FRANCE", "FRENCH" };

            var frCats = all.Where(c =>
                frKeywords.Any(k => c.CategoryName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (frCats.Count == 0) frCats = all.Take(50).ToList();
            return frCats;
        }

        public async Task<List<XtreamCategory>> GetFrenchVodCategoriesAsync(MediaSource source, CancellationToken ct = default)
        {
            var all = await GetVodCategoriesAsync(source, ct);
            var frKeywords = new[] { "|FR|", "|MULTI|", "FILM", "NETFLIX", "DISNEY", "AMAZON",
                "CANAL", "COMEDIE", "ACTION", "HORREUR", "THRILLER", "SCIENCE", "4K",
                "2026", "2025", "2024", "ANIMATION", "DRAME", "AVENTURE" };

            var frCats = all.Where(c =>
                frKeywords.Any(k => c.CategoryName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (frCats.Count == 0) frCats = all.Take(50).ToList();
            return frCats;
        }

        public async Task<List<XtreamCategory>> GetFrenchSeriesCategoriesAsync(MediaSource source, CancellationToken ct = default)
        {
            var all = await GetSeriesCategoriesAsync(source, ct);
            var frKeywords = new[] { "|FR|", "|MULTI|", "SERIE", "NETFLIX", "DISNEY", "AMAZON",
                "CANAL", "APPLE", "HBO", "PARAMOUNT", "2026", "2025", "2024" };

            var frCats = all.Where(c =>
                frKeywords.Any(k => c.CategoryName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (frCats.Count == 0) frCats = all.Take(50).ToList();
            return frCats;
        }

        // ==================== LIVE STREAMS ====================

        /// <summary>
        /// Charge les chaines d'une categorie specifique (recommande).
        /// </summary>
        public async Task<List<Channel>> GetLiveStreamsByCategoryAsync(
            MediaSource source, string categoryId, CancellationToken ct = default)
        {
            string extra = $"category_id={categoryId}";
            string url = BuildApiUrl(source, "get_live_streams", extra);
            string response = await FetchWithRetryAsync(url, ct);
            return ParseLiveStreams(source, response);
        }

        /// <summary>
        /// Charge TOUTES les chaines (attention : peut etre tres lent avec 33000+ chaines).
        /// Utiliser GetLiveStreamsByCategoryAsync a la place.
        /// </summary>
        public async Task<List<Channel>> GetAllLiveStreamsAsync(MediaSource source, CancellationToken ct = default)
        {
            string url = BuildApiUrl(source, "get_live_streams");
            string response = await FetchWithRetryAsync(url, ct);
            return ParseLiveStreams(source, response);
        }

        private List<Channel> ParseLiveStreams(MediaSource source, string json)
        {
            var channels = new List<Channel>();
            using var doc = JsonDocument.Parse(json);
            string baseUrl = source.Url.TrimEnd('/');

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                int streamId = GetIntProperty(item, "stream_id");
                channels.Add(new Channel
                {
                    Name = GetStringProperty(item, "name"),
                    Url = $"{baseUrl}/live/{source.Username}/{source.Password}/{streamId}.m3u8",
                    LogoUrl = GetStringProperty(item, "stream_icon"),
                    Group = GetStringProperty(item, "category_id", "0"),
                    EpgId = GetStringProperty(item, "epg_channel_id")
                });
            }
            return channels;
        }

        // ==================== VOD ====================

        public async Task<List<VodItem>> GetVodStreamsByCategoryAsync(
            MediaSource source, string categoryId, CancellationToken ct = default)
        {
            string extra = $"category_id={categoryId}";
            string url = BuildApiUrl(source, "get_vod_streams", extra);
            string response = await FetchWithRetryAsync(url, ct);
            return ParseVodStreams(source, response);
        }

        public async Task<List<VodItem>> GetVodStreamsAsync(
            MediaSource source, string categoryId = "", CancellationToken ct = default)
        {
            string extra = string.IsNullOrEmpty(categoryId) ? "" : $"category_id={categoryId}";
            string url = BuildApiUrl(source, "get_vod_streams", extra);
            string response = await FetchWithRetryAsync(url, ct);
            return ParseVodStreams(source, response);
        }

        private List<VodItem> ParseVodStreams(MediaSource source, string json)
        {
            var items = new List<VodItem>();
            using var doc = JsonDocument.Parse(json);
            string baseUrl = source.Url.TrimEnd('/');

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                int streamId = GetIntProperty(item, "stream_id");
                string ext = GetStringProperty(item, "container_extension", "ts");

                items.Add(new VodItem
                {
                    Name = GetStringProperty(item, "name"),
                    StreamId = streamId,
                    StreamIcon = GetStringProperty(item, "stream_icon"),
                    ContainerExtension = ext,
                    CategoryId = GetStringProperty(item, "category_id"),
                    Rating = GetStringProperty(item, "rating"),
                    Added = GetStringProperty(item, "added"),
                    Genre = GetStringProperty(item, "genre"),
                    Url = $"{baseUrl}/movie/{source.Username}/{source.Password}/{streamId}.{ext}"
                });
            }
            return items;
        }

        // ==================== SERIES ====================

        public async Task<List<SeriesItem>> GetSeriesByCategoryAsync(
            MediaSource source, string categoryId, CancellationToken ct = default)
        {
            string extra = $"category_id={categoryId}";
            string url = BuildApiUrl(source, "get_series", extra);
            string response = await FetchWithRetryAsync(url, ct);
            return ParseSeries(response);
        }

        public async Task<List<SeriesItem>> GetSeriesAsync(
            MediaSource source, string categoryId = "", CancellationToken ct = default)
        {
            string extra = string.IsNullOrEmpty(categoryId) ? "" : $"category_id={categoryId}";
            string url = BuildApiUrl(source, "get_series", extra);
            string response = await FetchWithRetryAsync(url, ct);
            return ParseSeries(response);
        }

        public async Task<SeriesInfo> GetSeriesInfoAsync(
            MediaSource source, int seriesId, CancellationToken ct = default)
        {
            string url = BuildApiUrl(source, "get_series_info", $"series_id={seriesId}");
            string response = await FetchWithRetryAsync(url, ct);
            var info = new SeriesInfo();

            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("info", out var infoEl))
            {
                info.Name = GetStringProperty(infoEl, "name");
                info.Cover = GetStringProperty(infoEl, "cover");
                info.Plot = GetStringProperty(infoEl, "plot");
                info.Cast = GetStringProperty(infoEl, "cast");
                info.Genre = GetStringProperty(infoEl, "genre");
                info.Rating = GetStringProperty(infoEl, "rating");
                info.ReleaseDate = GetStringProperty(infoEl, "releaseDate");
            }

            if (doc.RootElement.TryGetProperty("episodes", out var episodes))
            {
                string baseUrl = source.Url.TrimEnd('/');
                foreach (var seasonProp in episodes.EnumerateObject())
                {
                    int.TryParse(seasonProp.Name, out int seasonNum);
                    var season = new SeriesSeason
                    {
                        SeasonNumber = seasonNum,
                        Name = $"Saison {seasonNum}"
                    };

                    foreach (var ep in seasonProp.Value.EnumerateArray())
                    {
                        int epId = GetIntProperty(ep, "id");
                        string ext = GetStringProperty(ep, "container_extension", "ts");
                        season.Episodes.Add(new SeriesEpisode
                        {
                            Id = epId,
                            EpisodeNum = GetIntProperty(ep, "episode_num"),
                            Title = GetStringProperty(ep, "title"),
                            ContainerExtension = ext,
                            Url = $"{baseUrl}/series/{source.Username}/{source.Password}/{epId}.{ext}",
                            Info = GetStringProperty(ep, "info")
                        });
                    }

                    info.Seasons.Add(season);
                }
                info.Seasons = info.Seasons.OrderBy(s => s.SeasonNumber).ToList();
            }

            return info;
        }

        private List<SeriesItem> ParseSeries(string json)
        {
            var items = new List<SeriesItem>();
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                items.Add(new SeriesItem
                {
                    Name = GetStringProperty(item, "name"),
                    SeriesId = GetIntProperty(item, "series_id"),
                    Cover = GetStringProperty(item, "cover"),
                    CategoryId = GetStringProperty(item, "category_id"),
                    Rating = GetStringProperty(item, "rating"),
                    Plot = GetStringProperty(item, "plot"),
                    Genre = GetStringProperty(item, "genre"),
                    ReleaseDate = GetStringProperty(item, "releaseDate"),
                    Cast = GetStringProperty(item, "cast")
                });
            }
            return items;
        }

        // ==================== CACHE MANAGEMENT ====================

        public static void ClearCache()
        {
            _cacheLock.Wait();
            try { _cache.Clear(); }
            finally { _cacheLock.Release(); }
        }

        public static void InvalidateCacheForSource(MediaSource source)
        {
            _cacheLock.Wait();
            try
            {
                var keysToRemove = _cache.Keys
                    .Where(k => k.Contains(source.Username) && k.Contains(source.Password))
                    .ToList();
                foreach (var key in keysToRemove)
                    _cache.Remove(key);
            }
            finally { _cacheLock.Release(); }
        }

        // ==================== HELPERS ====================

        private static string GetStringProperty(JsonElement el, string name, string defaultVal = "")
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? defaultVal;
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.ToString();
                if (prop.ValueKind == JsonValueKind.Null)
                    return defaultVal;
                return prop.ToString();
            }
            return defaultVal;
        }

        private static int GetIntProperty(JsonElement el, string name, int defaultVal = 0)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetInt32();
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int parsed))
                    return parsed;
            }
            return defaultVal;
        }
    }
}
