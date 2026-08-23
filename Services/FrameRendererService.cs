using System;
using NovaStreamMobile.Models;

#if ANDROID
using ABitmap = Android.Graphics.Bitmap;
using ACanvas = Android.Graphics.Canvas;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using APath = Android.Graphics.Path;
#endif

namespace NovaStreamMobile.Services
{
    /// <summary>
    /// Dessine, image par image, l'animation decrite par le prompt.
    /// Tout est calcule sur l'appareil avec le Canvas natif Android.
    /// </summary>
    public sealed class FrameRendererService : IDisposable
    {
        private readonly VideoPromptSpec _spec;
        private readonly int _width;
        private readonly int _height;
        private readonly int[] _pixels;

#if ANDROID
        private readonly ABitmap _bitmap;
        private readonly ACanvas _canvas;
        private readonly APaint _paint;
        private readonly APaint _textPaint;
#endif

        public FrameRendererService(VideoPromptSpec spec)
        {
            _spec = spec;
            _width = spec.Width;
            _height = spec.Height;
            _pixels = new int[_width * _height];

#if ANDROID
            _bitmap = ABitmap.CreateBitmap(_width, _height, ABitmap.Config.Argb8888!)!;
            _canvas = new ACanvas(_bitmap);
            _paint = new APaint(Android.Graphics.PaintFlags.AntiAlias);
            _textPaint = new APaint(Android.Graphics.PaintFlags.AntiAlias)
            {
                TextAlign = APaint.Align.Center
            };
            _textPaint.SetTypeface(Android.Graphics.Typeface.Create("sans-serif-medium",
                Android.Graphics.TypefaceStyle.Bold));
#endif
        }

        public int Width => _width;
        public int Height => _height;

        /// <summary>Rend l'image <paramref name="frameIndex"/> et renvoie ses pixels ARGB.</summary>
        public int[] RenderFrame(int frameIndex, int totalFrames)
        {
#if ANDROID
            float progress = totalFrames <= 1 ? 0f : (float)frameIndex / (totalFrames - 1);
            float seconds = (float)(frameIndex / (double)_spec.Fps);
            float t = (float)(seconds * _spec.Speed);

            DrawBackground(progress);

            switch (_spec.Scene)
            {
                case VideoScene.Waves: DrawWaves(t); break;
                case VideoScene.Particles: DrawParticles(t); break;
                case VideoScene.NeonGrid: DrawNeonGrid(t); break;
                case VideoScene.Pulse: DrawPulse(t); break;
                default: DrawAurora(t); break;
            }

            DrawVignette();
            DrawOverlayText(seconds);
            DrawFades(seconds);

            _bitmap.GetPixels(_pixels, 0, _width, 0, 0, _width, _height);
#endif
            return _pixels;
        }

#if ANDROID

        // ==================== Fond ====================

        private void DrawBackground(float progress)
        {
            int[] colors = Palette;
            // Le degrade de fond derive lentement pendant la video.
            int top = Blend(colors[0], colors[1], 0.25f + 0.35f * Wave(progress));
            int bottom = Blend(colors[1], colors[0], 0.15f + 0.30f * Wave(1f - progress));

            _paint.Reset();
            _paint.AntiAlias = true;
            _paint.SetShader(new Android.Graphics.LinearGradient(
                0, 0, 0, _height,
                top, bottom,
                Android.Graphics.Shader.TileMode.Clamp));
            _canvas.DrawRect(0, 0, _width, _height, _paint);
            _paint.SetShader(null);
        }

        // ==================== Scenes ====================

