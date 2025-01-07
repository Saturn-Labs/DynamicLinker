using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Utils {
    public static class ArchitectureUtils {
        public static bool IsArchitectureValid(string arch) {
            return arch is "x64" or "x86";
        }

        public static string GetArchitecture() {
            return Environment.Is64BitOperatingSystem ? "x64" : "x86";
        }
    }
}
