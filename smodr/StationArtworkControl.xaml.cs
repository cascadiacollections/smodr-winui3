using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using smodr.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace smodr;

public sealed partial class StationArtworkControl : UserControl, IDisposable
{
    private static readonly HttpClient _client =
        new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly StationArtworkDiskCache _diskCache = new(
        Path.Combine(App.StorageDirectory, "station-artwork"));

    private static readonly StationArtworkLoader _loader = new(_client, _diskCache);
#pragma warning disable IDE0028 // A weak-key native dispatcher table is not a normal collection initializer.
    private static readonly ConditionalWeakTable<DispatcherQueue, ArtworkMemoryCache<DecodedArtwork>> _decoded = new();
#pragma warning restore IDE0028

    public static readonly DependencyProperty ArtworkUrlProperty = DependencyProperty.Register(
        nameof(ArtworkUrl), typeof(string), typeof(StationArtworkControl),
        new PropertyMetadata(string.Empty, OnArtworkUrlChanged));

    public static readonly DependencyProperty FallbackArtworkUrlProperty = DependencyProperty.Register(
        nameof(FallbackArtworkUrl), typeof(string), typeof(StationArtworkControl),
        new PropertyMetadata(string.Empty, OnArtworkUrlChanged));

    public static readonly DependencyProperty ImageStretchProperty = DependencyProperty.Register(
        nameof(ImageStretch), typeof(Stretch), typeof(StationArtworkControl),
        new PropertyMetadata(Stretch.UniformToFill));

    private readonly DispatcherQueue _cacheDispatcher;
    private int _decodeEdge;
    private CancellationTokenSource? _loadCancellation;

    public StationArtworkControl()
    {
        _cacheDispatcher = DispatcherQueue;
        InitializeComponent();
        Loaded += (_, _) => LoadArtwork();
        Unloaded += (_, _) => CancelLoad();
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && CurrentDecodeEdge != _decodeEdge)
            {
                LoadArtwork();
            }
        };
    }

    public Stretch ImageStretch
    {
        get => (Stretch)GetValue(ImageStretchProperty);
        set => SetValue(ImageStretchProperty, value);
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

    private int CurrentDecodeEdge =>
        ArtworkSizing.DecodeEdge(Math.Max(ActualWidth, ActualHeight), XamlRoot?.RasterizationScale ?? 1);

    public void Dispose()
    {
        CancelLoad();
        GC.SuppressFinalize(this);
    }

    internal static Task ShutdownTransportAsync()
    {
        return _loader.ShutdownAsync();
    }

    public event EventHandler<Color?>? AccentColorChanged;

    private static void OnArtworkUrlChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (StationArtworkControl)sender;
        control.CancelLoad();
        // Never display the previous station or song's cover under a new title.
        control.ArtworkImage.Source = null;
        control.Placeholder.Visibility = Visibility.Visible;
        control.AccentColorChanged?.Invoke(control, null);
        if (control.IsLoaded)
        {
            control.LoadArtwork();
        }
    }

    private void LoadArtwork()
    {
        CancelLoad();
        _decodeEdge = CurrentDecodeEdge;
        var value = Uri.TryCreate(ArtworkUrl, UriKind.Absolute, out var primary)
                    && primary.Scheme is "https" or "http"
            ? primary
            : null;
        var fallback = Uri.TryCreate(FallbackArtworkUrl, UriKind.Absolute, out var parsed)
                       && parsed.Scheme is "https" or "http"
            ? parsed
            : null;
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
                                                             || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await TrySetArtworkAsync(fallback, cancellationToken);
    }

    private async Task<bool> TrySetArtworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var edge = _decodeEdge;
            var cache = _decoded.GetValue(_cacheDispatcher,
                _ => new ArtworkMemoryCache<DecodedArtwork>(64, 16 * 1024 * 1024));
            var key = uri.AbsoluteUri + "|" + edge.ToString(CultureInfo.InvariantCulture);
            if (cache.TryGet(key, out var cached) && cached is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                ArtworkImage.Source = cached.Image;
                Placeholder.Visibility = Visibility.Collapsed;
                AccentColorChanged?.Invoke(this, cached.Accent);
                return true;
            }

            var bytes = await _loader.GetAsync(uri, cancellationToken);
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
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            var dimensions = ArtworkSizing.DecodeDimensions(decoder.PixelWidth, decoder.PixelHeight, edge);
            if (dimensions is not { } size)
            {
                return false;
            }

            var accent = await ReadAccentAsync(decoder);
            cancellationToken.ThrowIfCancellationRequested();
            stream.Seek(0);
            var image = new BitmapImage
            {
                DecodePixelWidth = size.Width,
                DecodePixelHeight = size.Height,
                DecodePixelType = DecodePixelType.Physical
            };
            await image.SetSourceAsync(stream).AsTask(cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                ArtworkImage.Source = image;
                Placeholder.Visibility = Visibility.Collapsed;
                cache.Put(key, new DecodedArtwork(image, accent), (long)size.Width * size.Height * 4);
                AccentColorChanged?.Invoke(this, accent);
                return true;
            }
        }
        catch (Exception)
        {
            // Artwork is optional; try the station cover when the album cover fails.
        }

        return false;
    }

    private static async Task<Color?> ReadAccentAsync(BitmapDecoder decoder)
    {
        try
        {
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
                if (saturation <= bestSaturation || Math.Max(red, Math.Max(green, blue)) < 64)
                {
                    continue;
                }

                bestSaturation = saturation;
                best = Color.FromArgb(255, red, green, blue);
            }

            return best;
        }
        catch (Exception) { return null; }
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private sealed record DecodedArtwork(BitmapImage Image, Color? Accent);
}
