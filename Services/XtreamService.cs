using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    public class XtreamCategory
    {
        public string CategoryId { get; set; } = "";
        public string CategoryName { get; set; } = "";
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
    }

    public class SeriesItem
    {
        public string Name { get; set; } = "";
        public int SeriesId { get; set; }
        public string Cover { get; set; } = "";
        public string CategoryId { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Plot { get; set; } = "";
    }

    public class XtreamService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private string BuildApiUrl(MediaSource source, string action, string extraParams = "")
        {
            string baseUrl = source.Url.TrimEnd('/');
            string url = $"{baseUrl}/player_api.php?username={source.Username}&password={source.Password}&action={action}";
            if (!string.IsNullOrEmpty(extraParams))
                url += "&" + extraParams;
            return url;
        }

        public async Task<List<Channel>> GetLiveStreamsAsync(MediaSource source)
        {
            var channels = new List<Channel>();
            try
            {
                string url = BuildApiUrl(source, "get_live_streams");
                string response = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                string baseUrl = source.Url.TrimEnd('/');

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    int streamId = 0;
                    if (item.TryGetProperty("stream_id", out var sid))
                    {
                        if (sid.ValueKind == JsonValueKind.Number)
                            streamId = sid.GetInt32();
                        else if (sid.ValueKind == JsonValueKind.String && int.TryParse(sid.GetString(), out int parsed))
                            streamId = parsed;
                    }

                    string name = "";
                    if (item.TryGetProperty("name", out var nameProp))
                        name = nameProp.GetString() ?? "";

                    string logo = "";
                    if (item.TryGetProperty("stream_icon", out var iconProp))
                        logo = iconProp.GetString() ?? "";

                    string group = "General";
                    if (item.TryGetProperty("category_id", out var catProp))
                        group = catProp.ToString();

                    string epgId = "";
                    if (item.TryGetProperty("epg_channel_id", out var epgProp))
                        epgId = epgProp.GetString() ?? "";

                    var channel = new Channel
                    {
                        Name = name,
                        Url = $"{baseUrl}/live/{source.Username}/{source.Password}/{streamId}.m3u8",
                        LogoUrl = logo,
                        Group = group,
                        EpgId = epgId
                    };
                    channels.Add(channel);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XtreamService.GetLiveStreams error: {ex.Message}");
            }
            return channels;
        }

        public async Task<List<XtreamCategory>> GetLiveCategoriesAsync(MediaSource source)
        {
            return await GetCategoriesAsync(source, "get_live_categories");
        }

        public async Task<List<XtreamCategory>> GetVodCategoriesAsync(MediaSource source)
        {
            return await GetCategoriesAsync(source, "get_vod_categories");
        }

        public async Task<List<XtreamCategory>> GetSeriesCategoriesAsync(MediaSource source)
        {
            return await GetCategoriesAsync(source, "get_series_categories");
        }

        private async Task<List<XtreamCategory>> GetCategoriesAsync(MediaSource source, string action)
        {
            var categories = new List<XtreamCategory>();
            try
            {
                string url = BuildApiUrl(source, action);
                string response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    categories.Add(new XtreamCategory
                    {
                        CategoryId = item.TryGetProperty("category_id", out var id) ? id.ToString() : "",
                        CategoryName = item.TryGetProperty("category_name", out var name) ? name.GetString() ?? "" : ""
                    });
                }
            }
            catch { }
            return categories;
        }

        public async Task<List<VodItem>> GetVodStreamsAsync(MediaSource source, string categoryId = "")
        {
            var items = new List<VodItem>();
            try
            {
                string extra = string.IsNullOrEmpty(categoryId) ? "" : $"category_id={categoryId}";
                string url = BuildApiUrl(source, "get_vod_streams", extra);
                string response = await _httpClient.GetStringAsync(url);
                string baseUrl = source.Url.TrimEnd('/');

                using var doc = JsonDocument.Parse(response);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    int streamId = 0;
                    if (item.TryGetProperty("stream_id", out var sid))
                    {
                        if (sid.ValueKind == JsonValueKind.Number)
                            streamId = sid.GetInt32();
                        else if (sid.ValueKind == JsonValueKind.String && int.TryParse(sid.GetString(), out int parsed))
                            streamId = parsed;
                    }

                    string ext = "ts";
                    if (item.TryGetProperty("container_extension", out var extProp))
                        ext = extProp.GetString() ?? "ts";

                    items.Add(new VodItem
                    {
                        Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        StreamId = streamId,
                        StreamIcon = item.TryGetProperty("stream_icon", out var ic) ? ic.GetString() ?? "" : "",
                        ContainerExtension = ext,
                        CategoryId = item.TryGetProperty("category_id", out var cat) ? cat.ToString() : "",
                        Rating = item.TryGetProperty("rating", out var r) ? r.ToString() : "",
                        Url = $"{baseUrl}/movie/{source.Username}/{source.Password}/{streamId}.{ext}"
                    });
                }
            }
            catch { }
            return items;
        }

        public async Task<List<SeriesItem>> GetSeriesAsync(MediaSource source, string categoryId = "")
        {
            var items = new List<SeriesItem>();
            try
            {
                string extra = string.IsNullOrEmpty(categoryId) ? "" : $"category_id={categoryId}";
                string url = BuildApiUrl(source, "get_series", extra);
                string response = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    int seriesId = 0;
                    if (item.TryGetProperty("series_id", out var sid))
                    {
                        if (sid.ValueKind == JsonValueKind.Number)
                            seriesId = sid.GetInt32();
                        else if (sid.ValueKind == JsonValueKind.String && int.TryParse(sid.GetString(), out int parsed))
                            seriesId = parsed;
                    }

                    items.Add(new SeriesItem
                    {
                        Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        SeriesId = seriesId,
                        Cover = item.TryGetProperty("cover", out var c) ? c.GetString() ?? "" : "",
                        CategoryId = item.TryGetProperty("category_id", out var cat) ? cat.ToString() : "",
                        Rating = item.TryGetProperty("rating", out var r) ? r.ToString() : "",
                        Plot = item.TryGetProperty("plot", out var p) ? p.GetString() ?? "" : ""
                    });
                }
            }
            catch { }
            return items;
        }

        public async Task<bool> ValidateLoginAsync(MediaSource source)
        {
            try
            {
                string url = BuildApiUrl(source, "");
                // Remove action param for auth check
                url = $"{source.Url.TrimEnd('/')}/player_api.php?username={source.Username}&password={source.Password}";
                string response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                return doc.RootElement.TryGetProperty("user_info", out var userInfo) &&
                       userInfo.TryGetProperty("auth", out var auth) &&
                       auth.ToString() == "1";
            }
            catch { return false; }
        }
    }
}
