using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using LibVLCSharp.Shared;

namespace NovaStreamMobile.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class LiveTvViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        private readonly M3UParserService _parserService;
        private readonly XmlTvParserService _epgService;
        private readonly XtreamService _xtreamService;
        private readonly ImageCacheService _imageCacheService;
        
        private bool _isLoading;
        private bool _isPlaying;
        private int _volume = 100;
        private MediaSource? _selectedSource;
        private Channel? _selectedChannel;
        private List<string> _favoriteUrls;
        private List<string> _historyUrls;
        private List<Program> _allPrograms = new List<Program>();
        private TrackDescription? _selectedSubtitle;
        
        private string _searchText = string.Empty;
        private string _selectedCategory = "All";
        private List<Channel> _allChannels = new List<Channel>();

        public LibVLC? LibVLC { get; private set; }
        public MediaPlayer? MediaPlayer { get; private set; }

        public ObservableCollection<MediaSource> Sources { get; }
        public ObservableCollection<Channel> FilteredChannels { get; } = new ObservableCollection<Channel>();
        public ObservableCollection<string> Categories { get; } = new ObservableCollection<string>();
        public ObservableCollection<TrackDescription> Subtitles { get; } = new ObservableCollection<TrackDescription>();

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsPlaying { get => _isPlaying; set => SetProperty(ref _isPlaying, value); }
        
        public int Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value) && MediaPlayer != null)
                {
                    MediaPlayer.Volume = value;
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    Task.Delay(300).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(ApplyFilters));
                }
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    ApplyFilters();
            }
        }

        public MediaSource? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (SetProperty(ref _selectedSource, value) && value != null)
                {
                    _ = LoadChannelsAsync(value);
                }
            }
        }

        public Channel? SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value) && value != null)
                {
                    PlayChannel(value);
                }
            }
        }

        public TrackDescription? SelectedSubtitle
        {
            get => _selectedSubtitle;
            set
            {
                if (SetProperty(ref _selectedSubtitle, value) && value != null && MediaPlayer != null)
                {
                    MediaPlayer.SetSpu(value.Id);
                }
            }
        }

        public ICommand ToggleFavoriteCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand NextChannelCommand { get; }
        public ICommand PreviousChannelCommand { get; }

        public LiveTvViewModel()
        {
            _storageService = new StorageService();
            _parserService = new M3UParserService();
            _epgService = new XmlTvParserService();
            _xtreamService = new XtreamService();
            _imageCacheService = new ImageCacheService();
            _favoriteUrls = _storageService.LoadFavorites();
            _historyUrls = _storageService.LoadHistory();
            Sources = new ObservableCollection<MediaSource>(_storageService.LoadSources());
            
            LibVLC = new LibVLC();
            MediaPlayer = new MediaPlayer(LibVLC);
            
            MediaPlayer.Playing += (s, e) => { IsPlaying = true; UpdateSubtitles(); };
            MediaPlayer.Paused += (s, e) => IsPlaying = false;
            MediaPlayer.Stopped += (s, e) => IsPlaying = false;

            ToggleFavoriteCommand = new Command<Channel>(ToggleFavorite);
            PlayPauseCommand = new Command(() => {
                if (MediaPlayer == null) return;
                if (MediaPlayer.IsPlaying) MediaPlayer.Pause();
                else MediaPlayer.Play();
            });
            StopCommand = new Command(() => MediaPlayer?.Stop());
            
            NextChannelCommand = new Command(ZappingNext);
            PreviousChannelCommand = new Command(ZappingPrevious);
        }

        private void ZappingNext()
        {
            if (FilteredChannels.Count == 0 || SelectedChannel == null) return;
            int index = FilteredChannels.IndexOf(SelectedChannel);
            int nextIndex = (index + 1) % FilteredChannels.Count;
            SelectedChannel = FilteredChannels[nextIndex];
        }

        private void ZappingPrevious()
        {
            if (FilteredChannels.Count == 0 || SelectedChannel == null) return;
            int index = FilteredChannels.IndexOf(SelectedChannel);
            int prevIndex = (index - 1 + FilteredChannels.Count) % FilteredChannels.Count;
            SelectedChannel = FilteredChannels[prevIndex];
        }

        private void UpdateSubtitles()
        {
            if (MediaPlayer == null) return;
            MainThread.BeginInvokeOnMainThread(() => {
                Subtitles.Clear();
                foreach (var track in MediaPlayer.SpuDescription) Subtitles.Add(track);
                SelectedSubtitle = Subtitles.FirstOrDefault(t => t.Id == MediaPlayer.Spu);
            });
        }

        private async Task LoadChannelsAsync(MediaSource source)
        {
            IsLoading = true;
            _allChannels.Clear();
            FilteredChannels.Clear();
            Categories.Clear();
            Categories.Add("All");
            Categories.Add("⭐ Favorites");
            SelectedCategory = "All";
            SearchText = string.Empty;
            
            List<Channel> result;
            if (source.Type == SourceType.Xtream)
            {
                result = await _xtreamService.GetLiveStreamsAsync(source);
            }
            else
            {
                if (source.Url.Contains(".xml")) _allPrograms = await _epgService.ParseFromUrlAsync(source.Url);
                result = await _parserService.ParseFromUrlAsync(source.Url);
            }

            _allChannels = result;
            var cats = _allChannels.Select(c => c.Group).Distinct().OrderBy(g => g);
            foreach (var cat in cats) Categories.Add(cat);

            ApplyFilters();
            IsLoading = false;

            _ = Task.Run(async () => {
                foreach (var channel in _allChannels.Where(c => !string.IsNullOrEmpty(c.LogoUrl)))
                {
                    string cachedPath = await _imageCacheService.GetCachedImagePathAsync(channel.LogoUrl);
                    if (cachedPath != channel.LogoUrl)
                    {
                        channel.LogoUrl = cachedPath;
                    }
                }
            });
        }

        private void ApplyFilters()
        {
            var filtered = _allChannels.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
                filtered = filtered.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            
            if (SelectedCategory == "⭐ Favorites")
                filtered = filtered.Where(c => _favoriteUrls.Contains(c.Url));
            else if (SelectedCategory != "All")
                filtered = filtered.Where(c => c.Group == SelectedCategory);

            FilteredChannels.Clear();
            foreach (var channel in filtered)
            {
                channel.IsFavorite = _favoriteUrls.Contains(channel.Url);
                channel.CurrentProgram = _allPrograms.FirstOrDefault(p => 
                    p.ChannelId == channel.EpgId && 
                    DateTime.Now >= p.StartTime && DateTime.Now <= p.EndTime);
                FilteredChannels.Add(channel);
            }
        }

        private void ToggleFavorite(Channel channel)
        {
            if (channel == null) return;
            if (_favoriteUrls.Contains(channel.Url)) _favoriteUrls.Remove(channel.Url);
            else _favoriteUrls.Add(channel.Url);
            channel.IsFavorite = _favoriteUrls.Contains(channel.Url);
            _storageService.SaveFavorites(_favoriteUrls);
            
            if (SelectedCategory == "⭐ Favorites") ApplyFilters();
        }

        private void PlayChannel(Channel channel)
        {
            if (MediaPlayer == null || LibVLC == null) return;
            
            // Add to history
            if (_historyUrls.Contains(channel.Url)) _historyUrls.Remove(channel.Url);
            _historyUrls.Insert(0, channel.Url);
            _storageService.SaveHistory(_historyUrls);

            Subtitles.Clear();
            using var media = new Media(LibVLC, new Uri(channel.Url));
            MediaPlayer.Play(media);
        }
    }

    public class HomeViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        public ObservableCollection<string> RecentUrls { get; }

        public HomeViewModel()
        {
            _storageService = new StorageService();
            RecentUrls = new ObservableCollection<string>(_storageService.LoadHistory());
        }
    }

    public class SettingsViewModel : ViewModelBase
    {
        private readonly ImageCacheService _imageCacheService;
        public ICommand ClearCacheCommand { get; }

        public SettingsViewModel()
        {
            _imageCacheService = new ImageCacheService();
            ClearCacheCommand = new Command(() => {
                _imageCacheService.ClearCache();
            });
        }
    }

    public class SourcesViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        private string _sourceName = "";
        private string _sourceUrl = "";
        private string _username = "";
        private string _password = "";
        private string _statusMessage = "";
        private SourceType _selectedType = SourceType.M3U;

        public string SourceName { get => _sourceName; set => SetProperty(ref _sourceName, value); }
        public string SourceUrl { get => _sourceUrl; set => SetProperty(ref _sourceUrl, value); }
        public string Username { get => _username; set => SetProperty(ref _username, value); }
        public string Password { get => _password; set => SetProperty(ref _password, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public SourceType SelectedType 
        { 
            get => _selectedType; 
            set { 
                if (SetProperty(ref _selectedType, value)) {
                    OnPropertyChanged(nameof(IsM3U));
                    OnPropertyChanged(nameof(IsXtream));
                }
            } 
        }

        public bool IsM3U => SelectedType == SourceType.M3U;
        public bool IsXtream => SelectedType == SourceType.Xtream;
        public IEnumerable<SourceType> SourceTypes => Enum.GetValues(typeof(SourceType)).Cast<SourceType>();

        public ObservableCollection<MediaSource> Sources { get; }
        public ICommand AddSourceCommand { get; }

        public SourcesViewModel()
        {
            _storageService = new StorageService();
            Sources = new ObservableCollection<MediaSource>(_storageService.LoadSources());
            AddSourceCommand = new Command(() => {
                var newSource = new MediaSource { 
                    Name = SourceName, 
                    Url = SourceUrl, 
                    Type = SelectedType,
                    Username = Username,
                    Password = Password
                };
                Sources.Add(newSource);
                _storageService.SaveSources(new List<MediaSource>(Sources));
                StatusMessage = "Source added!";
                SourceName = ""; SourceUrl = ""; Username = ""; Password = "";
            });
        }
    }

    public class ProfileViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        private UserProfile? _selectedProfile;

        public ObservableCollection<UserProfile> Profiles { get; }
        public UserProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value) && value != null)
                {
                    SelectProfile(value);
                }
            }
        }

        public ICommand ManageProfilesCommand { get; }

        public ProfileViewModel()
        {
            _storageService = new StorageService();
            var loaded = _storageService.LoadProfiles();
            if (!loaded.Any())
            {
                loaded.Add(new UserProfile { Name = "Guest", AvatarSource = "profile_default.png" });
                _storageService.SaveProfiles(loaded);
            }
            Profiles = new ObservableCollection<UserProfile>(loaded);
            ManageProfilesCommand = new Command(() => { /* Open management view */ });
        }

        private void SelectProfile(UserProfile profile)
        {
            _storageService.SetCurrentProfile(profile);
            // Navigate to main app shell
            Application.Current.MainPage = new AppShell();
        }
    }
}
