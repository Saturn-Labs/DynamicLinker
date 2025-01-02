using DynamicLinker.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Utils {
    public static class VC {
        private static string? toolsVersion;
        public static string? ToolsVersion {
            get => toolsVersion ??= GetToolsVersion();
        }

        private static string? toolsPath;
        public static string? ToolsPath {
            get => toolsPath ??= GetToolsPath();
        }

        public static string? GetToolsVersion() {
            string txtPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}/Microsoft Visual Studio/2022/Community/VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt";
            if (!File.Exists(txtPath)) {
                return null;
            }

            return File.ReadAllText(txtPath).Trim();
        }

        public static string? GetToolsPath(string arch = "x64") {
            if (ToolsVersion is null) {
                return null;
            }

            if (!ArchitectureUtils.IsArchitectureValid(arch)) {
                return null;
            }

            if (ArchitectureUtils.IsTechnicalName(arch)) {
                arch = ArchitectureUtils.ConvertToFriendlyNameArch(arch);
            }

            string machineFriendlyArch = ArchitectureUtils.ConvertToFriendlyNameArch(ArchitectureUtils.GetArchitecture());
            string toolsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC/{ToolsVersion}/bin/Host{machineFriendlyArch}/{arch}";
            if (!Directory.Exists(toolsPath)) {
                return null;
            }

            return toolsPath;
        }

        public static async Task<bool> LibAsync(string arguments) {
            string? toolsPath = ToolsPath;
            if (toolsPath is null) {
                return false;
            }

            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = $"{toolsPath}/lib.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = new() {
                StartInfo = psi
            };
            
            process.Start();
            while (!process.HasExited)
                Log.Trace((await process.StandardOutput.ReadLineAsync()) ?? "");
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
    }
}
