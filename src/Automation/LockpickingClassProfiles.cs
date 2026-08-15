namespace CuePilot;

internal sealed record LockpickingClassProfile(
    string VehicleClass,
    double SpinDegreesPerSecond,
    double SpinRadiusRatio,
    double MaximumSpinSeconds);

internal static class LockpickingClassProfiles
{
    // Session 20260814-133216: median 1,144 degrees/s and 0.613 HUD-radius orbit.
    internal static LockpickingClassProfile ClassC { get; } = new("C", 1140, 0.61, 2.8);

    internal static bool TryGet(string vehicleClass, out LockpickingClassProfile profile)
    {
        if (string.Equals(vehicleClass, ClassC.VehicleClass, StringComparison.OrdinalIgnoreCase))
        {
            profile = ClassC;
            return true;
        }

        profile = null!;
        return false;
    }
}
