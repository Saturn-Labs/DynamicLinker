﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Models {
    public class DynamicImportModel {
        public const string Schema = @"
        {
            ""$id"": ""dynamic-linker-symbol-file"",
            ""$schema"": ""http://json-schema.org/draft-04/schema#"",
            ""type"": ""object"",
            ""properties"": {
                ""target"": {
                    ""type"": ""string""
                },
                ""default_version"": {
                    ""type"": ""string"",
                    ""value"": { ""pattern"": ""^(ignore|(\\d+|\\*)(\\.(\\d+|\\*)){0,3})$"" }
                },
                ""symbols"": {
                    ""type"": ""array"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""version"": {
                                ""type"": ""string"",
                                ""value"": { ""pattern"": ""^(ignore|(\\d+|\\*)(\\.(\\d+|\\*)){0,3})$"" }
                            },
                            ""architecture"": {
                                ""type"": ""string"",
                                ""enum"": [""x64"", ""x86""]
                            },
                            ""symbol"": {
                                ""type"": ""string""
                            },
                            ""pointer"": {
                                ""type"": ""string"",
                                ""pattern"": ""^(0x[0-9a-fA-F]{1,16}|([0-9A-Fa-f]{2}|\\?)(( [0-9A-Fa-f]{2}| \\?))*)$""
                            }
                        },
                        ""required"": [""symbol"", ""pointer""]
                    }
                }
            },
            ""required"": [""target"", ""symbols""]
        }";
        public static JSchema JsonSchema = JSchema.Parse(Schema);

        [JsonProperty("default_version")]
        public string DefaultVersion { get; set; } = "*";
        [JsonProperty("target")]
        public string Target { get; set; } = "";
        [JsonProperty("symbols")]
        public DynamicSymbolModel[] Symbols { get; set; } = [];

        public static DynamicImportModel? Parse(string json) {
            JObject model = JObject.Parse(json);
            bool isValid = model.IsValid(JsonSchema);
            if (!isValid) {
                return null;
            }
            DynamicImportModel? obj = model.ToObject<DynamicImportModel>();
            if (obj == null) {
                return null;
            }

            foreach (var symbol in obj.Symbols) {
                symbol.Version = string.IsNullOrWhiteSpace(symbol.Version) ? obj.DefaultVersion : symbol.Version;
            }
            return obj;
        }
    }
}
