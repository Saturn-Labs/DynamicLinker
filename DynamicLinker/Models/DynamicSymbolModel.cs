using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicLinker.Models {
    public enum PointerType {
        Unknown,
        Offset,
        Signature
    }

    public partial class DynamicSymbolModel {
        [JsonProperty("version")]
        public string Version { get; set; } = "";
        [JsonProperty("architecture")]
        public string Architecture { get; set; } = "x64";
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = "";
        [JsonProperty("pointer")]
        public string Pointer { get; set; } = "0x0";

        public PointerType GetPointerType() {
            var offsetPattern = OffsetRegex();
            var signaturePattern = SignatureRegex();

            if (offsetPattern.IsMatch(Pointer)) {
                return PointerType.Offset;
            }
            else if (signaturePattern.IsMatch(Pointer)) {
                return PointerType.Signature;
            }
            else {
                return PointerType.Unknown;
            }
        }

        [GeneratedRegex(@"^0x[0-9a-fA-F]{1,16}$")]
        public static partial Regex OffsetRegex();
        [GeneratedRegex(@"^([0-9A-Fa-f]{2}|\?)(( [0-9A-Fa-f]{2}| \?))*$")]
        public static partial Regex SignatureRegex();
    }
}
