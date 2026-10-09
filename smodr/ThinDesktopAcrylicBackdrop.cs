using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace smodr;

/// <summary>Desktop glass using the OS thin recipe and XAML's automatic policy/theme configuration.</summary>
#pragma warning disable CA1001 // SystemBackdrop disconnect owns controller disposal.
public sealed class ThinDesktopAcrylicBackdrop : SystemBackdrop
#pragma warning restore CA1001
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        _controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }
}
