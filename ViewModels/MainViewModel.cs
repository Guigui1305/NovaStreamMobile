using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.ViewModels
{
    // ==================== BASE VIEW MODEL ====================
    public class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }

    // ==================== LIVE TV VIEW MODEL ====================
    public class LiveTvViewModel : BaseViewModel
    {
        private readonly XtreamService _xtreamService = new();
        private readonly StorageService _storageService = new();
        private CancellationTokenSource? _cts;

        private ObservableCollection<Channel> _channels = new();
        public ObservableCollection<Channel> Channels
        {
            get => _channels;
            set => SetProperty(ref _channels, value);
        }

        private ObservableCollection<XtreamService.XtreamCategory> _categories = new();
        public ObservableCollection<XtreamService.XtreamCategory> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        private Channel? _selectedChannel;
        public Channel? SelectedChannel
        {
            get => _selectedChannel;
            set => SetProperty(ref _selectedChannel, value);
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _errorText = "";
        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _currentChannelName = "";
        public string CurrentChannelName
        {
            get => _currentChannelName;
            set => SetProperty(ref _currentChannelName, value);
        }

        public MediaSource? GetCurrentSource()
        {
            try
            {
                var sources = _storageService.LoadSources();
                return sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
            }
            catch { return null; }
        }

        public async Task LoadCategoriesAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                IsLoading = true;
                ErrorText = "";
                StatusText = "Chargement des categories...";

                var source = GetCurrentSource();
                if (source == null)
                {
                    ErrorText = "Aucune source configuree. Allez dans Sources.";
                    return;
                }

                var cats = await _xtreamService.GetFrenchLiveCategoriesAsync(source, _cts.Token);
                Categories = new ObservableCollection<XtreamService.XtreamCategory>(cats);
                StatusText = $"{cats.Count} categories";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorText = $"Erreur: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadChannelsByCategoryAsync(string categoryId, string categoryName)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                IsLoading = true;
                ErrorText = "";
                StatusText = "Chargement...";

                var source = GetCurrentSource();
                if (source == null) return;

                var channels = await _xtreamService.GetLiveStreamsByCategoryAsync(source, categoryId, _cts.Token);
                Channels = new ObservableCollection<Channel>(channels.Take(300));
                StatusText = $"{channels.Count} chaines dans {categoryName}";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorText = $"Erreur: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    // ==================== HOME VIEW MODEL ====================
    public class HomeViewModel : BaseViewModel
    {
        private readonly XtreamService _xtreamService = new();
        private readonly StorageService _storageService = new();

        // Computed properties for HomeView bindings
        public string WelcomeMessage
        {
            get
            {
                var hour = DateTime.Now.Hour;
                if (hour < 12) return "Bonjour";
                if (hour < 18) return "Bon apres-midi";
                return "Bonsoir";
            }
        }

        public int SourceCount
        {
            get
            {
                try { return _storageService.LoadSources().Count; }
                catch { return 0; }
            }
        }

        public int FavoriteCount => 0;

        public bool HasPopularChannels => PopularChannels.Count > 0;
        public bool HasTrendingMovies => TrendingMovies.Count > 0;
        public bool HasTrendingSeries => TrendingSeries.Count > 0;

        private ObservableCollection<Channel> _popularChannels = new();
        public ObservableCollection<Channel> PopularChannels
        {
            get => _popularChannels;
            set
            {
                SetProperty(ref _popularChannels, value);
                OnPropertyChanged(nameof(HasPopularChannels));
            }
        }

        private ObservableCollection<XtreamService.VodItem> _trendingMovies = new();
        public ObservableCollection<XtreamService.VodItem> TrendingMovies
        {
            get => _trendingMovies;
            set
            {
                SetProperty(ref _trendingMovies, value);
                OnPropertyChanged(nameof(HasTrendingMovies));
            }
        }

        private ObservableCollection<XtreamService.SeriesItem> _trendingSeries = new();
        public ObservableCollection<XtreamService.SeriesItem> TrendingSeries
        {
            get => _trendingSeries;
            set
            {
                SetProperty(ref _trendingSeries, value);
                OnPropertyChanged(nameof(HasTrendingSeries));
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _errorText = "";
        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }

        public HomeViewModel()
        {
            _ = LoadHomeContentAsync();
        }

        public async Task LoadHomeContentAsync()
        {
            try
            {
                IsLoading = true;
                ErrorText = "";
                OnPropertyChanged(nameof(SourceCount));
                OnPropertyChanged(nameof(FavoriteCount));

                var sources = _storageService.LoadSources();
                var source = sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
                if (source == null)
                {
                    ErrorText = "Aucune source configuree.";
                    return;
                }

                var channelsTask = LoadPopularChannelsAsync(source);
                var moviesTask = LoadTrendingMoviesAsync(source);
                var seriesTask = LoadTrendingSeriesAsync(source);

                await Task.WhenAll(channelsTask, moviesTask, seriesTask);
            }
            catch (Exception ex)
            {
                ErrorText = $"Erreur: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadPopularChannelsAsync(MediaSource source)
        {
            try
            {
                var cats = await _xtreamService.GetFrenchLiveCategoriesAsync(source);
                if (cats.Count > 0)
                {
                    var channels = await _xtreamService.GetLiveStreamsByCategoryAsync(source, cats[0].CategoryId);
                    PopularChannels = new ObservableCollection<Channel>(channels.Take(20));
                }
            }
            catch { }
        }

        private async Task LoadTrendingMoviesAsync(MediaSource source)
        {
            try
            {
                var cats = await _xtreamService.GetFrenchVodCategoriesAsync(source);
                if (cats.Count > 0)
                {
                    var movies = await _xtreamService.GetVodStreamsByCategoryAsync(source, cats[0].CategoryId);
                    TrendingMovies = new ObservableCollection<XtreamService.VodItem>(movies.Take(20));
                }
            }
            catch { }
        }

        private async Task LoadTrendingSeriesAsync(MediaSource source)
        {
            try
            {
                var cats = await _xtreamService.GetFrenchSeriesCategoriesAsync(source);
                if (cats.Count > 0)
                {
                    var series = await _xtreamService.GetSeriesByCategoryAsync(source, cats[0].CategoryId);
                    TrendingSeries = new ObservableCollection<XtreamService.SeriesItem>(series.Take(20));
                }
            }
            catch { }
        }
    }

    // ==================== SETTINGS VIEW MODEL ====================
    public class SettingsViewModel : BaseViewModel
    {
        private string _epgUrl = "";
        public string EpgUrl
        {
            get => _epgUrl;
            set => SetProperty(ref _epgUrl, value);
        }
    }
}
