namespace smodr.Services;

public static class ArtworkSizing
{
    public static int DecodeEdge(double logicalEdge, double rasterScale)
    {
        if (!double.IsFinite(logicalEdge) || logicalEdge <= 0)
        {
            logicalEdge = 52;
        }

        if (!double.IsFinite(rasterScale) || rasterScale <= 0)
        {
            rasterScale = 1;
        }

        return (int)(Math.Ceiling(Math.Clamp(logicalEdge * rasterScale, 64, 1024) / 64) * 64);
    }

    public static (int Width, int Height)? DecodeDimensions(uint width, uint height, int edge)
    {
        if (width == 0 || height == 0 || width > 8192 || height > 8192 || (long)width * height > 16_777_216)
        {
            return null;
        }

        var scale = Math.Min(1, Math.Clamp(edge, 64, 1024) / (double)Math.Max(width, height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