        private void DrawAurora(float t)
        {
            int[] colors = Palette;
            int blobs = 5;
            float baseRadius = Math.Min(_width, _height) * 0.55f;

            for (int i = 0; i < blobs; i++)
            {
                float phase = Rand(i, 11) * 6.283f;
                float speedX = 0.11f + Rand(i, 23) * 0.16f;
                float speedY = 0.09f + Rand(i, 37) * 0.14f;

                float cx = _width * (0.5f + 0.42f * (float)Math.Sin(t * speedX + phase));
                float cy = _height * (0.5f + 0.40f * (float)Math.Cos(t * speedY + phase * 1.7f));
                float radius = baseRadius * (0.55f + 0.30f * (float)Math.Sin(t * 0.35f + i));

                int color = colors[2 + (i % Math.Max(1, colors.Length - 2))];
                int center = WithAlpha(color, 150);
                int edge = WithAlpha(color, 0);

                _paint.Reset();
                _paint.AntiAlias = true;
                _paint.SetShader(new Android.Graphics.RadialGradient(
                    cx, cy, Math.Max(1f, radius),
                    center, edge,
                    Android.Graphics.Shader.TileMode.Clamp));
                _canvas.DrawCircle(cx, cy, radius, _paint);
                _paint.SetShader(null);
            }
        }

        private void DrawWaves(float t)
        {
            int[] colors = Palette;
            int layers = 5;

            for (int i = 0; i < layers; i++)
            {
                float depth = i / (float)(layers - 1);
                float baseY = _height * (0.42f + 0.13f * i);
                float amplitude = _height * (0.055f - 0.006f * i) * (1f + 0.25f * (float)Math.Sin(t * 0.6f + i));
                float frequency = (float)(1.6 + i * 0.7);
                float phase = t * (0.9f + i * 0.35f) + Rand(i, 71) * 6.283f;

                var path = new APath();
                path.MoveTo(0, _height);

                int steps = Math.Max(24, _width / 6);
                for (int s = 0; s <= steps; s++)
                {
                    float x = _width * s / (float)steps;
                    float u = s / (float)steps;
                    float y = baseY
                              + amplitude * (float)Math.Sin(u * frequency * 6.283f + phase)
                              + amplitude * 0.4f * (float)Math.Sin(u * frequency * 2.7f - phase * 1.3f);
                    path.LineTo(x, y);
                }

                path.LineTo(_width, _height);
                path.Close();

                int color = colors[Math.Min(colors.Length - 1, 2 + (i % 3))];
                _paint.Reset();
                _paint.AntiAlias = true;
                _paint.Color = ToColor(WithAlpha(color, (int)(70 + 120 * depth)));
                _canvas.DrawPath(path, _paint);
                path.Dispose();
            }
        }

        private void DrawParticles(float t)
        {
            int[] colors = Palette;
            int count = 110;
            float maxRadius = Math.Min(_width, _height) * 0.012f;

            _paint.Reset();
            _paint.AntiAlias = true;

            for (int i = 0; i < count; i++)
            {
                float speed = 0.02f + Rand(i, 3) * 0.09f;
                float x = _width * Frac(Rand(i, 5) + 0.04f * (float)Math.Sin(t * 0.5f + i));
                float y = _height * Frac(Rand(i, 7) + t * speed);
                float size = maxRadius * (0.35f + Rand(i, 13) * 1.2f);

                float twinkle = 0.45f + 0.55f * (float)Math.Abs(Math.Sin(t * (0.8f + Rand(i, 17)) + i));
                int color = colors[2 + (i % Math.Max(1, colors.Length - 2))];

                // Halo
                _paint.Color = ToColor(WithAlpha(color, (int)(45 * twinkle)));
                _canvas.DrawCircle(x, y, size * 3.2f, _paint);

                // Coeur
                _paint.Color = ToColor(WithAlpha(color, (int)(235 * twinkle)));
                _canvas.DrawCircle(x, y, size, _paint);
            }
        }

