using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    public class XmlTvParserService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<List<Program>> ParseFromUrlAsync(string url)
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
                else return new List<Program>();

                return ParseContent(content);
            }
            catch { return new List<Program>(); }
        }

        private List<Program> ParseContent(string content)
        {
            var programs = new List<Program>();
            try
            {
                var doc = XDocument.Parse(content);
                foreach (var element in doc.Descendants("programme"))
                {
                    var program = new Program
                    {
                        ChannelId = element.Attribute("channel")?.Value ?? string.Empty,
                        Title = element.Element("title")?.Value ?? "No Title",
                        Description = element.Element("desc")?.Value ?? string.Empty,
                        StartTime = ParseDate(element.Attribute("start")?.Value),
                        EndTime = ParseDate(element.Attribute("stop")?.Value)
                    };
                    programs.Add(program);
                }
            }
            catch { }
            return programs;
        }

        private DateTime ParseDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.MinValue;
            // Format XMLTV: 20231027120000 +0200
            try
            {
                string cleanDate = dateStr.Split(' ')[0];
                return DateTime.ParseExact(cleanDate.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            }
            catch { return DateTime.MinValue; }
        }
    }
}
