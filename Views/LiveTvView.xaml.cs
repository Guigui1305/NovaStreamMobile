using CommunityToolkit.Maui.Views;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;
using NovaStreamMobile.ViewModels;

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

        private void OnChannelSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Channel channel)
            {
                PlayChannel(channel);
                ChannelsList.SelectedItem = null;
            }
        }

        public void PlayChannel(Channel channel)
        {
            try
            {
                _currentStreamUrl = channel.Url;
                NowPlayingLabel.Text = channel.Name;
                PlayerSection.IsVisible = true;
                PlayerLoading.IsRunning = true;
                ErrorLabel.IsVisible = false;

                Player.Stop();
                Player.Source = Microsoft.Maui.Controls.MediaSource.FromUri(_currentStreamUrl);
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
