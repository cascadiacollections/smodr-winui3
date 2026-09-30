using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace smodr;

public sealed partial class StationArtworkControl : UserControl, IDisposable
{
    private const int MaxArtworkBytes = 1_000_000;
    private const int MaxCachedArtwork = 48;
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly SemaphoreSlim _downloads = new(4);
    private static readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> _cacheOrder = new();
    private static readonly StationArtworkDiskCache _diskCache = new(
        Path.Combine(App.StorageDirectory, "station-artwork"));
    private CancellationTokenSource? _loadCancellation;
    public event EventHandler<Color?>? AccentColorChanged;

    public static readonly DependencyProperty ArtworkUrlProperty = DependencyProperty.Register(
        nameof(ArtworkUrl), typeof(string), typeof(StationArtworkControl),
        new PropertyMetadata(string.Empty, OnArtworkUrlChanged));
    public static readonly DependencyProperty FallbackArtworkUrlProperty = DependencyProperty.Register(
        nameof(FallbackArtworkUrl), typeof(string), typeof(StationArtworkControl),
        new PropertyMetadata(string.Empty, OnArtworkUrlChanged));

    public StationArtworkControl()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadArtwork();
        Unloaded += (_, _) => CancelLoad();
    }

    public string ArtworkUrl
    {
        get => (string)GetValue(ArtworkUrlProperty);
        set => SetValue(ArtworkUrlProperty, value);
    }

    public string FallbackArtworkUrl
    {
        get => (string)GetValue(FallbackArtworkUrlProperty);
        set => SetValue(FallbackArtworkUrlProperty, value);
    }

    public void Dispose()
    {
        CancelLoad();
        GC.SuppressFinalize(this);
    }

    private static void OnArtworkUrlChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (StationArtworkControl)sender;
        control.CancelLoad();
        // Keep the station cover visible while the album cover loads.
        if (string.IsNullOrWhiteSpace(control.FallbackArtworkUrl)
            || control.ArtworkUrl == control.FallbackArtworkUrl)
        {
            control.ArtworkImage.Source = null;
            control.Placeholder.Visibility = Visibility.Visible;
            control.AccentColorChanged?.Invoke(control, null);
        }
        if (control.IsLoaded)
        {
            control.LoadArtwork();
        }
    }

    private void LoadArtwork()
    {
        var value = Uri.TryCreate(ArtworkUrl, UriKind.Absolute, out var primary)
            && primary.Scheme is "https" or "http" ? primary : null;
        var fallback = Uri.TryCreate(FallbackArtworkUrl, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "https" or "http" ? parsed : null;
        var uri = value ?? fallback;
        if (uri is null)
        {
            return;
        }

        _loadCancellation = new CancellationTokenSource();
        _ = LoadArtworkAsync(uri, fallback != uri ? fallback : null, _loadCancellation.Token);
    }

    private async Task LoadArtworkAsync(Uri uri, Uri? fallback, CancellationToken cancellationToken)
    {
        if (await TrySetArtworkAsync(uri, cancellationToken) || fallback is null
            || cancellationToken.IsCancellationRequested) return;
        await TrySetArtworkAsync(fallback, cancellationToken);
    }

    private async Task<bool> TrySetArtworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await GetArtworkBytesAsync(uri, cancellationToken);
            if (bytes is null || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = 440 };
            await image.SetSourceAsync(stream);
            if (!cancellationToken.IsCancellationRequested)
            {
                this.ArtworkImage.Source = image;
                this.Placeholder.Visibility = Visibility.Collapsed;
                if (AccentColorChanged is not null)
                {
                    var accent = await ReadAccentAsync(bytes);
                    if (!cancellationToken.IsCancellationRequested)
                        AccentColorChanged?.Invoke(this, accent);
                }
                return true;
            }
        }
        catch (Exception)
        {
            // Artwork is optional; try the station cover when the album cover fails.
        }
        return false;
    }

    private static async Task<Color?> ReadAccentAsync(byte[] bytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = 12, ScaledHeight = 12 },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var data = pixels.DetachPixelData();
            var bestSaturation = -1;
            Color? best = null;
            for (var index = 0; index + 3 < data.Length; index += 4)
            {
                var blue = data[index];
                var green = data[index + 1];
                var red = data[index + 2];
                var saturation = Math.Max(red, Math.Max(green, blue))
                    - Math.Min(red, Math.Min(green, blue));
                if (saturation <= bestSaturation || Math.Max(red, Math.Max(green, blue)) < 64) continue;
                bestSaturation = saturation;
                best = Color.FromArgb(255, red, green, blue);
            }
            return best;
        }
        catch (Exception) { return null; }
    }

    private static async Task<byte[]?> GetArtworkBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(uri.AbsoluteUri, out var cached))
        {
            return cached;
        }

        var saved = await _diskCache.TryReadAsync(uri, cancellationToken);
        if (saved is not null)
        {
            Remember(uri, saved);
            return saved;
        }

        await _downloads.WaitAsync(cancellationToken);
        try
        {
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength > MaxArtworkBytes
                || response.Content.Headers.ContentType?.MediaType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp"))
            {
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[16_384];
            int read;
            while ((read = await source.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + read > MaxArtworkBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            var bytes = buffer.ToArray();
            Remember(uri, bytes);
            await _diskCache.StoreAsync(uri, bytes, cancellationToken);
            return bytes;
        }
        finally
        {
            _downloads.Release();
        }
    }

    private static void Remember(Uri uri, byte[] bytes)
    {
        if (!_cache.TryAdd(uri.AbsoluteUri, bytes)) return;
        _cacheOrder.Enqueue(uri.AbsoluteUri);
        while (_cache.Count > MaxCachedArtwork && _cacheOrder.TryDequeue(out var oldest))
        {
            _cache.TryRemove(oldest, out _);
        }
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
