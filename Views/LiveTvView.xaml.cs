using CommunityToolkit.Maui.Views;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class LiveTvView : ContentPage
    {
        private readonly XtreamService _xtreamService = new();
        private readonly StorageService _storageService = new();
        private List<XtreamCategory> _categories = new();
        private List<Channel> _allChannels = new();
        private string _currentStreamUrl = "";
        private bool _isMuted = false;
        private bool _useInternalPlayer = false;
        private CancellationTokenSource? _searchCts;

        public LiveTvView()
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
                    // Si le lecteur interne echoue, proposer le lecteur externe
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

                _categories = await _xtreamService.GetFrenchLiveCategoriesAsync(source);
                BuildCategoryButtons();
                StatusLabel.Text = $"{_categories.Count} categories";

                if (_categories.Count > 0)
                    await LoadChannelsAsync(_categories[0].CategoryId, _categories[0].CategoryName);
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
                    await LoadChannelsAsync(cat.CategoryId, cat.CategoryName);
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

        // ==================== CHANNELS ====================

        private async Task LoadChannelsAsync(string categoryId, string categoryName)
        {
            try
            {
                LoadingIndicator.IsRunning = true;
                ErrorLabel.IsVisible = false;
                UpdateCategoryHighlight(categoryId);

                var source = GetSource();
                if (source == null) return;

                _allChannels = await _xtreamService.GetLiveStreamsByCategoryAsync(source, categoryId);
                ChannelsList.ItemsSource = _allChannels.Take(300).ToList();
                StatusLabel.Text = $"{_allChannels.Count} chaines dans {CleanCategoryName(categoryName)}";
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
                            ChannelsList.ItemsSource = _allChannels.Take(300).ToList();
                        else
                            ChannelsList.ItemsSource = _allChannels
                                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                                .Take(300).ToList();
                    });
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        // ==================== PLAYBACK ====================

        private async void OnChannelSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Channel channel)
            {
                await PlayChannelAsync(channel);
                ChannelsList.SelectedItem = null;
            }
        }

        public async Task PlayChannelAsync(Channel channel)
        {
            try
            {
                _currentStreamUrl = channel.Url;
                NowPlayingLabel.Text = channel.Name;
                ErrorLabel.IsVisible = false;

                if (_useInternalPlayer)
                {
                    // Mode lecteur interne MediaElement
                    PlayerSection.IsVisible = true;
                    PlayerLoading.IsRunning = true;
                    Player.Stop();
                    Player.Source = CommunityToolkit.Maui.Views.MediaSource.FromUri(_currentStreamUrl);
                    BtnPlayPause.Text = "⏸";
                }
                else
                {
                    // Mode lecteur externe par defaut (plus fiable pour IPTV)
                    await OpenInExternalPlayerAsync(_currentStreamUrl);
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

        private void OnTogglePlayerClicked(object? sender, EventArgs e)
        {
            _useInternalPlayer = !_useInternalPlayer;
            if (sender is Button btn)
            {
                btn.Text = _useInternalPlayer ? "🔄 Externe" : "🔄 Interne";
            }
        }

        // ==================== EXTERNAL PLAYER ====================

        private async Task OpenInExternalPlayerAsync(string url)
        {
            try
            {
#if ANDROID
                // Essayer d'abord VLC
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
                            "Aucun lecteur video trouve.\n\nInstallez VLC ou MX Player depuis le Play Store pour lire les chaines.",
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

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
        }
    }
}
