using System.Diagnostics;

namespace SIR2CPU;

public static class Utils
{
    public static void StartSilent(string name, string args) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = name,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        })?.WaitForExit();
}