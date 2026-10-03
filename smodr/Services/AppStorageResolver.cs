using Windows.ApplicationModel;
using Windows.Storage;

namespace smodr.Services;

internal static class AppStorageResolver
{
    // GetFolderPath can resolve inside an MSIX package's LocalCache. The
    // unpackaged profile lives under the user's original LOCALAPPDATA path.
    public static string LegacyDirectory
    {
        get
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            return Path.Combine(localAppData, "CascadiaCollections", "ShoutkitWindows");
        }
    }

    public static string ResolveDirectory()
    {
        string packaged;
        try
        {
            _ = Package.Current.Id;
            packaged = ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return LegacyDirectory;
        }

        try { PackagedDataMigration.Import(LegacyDirectory, packaged); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Record("package.data-import", exception);
        }
        return packaged;
    }
}
