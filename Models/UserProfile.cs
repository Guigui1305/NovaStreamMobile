using System;

namespace NovaStreamMobile.Models
{
    public class UserProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string AvatarSource { get; set; } = "profile_default.png";
        public string PinCode { get; set; } = string.Empty;
        public bool IsLocked => !string.IsNullOrEmpty(PinCode);
        public DateTime LastUsed { get; set; } = DateTime.Now;
    }
}
