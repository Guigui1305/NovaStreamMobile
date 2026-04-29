using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public class VodDisplayItem
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
        public bool IsSeries { get; set; }
        public int Id { get; set; }
        public string ContainerExtension { get; set; } = "";
    }

    public partial class VodView : ContentPage
    {
        private readonly XtreamService _xtreamService;
        private readonly StorageService _storageService;
        private MediaSource? _source;
        private bool _isFilmsMode = true;
        private string _selectedCategoryId = "";
        private string _searchText = "";
        private CancellationTokenSource? _loadCts;

        private List<VodDisplayItem> _allItems = new();
        private List<XtreamCategory> _filmCategories = new();
        private List<XtreamCategory> _seriesCategories = new();
        private int _displayedCount = 0;
        private const int PageSize = 60;

        public VodView()
        {
            InitializeComponent();
            _xtreamService = new XtreamService();
            _storageService = new StorageService();
            LoadSource();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_source != null && _filmCategories.Count == 0 && _seriesCategories.Count == 0)
                _ = LoadCategoriesAsync();
        }

        private void LoadSource()
        {
            var sources = _storageService.LoadSources();
            _source = sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
            if (_source == null)
                _source = sources.FirstOrDefault();
        }

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
                if (_isFilmsMode)
                {
                    _filmCategories = await _xtreamService.GetFrenchVodCategoriesAsync(_source);
                    UpdateCategoriesBar(_filmCategories);
                    StatusLabel.Text = $"{_filmCategories.Count} categories de films";
                }
                else
                {
                    _seriesCategories = await _xtreamService.GetFrenchSeriesCategoriesAsync(_source);
                    UpdateCategoriesBar(_seriesCategories);
                    StatusLabel.Text = $"{_seriesCategories.Count} categories de series";
                }

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
                if (_isFilmsMode)
                {
                    StatusLabel.Text = "Chargement des films...";
                    var items = await _xtreamService.GetVodStreamsByCategoryAsync(_source, _selectedCategoryId, ct);
                    foreach (var item in items)
                    {
                        _allItems.Add(new VodDisplayItem
                        {
                            Name = item.Name,
                            ImageUrl = item.StreamIcon,
                            StreamUrl = item.Url,
                            Rating = item.Rating,
                            Genre = item.Genre,
                            Year = ExtractYear(item.Added),
                            IsSeries = false,
                            Id = item.StreamId,
                            ContainerExtension = item.ContainerExtension
                        });
                    }
                    StatusLabel.Text = $"{items.Count} films trouves";
                }
                else
                {
                    StatusLabel.Text = "Chargement des series...";
                    var items = await _xtreamService.GetSeriesByCategoryAsync(_source, _selectedCategoryId, ct);
                    foreach (var item in items)
                    {
                        _allItems.Add(new VodDisplayItem
                        {
                            Name = item.Name,
                            ImageUrl = item.Cover,
                            Rating = item.Rating,
                            Plot = item.Plot,
                            Genre = item.Genre,
                            Year = item.ReleaseDate,
                            IsSeries = true,
                            Id = item.SeriesId
                        });
                    }
                    StatusLabel.Text = $"{items.Count} series trouvees";
                }

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

            ContentCollection.ItemsSource = new ObservableCollection<VodDisplayItem>(
                filteredList.Take(_displayedCount));

            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;

            if (!filteredList.Any())
            {
                EmptyLabel.Text = string.IsNullOrWhiteSpace(_searchText)
                    ? "Aucun contenu dans cette categorie."
                    : $"Aucun resultat pour '{_searchText}'.";
            }
        }

        private void OnLoadMoreClicked(object? sender, EventArgs e)
        {
            var filtered = _allItems.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchText))
                filtered = filtered.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

            var filteredList = filtered.ToList();
            _displayedCount = Math.Min(_displayedCount + PageSize, filteredList.Count);

            ContentCollection.ItemsSource = new ObservableCollection<VodDisplayItem>(
                filteredList.Take(_displayedCount));

            LoadMoreButton.IsVisible = filteredList.Count > _displayedCount;
        }

        private void OnFilmsTabClicked(object? sender, EventArgs e)
        {
            if (_isFilmsMode) return;
            _isFilmsMode = true;
            _selectedCategoryId = "";
            _allItems.Clear();
            ContentCollection.ItemsSource = null;
            FilmsTab.TextColor = Color.FromArgb("#E50914");
            FilmsTab.FontAttributes = FontAttributes.Bold;
            SeriesTab.TextColor = Color.FromArgb("#808080");
            SeriesTab.FontAttributes = FontAttributes.None;

            if (_filmCategories.Count > 0)
                UpdateCategoriesBar(_filmCategories);
            else
                _ = LoadCategoriesAsync();
        }

        private void OnSeriesTabClicked(object? sender, EventArgs e)
        {
            if (!_isFilmsMode) return;
            _isFilmsMode = false;
            _selectedCategoryId = "";
            _allItems.Clear();
            ContentCollection.ItemsSource = null;
            SeriesTab.TextColor = Color.FromArgb("#E50914");
            SeriesTab.FontAttributes = FontAttributes.Bold;
            FilmsTab.TextColor = Color.FromArgb("#808080");
            FilmsTab.FontAttributes = FontAttributes.None;

            if (_seriesCategories.Count > 0)
                UpdateCategoriesBar(_seriesCategories);
            else
                _ = LoadCategoriesAsync();
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchText = e.NewTextValue ?? "";
            if (_allItems.Count > 0)
                ApplyFilter();
        }

        private async void OnItemTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var frame = sender as Frame;
                if (frame?.BindingContext is not VodDisplayItem item) return;

                if (item.IsSeries)
                    await ShowSeriesDetailAsync(item);
                else
                    await ShowMovieDetailAsync(item);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Une erreur est survenue: {ex.Message}", "OK");
            }
        }

        private async Task ShowMovieDetailAsync(VodDisplayItem item)
        {
            string details = "";
            if (!string.IsNullOrEmpty(item.Genre)) details += $"Genre: {item.Genre}\n";
            if (item.HasRating) details += $"Note: {item.Rating}/10\n";
            if (item.HasYear) details += $"Annee: {item.Year}\n";
            details += "\nLancer la lecture ?";

            bool play = await DisplayAlert(item.Name, details, "Lire", "Annuler");
            if (play && !string.IsNullOrEmpty(item.StreamUrl))
            {
                await PlayOnLiveTvAsync(item.Name, item.StreamUrl, item.ImageUrl);
            }
        }

        private async Task ShowSeriesDetailAsync(VodDisplayItem item)
        {
            if (_source == null) return;

            try
            {
                StatusLabel.Text = "Chargement des details...";
                var info = await _xtreamService.GetSeriesInfoAsync(_source, item.Id);

                if (info.Seasons.Count == 0)
                {
                    string desc = !string.IsNullOrEmpty(info.Plot) ? info.Plot : item.Plot ?? "Aucune description.";
                    await DisplayAlert(item.Name, desc, "OK");
                    StatusLabel.Text = "";
                    return;
                }

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
                        await PlayOnLiveTvAsync(epName, episode.Url, item.ImageUrl);
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

        /// <summary>
        /// Navigation securisee vers l'onglet TV Direct pour lancer la lecture.
        /// Utilise PlayChannelSafeAsync pour attendre que la VideoView soit prete.
        /// </summary>
        private async Task PlayOnLiveTvAsync(string name, string url, string imageUrl)
        {
            try
            {
                // Valider l'URL avant de naviguer
                if (string.IsNullOrWhiteSpace(url))
                {
                    await DisplayAlert("Erreur", "URL de lecture invalide.", "OK");
                    return;
                }

                var channel = new Channel
                {
                    Name = name,
                    Url = url,
                    LogoUrl = imageUrl ?? ""
                };

                // Naviguer vers l'onglet TV Direct
                if (Shell.Current?.Items.Count > 0)
                {
                    var tabBar = Shell.Current.Items[0];
                    if (tabBar.Items.Count > 1)
                    {
                        Shell.Current.CurrentItem = tabBar.Items[1];

                        // Attendre que la navigation soit complete et la page soit prete
                        LiveTvView? liveTv = null;
                        for (int i = 0; i < 30; i++) // Max 3 secondes d'attente
                        {
                            await Task.Delay(100);
                            try
                            {
                                liveTv = Shell.Current.CurrentPage as LiveTvView;
                                if (liveTv != null) break;
                            }
                            catch { }
                        }

                        if (liveTv != null)
                        {
                            // Utiliser PlayChannelSafeAsync qui attend la VideoView
                            await liveTv.PlayChannelSafeAsync(channel);
                            return;
                        }

                        // Fallback
                        await DisplayAlert("Info", "Navigation vers le lecteur en cours. Selectionnez l'onglet TV Direct.", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", $"Impossible de lancer la lecture: {ex.Message}", "OK");
            }
        }
    }
}
