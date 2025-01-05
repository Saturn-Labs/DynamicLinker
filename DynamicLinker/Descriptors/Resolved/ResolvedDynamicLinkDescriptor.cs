using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Descriptors.Resolved {
    public class ResolvedDynamicLinkDescriptor {
        public string TargetName { get; set; } = "";
        public List<ResolvedDynamicSymbolDescriptor> ResolvedSymbols { get; set; } = [];

        public override bool Equals(object? obj) {
            if (obj is not ResolvedDynamicLinkDescriptor other)
                return false;
            return TargetName == other.TargetName;
        }

        public override int GetHashCode() {
            return TargetName.GetHashCode();
        }
    }
}
