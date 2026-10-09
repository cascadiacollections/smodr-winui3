using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace smodr.Services;

internal static class WindowsWarmupPolicy
{
    public static bool CanPrefetch()
    {
        try
        {
            if (PowerManager.EnergySaverStatus == EnergySaverStatus.On)
            {
                return false;
            }

            var connection = NetworkInformation.GetInternetConnectionProfile();
            if (connection?.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.InternetAccess)
            {
                return false;
            }

            var cost = connection.GetConnectionCost();
            return cost.NetworkCostType == NetworkCostType.Unrestricted
                   && !cost.Roaming
                   && !cost.OverDataLimit
                   && !cost.BackgroundDataUsageRestricted;
        }
        catch (Exception)
        {
            // Unknown cost/power state is not permission for speculative traffic.
            return false;
        }
    }
}
