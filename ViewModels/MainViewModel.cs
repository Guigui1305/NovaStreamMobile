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

    // ==================== LIVE TV VIEWMODEL ====================

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
        private string _statusMessage = "";
        private string _errorMessage = "";
        private int _volume = 100;
        private int _totalChannelCount;
        private MediaSource? _selectedSource;
        private Channel? _selectedChannel;
        private List<string> _favoriteUrls;
        private List<string> _historyUrls;
        private List<Program> _allPrograms = new();
        private SubtitleTrack? _selectedSubtitle;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _loadCts;
        private Dictionary<string, string> _categoryMap = new();

        private string _searchText = string.Empty;
        private string _selectedCategoryId = "";
        private string _selectedCategoryName = "Toutes";
        private List<Channel> _allChannels = new();
        private Dictionary<string, long> _resumePositions = new();

        public LibVLC? LibVLC { get; private set; }
        public MediaPlayer? MediaPlayer { get; private set; }

        public ObservableCollection<MediaSource> Sources { get; }
        public ObservableCollection<Channel> FilteredChannels { get; } = new();
        public ObservableCollection<XtreamCategory> LiveCategories { get; } = new();
        public ObservableCollection<SubtitleTrack> Subtitles { get; } = new();

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsPlaying { get => _isPlaying; set => SetProperty(ref _isPlaying, value); }
        public bool HasChannel { get => _hasChannel; set => SetProperty(ref _hasChannel, value); }
        public string CurrentChannelName { get => _currentChannelName; set => SetProperty(ref _currentChannelName, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public int TotalChannelCount { get => _totalChannelCount; set => SetProperty(ref _totalChannelCount, value); }

        public int Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value) && MediaPlayer != null)
                    MediaPlayer.Volume = value;
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

        public string SelectedCategoryId
        {
            get => _selectedCategoryId;
            set
            {
                if (SetProperty(ref _selectedCategoryId, value))
                {
                    OnPropertyChanged(nameof(SelectedCategoryName));
                    if (_selectedSource != null && _selectedSource.Type == SourceType.Xtream && !string.IsNullOrEmpty(value))
                        _ = LoadChannelsByCategoryAsync(value);
                    else
                        ApplyFilters();
                }
            }
        }

        public string SelectedCategoryName
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedCategoryId)) return "Toutes";
                if (_selectedCategoryId == "favorites") return "Favoris";
                var cat = LiveCategories.FirstOrDefault(c => c.CategoryId == _selectedCategoryId);
                return cat?.CategoryName ?? "Toutes";
            }
        }

        public MediaSource? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (SetProperty(ref _selectedSource, value) && value != null)
                    _ = InitializeSourceAsync(value);
            }
        }

        public Channel? SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value) && value != null)
                {
                    try
                    {
                        PlayChannel(value);
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Erreur de lecture: {ex.Message}";
                        OnPropertyChanged(nameof(HasError));
                        System.Diagnostics.Debug.WriteLine($"[LiveTV] SelectedChannel setter error: {ex}");
                    }
                }
            }
        }

        public SubtitleTrack? SelectedSubtitle
        {
            get => _selectedSubtitle;
            set
            {
                if (SetProperty(ref _selectedSubtitle, value) && value != null && MediaPlayer != null)
                    MediaPlayer.SetSpu(value.Id);
            }
        }

        public ICommand ToggleFavoriteCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand NextChannelCommand { get; }
        public ICommand PreviousChannelCommand { get; }
        public ICommand SelectCategoryCommand { get; }
        public ICommand RefreshCommand { get; }

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
                LibVLC = new LibVLC("--no-osd", "--network-caching=3000");
                MediaPlayer = new MediaPlayer(LibVLC);
                MediaPlayer.Playing += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        IsPlaying = true;
                        UpdateSubtitles();
                    }
                    catch { }
                });
                MediaPlayer.Paused += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { IsPlaying = false; } catch { }
                });
                MediaPlayer.Stopped += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        IsPlaying = false;
                        HasChannel = false;
                        CurrentChannelName = "";
                    }
                    catch { }
                });
                MediaPlayer.EncounteredError += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        ErrorMessage = "Erreur de lecture. Verifiez votre connexion.";
                        OnPropertyChanged(nameof(HasError));
                    }
                    catch { }
                });
                MediaPlayer.PositionChanged += (s, e) =>
                {
                    try { SaveResumePosition(); } catch { }
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur LibVLC: {ex.Message}";
                OnPropertyChanged(nameof(HasError));
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
            SelectCategoryCommand = new Command<string>(catId => SelectedCategoryId = catId ?? "");
            RefreshCommand = new Command(() =>
            {
                if (_selectedSource != null)
                {
                    XtreamService.ClearCache();
                    _ = InitializeSourceAsync(_selectedSource);
                }
            });

            if (Sources.Count > 0)
                SelectedSource = Sources[0];
        }

        private async Task InitializeSourceAsync(MediaSource source)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            IsLoading = true;
            ErrorMessage = "";
            OnPropertyChanged(nameof(HasError));
            StatusMessage = "Connexion au serveur...";
            _allChannels.Clear();
            FilteredChannels.Clear();
            LiveCategories.Clear();

            try
            {
                if (source.Type == SourceType.Xtream)
                {
                    // Etape 1: Charger les categories (rapide)
                    StatusMessage = "Chargement des categories...";
                    var cats = await _xtreamService.GetFrenchLiveCategoriesAsync(source, ct);
                    _categoryMap.Clear();

                    foreach (var cat in cats)
                    {
                        _categoryMap[cat.CategoryId] = cat.CategoryName;
                        LiveCategories.Add(cat);
                    }

                    StatusMessage = $"{cats.Count} categories chargees";

                    // Etape 2: Charger la premiere categorie FR (TNT HD = 29, ou la premiere)
                    string defaultCatId = cats.FirstOrDefault(c =>
                        c.CategoryName.Contains("TNT", StringComparison.OrdinalIgnoreCase))?.CategoryId
                        ?? cats.FirstOrDefault()?.CategoryId ?? "";

                    if (!string.IsNullOrEmpty(defaultCatId))
                    {
                        _selectedCategoryId = defaultCatId;
                        OnPropertyChanged(nameof(SelectedCategoryId));
                        OnPropertyChanged(nameof(SelectedCategoryName));
                        await LoadChannelsByCategoryAsync(defaultCatId);
                    }

                    // Etape 3: EPG en arriere-plan
                    string epgUrl = source.GetEpgUrl();
                    if (!string.IsNullOrEmpty(epgUrl))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                _allPrograms = await _epgService.ParseFromUrlAsync(epgUrl);
                                MainThread.BeginInvokeOnMainThread(ApplyFilters);
                            }
                            catch { }
                        });
                    }
                }
                else
                {
                    StatusMessage = "Chargement de la playlist M3U...";
                    string m3uUrl = source.GetM3UUrl();
                    _allChannels = await _parserService.ParseFromUrlAsync(m3uUrl);
                    TotalChannelCount = _allChannels.Count;
                    StatusMessage = $"{_allChannels.Count} chaines chargees";
                    ApplyFilters();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur: {ex.Message}";
                OnPropertyChanged(nameof(HasError));
                StatusMessage = "Echec du chargement";
                System.Diagnostics.Debug.WriteLine($"[LiveTV] InitializeSource error: {ex}");
            }

            IsLoading = false;
        }

        private async Task LoadChannelsByCategoryAsync(string categoryId)
        {
            if (_selectedSource == null || _selectedSource.Type != SourceType.Xtream) return;

            IsLoading = true;
            ErrorMessage = "";
            OnPropertyChanged(nameof(HasError));

            string catName = _categoryMap.TryGetValue(categoryId, out var cn) ? cn : categoryId;
            StatusMessage = $"Chargement: {catName}...";

            try
            {
                var channels = await _xtreamService.GetLiveStreamsByCategoryAsync(
                    _selectedSource, categoryId, _loadCts?.Token ?? CancellationToken.None);

                // Mapper les noms de categories
                foreach (var ch in channels)
                {
                    if (_categoryMap.TryGetValue(ch.Group, out string? name) && !string.IsNullOrEmpty(name))
                        ch.Group = name;
                }

                _allChannels = channels;
                TotalChannelCount = channels.Count;
                StatusMessage = $"{channels.Count} chaines dans {catName}";
                ApplyFilters();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur: {ex.Message}";
                OnPropertyChanged(nameof(HasError));
                StatusMessage = "Echec du chargement";
            }

            IsLoading = false;
        }

        private void SaveResumePosition()
        {
            if (SelectedChannel == null || MediaPlayer == null) return;
            if (!Preferences.Get("resume_enabled", true)) return;
            long time = MediaPlayer.Time;
            if (time > 5000)
                _resumePositions[SelectedChannel.Url] = time;
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

        private void ApplyFilters()
        {
            var filtered = _allChannels.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
                filtered = filtered.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (_selectedCategoryId == "favorites")
                filtered = filtered.Where(c => _favoriteUrls.Contains(c.Url));

            FilteredChannels.Clear();
            foreach (var channel in filtered.Take(300))
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
            if (_selectedCategoryId == "favorites") ApplyFilters();
        }

        private Media? _currentMedia;

        private void PlayChannel(Channel channel)
        {
            if (MediaPlayer == null || LibVLC == null) return;

            try
            {
                HasChannel = true;
                CurrentChannelName = channel.Name;
                ErrorMessage = "";
                OnPropertyChanged(nameof(HasError));

                if (_historyUrls.Contains(channel.Url)) _historyUrls.Remove(channel.Url);
                _historyUrls.Insert(0, channel.Url);
                if (_historyUrls.Count > 50) _historyUrls = _historyUrls.Take(50).ToList();
                _storageService.SaveHistory(_historyUrls);

                Subtitles.Clear();

                // IMPORTANT: Ne PAS utiliser 'using' ici !
                // Le Media doit rester vivant pendant toute la lecture.
                // On garde une reference et on dispose l'ancien avant d'en creer un nouveau.
                _currentMedia?.Dispose();
                _currentMedia = new Media(LibVLC, new Uri(channel.Url));
                _currentMedia.AddOption(":network-caching=3000");
                MediaPlayer.Play(_currentMedia);

                if (Preferences.Get("resume_enabled", true) && _resumePositions.ContainsKey(channel.Url))
                {
                    long pos = _resumePositions[channel.Url];
                    Task.Delay(1500).ContinueWith(_ =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                if (MediaPlayer != null && MediaPlayer.IsPlaying)
                                    MediaPlayer.Time = pos;
                            }
                            catch { }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur de lecture: {ex.Message}";
                OnPropertyChanged(nameof(HasError));
                System.Diagnostics.Debug.WriteLine($"[LiveTV] PlayChannel error: {ex}");
            }
        }
    }

    // ==================== HOME VIEWMODEL ====================

    public class HomeViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        private readonly XtreamService _xtreamService;
        private bool _isLoading;
        private string _welcomeMessage = "";

        public ObservableCollection<string> RecentChannels { get; }
        public ObservableCollection<VodItem> TrendingMovies { get; } = new();
        public ObservableCollection<Channel> PopularChannels { get; } = new();
        public int SourceCount { get; }
        public int FavoriteCount { get; }
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public string WelcomeMessage { get => _welcomeMessage; set => SetProperty(ref _welcomeMessage, value); }
        public bool HasTrendingMovies => TrendingMovies.Count > 0;
        public bool HasPopularChannels => PopularChannels.Count > 0;

        public HomeViewModel()
        {
            _storageService = new StorageService();
            _xtreamService = new XtreamService();
            RecentChannels = new ObservableCollection<string>(_storageService.LoadHistory().Take(10));
            SourceCount = _storageService.LoadSources().Count;
            FavoriteCount = _storageService.LoadFavorites().Count;

            int hour = DateTime.Now.Hour;
            if (hour < 12) WelcomeMessage = "Bonjour !";
            else if (hour < 18) WelcomeMessage = "Bon apres-midi !";
            else WelcomeMessage = "Bonsoir !";

            _ = LoadHomeContentAsync();
        }

        private async Task LoadHomeContentAsync()
        {
            IsLoading = true;
            try
            {
                var sources = _storageService.LoadSources();
                var xtreamSource = sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
                if (xtreamSource == null) { IsLoading = false; return; }

                // Charger quelques films recents pour l'accueil
                try
                {
                    var vodCats = await _xtreamService.GetFrenchVodCategoriesAsync(xtreamSource);
                    var firstCat = vodCats.FirstOrDefault();
                    if (firstCat != null)
                    {
                        var movies = await _xtreamService.GetVodStreamsByCategoryAsync(xtreamSource, firstCat.CategoryId);
                        foreach (var m in movies.Take(10))
                            TrendingMovies.Add(m);
                        OnPropertyChanged(nameof(HasTrendingMovies));
                    }
                }
                catch { }

                // Charger quelques chaines populaires
                try
                {
                    var liveCats = await _xtreamService.GetFrenchLiveCategoriesAsync(xtreamSource);
                    var tntCat = liveCats.FirstOrDefault(c =>
                        c.CategoryName.Contains("TNT", StringComparison.OrdinalIgnoreCase));
                    if (tntCat != null)
                    {
                        var channels = await _xtreamService.GetLiveStreamsByCategoryAsync(xtreamSource, tntCat.CategoryId);
                        foreach (var ch in channels.Take(10))
                            PopularChannels.Add(ch);
                        OnPropertyChanged(nameof(HasPopularChannels));
                    }
                }
                catch { }
            }
            catch { }
            IsLoading = false;
        }
    }

    // ==================== SETTINGS VIEWMODEL ====================

    public class SettingsViewModel : ViewModelBase
    {
        private readonly ImageCacheService _imageCacheService;
        public ICommand ClearCacheCommand { get; }
        public ICommand ClearXtreamCacheCommand { get; }

        public SettingsViewModel()
        {
            _imageCacheService = new ImageCacheService();
            ClearCacheCommand = new Command(() => _imageCacheService.ClearCache());
            ClearXtreamCacheCommand = new Command(() => XtreamService.ClearCache());
        }
    }

    // ==================== SOURCES VIEWMODEL ====================

    public class SourcesViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        private readonly XtreamService _xtreamService;
        private string _sourceName = "";
        private string _sourceUrl = "";
        private string _username = "";
        private string _password = "";
        private string _statusMessage = "";
        private bool _isTesting;
        private SourceType _selectedType = SourceType.M3U;

        public string SourceName { get => _sourceName; set => SetProperty(ref _sourceName, value); }
        public string SourceUrl { get => _sourceUrl; set => SetProperty(ref _sourceUrl, value); }
        public string Username { get => _username; set => SetProperty(ref _username, value); }
        public string Password { get => _password; set => SetProperty(ref _password, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public bool IsTesting { get => _isTesting; set => SetProperty(ref _isTesting, value); }
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
        public ICommand TestSourceCommand { get; }

        public SourcesViewModel()
        {
            _storageService = new StorageService();
            _xtreamService = new XtreamService();
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

            TestSourceCommand = new Command<MediaSource>(async (source) =>
            {
                if (source == null || source.Type != SourceType.Xtream) return;
                IsTesting = true;
                StatusMessage = "Test de connexion en cours...";
                try
                {
                    bool valid = await _xtreamService.ValidateLoginAsync(source);
                    StatusMessage = valid ? "Connexion reussie !" : "Echec de l'authentification.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Erreur: {ex.Message}";
                }
                IsTesting = false;
            });
        }
    }

    // ==================== PROFILE VIEWMODEL ====================

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
                    SelectProfile(value);
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
                Application.Current.MainPage = new AppShell();
        }
    }
}
