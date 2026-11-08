namespace TacticalRequests;

internal static class BuildFeatures
{
#if TACTICAL_SOLO_TEST
    public static bool SoloTestingEnabled => true;
#else
    public static bool SoloTestingEnabled => false;
#endif
}
