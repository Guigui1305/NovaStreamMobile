using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        public bool HasRating => !string.IsNullOrEmpty(Rating) && Rating != "0" && Rating != "";
        public bool IsSeries { get; set; }
        public int Id { get; set; }
    }

    public partial class VodView : ContentPage
    {
        private readonly XtreamService _xtreamService;
        private readonly StorageService _storageService;
        private MediaSource? _source;
        private bool _isFilmsMode = true;
        private string _selectedCategoryId = "";
        private string _searchText = "";

        private List<VodDisplayItem> _allItems = new();
        private List<XtreamCategory> _filmCategories = new();
        private List<XtreamCategory> _seriesCategories = new();

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
            if (_source != null && _allItems.Count == 0)
            {
                _ = LoadContentAsync();
            }
        }

        private void LoadSource()
        {
            var sources = _storageService.LoadSources();
            _source = sources.FirstOrDefault(s => s.Type == SourceType.Xtream);
            if (_source == null)
                _source = sources.FirstOrDefault();
        }

        private async System.Threading.Tasks.Task LoadContentAsync()
        {
            if (_source == null || _source.Type != SourceType.Xtream)
            {
                LoadingIndicator.IsRunning = false;
                EmptyLabel.Text = "Aucune source Xtream configuree.\nAjoutez-en une dans l'onglet Sources.";
                return;
            }

            LoadingIndicator.IsRunning = true;
            EmptyLabel.Text = "Chargement...";
            _allItems.Clear();

            try
            {
                if (_isFilmsMode)
                {
                    if (_filmCategories.Count == 0)
                        _filmCategories = await _xtreamService.GetVodCategoriesAsync(_source);

                    UpdateCategoriesBar(_filmCategories);

                    var items = await _xtreamService.GetVodStreamsAsync(_source, _selectedCategoryId);
                    foreach (var item in items)
                    {
                        _allItems.Add(new VodDisplayItem
                        {
                            Name = item.Name,
                            ImageUrl = item.StreamIcon,
                            StreamUrl = item.Url,
                            Rating = item.Rating,
                            IsSeries = false,
                            Id = item.StreamId
                        });
                    }
                }
                else
                {
                    if (_seriesCategories.Count == 0)
                        _seriesCategories = await _xtreamService.GetSeriesCategoriesAsync(_source);

                    UpdateCategoriesBar(_seriesCategories);

                    var items = await _xtreamService.GetSeriesAsync(_source, _selectedCategoryId);
                    foreach (var item in items)
                    {
                        _allItems.Add(new VodDisplayItem
                        {
                            Name = item.Name,
                            ImageUrl = item.Cover,
                            Rating = item.Rating,
                            Plot = item.Plot,
                            IsSeries = true,
                            Id = item.SeriesId
                        });
                    }
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                EmptyLabel.Text = $"Erreur : {ex.Message}";
            }

            LoadingIndicator.IsRunning = false;
        }

        private void UpdateCategoriesBar(List<XtreamCategory> categories)
        {
            CategoriesBar.Children.Clear();

            var allBtn = new Button
            {
                Text = "Toutes",
                BackgroundColor = string.IsNullOrEmpty(_selectedCategoryId) ? Color.FromArgb("#E50914") : Color.FromArgb("#1F1F1F"),
                TextColor = Colors.White,
                FontSize = 12,
                CornerRadius = 15,
                HeightRequest = 32,
                Padding = new Thickness(12, 0)
            };
            allBtn.Clicked += (s, e) =>
            {
                _selectedCategoryId = "";
                _ = LoadContentAsync();
            };
            CategoriesBar.Children.Add(allBtn);

            // Show only FR categories first, limit to 30 for performance
            var frCategories = categories
                .Where(c => c.CategoryName.Contains("|FR|", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("FILM", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("SERIE", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("NETFLIX", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("DISNEY", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("AMAZON", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("CANAL", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("COMEDIE", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("ACTION", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("HORREUR", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("THRILLER", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("SCIENCE", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("2026", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("2025", StringComparison.OrdinalIgnoreCase) ||
                            c.CategoryName.Contains("4K", StringComparison.OrdinalIgnoreCase))
                .Take(30)
                .ToList();

            if (frCategories.Count == 0)
                frCategories = categories.Take(30).ToList();

            foreach (var cat in frCategories)
            {
                bool isSelected = cat.CategoryId == _selectedCategoryId;
                var btn = new Button
                {
                    Text = CleanCategoryName(cat.CategoryName),
                    BackgroundColor = isSelected ? Color.FromArgb("#E50914") : Color.FromArgb("#1F1F1F"),
                    TextColor = Colors.White,
                    FontSize = 11,
                    CornerRadius = 15,
                    HeightRequest = 32,
                    Padding = new Thickness(10, 0)
                };
                string catId = cat.CategoryId;
                btn.Clicked += (s, e) =>
                {
                    _selectedCategoryId = catId;
                    _ = LoadContentAsync();
                };
                CategoriesBar.Children.Add(btn);
            }
        }

        private string CleanCategoryName(string name)
        {
            // Remove |FR| prefix and special chars
            name = name.Replace("|FR|", "").Replace("|MULTI|", "").Replace("|CH|", "").Trim();
            if (name.StartsWith("*")) name = name.TrimStart('*').Trim();
            return name;
        }

        private void ApplyFilter()
        {
            var filtered = _allItems.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchText))
                filtered = filtered.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

            ContentCollection.ItemsSource = new ObservableCollection<VodDisplayItem>(filtered.Take(100));

            if (!filtered.Any())
            {
                EmptyLabel.Text = string.IsNullOrWhiteSpace(_searchText)
                    ? "Aucun contenu dans cette categorie."
                    : $"Aucun resultat pour '{_searchText}'.";
            }
        }

        private void OnFilmsTabClicked(object? sender, EventArgs e)
        {
            _isFilmsMode = true;
            _selectedCategoryId = "";
            _allItems.Clear();
            FilmsTab.TextColor = Color.FromArgb("#E50914");
            FilmsTab.FontAttributes = FontAttributes.Bold;
            SeriesTab.TextColor = Color.FromArgb("#808080");
            SeriesTab.FontAttributes = FontAttributes.None;
            _ = LoadContentAsync();
        }

        private void OnSeriesTabClicked(object? sender, EventArgs e)
        {
            _isFilmsMode = false;
            _selectedCategoryId = "";
            _allItems.Clear();
            SeriesTab.TextColor = Color.FromArgb("#E50914");
            SeriesTab.FontAttributes = FontAttributes.Bold;
            FilmsTab.TextColor = Color.FromArgb("#808080");
            FilmsTab.FontAttributes = FontAttributes.None;
            _ = LoadContentAsync();
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _searchText = e.NewTextValue ?? "";
            ApplyFilter();
        }

        private async void OnItemTapped(object? sender, TappedEventArgs e)
        {
            var frame = sender as Frame;
            if (frame?.BindingContext is not VodDisplayItem item) return;

            if (item.IsSeries)
            {
                await DisplayAlert(item.Name, item.Plot ?? "Aucune description disponible.", "OK");
            }
            else
            {
                bool play = await DisplayAlert(item.Name, "Lancer la lecture ?", "Lire", "Annuler");
                if (play && !string.IsNullOrEmpty(item.StreamUrl))
                {
                    // Navigate to TV Direct tab and play
                    try
                    {
                        if (Shell.Current?.Items.Count > 0)
                        {
                            var tabBar = Shell.Current.Items[0];
                            if (tabBar.Items.Count > 1)
                            {
                                Shell.Current.CurrentItem = tabBar.Items[1]; // TV Direct tab
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
