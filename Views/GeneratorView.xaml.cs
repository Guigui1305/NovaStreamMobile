using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NovaStreamMobile.Models;
using NovaStreamMobile.Services;

namespace NovaStreamMobile.Views
{
    public partial class GeneratorView : ContentPage
    {
        private readonly PromptInterpreterService _interpreter = new();
        private readonly VideoGeneratorService _generator = new();
        private readonly VideoLibraryService _library = new();

        private CancellationTokenSource? _cts;
        private bool _isGenerating;
        private int _shortSide = 540;
        private int _fps = 24;
        private string? _lastGeneratedPath;

        public GeneratorView()
        {
            InitializeComponent();
            BuildSuggestions();
            UpdateDetection();
            FolderLabel.Text = $"Dossier : {_library.Folder}";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            RefreshLibrary();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try { Preview.Stop(); } catch { }
        }

        // ==================== SUGGESTIONS ====================

        private void BuildSuggestions()
        {
            SuggestionsLayout.Children.Clear();

            foreach (string example in PromptInterpreterService.Examples)
            {
                string label = example.Length > 28 ? example.Substring(0, 28) + "..." : example;
                var button = new Button
                {
                    Text = label,
                    CommandParameter = example
                };

                ApplyChipStyle(button, false);

                button.Clicked += OnSuggestionClicked;
                SuggestionsLayout.Children.Add(button);
            }
        }

        private void OnSuggestionClicked(object? sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is string example)
            {
                PromptEditor.Text = example;
                UpdateDetection();
            }
        }

        // ==================== REGLAGES ====================

        private void OnPromptChanged(object sender, TextChangedEventArgs e) => UpdateDetection();

        private void OnDurationChanged(object sender, ValueChangedEventArgs e)
        {
            DurationLabel.Text = $"{Math.Round(e.NewValue)} s";
            UpdateDetection();
        }

        private void OnQuality540Clicked(object sender, EventArgs e) => SetQuality(540);

        private void OnQuality720Clicked(object sender, EventArgs e) => SetQuality(720);

        private void SetQuality(int shortSide)
        {
            _shortSide = shortSide;
            ApplyChipStyle(Quality540Button, shortSide == 540);
            ApplyChipStyle(Quality720Button, shortSide == 720);
            UpdateDetection();
        }

        private void OnFps24Clicked(object sender, EventArgs e) => SetFps(24);

        private void OnFps30Clicked(object sender, EventArgs e) => SetFps(30);

        private void SetFps(int fps)
        {
            _fps = fps;
            ApplyChipStyle(Fps24Button, fps == 24);
            ApplyChipStyle(Fps30Button, fps == 30);
            UpdateDetection();
        }

