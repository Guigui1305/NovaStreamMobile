using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NovaStreamMobile.Models;

namespace NovaStreamMobile.Services
{
    public class VideoGenerationResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public bool Cancelled { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    /// <summary>
    /// Encode localement une video MP4 (H.264) image par image, sans reseau.
    /// Utilise MediaCodec (encodeur materiel du telephone) + MediaMuxer.
    /// </summary>
    public class VideoGeneratorService
    {
        private const string MimeType = "video/avc";

        // Formats couleur MediaCodec (valeurs OMX, stables entre versions d'Android).
        private const int ColorFormatYuv420Planar = 19;      // I420
        private const int ColorFormatYuv420SemiPlanar = 21;  // NV12
        private const int ColorFormatYuv420Flexible = 0x7F420888;

        public Task<VideoGenerationResult> GenerateAsync(
            VideoPromptSpec spec,
            string outputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Generate(spec, outputPath, progress, cancellationToken), cancellationToken);
        }

        private VideoGenerationResult Generate(
            VideoPromptSpec spec,
            string outputPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var started = DateTime.UtcNow;
            var result = new VideoGenerationResult { FilePath = outputPath };

#if ANDROID
            Android.Media.MediaCodec? codec = null;
            Android.Media.MediaMuxer? muxer = null;
            FrameRendererService? renderer = null;
            bool muxerStarted = false;
            int trackIndex = -1;

            try
            {
                int width = spec.Width;
                int height = spec.Height;
                int fps = Math.Clamp(spec.Fps, 12, 60);
                int totalFrames = spec.TotalFrames;

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                codec = Android.Media.MediaCodec.CreateEncoderByType(MimeType)
                        ?? throw new InvalidOperationException("Aucun encodeur H.264 disponible sur cet appareil.");

                int colorFormat = SelectColorFormat(codec);
                bool semiPlanar = colorFormat != ColorFormatYuv420Planar;

                int bitRate = (int)Math.Clamp((long)(width * (long)height * fps * 0.14), 1_200_000L, 12_000_000L);

                var format = Android.Media.MediaFormat.CreateVideoFormat(MimeType, width, height);
                format.SetInteger(Android.Media.MediaFormat.KeyColorFormat, colorFormat);
                format.SetInteger(Android.Media.MediaFormat.KeyBitRate, bitRate);
                format.SetInteger(Android.Media.MediaFormat.KeyFrameRate, fps);
                format.SetInteger(Android.Media.MediaFormat.KeyIFrameInterval, 1);

                codec.Configure(format, null, null, Android.Media.MediaCodecConfigFlags.Encode);
                codec.Start();

                muxer = new Android.Media.MediaMuxer(outputPath, Android.Media.MuxerOutputType.Mpeg4);

                renderer = new FrameRendererService(spec);
                var frameBytes = new byte[width * height * 3 / 2];
                var bufferInfo = new Android.Media.MediaCodec.BufferInfo();

                int frameIndex = 0;
                bool inputDone = false;
                bool outputDone = false;
                int lastReported = -1;
                var watchdog = System.Diagnostics.Stopwatch.StartNew();

                while (!outputDone)
                {
                    if (watchdog.Elapsed.TotalMinutes > 10)
                        throw new TimeoutException("L'encodage a depasse le temps maximum autorise.");

                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    // ---------- Alimentation de l'encodeur ----------
                    if (!inputDone)
                    {
                        int inputIndex = codec.DequeueInputBuffer(10000);
                        if (inputIndex >= 0)
                        {
                            long presentationTimeUs = frameIndex * 1_000_000L / fps;

                            if (frameIndex >= totalFrames)
                            {
                                codec.QueueInputBuffer(inputIndex, 0, 0, presentationTimeUs,
                                    Android.Media.MediaCodecBufferFlags.EndOfStream);
                                inputDone = true;
                            }
                            else
                            {
                                int[] pixels = renderer.RenderFrame(frameIndex, totalFrames);
                                ArgbToYuv420(pixels, frameBytes, width, height, semiPlanar);

                                var inputBuffer = codec.GetInputBuffer(inputIndex);
                                if (inputBuffer == null)
                                    throw new InvalidOperationException("Buffer d'entree indisponible.");

                                inputBuffer.Clear();
                                inputBuffer.Put(frameBytes);

                                codec.QueueInputBuffer(inputIndex, 0, frameBytes.Length,
                                    presentationTimeUs, (Android.Media.MediaCodecBufferFlags)0);

                                frameIndex++;

                                int percent = (int)(frameIndex * 100.0 / totalFrames);
                                if (percent != lastReported)
                                {
                                    lastReported = percent;
                                    progress?.Report(Math.Min(1.0, frameIndex / (double)totalFrames));
                                }
                            }
                        }
                    }

                    // ---------- Recuperation du flux encode ----------
                    int outputIndex = codec.DequeueOutputBuffer(bufferInfo, 10000);

                    if (outputIndex == -2) // INFO_OUTPUT_FORMAT_CHANGED
                    {
                        if (!muxerStarted)
                        {
                            trackIndex = muxer.AddTrack(codec.OutputFormat!);
                            muxer.Start();
                            muxerStarted = true;
                        }
                    }
                    else if (outputIndex >= 0)
                    {
                        bool isCodecConfig =
                            (bufferInfo.Flags & Android.Media.MediaCodecBufferFlags.CodecConfig) != 0;

                        if (isCodecConfig)
                            bufferInfo.Size = 0;

                        if (bufferInfo.Size > 0 && muxerStarted)
                        {
                            var encoded = codec.GetOutputBuffer(outputIndex);
                            if (encoded != null)
                                muxer.WriteSampleData(trackIndex, encoded, bufferInfo);
                        }

                        bool endOfStream =
                            (bufferInfo.Flags & Android.Media.MediaCodecBufferFlags.EndOfStream) != 0;

                        codec.ReleaseOutputBuffer(outputIndex, false);

                        if (endOfStream)
                            outputDone = true;
                    }
                    // -1 (TRY_AGAIN_LATER) et -3 (buffers changed) : on continue simplement.
                }

                if (result.Cancelled)
                {
                    SafeDelete(outputPath);
                    return result;
                }

                progress?.Report(1.0);
                result.Success = File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
                if (!result.Success)
                    result.Error = "Le fichier genere est vide.";
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                SafeDelete(outputPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                SafeDelete(outputPath);
                System.Diagnostics.Debug.WriteLine($"[VideoGenerator] {ex}");
            }
            finally
            {
                try { codec?.Stop(); } catch { }
                try { codec?.Release(); } catch { }
                try { if (muxerStarted) muxer?.Stop(); } catch { }
                try { muxer?.Release(); } catch { }
                renderer?.Dispose();
            }
#else
            result.Error = "La generation video n'est disponible que sur Android.";
#endif

            result.Elapsed = DateTime.UtcNow - started;
            return result;
        }

#if ANDROID
        /// <summary>Choisit un format couleur d'entree supporte par l'encodeur du telephone.</summary>
        private static int SelectColorFormat(Android.Media.MediaCodec codec)
        {
            try
            {
                var capabilities = codec.CodecInfo?.GetCapabilitiesForType(MimeType);
                var formats = capabilities?.ColorFormats;
                if (formats != null)
                {
                    foreach (int format in formats)
                        if (format == ColorFormatYuv420SemiPlanar) return format;

                    foreach (int format in formats)
                        if (format == ColorFormatYuv420Planar) return format;

                    foreach (int format in formats)
                        if (format == ColorFormatYuv420Flexible) return format;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoGenerator] SelectColorFormat: {ex.Message}");
            }

            return ColorFormatYuv420SemiPlanar;
        }

        /// <summary>
        /// Convertit une image ARGB en YUV 4:2:0 (NV12 ou I420) attendu par l'encodeur.
        /// Coefficients BT.601 plage reduite, comme MediaCodec l'attend.
        /// </summary>
        private static void ArgbToYuv420(int[] argb, byte[] output, int width, int height, bool semiPlanar)
        {
            int frameSize = width * height;
            int chromaWidth = width / 2;

            for (int j = 0; j < height; j++)
            {
                int rowOffset = j * width;

                for (int i = 0; i < width; i++)
                {
                    int color = argb[rowOffset + i];
                    int r = (color >> 16) & 0xFF;
                    int g = (color >> 8) & 0xFF;
                    int b = color & 0xFF;

                    int y = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                    output[rowOffset + i] = (byte)(y < 16 ? 16 : (y > 235 ? 235 : y));
                }
            }

            for (int j = 0; j < height; j += 2)
            {
                int row0 = j * width;
                int row1 = (j + 1) * width;

                for (int i = 0; i < width; i += 2)
                {
                    // Moyenne du bloc 2x2 pour la chrominance.
                    int r = 0, g = 0, b = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int index = (k < 2 ? row0 : row1) + i + (k % 2);
                        int color = argb[index];
                        r += (color >> 16) & 0xFF;
                        g += (color >> 8) & 0xFF;
                        b += color & 0xFF;
                    }
                    r >>= 2; g >>= 2; b >>= 2;

                    int u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                    int v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

                    u = u < 16 ? 16 : (u > 240 ? 240 : u);
                    v = v < 16 ? 16 : (v > 240 ? 240 : v);

                    int chromaIndex = (j / 2) * chromaWidth + (i / 2);

                    if (semiPlanar)
                    {
                        output[frameSize + chromaIndex * 2] = (byte)u;
                        output[frameSize + chromaIndex * 2 + 1] = (byte)v;
                    }
                    else
                    {
                        output[frameSize + chromaIndex] = (byte)u;
                        output[frameSize + frameSize / 4 + chromaIndex] = (byte)v;
                    }
                }
            }
        }
#endif

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
