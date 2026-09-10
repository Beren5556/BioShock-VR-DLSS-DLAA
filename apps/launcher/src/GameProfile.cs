using System;
using System.IO;

namespace BioshockVrLauncher
{
    // Both executables compile the same form and Image tab. The build binds the
    // game identity; a command-line path never changes the selected profile.
    internal static class GameProfile
    {
#if BIOSHOCK2
        internal static readonly bool IsBioShock2 = true;
        internal const string ExeName = "Bioshock2HD.exe";
        internal const string ExeHash = "C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C";
        internal const string AppId = "409720";
        internal const string DisplayName = "BioShock 2";
        internal const string SteamFolder = "BioShock 2 Remastered";
        internal const string IniName = "Bioshock2SP.ini";
#else
        internal static readonly bool IsBioShock2 = false;
        internal const string ExeName = "BioshockHD.exe";
        internal const string ExeHash = "AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B";
        internal const string AppId = "409710";
        internal const string DisplayName = "BioShock";
        internal const string SteamFolder = "BioShock Remastered";
        internal const string IniName = "Bioshock.ini";
#endif
        internal static string ProcessName { get { return Path.GetFileNameWithoutExtension(ExeName); } }
        internal static string LocalDirectory { get { return GetLocalDirectory(Program.SandboxRoot); } }
        internal static string GameIniDirectory { get { return GetGameIniDirectory(Program.SandboxRoot); } }
        internal static string GetLocalDirectory(string sandbox)
        {
            string root = string.IsNullOrEmpty(sandbox)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(sandbox, "LocalAppData");
            string local = Path.Combine(root, "BioshockVR");
            return IsBioShock2 ? Path.Combine(local, "bs2") : local;
        }
        internal static string GetGameIniDirectory(string sandbox)
        {
            string root = string.IsNullOrEmpty(sandbox)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Path.Combine(sandbox, "AppData");
            return Path.Combine(root, "BioshockHD", IsBioShock2 ? "Bioshock2" : "Bioshock");
        }
        internal static bool VerifyExecutable(string path, out string problem)
        {
            problem = null;
            if (!File.Exists(path) || !string.Equals(Path.GetFileName(path), ExeName, StringComparison.OrdinalIgnoreCase))
                problem = "Selecciona " + ExeName + " de " + DisplayName + " Remastered.";
            else if (!string.Equals(Bs2Profile.Hash(path), ExeHash, StringComparison.OrdinalIgnoreCase))
                problem = "El ejecutable no coincide con la compilación Steam compatible de " + DisplayName + ".";
            return problem == null;
        }
        internal static bool SelfTest()
        {
            string root = Path.Combine(Path.GetTempPath(), "BVR-profile-test");
            return GetGameIniDirectory(root).StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                GetLocalDirectory(root).StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                (IsBioShock2 ? AppId == "409720" && IniName == "Bioshock2SP.ini" &&
                    GetLocalDirectory(root).EndsWith(Path.Combine("BioshockVR", "bs2"))
                : AppId == "409710" && IniName == "Bioshock.ini" &&
                    GetLocalDirectory(root).EndsWith("BioshockVR"));
        }
    }
}
