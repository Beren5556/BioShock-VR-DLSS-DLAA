namespace BioShockMsi
{
    // Installer identity is independent from the shared rendering/UI code.
    // The combined MSI reuses BS1's accepted payload without rebuilding it.
    internal static class GamePackage
    {
#if BIOSHOCK2
        internal const string Id = "bs2";
        internal const string ExeName = "Bioshock2HD.exe";
        internal const string GameHash = "C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C";
        internal const string Name = "BioShock 2";
        internal const string SteamFolder = "BioShock 2 Remastered";
        internal const string AppId = "409720";
        internal const string LauncherName = "Lanzador BioShock 2 VR DLSS-DLAA.exe";
        internal const string ShortcutBase = "BioShock 2 VR DLSS-DLAA";
        internal const string LocalRelative = @"BioshockVR\bs2";
        internal const string IniRelative = @"BioshockHD\Bioshock2\Bioshock2SP.ini";
        internal const string LegacyRelative = @"BioshockVR\bs2\Installer-DLSS-DLAA\install.manifest";
#else
        internal const string Id = "bs1";
        internal const string ExeName = "BioshockHD.exe";
        internal const string GameHash = "AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B";
        internal const string Name = "BioShock";
        internal const string SteamFolder = "BioShock Remastered";
        internal const string AppId = "409710";
        internal const string LauncherName = "Lanzador BioShock VR DLSS-DLAA.exe";
        internal const string ShortcutBase = "BioShock VR DLSS-DLAA";
        internal const string LocalRelative = @"BioshockVR";
        internal const string IniRelative = @"BioshockHD\Bioshock\Bioshock.ini";
        internal const string LegacyRelative = @"BioshockVR\Installer-DLSS-DLAA-Beta-0.2\install.manifest";
#endif
    }
}
