using System.Buffers.Binary;
using System.Text;
using smodr.Models;

namespace smodr.Services;

/// <summary>Reads plain TIT2/TPE1 frames from bounded HLS timed-ID3 cues.</summary>
public static class HlsId3TrackParser
{
    private const int MaxCueBytes = 65_536;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private static readonly UnicodeEncoding _strictLittleEndian = new(false, false, true);
    private static readonly UnicodeEncoding _strictBigEndian = new(true, false, true);

    public static RadioTrackInfo? Parse(ReadOnlySpan<byte> bytes, string stationName)
    {
        return Parse(bytes, stationName, out _);
    }

    public static RadioTrackInfo? Parse(ReadOnlySpan<byte> bytes, string stationName, out bool hasDamagedText)
    {
        hasDamagedText = false;
        if (bytes.Length is < 20 or > MaxCueBytes || !bytes[..3].SequenceEqual("ID3"u8)
                                                  || bytes[3] is not (3 or 4) || bytes[5] != 0
                                                  || !TrySynchsafe(bytes.Slice(6, 4), out var tagSize)
                                                  || tagSize > bytes.Length - 10)
        {
            return null;
        }

        var version = bytes[3];
        var end = 10 + tagSize;
        var offset = 10;
        string? title = null;
        string? artist = null;
        while (offset + 10 <= end)
        {
            var frame = bytes[offset..end];
            if (frame[0] == 0)
            {
                break;
            }

            if (!IsFrameId(frame[..4]))
            {
                return null;
            }

            var frameSize = version == 4
                ? TrySynchsafe(frame.Slice(4, 4), out var size) ? size : -1
                : BinaryPrimitives.ReadInt32BigEndian(frame.Slice(4, 4));
            if (frameSize < 0 || frameSize > end - offset - 10)
            {
                return null;
            }

            if (frameSize > 0 && frame[8] == 0 && frame[9] == 0)
            {
                var payload = frame.Slice(10, frameSize);
                if (frame[..4].SequenceEqual("TIT2"u8))
                {
                    title = DecodeText(payload);
                    if (title is null)
                    {
                        hasDamagedText = true;
                        return null;
                    }
                }
                else if (frame[..4].SequenceEqual("TPE1"u8))
                {
                    artist = DecodeText(payload);
                    if (artist is null)
                    {
                        hasDamagedText = true;
                        return null;
                    }
                }
            }

            offset += 10 + frameSize;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        hasDamagedText = title.Contains('\uFFFD', StringComparison.Ordinal)
                         || artist?.Contains('\uFFFD', StringComparison.Ordinal) == true;
        return string.IsNullOrWhiteSpace(artist)
            ? IcyTrackParser.Parse(title, stationName)
            : IcyTrackParser.Accept(new RadioTrackInfo(title, artist), stationName);
    }

    private static bool IsFrameId(ReadOnlySpan<byte> id)
    {
        foreach (var value in id)
        {
            if (value is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySynchsafe(ReadOnlySpan<byte> value, out int size)
    {
        size = 0;
        foreach (var octet in value)
        {
            if ((octet & 0x80) != 0)
            {
                return false;
            }

            size = (size << 7) | octet;
        }

        return true;
    }

    private static string? DecodeText(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return null;
        }

        var encoding = payload[0];
        var text = payload[1..];
        try
        {
            var decoded = encoding switch
            {
                0 => Encoding.Latin1.GetString(text),
                1 when text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF =>
                    _strictBigEndian.GetString(text[2..]),
                1 when text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE =>
                    _strictLittleEndian.GetString(text[2..]),
                2 => _strictBigEndian.GetString(text),
                3 => _strictUtf8.GetString(text),
                _ => null
            };
            return decoded?.Split('\0', 2)[0].Trim();
        }
        catch (DecoderFallbackException) { return null; }
    }
}