        private void DrawNeonGrid(float t)
        {
            int[] colors = Palette;
            float horizon = _height * 0.52f;
            int sun = colors[colors.Length - 2];
            int grid = colors[colors.Length - 1];

            // Soleil
            float sunRadius = Math.Min(_width, _height) * 0.22f;
            _paint.Reset();
            _paint.AntiAlias = true;
            _paint.SetShader(new Android.Graphics.RadialGradient(
                _width * 0.5f, horizon - sunRadius * 0.35f, sunRadius * 1.9f,
                WithAlpha(sun, 210), WithAlpha(sun, 0),
                Android.Graphics.Shader.TileMode.Clamp));
            _canvas.DrawCircle(_width * 0.5f, horizon - sunRadius * 0.35f, sunRadius * 1.9f, _paint);
            _paint.SetShader(null);

            _paint.Color = ToColor(WithAlpha(sun, 235));
            _canvas.DrawCircle(_width * 0.5f, horizon - sunRadius * 0.35f, sunRadius, _paint);

            // Bandes sombres sur le soleil (effet retro)
            _paint.Color = ToColor(WithAlpha(Palette[0], 220));
            for (int i = 0; i < 6; i++)
            {
                float y = horizon - sunRadius * 0.35f + sunRadius * (i / 6f) * 0.95f;
                float thickness = sunRadius * (0.035f + i * 0.012f);
                _canvas.DrawRect(_width * 0.5f - sunRadius, y, _width * 0.5f + sunRadius, y + thickness, _paint);
            }

            // Sol : grille en perspective
            _paint.Reset();
            _paint.AntiAlias = true;
            _paint.StrokeWidth = Math.Max(1.5f, _width * 0.0035f);
            _paint.SetStyle(APaint.Style.Stroke);
            _paint.Color = ToColor(WithAlpha(grid, 190));

            float vanishX = _width * 0.5f;
            for (int i = -12; i <= 12; i++)
            {
                float xBottom = vanishX + i * (_width * 0.16f);
                _canvas.DrawLine(vanishX, horizon, xBottom, _height, _paint);
            }

            float scroll = Frac(t * 0.35f);
            for (int i = 0; i < 16; i++)
            {
                float u = (i + scroll) / 16f;
                float y = horizon + (_height - horizon) * (u * u);
                if (y <= horizon || y > _height) continue;
                _paint.Color = ToColor(WithAlpha(grid, (int)(60 + 165 * u)));
                _canvas.DrawLine(0, y, _width, y, _paint);
            }

            _paint.SetStyle(APaint.Style.Fill);
        }

        private void DrawPulse(float t)
        {
            int[] colors = Palette;
            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float maxRadius = (float)Math.Sqrt(_width * _width + _height * _height) * 0.55f;
            int rings = 7;

            _paint.Reset();
            _paint.AntiAlias = true;
            _paint.SetStyle(APaint.Style.Stroke);

            for (int i = 0; i < rings; i++)
            {
                float u = Frac(t * 0.32f + i / (float)rings);
                float radius = maxRadius * u;
                int alpha = (int)(210 * (1f - u));
                if (alpha <= 2) continue;

                int color = colors[2 + (i % Math.Max(1, colors.Length - 2))];
                _paint.StrokeWidth = Math.Max(2f, _width * 0.012f * (1f - u) + 1.5f);
                _paint.Color = ToColor(WithAlpha(color, alpha));
                _canvas.DrawCircle(cx, cy, radius, _paint);
            }

            _paint.SetStyle(APaint.Style.Fill);

            // Coeur lumineux qui bat
            float beat = 0.5f + 0.5f * (float)Math.Sin(t * 3.2f);
            float coreRadius = Math.Min(_width, _height) * (0.07f + 0.035f * beat);
            int coreColor = colors[colors.Length - 1];

            _paint.SetShader(new Android.Graphics.RadialGradient(
                cx, cy, coreRadius * 3.4f,
                WithAlpha(coreColor, (int)(150 + 80 * beat)), WithAlpha(coreColor, 0),
                Android.Graphics.Shader.TileMode.Clamp));
            _canvas.DrawCircle(cx, cy, coreRadius * 3.4f, _paint);
            _paint.SetShader(null);

            _paint.Color = ToColor(WithAlpha(coreColor, 245));
            _canvas.DrawCircle(cx, cy, coreRadius, _paint);
        }

        // ==================== Habillage ====================

        private void DrawVignette()
        {
            float radius = Math.Max(_width, _height) * 0.78f;
            _paint.Reset();
            _paint.AntiAlias = true;
            _paint.SetShader(new Android.Graphics.RadialGradient(
                _width * 0.5f, _height * 0.5f, radius,
                0x00000000, unchecked((int)0x96000000),
                Android.Graphics.Shader.TileMode.Clamp));
            _canvas.DrawRect(0, 0, _width, _height, _paint);
            _paint.SetShader(null);
        }

