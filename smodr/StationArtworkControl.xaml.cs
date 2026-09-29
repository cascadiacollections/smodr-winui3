using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Services;
using Windows.Storage.Streams;

namespace smodr;

public sealed partial class StationArtworkControl : UserControl, IDisposable
{
    private const int MaxArtworkBytes = 1_000_000;
    private const int MaxCachedArtwork = 48;
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly SemaphoreSlim _downloads = new(4);
    private static readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> _cacheOrder = new();
    private static readonly StationArtworkDiskCache _diskCache = new();
    private CancellationTokenSource? _loadCancellation;

    public static readonly DependencyProperty ArtworkUrlProperty = DependencyProperty.Register(
        nameof(ArtworkUrl), typeof(string), typeof(StationArtworkControl),
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

    public void Dispose()
    {
        CancelLoad();
        GC.SuppressFinalize(this);
    }

    private static void OnArtworkUrlChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (StationArtworkControl)sender;
        control.CancelLoad();
        control.ArtworkImage.Source = null;
        control.Placeholder.Visibility = Visibility.Visible;
        if (control.IsLoaded)
        {
            control.LoadArtwork();
        }
    }

    private void LoadArtwork()
    {
        if (!Uri.TryCreate(ArtworkUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
        {
            return;
        }

        _loadCancellation = new CancellationTokenSource();
        _ = LoadArtworkAsync(uri, _loadCancellation.Token);
    }

    private async Task LoadArtworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await GetArtworkBytesAsync(uri, cancellationToken);
            if (bytes is null || cancellationToken.IsCancellationRequested)
            {
                return;
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
            }
        }
        catch (Exception)
        {
            // Directory artwork is optional; keep the station's placeholder.
        }
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
