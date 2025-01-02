using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Models {
    public class DynamicSymbolPointerModel {
        [JsonProperty("type")]
        public string Type { get; set; } = "";
        [JsonProperty("value")]
        public string Value { get; set; } = "";
    }
}