        private void DrawOverlayText(float seconds)
        {
            string text = _spec.OverlayText;
            if (string.IsNullOrWhiteSpace(text)) return;

            float duration = (float)_spec.DurationSeconds;
            float alpha = 1f;
            if (seconds < 0.6f) alpha = seconds / 0.6f;
            else if (seconds > duration - 0.8f) alpha = Math.Max(0f, (duration - seconds) / 0.8f);
            if (alpha <= 0.01f) return;

            float textSize = _width * 0.115f;
            _textPaint.TextSize = textSize;
            _textPaint.SetShadowLayer(textSize * 0.22f, 0, textSize * 0.05f, AColor.Argb(190, 0, 0, 0));

            // Reduit la taille jusqu'a tenir dans 84% de la largeur.
            while (_textPaint.MeasureText(text) > _width * 0.84f && _textPaint.TextSize > _width * 0.03f)
                _textPaint.TextSize = _textPaint.TextSize * 0.92f;

            float y = _height * 0.86f;
            _textPaint.Color = ToColor(WithAlpha(unchecked((int)0xFFFFFFFF), (int)(255 * alpha)));
            _canvas.DrawText(text, _width * 0.5f, y, _textPaint);

            _textPaint.ClearShadowLayer();
        }

        private void DrawFades(float seconds)
        {
            float duration = (float)_spec.DurationSeconds;
            float fade = 0f;
            if (seconds < 0.35f) fade = 1f - seconds / 0.35f;
            else if (seconds > duration - 0.35f) fade = Math.Max(0f, 1f - (duration - seconds) / 0.35f);
            if (fade <= 0.01f) return;

            _paint.Reset();
            _paint.Color = AColor.Argb((int)(255 * Math.Clamp(fade, 0f, 1f)), 0, 0, 0);
            _canvas.DrawRect(0, 0, _width, _height, _paint);
        }

        // ==================== Utilitaires ====================

        private int[] Palette
        {
            get
            {
                var colors = _spec.Palette;
                return colors != null && colors.Length >= 3
                    ? colors
                    : new[] { unchecked((int)0xFF0A0A14), unchecked((int)0xFF2A0A2E),
                              unchecked((int)0xFF8B5CF6), unchecked((int)0xFFE50914),
                              unchecked((int)0xFF00D4AA) };
            }
        }

        private static AColor ToColor(int argb)
            => AColor.Argb((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);

        private static int WithAlpha(int argb, int alpha)
        {
            int a = Math.Clamp(alpha, 0, 255);
            return (a << 24) | (argb & 0x00FFFFFF);
        }

        private static int Blend(int from, int to, float amount)
        {
            float k = Math.Clamp(amount, 0f, 1f);
            int a = (int)(((from >> 24) & 0xFF) + (((to >> 24) & 0xFF) - ((from >> 24) & 0xFF)) * k);
            int r = (int)(((from >> 16) & 0xFF) + (((to >> 16) & 0xFF) - ((from >> 16) & 0xFF)) * k);
            int g = (int)(((from >> 8) & 0xFF) + (((to >> 8) & 0xFF) - ((from >> 8) & 0xFF)) * k);
            int b = (int)((from & 0xFF) + ((to & 0xFF) - (from & 0xFF)) * k);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        private static float Frac(float value) => value - (float)Math.Floor(value);

        private static float Wave(float value) => 0.5f + 0.5f * (float)Math.Sin(value * 6.283f);

        /// <summary>Pseudo-aleatoire deterministe : meme prompt = meme rendu.</summary>
        private float Rand(int index, int salt)
        {
            unchecked
            {
                int h = (index * 374761393) + (salt * 668265263) + _spec.Seed;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }
#endif

        public void Dispose()
        {
#if ANDROID
            _paint?.Dispose();
            _textPaint?.Dispose();
            _canvas?.Dispose();
            if (_bitmap != null && !_bitmap.IsRecycled)
            {
                _bitmap.Recycle();
                _bitmap.Dispose();
            }
#endif
        }
    }
}