        private static void ApplyChipStyle(Button button, bool active)
        {
            string key = active ? "CategoryChipActive" : "CategoryChip";
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style style)
                button.Style = style;
        }

        // ==================== ANALYSE DU PROMPT ====================

        private VideoPromptSpec BuildSpec()
        {
            var spec = _interpreter.Interpret(PromptEditor.Text ?? string.Empty);

            if (!spec.HasExplicitDuration)
                spec.DurationSeconds = Math.Round(DurationSlider.Value);

            spec.DurationSeconds = Math.Clamp(spec.DurationSeconds, 2, 60);
            spec.ShortSide = _shortSide;
            spec.Fps = _fps;
            return spec;
        }

        private void UpdateDetection()
        {
            try
            {
                var spec = BuildSpec();
                string text = string.IsNullOrWhiteSpace(spec.OverlayText)
                    ? string.Empty
                    : $" • texte \"{spec.OverlayText}\"";

                DetectionLabel.Text = $"{spec.Describe()} • {spec.OrientationLabel}{text}";
            }
            catch
            {
                DetectionLabel.Text = string.Empty;
            }
        }

        // ==================== GENERATION ====================

        private async void OnGenerateClicked(object sender, EventArgs e)
        {
            if (_isGenerating) return;

            string prompt = (PromptEditor.Text ?? string.Empty).Trim();
            if (prompt.Length < 3)
            {
                ShowError("Ecris d abord un prompt (au moins quelques mots).");
                return;
            }

            var spec = BuildSpec();
            string outputPath = _library.BuildOutputPath(spec);

            _isGenerating = true;
            _cts = new CancellationTokenSource();

            ErrorLabel.IsVisible = false;
            ResultCard.IsVisible = false;
            ProgressCard.IsVisible = true;
            GenerationProgress.Progress = 0;
            ProgressLabel.Text = "Preparation...";
            GenerateButton.IsEnabled = false;
            GenerateButton.Text = "Generation en cours...";

            try { Preview.Stop(); } catch { }

            int totalFrames = spec.TotalFrames;
            var progress = new Progress<double>(value =>
            {
                GenerationProgress.Progress = Math.Clamp(value, 0, 1);
                int done = (int)Math.Round(value * totalFrames);
                ProgressLabel.Text = $"Encodage {(int)(value * 100)}% — image {done}/{totalFrames}";
            });

            try
            {
                var result = await _generator.GenerateAsync(spec, outputPath, progress, _cts.Token);

                if (result.Cancelled)
                {
                    ShowError("Generation annulee.");
                }
                else if (result.Success)
                {
                    _lastGeneratedPath = result.FilePath;
                    _library.Add(spec, result.FilePath);
                    RefreshLibrary();
                    ShowResult(spec, result);
                }
                else
                {
                    ShowError($"Echec de la generation : {result.Error}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Erreur : {ex.Message}");
            }
            finally
            {
                _isGenerating = false;
                _cts?.Dispose();
                _cts = null;
                ProgressCard.IsVisible = false;
                GenerateButton.IsEnabled = true;
                GenerateButton.Text = "Generer la video";
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            ProgressLabel.Text = "Annulation...";
        }

        private void ShowResult(VideoPromptSpec spec, VideoGenerationResult result)
        {
            long size = 0;
            try { size = new FileInfo(result.FilePath).Length; } catch { }

            ResultInfoLabel.Text =
                $"{spec.Describe()} • {size / 1024.0 / 1024.0:0.0} Mo • rendu en {result.Elapsed.TotalSeconds:0.#}s";

            Preview.Source = CommunityToolkit.Maui.Views.MediaSource.FromFile(result.FilePath);
            ResultCard.IsVisible = true;
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorLabel.IsVisible = true;
        }

        // ==================== PARTAGE ====================

        private async void OnShareClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastGeneratedPath) || !File.Exists(_lastGeneratedPath))
            {
                ShowError("Aucune video a partager.");
                return;
            }

            await ShareFileAsync(_lastGeneratedPath);
        }

        private async Task ShareFileAsync(string path)
        {
            try
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Partager la video",
                    File = new ShareFile(path)
                });
            }
            catch (Exception ex)
            {
                ShowError($"Partage impossible : {ex.Message}");
            }
        }

        // ==================== BIBLIOTHEQUE ====================

        private void RefreshLibrary()
        {
            try
            {
                List<GeneratedVideo> videos = _library.Load();
                BindableLayout.SetItemsSource(LibraryLayout, videos);
                EmptyLibraryLabel.IsVisible = videos.Count == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Generator] RefreshLibrary: {ex.Message}");
            }
        }

        private void OnLibraryPlayClicked(object sender, EventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not GeneratedVideo video)
                return;

            if (!File.Exists(video.FilePath))
            {
                ShowError("Fichier introuvable, il a peut-etre ete supprime.");
                RefreshLibrary();
                return;
            }

            _lastGeneratedPath = video.FilePath;
            ResultInfoLabel.Text = $"{video.SceneName} • {video.Summary}";
            Preview.Source = CommunityToolkit.Maui.Views.MediaSource.FromFile(video.FilePath);
            ResultCard.IsVisible = true;
            ErrorLabel.IsVisible = false;
        }

        private async void OnLibraryDeleteClicked(object sender, EventArgs e)
        {
            if (sender is not Button button || button.CommandParameter is not GeneratedVideo video)
                return;

            bool confirmed = await DisplayAlert("Supprimer",
                $"Supprimer definitivement {video.FileName} ?", "Supprimer", "Annuler");
            if (!confirmed) return;

            if (_lastGeneratedPath == video.FilePath)
            {
                try { Preview.Stop(); } catch { }
                ResultCard.IsVisible = false;
                _lastGeneratedPath = null;
            }

            _library.Delete(video);
            RefreshLibrary();
        }
    }
}
