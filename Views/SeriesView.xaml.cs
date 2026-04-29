using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using LibVLCSharp.Shared;
#if ANDROID
using Android.App;
using Android.Content;
using LibVLCSharp.Platforms.Android;
#endif

namespace NovaStreamMobile.Views
{
    public class SeriesDisplayItem
    {
        public string Name { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Year { get; set; } = "";
        public string Genre { get; set; } = "";
        public bool HasRating => !string.IsNullOrEmpty(Rating) && Rating != "0" && Rating != "0.0";
        public bool HasYear => !string.IsNullOrEmpty(Year) && Year.Length >= 4;
        public int Id { get; set; }
    }

    public partial class SeriesView : ContentPage
    {
        private readonly XtreamService _xtreamService;
        private readonly StorageService _storageService;
        private MediaSource? _source;
        private string _selectedCategoryId = "";
        private string _searchText = "";
        private CancellationTokenSource? _loadCts;

        private List<SeriesDisplayItem> _allItems = new();
        private List<XtreamCategory> _seriesCategories = new();
        private int _displayedCount = 0;
        private const int PageSize = 60;

        // Lecteur video integre
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;
        private bool _videoViewReady = false;
        private bool _isFullscreen = false;
        private string _currentEpisodeUrl = "";
        private string _currentEpisodeName = "";

#if ANDROID
        private LibVLCSharp.Platforms.Android.VideoView? _androidVideoView;
#endif

        public SeriesView()
        {
            InitializeComponent();
            _xtreamService = new XtreamService();
            _storageService = new StorageService();
            InitializePlayer();
            LoadSource();
        }

        private void InitializePlayer()
        {
            try
            {
                _libVLC = new LibVLC("--no-osd", "--network-caching=3000");
                _mediaPlayer = new MediaPlayer(_libVLC);
                _mediaPlayer.Playing += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { PlayPauseBtn.Text = "||"; PlayerLoading.IsRunning = false; } catch { }
                });
                _mediaPlayer.Paused += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { PlayPauseBtn.Text = ">"; } catch { }
                });
                _mediaPlayer.Stopped += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { PlayPauseBtn.Text = ">"; } catch { }
                });
                _mediaPlayer.EncounteredError += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        PlayerLoading.IsRunning = false;
                        ErrorLabel.Text = "Erreur de lecture. Essayez avec le lecteur externe.";
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeriesView] InitializePlayer error: {ex}");
            }
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
                if (!_videoViewReady)
                    _ = AttachVideoViewAsync();

                if (_source != null && _seriesCategories.Count == 0)
                    _ = LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeriesView] OnAppearing error: {ex}");
            }
        }

        private async Task AttachVideoViewAsync()
        {
            if (_mediaPlayer == null) { _videoViewReady = true; return; }

#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null) { _videoViewReady = true; return; }

                _androidVideoView = new LibVLCSharp.Platforms.Android.VideoView(activity);

                var placeholder = new Microsoft.Maui.Controls.ContentView();
                placeholder.Content = new BoxView { Color = Colors.Black };
                VideoContainer.Content = placeholder;

                await Task.Delay(300);

                bool attached = false;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        attached = await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try
                            {
                                var handler = VideoContainer.Handler;
                                if (handler?.PlatformView is Android.Views.ViewGroup container)
                                {
                                    container.RemoveAllViews();
                                    if (_androidVideoView.Parent is Android.Views.ViewGroup oldParent)
                                        oldParent.RemoveView(_androidVideoView);

                                    container.AddView(_androidVideoView, new Android.Views.ViewGroup.LayoutParams(
                                        Android.Views.ViewGroup.LayoutParams.MatchParent,
                                        Android.Views.ViewGroup.LayoutParams.MatchParent));
                                    return true;
                                }
                                return false;
                            }
                            catch { return false; }
                        });
                        if (attached) break;
                    }
                    catch { }
                    await Task.Delay(200 * (attempt + 1));
                }

                if (attached)
                {
                    await Task.Delay(500);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        try { _androidVideoView.MediaPlayer = _mediaPlayer; }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SeriesView] MediaPlayer assign error: {ex}"); }
                    });
                    await Task.Delay(300);
                }

                _videoViewReady = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeriesView] AttachVideoView error: {ex}");
                _videoViewReady = true;
            }
#else
            _videoViewReady = true;
