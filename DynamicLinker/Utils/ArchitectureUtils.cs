using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Utils {
    public static class ArchitectureUtils {
        public static bool IsArchitectureValid(string arch) {
            return IsTechnicalName(arch) || IsFriendlyName(arch);
        }

        public static bool IsTechnicalName(string arch) {
            return arch == "i386" || arch == "amd64";
        }

        public static bool IsFriendlyName(string arch) {
            return arch == "x86" || arch == "x64" || arch == "x86_64";
        }

        public static string GetArchitecture() {
            return Environment.Is64BitOperatingSystem ? "amd64" : "i386";
        }

        public static string ConvertToTechnicalNameArch(string arch = "x64") {
            return arch switch {
                "x86" => "i386",
                "x64" or "x86_64" => "amd64",
                _ => throw new ArgumentException("Invalid architecture")
            };
        }

        public static string ConvertToFriendlyNameArch(string arch = "amd64") {
            return arch switch {
                "i386" => "x86",
                "amd64" => "x64",
                _ => throw new ArgumentException("Invalid architecture")
            };
        }
    }
}
