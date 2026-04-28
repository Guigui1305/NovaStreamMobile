using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NovaStreamClean.Models;

namespace NovaStreamMobile.Services
{
    public class M3UParserService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<List<Channel>> ParseFromUrlAsync(string url)
        {
            try
            {
                string content;
                if (url.StartsWith("http"))
                {
                    content = await _httpClient.GetStringAsync(url);
                }
                else if (File.Exists(url))
                {
                    content = await File.ReadAllTextAsync(url);
                }
                else
                {
                    return new List<Channel>();
                }

                return ParseContent(content);
            }
            catch
            {
                return new List<Channel>();
            }
        }

        private List<Channel> ParseContent(string content)
        {
            var channels = new List<Channel>();
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Channel? currentChannel = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("#EXTINF:"))
                {
                    currentChannel = new Channel();
                    
                    // Extraction du nom (après la dernière virgule)
                    int lastComma = line.LastIndexOf(',');
                    if (lastComma != -1)
                    {
                        currentChannel.Name = line.Substring(lastComma + 1).Trim();
                    }

                    // Extraction du logo (tvg-logo="...")
                    var logoMatch = Regex.Match(line, "tvg-logo=\"([^\"]*)\"");
                    if (logoMatch.Success)
                    {
                        currentChannel.LogoUrl = logoMatch.Groups[1].Value;
                    }

                    // Extraction du groupe (group-title="...")
                    var groupMatch = Regex.Match(line, "group-title=\"([^\"]*)\"");
                    if (groupMatch.Success)
                    {
                        currentChannel.Group = groupMatch.Groups[1].Value;
                    }

                    // Extraction de l'ID EPG (tvg-id="...")
                    var epgMatch = Regex.Match(line, "tvg-id=\"([^\"]*)\"");
                    if (epgMatch.Success)
                    {
                        currentChannel.EpgId = epgMatch.Groups[1].Value;
                    }
                }
                else if (!line.StartsWith("#") && currentChannel != null)
                {
                    currentChannel.Url = line.Trim();
                    channels.Add(currentChannel);
                    currentChannel = null;
                }
            }

            return channels;
        }
    }
}
