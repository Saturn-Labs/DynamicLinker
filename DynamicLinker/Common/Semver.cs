using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Common {
    using System;
    using System.Text.RegularExpressions;

    public partial class Semver {
        public string Major { get; private set; }
        public string Minor { get; private set; }
        public string Patch { get; private set; }
        public string Build { get; private set; }

        public Semver(string version) {
            var versionRegex = VersionRegex();
            var match = versionRegex.Match(version);

            if (match.Success) {
                Major = match.Groups[1].Value;
                Minor = string.IsNullOrEmpty(match.Groups[3].Value) ? "*" : match.Groups[3].Value;
                Patch = string.IsNullOrEmpty(match.Groups[5].Value) ? "*" : match.Groups[5].Value;
                Build = string.IsNullOrEmpty(match.Groups[7].Value) ? "*" : match.Groups[7].Value;
            }
            else {
                throw new ArgumentException("Invalid version format");
            }
        }

        public override string ToString() {
            return $"{Major}.{Minor}.{Patch}.{Build}";
        }

        public bool Equals(Semver other) {
            return CompareComponent(Major, other.Major) == 0 &&
                   CompareComponent(Minor, other.Minor) == 0 &&
                   CompareComponent(Patch, other.Patch) == 0 &&
                   CompareComponent(Build, other.Build) == 0;
        }

        public override bool Equals(object? obj) {
            return obj is Semver other && Equals(other);
        }

        public override int GetHashCode() {
            return ToString().GetHashCode();
        }

        public static bool operator ==(Semver left, Semver right) {
            return left.Equals(right);
        }

        public static bool operator !=(Semver left, Semver right) {
            return !left.Equals(right);
        }

        public static bool operator <(Semver left, Semver right) {
            return left.Compare(left, right) < 0;
        }

        public static bool operator <=(Semver left, Semver right) {
            return left.Compare(left, right) <= 0;
        }

        public static bool operator >(Semver left, Semver right) {
            return left.Compare(left, right) > 0;
        }

        public static bool operator >=(Semver left, Semver right) {
            return left.Compare(left, right) >= 0;
        }

        public void IncrementMajor() {
            if (Major != "*") {
                Major = (int.Parse(Major) + 1).ToString();
            }
        }

        public void IncrementMinor() {
            if (Minor != "*") {
                Minor = (int.Parse(Minor) + 1).ToString();
            }
        }

        public void IncrementPatch() {
            if (Patch != "*") {
                Patch = (int.Parse(Patch) + 1).ToString();
            }
        }

        public void IncrementBuild() {
            if (Build != "*") {
                Build = (int.Parse(Build) + 1).ToString();
            }
        }

        public bool FullWildcard() {
            return Major == "*" && Minor == "*" && Patch == "*" && Build == "*";
        }

        private int CompareComponent(string a, string b) {
            if (a == "*" || b == "*")
                return 0;

            if (int.TryParse(a, out int aValue) && int.TryParse(b, out int bValue)) {
                return aValue - bValue;
            }

            throw new ArgumentException("Invalid component format");
        }

        private int Compare(Semver left, Semver right) {
            return CompareComponent(left.Major, right.Major) != 0 ? CompareComponent(left.Major, right.Major) :
                   CompareComponent(left.Minor, right.Minor) != 0 ? CompareComponent(left.Minor, right.Minor) :
                   CompareComponent(left.Patch, right.Patch) != 0 ? CompareComponent(left.Patch, right.Patch) :
                   CompareComponent(left.Build, right.Build);
        }

        public static bool IsValidVersion(string version) {
            var versionRegex = VersionRegex();
            var match = versionRegex.Match(version);
            return match.Success;
        }

        [GeneratedRegex(@"^(\d+|\*)(\.(\d+|\*)(\.(\d+|\*)(\.(\d+|\*))?)?)?$")]
        public static partial Regex VersionRegex();
    }
}
