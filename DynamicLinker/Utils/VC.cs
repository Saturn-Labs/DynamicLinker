using DynamicLinker.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Utils {
    public static class VC {
        public static string? GetToolsVersion() {
            string txtPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}/Microsoft Visual Studio/2022/Community/VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt";
            if (!File.Exists(txtPath)) {
                return null;
            }

            return File.ReadAllText(txtPath).Trim();
        }

        public static string? GetToolsPath(string arch) {
            if (GetToolsVersion() is not string tools) {
                return null;
            }

            if (!ArchitectureUtils.IsArchitectureValid(arch)) {
                return null;
            }

            string machineArch = ArchitectureUtils.GetArchitecture();
            string toolsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC/{tools}/bin/Host{machineArch}/{arch}";
            if (!Directory.Exists(toolsPath)) {
                return null;
            }

            return toolsPath;
        }

        public static async Task<bool> LibAsync(string arguments, string arch) {
            if (GetToolsPath(arch) is not string tools) {
                return false;
            }

            ProcessStartInfo psi = new() {
                FileName = $"{tools}/lib.exe",
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
