using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Descriptors.Resolved {
    public class ResolvedDynamicSymbolDescriptor {
        public string Name { get; set; } = "";
        public string DemangledName { get; set; } = "";
        public string? Signature { get; set; }
        public ulong? Address { get; set; }

        public override bool Equals(object? obj) {
            if (obj is not ResolvedDynamicSymbolDescriptor other)
                return false;
            return Name == other.Name;
        }

        public override int GetHashCode() {
            return Name.GetHashCode();
        }
    }
}
