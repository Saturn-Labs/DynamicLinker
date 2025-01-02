using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Models {
    public class DynamicSymbolModel {
        [JsonProperty("version")]
        public string Version { get; set; } = "";
        [JsonProperty("architecture")]
        public string Architecture { get; set; } = "amd64";
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = "";
        [JsonProperty("pointers")]
        public DynamicSymbolPointerModel[] Pointers { get; set; } = [];
    }
}
