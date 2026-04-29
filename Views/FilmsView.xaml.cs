using CommunityToolkit.Maui.Views;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class FilmsView : ContentPage
    {
        private readonly XtreamService _xtreamService = new();
        private readonly StorageService _storageService = new();
        private List<XtreamCategory> _categories = new();
        private List<VodItem> _allFilms = new();
        private string _currentStreamUrl = "";
        private bool _isMuted = false;
        private bool _useInternalPlayer = false;
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
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    PlayerLoading.IsRunning = false;
                    if (!string.IsNullOrEmpty(_currentStreamUrl))
                    {
                        ErrorLabel.Text = "Lecteur interne indisponible. Ouverture du lecteur externe...";
                        ErrorLabel.IsVisible = true;
                        await Task.Delay(500);
                        await OpenInExternalPlayerAsync(_currentStreamUrl);
                    }
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

        private async void OnFilmTapped(object? sender, TappedEventArgs e)
        {
            if (sender is VisualElement ve && ve.BindingContext is VodItem film)
            {
                await PlayFilmAsync(film);
            }
        }

        private void OnFilmSelected(object? sender, SelectionChangedEventArgs e)
        {
            // Not used - using TapGestureRecognizer instead
        }

        public async Task PlayFilmAsync(VodItem film)
        {
            try
            {
                var source = GetSource();
                if (source == null) return;

                var ext = !string.IsNullOrEmpty(film.ContainerExtension) ? film.ContainerExtension : "mp4";
                var url = $"{source.Url}/movie/{source.Username}/{source.Password}/{film.StreamId}.{ext}";
                _currentStreamUrl = url;
                NowPlayingLabel.Text = film.Name;
                ErrorLabel.IsVisible = false;

                if (_useInternalPlayer)
                {
                    PlayerSection.IsVisible = true;
                    PlayerLoading.IsRunning = true;
                    Player.Stop();
                    Player.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(url);
                    BtnPlayPause.Text = "⏸";
                }
                else
                {
                    await OpenInExternalPlayerAsync(url);
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
                // Essayer VLC d'abord
                var vlcIntent = new Android.Content.Intent(Android.Content.Intent.ActionView);
                vlcIntent.SetPackage("org.videolan.vlc");
                vlcIntent.SetDataAndType(Android.Net.Uri.Parse(url), "video/*");
                vlcIntent.PutExtra("title", NowPlayingLabel.Text ?? "NovaStream");
                vlcIntent.AddFlags(Android.Content.ActivityFlags.NewTask);

                var context = Android.App.Application.Context;

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
                            "Aucun lecteur video trouve.\n\nInstallez VLC ou MX Player depuis le Play Store.",
                            "OK");
                    });
                }
#endif
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Erreur", $"Impossible d'ouvrir le lecteur externe: {ex.Message}", "OK");
                });
            }
        }

        // ==================== HELPERS ====================

        private NovaStreamMobile.Models.MediaSource? GetSource()
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