#endif
        }

        // ==================== CHARGEMENT CATEGORIES & SERIES ====================

        private async Task LoadCategoriesAsync()
        {
            if (_source == null || _source.Type != SourceType.Xtream)
            {
                LoadingIndicator.IsRunning = false;
                EmptyLabel.Text = "Aucune source Xtream configuree.\nAjoutez-en une dans l'onglet Sources.";
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
                EmptyLabel.Text = "Selectionnez une categorie";
                ErrorLabel.Text = "";
            }
            catch (Exception ex)
            {
                LoadingIndicator.IsRunning = false;
                ErrorLabel.Text = $"Erreur: {ex.Message}";
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
            EmptyLabel.Text = "Chargement...";
            ErrorLabel.Text = "";
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
                EmptyLabel.Text = "Echec du chargement";
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
                    BackgroundColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#1F1F1F"),
                    TextColor = Colors.White,
                    FontSize = 11,
                    CornerRadius = 16,
                    HeightRequest = 32,
                    Padding = new Thickness(12, 0),
                    BorderColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#333333"),
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

            ContentCollection.ItemsSource = new ObservableCollection<SeriesDisplayItem>(
                filteredList.Take(_displayedCount));

            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;

            if (!filteredList.Any())
            {
                EmptyLabel.Text = string.IsNullOrWhiteSpace(_searchText)
                    ? "Aucune serie dans cette categorie."
                    : $"Aucun resultat pour '{_searchText}'.";
            }
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

            ContentCollection.ItemsSource = new ObservableCollection<SeriesDisplayItem>(
                filteredList.Take(_displayedCount));

            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;
        }

        private async void OnSeriesTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var frame = sender as Frame;
                if (frame?.BindingContext is not SeriesDisplayItem item) return;

                if (_source == null) return;

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
                    StatusLabel.Text = "";
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
                StatusLabel.Text = "";
            }
        }

        // ==================== LECTEUR VIDEO INTEGRE ====================

        private async Task PlayEpisodeAsync(string name, string url)
        {
            if (_mediaPlayer == null || _libVLC == null)
            {
                await OpenInExternalPlayerAsync(url, name);
                return;
            }

            try
            {
                PlayerSection.IsVisible = true;
                NowPlayingLabel.Text = name;
                _currentEpisodeName = name;
                _currentEpisodeUrl = url;
                PlayerLoading.IsRunning = true;
                ErrorLabel.Text = "";

                // Attendre que la VideoView soit prete
                for (int i = 0; i < 50; i++)
                {
                    if (_videoViewReady) break;
                    await Task.Delay(100);
                }
                await Task.Delay(500);

                // Arreter la lecture en cours
                try
                {
                    if (_mediaPlayer.IsPlaying)
                    {
                        _mediaPlayer.Stop();
                        await Task.Delay(300);
                    }
                }
                catch { }

                try { _currentMedia?.Dispose(); } catch { }
                _currentMedia = null;
                await Task.Delay(200);

                // Creer le nouveau media
                try
                {
                    Uri mediaUri;
                    try
                    {
                        mediaUri = new Uri(url);
                        _currentMedia = new Media(_libVLC, mediaUri);
                    }
                    catch (UriFormatException)
                    {
                        _currentMedia = new Media(_libVLC, url, FromType.FromLocation);
                    }

                    _currentMedia.AddOption(":network-caching=3000");
                    _currentMedia.AddOption(":clock-jitter=0");
                    _currentMedia.AddOption(":clock-synchro=0");
                }
                catch (Exception mediaEx)
                {
                    ErrorLabel.Text = $"Erreur creation media: {mediaEx.Message}";
                    PlayerLoading.IsRunning = false;
                    return;
                }

                // Lancer la lecture sur le MainThread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        if (_mediaPlayer != null && _currentMedia != null)
                        {
                            _mediaPlayer.Play(_currentMedia);
                            System.Diagnostics.Debug.WriteLine($"[SeriesView] Play started: {name}");
                        }
                    }
                    catch (Exception playEx)
                    {
                        ErrorLabel.Text = $"Erreur lecture: {playEx.Message}";
                        PlayerLoading.IsRunning = false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SeriesView] PlayEpisode error: {ex}");
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                PlayerLoading.IsRunning = false;
                await OpenInExternalPlayerAsync(url, name);
            }
        }

        private void OnPlayPauseClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_mediaPlayer == null) return;
                if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause();
                else _mediaPlayer.Play();
            }
            catch { }
        }

        private void OnVolumeChanged(object? sender, ValueChangedEventArgs e)
        {
            try
            {
                if (_mediaPlayer != null)
                    _mediaPlayer.Volume = (int)e.NewValue;
            }
            catch { }
        }

        private void OnClosePlayerClicked(object? sender, EventArgs e)
        {
            try
            {
                _mediaPlayer?.Stop();
                PlayerSection.IsVisible = false;
                NowPlayingLabel.Text = "";
            }
            catch { }
        }

        private void OnFullscreenClicked(object? sender, EventArgs e)
        {
            try
            {
                _isFullscreen = !_isFullscreen;
                if (_isFullscreen)
                {
                    Shell.SetTabBarIsVisible(this, false);
                    PlayerSection.HeightRequest = -1;
                }
                else
                {
                    Shell.SetTabBarIsVisible(this, true);
                    PlayerSection.HeightRequest = 220;
                }
            }
            catch { }
        }

        private async void OnExternalPlayerClicked(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentEpisodeUrl))
                await OpenInExternalPlayerAsync(_currentEpisodeUrl, _currentEpisodeName);
        }

        private async Task OpenInExternalPlayerAsync(string url, string title)
        {
#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null) return;

                var intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                intent.PutExtra("title", title);
                intent.AddFlags(ActivityFlags.NewTask);

                if (intent.ResolveActivity(activity.PackageManager!) != null)
                {
                    activity.StartActivity(intent);
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Lecteur externe",
                            "Aucun lecteur video externe trouve. Installez VLC ou MX Player depuis le Play Store.",
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
        }
    }
}
