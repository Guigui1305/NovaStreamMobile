using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    public class XtreamService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<List<Channel>> GetLiveStreamsAsync(MediaSource source)
        {
            var channels = new List<Channel>();
            try
            {
                // API Xtream: player_api.php?username=USER&password=PASS&action=get_live_streams
                string url = $"{source.Url}/player_api.php?username={source.Username}&password={source.Password}&action=get_live_streams";
                string response = await _httpClient.GetStringAsync(url);
                
                using var doc = JsonDocument.Parse(response);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var channel = new Channel
                    {
                        Name = item.GetProperty("name").GetString() ?? "Unknown",
                        Url = $"{source.Url}/live/{source.Username}/{source.Password}/{item.GetProperty("stream_id").GetInt32()}.m3u8",
                        LogoUrl = item.TryGetProperty("stream_icon", out var icon) ? icon.GetString() ?? "" : "",
                        Group = item.TryGetProperty("category_name", out var cat) ? cat.GetString() ?? "General" : "General",
                        EpgId = item.TryGetProperty("epg_channel_id", out var epg) ? epg.GetString() ?? "" : ""
                    };
                    channels.Add(channel);
                }
            }
            catch { }
            return channels;
        }

        public async Task<bool> ValidateLoginAsync(MediaSource source)
        {
            try
            {
                string url = $"{source.Url}/player_api.php?username={source.Username}&password={source.Password}";
                string response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                return doc.RootElement.TryGetProperty("user_info", out var userInfo) && 
                       userInfo.GetProperty("auth").GetInt32() == 1;
            }
            catch { return false; }
        }
    }
}
