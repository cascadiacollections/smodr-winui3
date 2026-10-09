namespace smodr.Models;

public static class RadioStationIdentity
{
    public static bool Matches(RadioStation left, RadioStation right)
    {
        return !string.IsNullOrWhiteSpace(left.Id) && !string.IsNullOrWhiteSpace(right.Id)
            ? string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
            : !string.IsNullOrWhiteSpace(left.StreamUrl)
              && !string.IsNullOrWhiteSpace(right.StreamUrl)
              && string.Equals(left.StreamUrl, right.StreamUrl, StringComparison.OrdinalIgnoreCase);
    }
}
