using Microsoft.UI.Xaml;
using smodr.Converters;

namespace smodr.Tests.Converters;

[TestClass]
public sealed class StringToVisibilityConverterTests
{
    private readonly StringToVisibilityConverter _converter = new();

    [TestMethod]
    public void Convert_NonEmptyString_ReturnsVisible()
    {
        var result = _converter.Convert("hello", typeof(Visibility), null!, string.Empty);

        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_EmptyString_ReturnsCollapsed()
    {
        var result = _converter.Convert(string.Empty, typeof(Visibility), null!, string.Empty);

        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.ThrowsExactly<NotSupportedException>(
            () => _converter.ConvertBack(Visibility.Visible, typeof(string), null!, string.Empty));
    }
}
