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
    public class FilmDisplayItem
    {
        public string Name { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string StreamUrl { get; set; } = "";
        public string Rating { get; set; } = "";
        public string Plot { get; set; } = "";
        public string Year { get; set; } = "";
        public string Genre { get; set; } = "";
        public bool HasRating => !string.IsNullOrEmpty(Rating) && Rating != "0" && Rating != "0.0";
        public bool HasYear => !string.IsNullOrEmpty(Year) && Year.Length >= 4;
        public int Id { get; set; }
        public string ContainerExtension { get; set; } = "";
    }

    public partial class FilmsView : ContentPage
    {
        private readonly XtreamService _xtreamService;
        private readonly StorageService _storageService;
        private MediaSource? _source;
        private string _selectedCategoryId = "";
        private string _searchText = "";
        private CancellationTokenSource? _loadCts;

        private List<FilmDisplayItem> _allItems = new();
        private List<XtreamCategory> _filmCategories = new();
        private int _displayedCount = 0;
        private const int PageSize = 60;

        // Lecteur video integre
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;
        private bool _videoViewReady = false;
        private bool _isFullscreen = false;
        private string _currentFilmName = "";

#if ANDROID
        private LibVLCSharp.Platforms.Android.VideoView? _androidVideoView;
#endif

        public FilmsView()
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
                    try
                    {
                        PlayPauseBtn.Text = "||";
                        PlayerLoading.IsRunning = false;
                    }
                    catch { }
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
                System.Diagnostics.Debug.WriteLine($"[FilmsView] InitializePlayer error: {ex}");
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

                if (_source != null && _filmCategories.Count == 0)
                    _ = LoadCategoriesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FilmsView] OnAppearing error: {ex}");
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
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FilmsView] MediaPlayer assign error: {ex}"); }
                    });
                    await Task.Delay(300);
                }

                _videoViewReady = true;
                System.Diagnostics.Debug.WriteLine($"[FilmsView] VideoView ready: attached={attached}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FilmsView] AttachVideoView error: {ex}");
                _videoViewReady = true;
            }
#else
            _videoViewReady = true;
#endif
        }

        // ==================== CHARGEMENT CATEGORIES & FILMS ====================

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
                _filmCategories = await _xtreamService.GetFrenchVodCategoriesAsync(_source);
                UpdateCategoriesBar(_filmCategories);
                StatusLabel.Text = $"{_filmCategories.Count} categories de films";
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
                StatusLabel.Text = "Chargement des films...";
                var items = await _xtreamService.GetVodStreamsByCategoryAsync(_source, _selectedCategoryId, ct);
                foreach (var item in items)
                {
                    _allItems.Add(new FilmDisplayItem
                    {
                        Name = item.Name,
                        ImageUrl = item.StreamIcon,
                        StreamUrl = item.Url,
                        Rating = item.Rating,
                        Genre = item.Genre,
                        Year = ExtractYear(item.Added),
                        Id = item.StreamId,
                        ContainerExtension = item.ContainerExtension
                    });
                }
                StatusLabel.Text = $"{items.Count} films trouves";
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

        private string ExtractYear(string added)
        {
            if (string.IsNullOrEmpty(added)) return "";
            if (added.Length >= 4 && int.TryParse(added.Substring(0, 4), out int year) && year > 1900)
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

            ContentCollection.ItemsSource = new ObservableCollection<FilmDisplayItem>(
                filteredList.Take(_displayedCount));

            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;

            if (!filteredList.Any())
            {
                EmptyLabel.Text = string.IsNullOrWhiteSpace(_searchText)
                    ? "Aucun film dans cette categorie."
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

            ContentCollection.ItemsSource = new ObservableCollection<FilmDisplayItem>(
                filteredList.Take(_displayedCount));

            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;
        }

        private async void OnFilmTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var frame = sender as Frame;
                if (frame?.BindingContext is not FilmDisplayItem item) return;

                string details = "";
                if (!string.IsNullOrEmpty(item.Genre)) details += $"Genre: {item.Genre}\n";
                if (item.HasRating) details += $"Note: {item.Rating}/10\n";
                if (item.HasYear) details += $"Annee: {item.Year}\n";
                details += "\nLancer la lecture ?";

                bool play = await DisplayAlert(item.Name, details, "Lire", "Annuler");
                if (play && !string.IsNullOrEmpty(item.StreamUrl))
                {
                    await PlayFilmAsync(item.Name, item.StreamUrl);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Une erreur est survenue: {ex.Message}", "OK");
            }
        }

        // ==================== LECTEUR VIDEO INTEGRE ====================

        private async Task PlayFilmAsync(string name, string url)
        {
            if (_mediaPlayer == null || _libVLC == null)
            {
                await OpenInExternalPlayerAsync(url, name);
                return;
            }

            try
            {
                // Afficher le lecteur
                PlayerSection.IsVisible = true;
                NowPlayingLabel.Text = name;
                _currentFilmName = name;
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

                // Disposer l'ancien media
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
                            System.Diagnostics.Debug.WriteLine($"[FilmsView] Play started: {name}");
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
                System.Diagnostics.Debug.WriteLine($"[FilmsView] PlayFilm error: {ex}");
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                PlayerLoading.IsRunning = false;
                // Fallback: lecteur externe
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
                    PlayerSection.HeightRequest = -1; // Fill available space
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
            if (!string.IsNullOrEmpty(_currentFilmName))
            {
                string url = _allItems.FirstOrDefault(i => i.Name == _currentFilmName)?.StreamUrl ?? "";
                if (!string.IsNullOrEmpty(url))
                    await OpenInExternalPlayerAsync(url, _currentFilmName);
            }
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
                System.Diagnostics.Debug.WriteLine($"[FilmsView] External player error: {ex}");
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
            // Ne pas arreter la lecture quand on quitte l'onglet (l'utilisateur peut revenir)
        }
    }
}
