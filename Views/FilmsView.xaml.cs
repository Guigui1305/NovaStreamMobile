using CommunityToolkit.Maui.Views;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class FilmsView : ContentPage
    {
        private readonly XtreamService _xtreamService = new();
        private readonly StorageService _storageService = new();
        private List<XtreamService.XtreamCategory> _categories = new();
        private List<XtreamService.VodItem> _allFilms = new();
        private string _currentStreamUrl = "";
        private bool _isMuted = false;
        private int _currentPage = 0;
        private const int PageSize = 60;
        private CancellationTokenSource? _searchCts;

        public FilmsView()
        {
            InitializeComponent();

            Player.MediaOpened += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PlayerLoading.IsRunning = false;
                    BtnPlayPause.Text = "⏸";
                });
            };

            Player.MediaFailed += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    PlayerLoading.IsRunning = false;
                    ErrorLabel.Text = "Erreur de lecture. Essayez le lecteur externe.";
                    ErrorLabel.IsVisible = true;
                });
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (_categories.Count == 0)
                await LoadCategoriesAsync();
        }

        // ==================== CATEGORIES ====================

        private async Task LoadCategoriesAsync()
        {
            try
            {
                LoadingIndicator.IsRunning = true;
                ErrorLabel.IsVisible = false;
                StatusLabel.Text = "Chargement...";

                var source = GetSource();
                if (source == null)
                {
                    ErrorLabel.Text = "Aucune source configuree. Allez dans Sources.";
                    ErrorLabel.IsVisible = true;
                    return;
                }

                _categories = await _xtreamService.GetFrenchVodCategoriesAsync(source);
                BuildCategoryButtons();
                StatusLabel.Text = $"{_categories.Count} categories";

                if (_categories.Count > 0)
                    await LoadFilmsAsync(_categories[0].CategoryId, _categories[0].CategoryName);
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
            }
            finally
            {
                LoadingIndicator.IsRunning = false;
            }
        }

        private void BuildCategoryButtons()
        {
            CategoriesBar.Children.Clear();
            foreach (var cat in _categories)
            {
                var btn = new Button
                {
                    Text = CleanCategoryName(cat.CategoryName),
                    BackgroundColor = Color.FromArgb("#252540"),
                    TextColor = Color.FromArgb("#A0A0B8"),
                    CornerRadius = 20,
                    HeightRequest = 36,
                    FontSize = 12,
                    Padding = new Thickness(16, 0),
                    FontAttributes = FontAttributes.Bold,
                    ClassId = cat.CategoryId
                };
                btn.Clicked += OnCategoryClicked;
                CategoriesBar.Children.Add(btn);
            }
        }

        private async void OnCategoryClicked(object? sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                var cat = _categories.FirstOrDefault(c => c.CategoryId == btn.ClassId);
                if (cat != null)
                    await LoadFilmsAsync(cat.CategoryId, cat.CategoryName);
            }
        }

        private void UpdateCategoryHighlight(string activeCatId)
        {
            foreach (var child in CategoriesBar.Children)
            {
                if (child is Button btn)
                {
                    bool isActive = btn.ClassId == activeCatId;
                    btn.BackgroundColor = isActive ? Color.FromArgb("#E50914") : Color.FromArgb("#252540");
                    btn.TextColor = isActive ? Colors.White : Color.FromArgb("#A0A0B8");
                }
            }
        }

        // ==================== FILMS ====================

        private async Task LoadFilmsAsync(string categoryId, string categoryName)
        {
            try
            {
                LoadingIndicator.IsRunning = true;
                ErrorLabel.IsVisible = false;
                UpdateCategoryHighlight(categoryId);
                _currentPage = 0;

                var source = GetSource();
                if (source == null) return;

                _allFilms = await _xtreamService.GetVodStreamsByCategoryAsync(source, categoryId);
                DisplayPage();
                StatusLabel.Text = $"{_allFilms.Count} films dans {CleanCategoryName(categoryName)}";
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Erreur: {ex.Message}";
                ErrorLabel.IsVisible = true;
            }
            finally
            {
                LoadingIndicator.IsRunning = false;
            }
        }

        private void DisplayPage()
        {
            var displayed = _allFilms.Take((_currentPage + 1) * PageSize).ToList();
            FilmsList.ItemsSource = displayed;
            LoadMoreButton.IsVisible = displayed.Count < _allFilms.Count;
        }

        private void OnLoadMoreClicked(object? sender, EventArgs e)
        {
            _currentPage++;
            DisplayPage();
        }

        // ==================== SEARCH ====================

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            Task.Delay(300, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var query = SearchEntry.Text?.Trim() ?? "";
                        if (string.IsNullOrEmpty(query))
                            DisplayPage();
                        else
                            FilmsList.ItemsSource = _allFilms
                                .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                                .Take(PageSize).ToList();
                    });
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        // ==================== PLAYBACK ====================

        private void OnFilmTapped(object? sender, TappedEventArgs e)
        {
            if (sender is VisualElement ve && ve.BindingContext is XtreamService.VodItem film)
            {
                PlayFilm(film);
            }
        }

        private void OnFilmSelected(object? sender, SelectionChangedEventArgs e)
        {
            // Not used - using TapGestureRecognizer instead
        }

        public void PlayFilm(XtreamService.VodItem film)
        {
            try
            {
                var source = GetSource();
                if (source == null) return;

                // Build VOD stream URL - try multiple formats
                var ext = !string.IsNullOrEmpty(film.ContainerExtension) ? film.ContainerExtension : "m3u8";
                var url = $"{source.Url}/movie/{source.Username}/{source.Password}/{film.StreamId}.{ext}";
                _currentStreamUrl = url;
                NowPlayingLabel.Text = film.Name;
                PlayerSection.IsVisible = true;
                PlayerLoading.IsRunning = true;
                ErrorLabel.IsVisible = false;

                Player.Stop();
                Player.Source = Microsoft.Maui.Controls.MediaSource.FromUri(url);
                BtnPlayPause.Text = "⏸";
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

        private async void OnExternalPlayerClicked(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentStreamUrl)) return;
            await OpenInExternalPlayerAsync(_currentStreamUrl);
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

        // ==================== EXTERNAL PLAYER ====================

        private async Task OpenInExternalPlayerAsync(string url)
        {
            try
            {
#if ANDROID
                var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                intent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);

                var context = Android.App.Application.Context;
                if (intent.ResolveActivity(context.PackageManager!) != null)
                {
                    context.StartActivity(intent);
                }
                else
                {
                    await DisplayAlert("Lecteur externe",
                        "Aucun lecteur video externe trouve. Installez VLC ou MX Player.",
                        "OK");
                }
#endif
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Impossible d'ouvrir le lecteur externe: {ex.Message}", "OK");
            }
        }

        // ==================== HELPERS ====================

        private MediaSource? GetSource()
        {
            try
            {
                var sources = _storageService.LoadSources();
                return sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
            }
            catch { return null; }
        }

        private static string CleanCategoryName(string name)
        {
            return name.Replace("|", "").Replace("  ", " ").Trim();
        }
    }
}
