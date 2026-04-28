using System;

namespace NovaStreamMobile.Models
{
    public class Program
    {
        public string ChannelId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public double Progress
        {
            get
            {
                var now = DateTime.Now;
                if (now < StartTime) return 0;
                if (now > EndTime) return 100;
                var total = (EndTime - StartTime).TotalMinutes;
                var elapsed = (now - StartTime).TotalMinutes;
                return (elapsed / total) * 100;
            }
        }
    }
}
