using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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

    public class SubtitleTrack
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
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
        private bool _hasChannel;
        private string _currentChannelName = "";
        private int _volume = 100;
        private MediaSource? _selectedSource;
        private Channel? _selectedChannel;
        private List<string> _favoriteUrls;
        private List<string> _historyUrls;
        private List<Program> _allPrograms = new List<Program>();
        private SubtitleTrack? _selectedSubtitle;
        private CancellationTokenSource? _searchCts;

        private string _searchText = string.Empty;
        private string _selectedCategory = "Toutes";
        private List<Channel> _allChannels = new List<Channel>();

        // Reprise de lecture : URL -> position en ms
        private Dictionary<string, long> _resumePositions = new Dictionary<string, long>();

        public LibVLC? LibVLC { get; private set; }
        public MediaPlayer? MediaPlayer { get; private set; }

        public ObservableCollection<MediaSource> Sources { get; }
        public ObservableCollection<Channel> FilteredChannels { get; } = new ObservableCollection<Channel>();
        public ObservableCollection<string> Categories { get; } = new ObservableCollection<string>();
        public ObservableCollection<SubtitleTrack> Subtitles { get; } = new ObservableCollection<SubtitleTrack>();

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsPlaying { get => _isPlaying; set => SetProperty(ref _isPlaying, value); }
        public bool HasChannel { get => _hasChannel; set => SetProperty(ref _hasChannel, value); }
        public string CurrentChannelName { get => _currentChannelName; set => SetProperty(ref _currentChannelName, value); }

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
                    _searchCts?.Cancel();
                    _searchCts = new CancellationTokenSource();
                    var token = _searchCts.Token;
                    Task.Delay(300, token).ContinueWith(_ =>
                    {
                        if (!token.IsCancellationRequested)
                            MainThread.BeginInvokeOnMainThread(ApplyFilters);
                    }, TaskContinuationOptions.OnlyOnRanToCompletion);
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

        public SubtitleTrack? SelectedSubtitle
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

            try
            {
                LibVLC = new LibVLC("--no-osd");
                MediaPlayer = new MediaPlayer(LibVLC);
                MediaPlayer.Playing += (s, e) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        IsPlaying = true;
                        UpdateSubtitles();
                    });
                };
                MediaPlayer.Paused += (s, e) => MainThread.BeginInvokeOnMainThread(() => IsPlaying = false);
                MediaPlayer.Stopped += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsPlaying = false;
                    HasChannel = false;
                    CurrentChannelName = "";
                });
                MediaPlayer.PositionChanged += (s, e) => SaveResumePosition();
            }
            catch
            {
                // LibVLC init failed
            }

            ToggleFavoriteCommand = new Command<Channel>(ToggleFavorite);
            PlayPauseCommand = new Command(() =>
            {
                if (MediaPlayer == null) return;
                if (MediaPlayer.IsPlaying) MediaPlayer.Pause();
                else MediaPlayer.Play();
            });
            StopCommand = new Command(() => MediaPlayer?.Stop());
            NextChannelCommand = new Command(ZappingNext);
            PreviousChannelCommand = new Command(ZappingPrevious);
        }

        private void SaveResumePosition()
        {
            if (SelectedChannel == null || MediaPlayer == null) return;
            if (!Preferences.Get("resume_enabled", true)) return;
            long time = MediaPlayer.Time;
            if (time > 5000)
            {
                _resumePositions[SelectedChannel.Url] = time;
            }
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
            try
            {
                Subtitles.Clear();
                var tracks = MediaPlayer.SpuDescription;
                if (tracks != null)
                {
                    foreach (var track in tracks)
                        Subtitles.Add(new SubtitleTrack { Id = track.Id, Name = track.Name ?? $"Piste {track.Id}" });
                }
                SelectedSubtitle = Subtitles.FirstOrDefault(t => t.Id == MediaPlayer.Spu);
            }
            catch { }
        }

        private async Task LoadChannelsAsync(MediaSource source)
        {
            IsLoading = true;
            _allChannels.Clear();
            FilteredChannels.Clear();
            Categories.Clear();
            Categories.Add("Toutes");
            Categories.Add("Favoris");
            SelectedCategory = "Toutes";
            SearchText = string.Empty;

            try
            {
                List<Channel> result;
                if (source.Type == SourceType.Xtream)
                {
                    result = await _xtreamService.GetLiveStreamsAsync(source);
                    string epgUrl = source.GetEpgUrl();
                    if (!string.IsNullOrEmpty(epgUrl))
                    {
                        try { _allPrograms = await _epgService.ParseFromUrlAsync(epgUrl); }
                        catch { }
                    }
                }
                else
                {
                    string m3uUrl = source.GetM3UUrl();
                    result = await _parserService.ParseFromUrlAsync(m3uUrl);
                }

                _allChannels = result;
                var cats = _allChannels.Select(c => c.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g);
                foreach (var cat in cats) Categories.Add(cat);

                ApplyFilters();
            }
            catch { }

            IsLoading = false;

            _ = Task.Run(async () =>
            {
                foreach (var channel in _allChannels.Where(c => !string.IsNullOrEmpty(c.LogoUrl)))
                {
                    try
                    {
                        string cachedPath = await _imageCacheService.GetCachedImagePathAsync(channel.LogoUrl);
                        if (cachedPath != channel.LogoUrl)
                            channel.LogoUrl = cachedPath;
                    }
                    catch { }
                }
            });
        }

        private void ApplyFilters()
        {
            var filtered = _allChannels.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
                filtered = filtered.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (SelectedCategory == "Favoris")
                filtered = filtered.Where(c => _favoriteUrls.Contains(c.Url));
            else if (SelectedCategory != "Toutes")
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
            if (SelectedCategory == "Favoris") ApplyFilters();
        }

        private void PlayChannel(Channel channel)
        {
            if (MediaPlayer == null || LibVLC == null) return;

            HasChannel = true;
            CurrentChannelName = channel.Name;

            if (_historyUrls.Contains(channel.Url)) _historyUrls.Remove(channel.Url);
            _historyUrls.Insert(0, channel.Url);
            if (_historyUrls.Count > 50) _historyUrls = _historyUrls.Take(50).ToList();
            _storageService.SaveHistory(_historyUrls);

            Subtitles.Clear();
            using var media = new Media(LibVLC, new Uri(channel.Url));
            MediaPlayer.Play(media);

            if (Preferences.Get("resume_enabled", true) && _resumePositions.ContainsKey(channel.Url))
            {
                long pos = _resumePositions[channel.Url];
                Task.Delay(1000).ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (MediaPlayer.IsPlaying)
                            MediaPlayer.Time = pos;
                    });
                });
            }
        }
    }

    public class HomeViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        public ObservableCollection<string> RecentChannels { get; }
        public int SourceCount { get; }
        public int FavoriteCount { get; }

        public HomeViewModel()
        {
            _storageService = new StorageService();
            RecentChannels = new ObservableCollection<string>(_storageService.LoadHistory().Take(10));
            SourceCount = _storageService.LoadSources().Count;
            FavoriteCount = _storageService.LoadFavorites().Count;
        }
    }

    public class SettingsViewModel : ViewModelBase
    {
        private readonly ImageCacheService _imageCacheService;
        public ICommand ClearCacheCommand { get; }

        public SettingsViewModel()
        {
            _imageCacheService = new ImageCacheService();
            ClearCacheCommand = new Command(() =>
            {
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
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
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
        public ICommand DeleteSourceCommand { get; }

        public SourcesViewModel()
        {
            _storageService = new StorageService();
            Sources = new ObservableCollection<MediaSource>(_storageService.LoadSources());
            AddSourceCommand = new Command(() =>
            {
                if (string.IsNullOrWhiteSpace(SourceName) || string.IsNullOrWhiteSpace(SourceUrl))
                {
                    StatusMessage = "Veuillez remplir le nom et l'URL.";
                    return;
                }
                var newSource = new MediaSource
                {
                    Name = SourceName,
                    Url = SourceUrl,
                    Type = SelectedType,
                    Username = Username,
                    Password = Password
                };
                Sources.Add(newSource);
                _storageService.SaveSources(new List<MediaSource>(Sources));
                StatusMessage = "Source ajoutee avec succes !";
                SourceName = ""; SourceUrl = ""; Username = ""; Password = "";
            });
            DeleteSourceCommand = new Command<MediaSource>((source) =>
            {
                if (source == null) return;
                Sources.Remove(source);
                _storageService.SaveSources(new List<MediaSource>(Sources));
                StatusMessage = "Source supprimee.";
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
                loaded.Add(new UserProfile { Name = "Invite", AvatarSource = "profile_default.png" });
                _storageService.SaveProfiles(loaded);
            }
            Profiles = new ObservableCollection<UserProfile>(loaded);
            ManageProfilesCommand = new Command(() => { });
        }

        private void SelectProfile(UserProfile profile)
        {
            _storageService.SetCurrentProfile(profile);
            if (Application.Current != null)
            {
                Application.Current.MainPage = new AppShell();
            }
        }
    }
}
