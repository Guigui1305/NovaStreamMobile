using CommunityToolkit.Maui.Views;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class SeriesView : ContentPage
    {
        private readonly XtreamService _xtreamService;
        private readonly StorageService _storageService;
        private NovaStreamMobile.Models.MediaSource? _source;
        private string _selectedCategoryId = "";
        private string _searchText = "";
        private CancellationTokenSource? _loadCts;

        private List<SeriesDisplayItem> _allItems = new();
        private List<XtreamCategory> _seriesCategories = new();
        private int _displayedCount = 0;
        private const int PageSize = 60;

        // Player state
        private string _currentEpisodeUrl = "";
        private string _currentEpisodeName = "";
        private bool _isMuted = false;
        private bool _useInternalPlayer = false;

        public SeriesView()
        {
            InitializeComponent();
            _xtreamService = new XtreamService();
            _storageService = new StorageService();
            LoadSource();

            Player.MediaOpened += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { PlayerLoading.IsRunning = false; BtnPlayPause.Text = "⏸"; } catch { }
                });
            };

            Player.MediaFailed += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        PlayerLoading.IsRunning = false;
                        if (!string.IsNullOrEmpty(_currentEpisodeUrl))
                        {
                            ErrorLabel.Text = "Lecteur interne indisponible. Ouverture du lecteur externe...";
                            ErrorLabel.IsVisible = true;
                            await Task.Delay(500);
                            await OpenInExternalPlayerAsync(_currentEpisodeUrl, _currentEpisodeName);
                        }
                    }
                    catch { }
                });
            };
        }

        private void LoadSource()
        {
            var sources = _storageService.LoadSources();
            _source = sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
            if (_source == null)
                _source = sources.FirstOrDefault();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                if (_source != null && _seriesCategories.Count == 0)
                    _ = LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeriesView] OnAppearing error: {ex}");
            }
        }

        // ==================== CHARGEMENT CATEGORIES & SERIES ====================

        private async Task LoadCategoriesAsync()
        {
            if (_source == null || _source.Type != SourceType.Xtream)
            {
                LoadingIndicator.IsRunning = false;
                return;
            }

            StatusLabel.Text = "Chargement des categories...";
            LoadingIndicator.IsRunning = true;

            try
            {
                _seriesCategories = await _xtreamService.GetFrenchSeriesCategoriesAsync(_source);
                UpdateCategoriesBar(_seriesCategories);
                StatusLabel.Text = $"{_seriesCategories.Count} categories de series";
                LoadingIndicator.IsRunning = false;
                ErrorLabel.IsVisible = false;
            }
            catch (Exception ex)
            {
                LoadingIndicator.IsRunning = false;
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
                StatusLabel.Text = "Echec du chargement";
            }
        }

        private async Task LoadContentAsync()
        {
            if (_source == null || _source.Type != SourceType.Xtream || string.IsNullOrEmpty(_selectedCategoryId))
                return;

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            LoadingIndicator.IsRunning = true;
            ErrorLabel.IsVisible = false;
            LoadMoreButton.IsVisible = false;
            _allItems.Clear();
            _displayedCount = 0;

            try
            {
                StatusLabel.Text = "Chargement des series...";
                var items = await _xtreamService.GetSeriesByCategoryAsync(_source, _selectedCategoryId, ct);
                foreach (var item in items)
                {
                    _allItems.Add(new SeriesDisplayItem
                    {
                        Name = item.Name,
                        ImageUrl = item.Cover,
                        Rating = item.Rating,
                        Genre = item.Genre,
                        Plot = item.Plot,
                        Year = ExtractYear(item.ReleaseDate),
                        Id = item.SeriesId
                    });
                }
                StatusLabel.Text = $"{items.Count} series trouvees";
                ApplyFilter();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
                StatusLabel.Text = "";
            }

            LoadingIndicator.IsRunning = false;
        }

        private string ExtractYear(string date)
        {
            if (string.IsNullOrEmpty(date)) return "";
            if (date.Length >= 4 && int.TryParse(date.Substring(0, 4), out int year) && year > 1900)
                return year.ToString();
            return "";
        }

        // ==================== UI HELPERS ====================

        private void UpdateCategoriesBar(List<XtreamCategory> categories)
        {
            CategoriesBar.Children.Clear();
            foreach (var cat in categories.Take(40))
            {
                bool isSelected = cat.CategoryId == _selectedCategoryId;
                var btn = new Button
                {
                    Text = CleanCategoryName(cat.CategoryName),
                    BackgroundColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#252540"),
                    TextColor = isSelected ? Colors.White : Color.FromArgb("#A0A0B8"),
                    FontSize = 12,
                    CornerRadius = 20,
                    HeightRequest = 36,
                    Padding = new Thickness(14, 0),
                    FontAttributes = FontAttributes.Bold,
                    BorderColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#353550"),
                    BorderWidth = 1
                };
                string catId = cat.CategoryId;
                btn.Clicked += (s, e) =>
                {
                    _selectedCategoryId = catId;
                    UpdateCategoriesBar(categories);
                    _ = LoadContentAsync();
                };
                CategoriesBar.Children.Add(btn);
            }
        }

        private string CleanCategoryName(string name)
        {
            name = name.Replace("|FR|", "").Replace("|MULTI|", "").Replace("|CH|", "").Trim();
            if (name.StartsWith("*")) name = name.TrimStart('*').Trim();
            while (name.Length > 0 && !char.IsLetterOrDigit(name[0]) && name[0] != '(')
                name = name.Substring(1).Trim();
            return name;
        }

        private void ApplyFilter()
        {
            var filtered = _allItems.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchText))
                filtered = filtered.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

            var filteredList = filtered.ToList();
            _displayedCount = Math.Min(PageSize, filteredList.Count);

            SeriesList.ItemsSource = filteredList.Take(_displayedCount).ToList();
            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;
        }

        // ==================== EVENT HANDLERS ====================

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchText = e.NewTextValue ?? "";
            if (_allItems.Count > 0)
                ApplyFilter();
        }

        private void OnLoadMoreClicked(object? sender, EventArgs e)
        {
            var filtered = _allItems.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchText))
                filtered = filtered.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

            var filteredList = filtered.ToList();
            _displayedCount = Math.Min(_displayedCount + PageSize, filteredList.Count);
            SeriesList.ItemsSource = filteredList.Take(_displayedCount).ToList();
            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;
        }

        private async void OnSeriesTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                SeriesDisplayItem? item = null;
                if (sender is Frame frame)
                    item = frame.BindingContext as SeriesDisplayItem;
                else if (sender is VisualElement ve)
                    item = ve.BindingContext as SeriesDisplayItem;

                if (item == null || _source == null) return;

                StatusLabel.Text = "Chargement des details...";

                var info = await _xtreamService.GetSeriesInfoAsync(_source, item.Id);

                if (info.Seasons.Count == 0)
                {
                    string desc = !string.IsNullOrEmpty(info.Plot) ? info.Plot : item.Plot ?? "Aucune description.";
                    await DisplayAlert(item.Name, desc, "OK");
                    StatusLabel.Text = "";
                    return;
                }

                // Choisir une saison
                var seasonNames = info.Seasons.Select(s => s.Name).ToArray();
                string? selectedSeason = await DisplayActionSheet(
                    $"{item.Name} - Choisir une saison", "Annuler", null, seasonNames);

                if (string.IsNullOrEmpty(selectedSeason) || selectedSeason == "Annuler")
                {
                    StatusLabel.Text = "";
                    return;
                }

                var season = info.Seasons.FirstOrDefault(s => s.Name == selectedSeason);
                if (season == null || season.Episodes.Count == 0)
                {
                    await DisplayAlert("Info", "Aucun episode disponible.", "OK");
                    return;
                }

                // Choisir un episode
                var epNames = season.Episodes.Select(ep =>
                    $"E{ep.EpisodeNum:D2} - {(string.IsNullOrEmpty(ep.Title) ? "Episode " + ep.EpisodeNum : ep.Title)}")
                    .ToArray();

                string? selectedEp = await DisplayActionSheet(
                    $"{item.Name} - {selectedSeason}", "Annuler", null, epNames);

                if (string.IsNullOrEmpty(selectedEp) || selectedEp == "Annuler")
                {
                    StatusLabel.Text = "";
                    return;
                }

                int epIndex = Array.IndexOf(epNames, selectedEp);
                if (epIndex >= 0 && epIndex < season.Episodes.Count)
                {
                    var episode = season.Episodes[epIndex];
                    if (!string.IsNullOrEmpty(episode.Url))
                    {
                        string epName = $"{item.Name} - {selectedSeason} - {selectedEp}";
                        await PlayEpisodeAsync(epName, episode.Url);
                    }
                    else
                    {
                        await DisplayAlert("Erreur", "URL de l'episode non disponible.", "OK");
                    }
                }

                StatusLabel.Text = "";
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
                StatusLabel.Text = "";
            }
        }

        private async void OnEpisodeTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                SeriesEpisode? episode = null;
                if (sender is Frame frame)
                    episode = frame.BindingContext as SeriesEpisode;
                else if (sender is VisualElement ve)
                    episode = ve.BindingContext as SeriesEpisode;

                if (episode == null || string.IsNullOrEmpty(episode.Url)) return;

                string epName = !string.IsNullOrEmpty(episode.Title)
                    ? $"E{episode.EpisodeNum:D2} - {episode.Title}"
                    : $"Episode {episode.EpisodeNum}";
                await PlayEpisodeAsync(epName, episode.Url);
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
            }
        }

        // ==================== LECTEUR VIDEO ====================

        private async Task PlayEpisodeAsync(string name, string url)
        {
            try
            {
                _currentEpisodeName = name;
                _currentEpisodeUrl = url;
                NowPlayingLabel.Text = name;
                ErrorLabel.IsVisible = false;

                if (_useInternalPlayer)
                {
                    // Mode lecteur interne MediaElement
                    PlayerSection.IsVisible = true;
                    PlayerLoading.IsRunning = true;
                    Player.Stop();
                    Player.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(url);
                    BtnPlayPause.Text = "⏸";
                }
                else
                {
                    // Mode lecteur externe par defaut (plus fiable)
                    await OpenInExternalPlayerAsync(url, name);
                }
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
                PlayerLoading.IsRunning = false;
            }
        }

        // ==================== PLAYER CONTROLS ====================

        private void OnPlayPauseClicked(object? sender, EventArgs e)
        {
            try
            {
                if (Player.CurrentState == CommunityToolkit.Maui.Core.Primitives.MediaElementState.Playing)
                {
                    Player.Pause();
                    BtnPlayPause.Text = "▶";
                }
                else
                {
                    Player.Play();
                    BtnPlayPause.Text = "⏸";
                }
            }
            catch { }
        }

        private void OnVolumeClicked(object? sender, EventArgs e)
        {
            _isMuted = !_isMuted;
            Player.ShouldMute = _isMuted;
        }

        private void OnClosePlayerClicked(object? sender, EventArgs e)
        {
            try
            {
                Player.Stop();
                Player.Source = null;
                PlayerSection.IsVisible = false;
                NowPlayingLabel.Text = "";
                PlayerLoading.IsRunning = false;
            }
            catch { }
        }

        private async void OnExternalPlayerClicked(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentEpisodeUrl))
                await OpenInExternalPlayerAsync(_currentEpisodeUrl, _currentEpisodeName);
        }

        private void OnBackToSeriesClicked(object? sender, EventArgs e)
        {
            // Not used in current flow (ActionSheet navigation)
        }

        private void OnTogglePlayerClicked(object? sender, EventArgs e)
        {
            _useInternalPlayer = !_useInternalPlayer;
            if (sender is Button btn)
            {
                btn.Text = _useInternalPlayer ? "🔄 Externe" : "🔄 Interne";
            }
        }

        // ==================== EXTERNAL PLAYER ====================

        private async Task OpenInExternalPlayerAsync(string url, string title)
        {
#if ANDROID
            try
            {
                var context = Android.App.Application.Context;

                // Essayer VLC d'abord
                var vlcIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                vlcIntent.SetPackage("org.videolan.vlc");
                vlcIntent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                vlcIntent.PutExtra("title", title);
                vlcIntent.AddFlags(Android.Content.ActivityFlags.NewTask);

                try
                {
                    context.StartActivity(vlcIntent);
                    return;
                }
                catch (Android.Content.ActivityNotFoundException) { }

                // Essayer MX Player
                var mxIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                mxIntent.SetPackage("com.mxtech.videoplayer.ad");
                mxIntent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                mxIntent.AddFlags(Android.Content.ActivityFlags.NewTask);

                try
                {
                    context.StartActivity(mxIntent);
                    return;
                }
                catch (Android.Content.ActivityNotFoundException) { }

                // Essayer MX Player Pro
                var mxProIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                mxProIntent.SetPackage("com.mxtech.videoplayer.pro");
                mxProIntent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                mxProIntent.AddFlags(Android.Content.ActivityFlags.NewTask);

                try
                {
                    context.StartActivity(mxProIntent);
                    return;
                }
                catch (Android.Content.ActivityNotFoundException) { }

                // Fallback : n'importe quel lecteur video
                var genericIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                genericIntent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                genericIntent.AddFlags(Android.Content.ActivityFlags.NewTask);

                try
                {
                    context.StartActivity(genericIntent);
                }
                catch (Android.Content.ActivityNotFoundException)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Lecteur externe requis",
                            "Aucun lecteur video trouve.\n\nInstallez VLC ou MX Player depuis le Play Store pour lire les series.",
                            "OK");
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeriesView] External player error: {ex}");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Erreur", $"Impossible d'ouvrir le lecteur externe: {ex.Message}", "OK");
                });
            }
#endif
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try
            {
                Player.Stop();
                Player.Source = null;
            }
            catch { }
        }
    }

    // Display model for series items
    public class SeriesDisplayItem
    {
        public string Name { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string Cover => ImageUrl;
        public string Rating { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Year { get; set; } = "";
        public string Genre { get; set; } = "";
        public bool HasRating => !string.IsNullOrEmpty(Rating) && Rating != "0" && Rating != "0.0";
        public bool HasYear => !string.IsNullOrEmpty(Year) && Year.Length >= 4;
        public int Id { get; set; }
    }
}
