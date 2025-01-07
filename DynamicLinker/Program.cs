﻿using AsmResolver;
using AsmResolver.IO;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using DynamicLinker.Common;
using DynamicLinker.Descriptors;
using DynamicLinker.Descriptors.Resolved;
using DynamicLinker.Models;
using DynamicLinker.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;

namespace DynamicLinker {
    public class Program {
        #region Statics and Constants
        public const string CreatedBy = "rydev";
        public const string License = "GPLv3";

        public const string ImportDescriptorSection = ".idnew";
        public const string DynamicImportDescriptorSection = ".didata";

        public const string DynamicLinkDescriptorSection = ".dlnkdt";
        public const string SymbolIndexerSection = ".symidx";
        public const string SymbolDescriptorSection = ".symsdt";
        public const string SymbolNameTableSection = ".symsnt";
        public const string SymbolDemangledNameTableSection = ".symsdn";
        public const string SymbolPointerSignatureTableSection = ".sympst";
        public const string DynamicLoaderSection = ".dlnkldr";
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file", Justification = "I'm validating it.")]
        public static string FilenameWithoutExtension => string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location) ? "dynalinker" : Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
        #endregion
        #region Singleton
        private static Program? instance;
        public static Program Instance {
            get {
                return instance ??= new Program();
            }
        }

        static int Main(string[] args) {
            return Instance.Start(args).GetAwaiter().GetResult();
        }
        #endregion

        private async Task<string?> GenerateExportDefinition(string targetOutput, string targetArch, DynamicImportModel model, Semver targetVersion) {
            string def = "";
            def += $"LIBRARY \"{model.Target}\"\n";
            def += "EXPORTS\n";
            Log.Trace("Generating export definition...");
            Log.Trace($"{model.Target}:");
            foreach (var symbol in model.Symbols) {
                if (symbol.Architecture != targetArch || symbol.Version == "ignore" || !Semver.IsValidVersion(symbol.Version) || new Semver(symbol.Version) != targetVersion)
                    continue;
                Log.Trace($"  {symbol.Symbol}");
                def += $"    {symbol.Symbol}\n";
            }
            Directory.CreateDirectory(targetOutput);
            string defFilePath = Path.Combine(targetOutput, $"{Path.GetFileNameWithoutExtension(model.Target)}.def");
            await File.WriteAllTextAsync(defFilePath, def);
            return defFilePath;
        }
        private async Task<string?> GenerateLinkLibrary(string defFilePath, string arch = "x64") {
            if (VC.GetToolsPath(arch) is not string vcToolsPath || string.IsNullOrEmpty(defFilePath) || !File.Exists(defFilePath))
                return null;
            Log.Trace("Visual C++ tools path: " + vcToolsPath);

            string libFilePath = Path.ChangeExtension(defFilePath, ".lib");
            string arguments = $"/def:\"{defFilePath}\" /machine:{arch} /out:\"{libFilePath}\"";
            bool success = await VC.LibAsync(arguments, arch);
            return success ? libFilePath : null;
        }
        private void RemoveAllSections(PEFile portableExecutable) {
            if (portableExecutable.Sections.Any(x => x.Name == ImportDescriptorSection)) {
                Log.Trace("Setting default Import Directory Table...");
                var importDescriptorSection = portableExecutable.Sections.First(x => x.Name == ImportDescriptorSection);
                var reader = portableExecutable.CreateReaderAtRva(importDescriptorSection.Rva);
                var oldImportDirectoryRVA = reader.ReadUInt32();
                var oldImportDirectorySize = reader.ReadUInt32();
                portableExecutable.OptionalHeader.SetDataDirectory(DataDirectoryIndex.ImportDirectory, new DataDirectory(oldImportDirectoryRVA, oldImportDirectorySize));
                portableExecutable.Sections.Remove(importDescriptorSection);
            }
            if (portableExecutable.Sections.Any(x => x.Name == DynamicImportDescriptorSection)) {
                Log.Trace("Removing the Dynamic Import Descriptor Table...");
                var dynamicImportDescriptorSection = portableExecutable.Sections.First(x => x.Name == DynamicImportDescriptorSection);
                portableExecutable.Sections.Remove(dynamicImportDescriptorSection);
            }
            if (portableExecutable.Sections.Any(x => x.Name == DynamicLinkDescriptorSection)) {
                Log.Trace("Removing the Dynamic Link Descriptor Table...");
                var dynamicLinkDescriptorSection = portableExecutable.Sections.First(x => x.Name == DynamicLinkDescriptorSection);
                portableExecutable.Sections.Remove(dynamicLinkDescriptorSection);
            }
            if (portableExecutable.Sections.Any(x => x.Name == SymbolDescriptorSection)) {
                Log.Trace("Removing the Symbol Descriptor Table...");
                var symbolDescriptorSection = portableExecutable.Sections.First(x => x.Name == SymbolDescriptorSection);
                portableExecutable.Sections.Remove(symbolDescriptorSection);
            }
            if (portableExecutable.Sections.Any(x => x.Name == SymbolNameTableSection)) {
                Log.Trace("Removing the Symbol Name Table...");
                var symbolNameSection = portableExecutable.Sections.First(x => x.Name == SymbolNameTableSection);
                portableExecutable.Sections.Remove(symbolNameSection);
            }
            if (portableExecutable.Sections.Any(x => x.Name == SymbolPointerSignatureTableSection)) {
                Log.Trace("Removing the Symbol Pointer Signature Table...");
                var symbolPointerSignatureSection = portableExecutable.Sections.First(x => x.Name == SymbolPointerSignatureTableSection);
                portableExecutable.Sections.Remove(symbolPointerSignatureSection);
            }
        }

        class Arguments {
            public bool Help { get; set; } = false;
            public bool Info { get; set; } = false;
            public string? Version { get; set; }
            public string? Input { get; set; }
            public string Output { get; set; } = Directory.GetCurrentDirectory();
            public string Architecture { get; set; } = ArchitectureUtils.GetArchitecture();
            public string? Module { get; set; }
            public bool GenerateLib { get; set; }
            public bool ModulePatch { get; set; }
            public bool ModuleRestore { get; set; }

            public bool Empty() {
                Type type = GetType();
                var other = new Arguments();
                foreach (var property in type.GetProperties()) {
                    if (property.GetValue(this, null)?.Equals(property.GetValue(other, null)) == false)
                        return false;
                }
                return true;
            }
        }

        private async Task<int> Start(string[] _arguments) {
            var arguments = ArgumentParser.ParseInto<Arguments>(_arguments);
            if (arguments.Help || arguments.Empty()) {
                Log.Trace($"Usage: {FilenameWithoutExtension} [options]");
                Log.Trace("Argument Syntax:");
                Log.Trace("  '<>'     | Required argument");
                Log.Trace("  '[]'     | Optional argument");
                Log.Write(null);
                Log.Trace("Types:");
                Log.Trace("  'string'           | A sequence of characters inside quotes");
                Log.Trace("  'enum Arch'        | An enumeration with valid values: x64 or x86");
                Log.Trace("  'class Semver'     | A version in the format of x[.x.x.x] with wildcard support ('*')");
                Log.Write(null);
                Log.Trace("Options:");
                Log.Trace("  /Help                          | Show this help message");
                Log.Trace("  /Info                          | Show the informations about DynamicLinker");
                Log.Trace("  /Version=(<class Semver>)      | Specify the target version to search for symbols");
                Log.Trace("  /Input=(<string>)              | Specify the input file to process");
                Log.Trace("  /Output=([<string>])           | Specify the output directory (default: current directory)");
                Log.Trace("  /Architecture=([<enum Arch>])  | Specify the architecture target (default: machine architecture)");
                Log.Trace("  /Module=(<string>)             | Specify the module to work on");
                Log.Trace("  /GenerateLib                   | Generate library from the input file");
                Log.Trace("  /ModulePatch                   | Patch a PE module to support dynamic/runtime linking");
                Log.Trace("  /ModuleRestore                 | Removes all patches applied to a PE module");
                return 0;
            }

            if (arguments.Info) {
                Log.Trace($"DynamicLinker v{Version}");
                Log.Trace($"DynamicLinker was created by {CreatedBy}");
                Log.Trace($"DynamicLinker is licensed under {License}");
            }

            if (arguments.Input is not string inputFile || string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile)) {
                Log.Error("Invalid or no input file specified.");
                return -1;
            }
            Log.Trace($"Input file: {inputFile}");

            string? outputDirectory = arguments.Output ?? "";
            if (string.IsNullOrWhiteSpace(outputDirectory) || Path.GetInvalidPathChars().Any(outputDirectory.Contains)) {
                Log.Error("Invalid output directory.");
                return -1;
            }
            Log.Trace($"Output directory: {outputDirectory}");

            if (arguments.Version is not string stringVersion || !Semver.IsValidVersion(stringVersion)) {
                Log.Error("Invalid version specified. Use -help to see the valid options.");
                return -1;
            }
            Semver version = new(stringVersion);
            Log.Trace("Target version: " + version);

            if (VC.GetToolsVersion() is not string vcToolsVersion) {
                Log.Error("Visual C++ tools not found.");
                return -1;
            }
            Log.Trace("Visual C++ tools version: " + vcToolsVersion);

            DynamicImportModel? model = DynamicImportModel.Parse(await File.ReadAllTextAsync(inputFile));
            if (model is null) {
                Log.Error("Failed to parse the input file.");
                return -1;
            }

            switch (arguments) {
                case { GenerateLib: true }: {
                    Log.Trace($"Architecture: {arguments.Architecture}");
                    string? export = await GenerateExportDefinition(outputDirectory, arguments.Architecture, model!, version);
                    if (string.IsNullOrWhiteSpace(export)) {
                        Log.Error($"Failed to generate export definition for {model!.Target}");
                        return -1;
                    }
                    string? lib = await GenerateLinkLibrary(export, arguments.Architecture);
                    if (string.IsNullOrWhiteSpace(lib)) {
                        Log.Error($"Failed to generate library for {model!.Target}");
                        return -1;
                    }
                    Log.Trace($"Library generated at: {lib}");
                    return 0;
                }
                case { ModulePatch: true } or { ModuleRestore: true }: {
                    if (arguments.Module is not string module || !File.Exists(module)) {
                        Log.Error("Invalid module specified.");
                        return -1;
                    }
                    Log.Trace($"Target PE: {module}");
                    string outputPortableExecutable = Path.Combine(outputDirectory, Path.GetFileName(module));
                    var portableExecutable = PEFile.FromFile(module);
                    if (portableExecutable is null) {
                        Log.Error("Failed to parse the specified PE file.");
                        return -1;
                    }

                    try {
                        if (arguments.ModuleRestore is true)
                            RemoveAllSections(portableExecutable);

                        if (arguments.ModulePatch) {
                            var importDirectory = portableExecutable.OptionalHeader.GetDataDirectory(DataDirectoryIndex.ImportDirectory);
                            if (!importDirectory.IsPresentInPE) {
                                Log.Error("The specified PE file does not have an import directory.");
                                return -1;
                            }

                            string arch = portableExecutable.OptionalHeader.Magic switch {
                                OptionalHeaderMagic.PE32 => "x86",
                                OptionalHeaderMagic.PE32Plus => "x64",
                                _ => "Unknown"
                            };

                            var filteredSymbols = model.Symbols.Where(x => {
                                return x.Architecture == arch && new Semver(x.Version) == version;
                            }).ToList();

                            // Parse the symbols and create/modify the link sections
                            {
                                List<ResolvedDynamicLinkDescriptor> linkDescriptors = [];
                                if (portableExecutable.Sections.Any(x => x.Name == DynamicLinkDescriptorSection) &&
                                    portableExecutable.Sections.Any(x => x.Name == SymbolIndexerSection) &&
                                    portableExecutable.Sections.Any(x => x.Name == SymbolDescriptorSection) &&
                                    portableExecutable.Sections.Any(x => x.Name == SymbolNameTableSection) &&
                                    portableExecutable.Sections.Any(x => x.Name == SymbolDemangledNameTableSection) &&
                                    portableExecutable.Sections.Any(x => x.Name == SymbolPointerSignatureTableSection)) {
                                    var linkDescriptorSection = portableExecutable.Sections.First(x => x.Name == DynamicLinkDescriptorSection);
                                    var linkDescriptorReader = portableExecutable.CreateReaderAtRva(linkDescriptorSection.Rva);
                                    var symbolIndexerSection = portableExecutable.Sections.First(x => x.Name == SymbolIndexerSection);
                                    var symbolIndexerReader = portableExecutable.CreateReaderAtRva(symbolIndexerSection.Rva);
                                    var symbolDescriptorSection = portableExecutable.Sections.First(x => x.Name == SymbolDescriptorSection);
                                    var symbolDescriptorReader = portableExecutable.CreateReaderAtRva(symbolDescriptorSection.Rva);
                                    var symbolNameSection = portableExecutable.Sections.First(x => x.Name == SymbolNameTableSection);
                                    var symbolNameReader = portableExecutable.CreateReaderAtRva(symbolNameSection.Rva);
                                    var symbolDemangledNameSection = portableExecutable.Sections.First(x => x.Name == SymbolDemangledNameTableSection);
                                    var symbolDemangledNameReader = portableExecutable.CreateReaderAtRva(symbolDemangledNameSection.Rva);
                                    var symbolPointerSignatureSection = portableExecutable.Sections.First(x => x.Name == SymbolPointerSignatureTableSection);
                                    var symbolPointerSignatureReader = portableExecutable.CreateReaderAtRva(symbolPointerSignatureSection.Rva);

                                    var existingSymbolPointerSignatureCount = symbolPointerSignatureReader.ReadUInt32();
                                    var symbolPointerSignatures = new Dictionary<uint, string>();
                                    for (uint i = 0; i < existingSymbolPointerSignatureCount; i++) {
                                        var signature = symbolPointerSignatureReader.ReadUtf8String();
                                        symbolPointerSignatures.Add(i + 1, signature);
                                    }

                                    var existingSymbolNameCount = symbolNameReader.ReadUInt32();
                                    var symbolNames = new Dictionary<uint, string>();
                                    for (uint i = 0; i < existingSymbolNameCount; i++) {
                                        var name = symbolNameReader.ReadUtf8String();
                                        symbolNames.Add(i + 1, name);
                                    }

                                    var existingSymbolDemangledNameCount = symbolDemangledNameReader.ReadUInt32();
                                    var demangledSymbolNames = new Dictionary<uint, string>();
                                    for (uint i = 0; i < existingSymbolDemangledNameCount; i++) {
                                        var name = symbolDemangledNameReader.ReadUtf8String();
                                        demangledSymbolNames.Add(i + 1, name);
                                    }

                                    var existingSymbolDescriptorCount = symbolDescriptorReader.ReadUInt32();
                                    var existingLinkDescriptorCount = linkDescriptorReader.ReadUInt32();
                                    var currentLinkDescriptorNameOffset = (existingLinkDescriptorCount * DynamicImportDescriptor.Size) + sizeof(uint);
                                    for (uint i = 0; i < existingLinkDescriptorCount; i++) {
                                        var descriptor = DynamicImportDescriptor.FromReader(ref linkDescriptorReader)!;
                                        var lastLinkDescriptorReaderPosition = linkDescriptorReader.RelativeOffset;
                                        linkDescriptorReader.RelativeOffset = currentLinkDescriptorNameOffset;
                                        var targetName = linkDescriptorReader.ReadUtf8String();
                                        linkDescriptorReader.RelativeOffset = lastLinkDescriptorReaderPosition;
                                        currentLinkDescriptorNameOffset += (uint)targetName.Length + 1;
                                        var resolvedDescriptor = new ResolvedDynamicLinkDescriptor() {
                                            TargetName = targetName,
                                            ResolvedSymbols = []
                                        };

                                        symbolIndexerReader.RelativeOffset = descriptor.SymbolIndexerOffset - 1;
                                        var symbolCount = symbolIndexerReader.ReadUInt32();
                                        for (uint si = 0; si < symbolCount; si++) {
                                            var symbolDescriptorIndex = symbolIndexerReader.ReadUInt32();
                                            symbolDescriptorReader.RelativeOffset = (symbolDescriptorIndex - 1) * DynamicSymbolDescriptor.Size + sizeof(uint);
                                            var symbolDescriptor = DynamicSymbolDescriptor.FromReader(ref symbolDescriptorReader)!;
                                            var resolvedSymbol = new ResolvedDynamicSymbolDescriptor() {
                                                Name = symbolNames[symbolDescriptor.SymbolIndex],
                                                DemangledName = demangledSymbolNames[symbolDescriptor.SymbolIndex],
                                                Signature = symbolDescriptor.SignatureIndex == 0 ? null : symbolPointerSignatures[symbolDescriptor.SignatureIndex],
                                                Address = symbolDescriptor.AddressOffset == 0 ? null : symbolDescriptor.AddressOffset
                                            };
                                            resolvedDescriptor.ResolvedSymbols.Add(resolvedSymbol);
                                        }

                                        linkDescriptors.Add(resolvedDescriptor);
                                    }

                                    portableExecutable.Sections.Remove(linkDescriptorSection);
                                    Log.Warn($"Removing ({linkDescriptorSection.Name}) section...");
                                    portableExecutable.Sections.Remove(symbolIndexerSection);
                                    Log.Warn($"Removing ({symbolIndexerSection.Name}) section...");
                                    portableExecutable.Sections.Remove(symbolDescriptorSection);
                                    Log.Warn($"Removing ({symbolDescriptorSection.Name}) section...");
                                    portableExecutable.Sections.Remove(symbolNameSection);
                                    Log.Warn($"Removing ({symbolNameSection.Name}) section...");
                                    portableExecutable.Sections.Remove(symbolDemangledNameSection);
                                    Log.Warn($"Removing ({symbolDemangledNameSection.Name}) section...");
                                    portableExecutable.Sections.Remove(symbolPointerSignatureSection);
                                    Log.Warn($"Removing ({symbolPointerSignatureSection.Name}) section...");
                                }

                                var linkDescriptorTableDataStream = new MemoryStream();
                                var linkDescriptorTableDataWriter = new BinaryStreamWriter(linkDescriptorTableDataStream);
                                linkDescriptorTableDataWriter.WriteUInt32(0u);

                                var symbolIndexerTableDataStream = new MemoryStream();
                                var symbolIndexerTableDataWriter = new BinaryStreamWriter(symbolIndexerTableDataStream);

                                var symbolDescriptorTableDataStream = new MemoryStream();
                                var symbolDescriptorTableDataWriter = new BinaryStreamWriter(symbolDescriptorTableDataStream);
                                symbolDescriptorTableDataWriter.WriteUInt32(0u);

                                var symbolNameTableDataStream = new MemoryStream();
                                var symbolNameTableDataWriter = new BinaryStreamWriter(symbolNameTableDataStream);
                                symbolNameTableDataWriter.WriteUInt32(0u);

                                var symbolDemangledNameTableDataStream = new MemoryStream();
                                var symbolDemangledNameTableDataWriter = new BinaryStreamWriter(symbolDemangledNameTableDataStream);
                                symbolDemangledNameTableDataWriter.WriteUInt32(0u);

                                var symbolPointerSignatureTableDataStream = new MemoryStream();
                                var symbolPointerSignatureTableDataWriter = new BinaryStreamWriter(symbolPointerSignatureTableDataStream);
                                symbolPointerSignatureTableDataWriter.WriteUInt32(0u);

                                linkDescriptors.Add(new() {
                                    TargetName = model.Target,
                                    ResolvedSymbols = filteredSymbols.Select(symbol => {
                                        bool isSignature = symbol.GetPointerType() == PointerType.Signature;
                                        return new ResolvedDynamicSymbolDescriptor() {
                                            Name = symbol.Symbol,
                                            Signature = isSignature ? symbol.Pointer : null,
                                            Address = isSignature ? null : Convert.ToUInt64(symbol.Pointer.StartsWith("0x") ? symbol.Pointer[2..] : symbol.Pointer, 16)
                                        };
                                    }).ToList()
                                });

                                foreach (var descriptor in linkDescriptors)
                                    descriptor.ResolvedSymbols = descriptor.ResolvedSymbols.Distinct().ToList();
                                linkDescriptors = linkDescriptors.Distinct().ToList();

                                Log.Trace($"Creating link descriptors on the binary...");
                                var linkDescriptorCount = 0u;
                                var symbolDescriptorCount = 0u;
                                var symbolNameCount = 0u;
                                var symbolPointerSignatureCount = 0u;
                                var byteSizeOfLinkDescriptors = (uint)(linkDescriptors.Count * DynamicImportDescriptor.Size);
                                var linkDescriptorNamesOffset = byteSizeOfLinkDescriptors + sizeof(uint);
                                var currentLinkDescriptorIndex = 0u;
                                foreach (var linkDescriptor in linkDescriptors) {
                                    Log.Trace($"  Link Descriptor Name: {linkDescriptor.TargetName}");
                                    var localSymbolDescriptorCount = 0u;
                                    var currentIndexerPosition = symbolIndexerTableDataWriter.Offset;
                                    symbolIndexerTableDataWriter.WriteUInt32(0u);
                                    foreach (var symbol in linkDescriptor.ResolvedSymbols) {
                                        Log.Trace($"    Symbol Name: {symbol.Name}");
                                        bool isSignature = symbol.Signature != null;
                                        if (isSignature)
                                            Log.Trace($"    Symbol Signature: {symbol.Signature}");
                                        else
                                            Log.Trace($"    Symbol Address: 0x{symbol.Address:x}");
                                        symbolNameTableDataWriter.WriteAsciiString(symbol.Name + '\0');
                                        symbolDemangledNameTableDataWriter.WriteAsciiString(await VC.UndnameAsync(symbol.Name, arch) + '\0');
                                        var currentSymbolNameIndex = ++symbolNameCount;
                                        var currentSignatureIndex = 0u;
                                        if (isSignature) {
                                            symbolPointerSignatureTableDataWriter.WriteAsciiString(symbol.Signature! + '\0');
                                            currentSignatureIndex = ++symbolPointerSignatureCount;
                                        }

                                        DynamicSymbolDescriptor symDesc = new() {
                                            SymbolIndex = currentSymbolNameIndex,
                                            SignatureIndex = currentSignatureIndex,
                                            AddressOffset = isSignature ? 0 : symbol.Address ?? 0
                                        };

                                        var currentSymbolDescriptorIndex = (uint)((symbolDescriptorTableDataWriter.Offset - sizeof(uint)) / DynamicSymbolDescriptor.Size) + 1;
                                        symDesc.ToWriter(symbolDescriptorTableDataWriter);
                                        symbolDescriptorCount++;
                                        localSymbolDescriptorCount++;
                                        symbolIndexerTableDataWriter.WriteUInt32(currentSymbolDescriptorIndex);
                                    }
                                    var lastSymbolIndexerPosition = symbolIndexerTableDataWriter.Offset;
                                    symbolIndexerTableDataWriter.Offset = currentIndexerPosition;
                                    symbolIndexerTableDataWriter.WriteUInt32(localSymbolDescriptorCount);
                                    symbolIndexerTableDataWriter.Offset = lastSymbolIndexerPosition;

                                    DynamicImportDescriptor linkDesc = new() {
                                        TargetNameRVA = 0,
                                        SymbolIndexerOffset = (uint)currentIndexerPosition + 1
                                    };

                                    var lastLinkDescriptorPosition = linkDescriptorTableDataWriter.Offset;
                                    linkDescriptorTableDataWriter.Offset = linkDescriptorNamesOffset;
                                    linkDescriptorTableDataWriter.WriteAsciiString(linkDescriptor.TargetName + '\0');
                                    linkDescriptorTableDataWriter.Offset = lastLinkDescriptorPosition;
                                    linkDesc.TargetNameRVA = linkDescriptorNamesOffset;
                                    linkDescriptorNamesOffset += (uint)linkDescriptor.TargetName.Length + 1;
                                    linkDesc.ToWriter(linkDescriptorTableDataWriter);
                                    currentLinkDescriptorIndex++;
                                    linkDescriptorCount++;
                                }

                                linkDescriptorTableDataWriter.Offset = 0;
                                linkDescriptorTableDataWriter.WriteUInt32(linkDescriptorCount);
                                symbolDescriptorTableDataWriter.Offset = 0;
                                symbolDescriptorTableDataWriter.WriteUInt32(symbolDescriptorCount);
                                symbolNameTableDataWriter.Offset = 0;
                                symbolNameTableDataWriter.WriteUInt32(symbolNameCount);
                                symbolDemangledNameTableDataWriter.Offset = 0;
                                symbolDemangledNameTableDataWriter.WriteUInt32(symbolNameCount);
                                symbolPointerSignatureTableDataWriter.Offset = 0;
                                symbolPointerSignatureTableDataWriter.WriteUInt32(symbolPointerSignatureCount);

                                var linkDescriptorTableSegment = new DataSegment(linkDescriptorTableDataStream.ToArray());
                                var linkDescriptorTableSection = new PESection(DynamicLinkDescriptorSection, SectionFlags.MemoryRead) {
                                    Contents = linkDescriptorTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({DynamicLinkDescriptorSection}) section...");
                                portableExecutable.Sections.Add(linkDescriptorTableSection);

                                var symbolIndexerTableSegment = new DataSegment(symbolIndexerTableDataStream.ToArray());
                                var symbolIndexerTableSection = new PESection(SymbolIndexerSection, SectionFlags.MemoryRead) {
                                    Contents = symbolIndexerTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({SymbolIndexerSection}) section...");
                                portableExecutable.Sections.Add(symbolIndexerTableSection);

                                var symbolDescriptorTableSegment = new DataSegment(symbolDescriptorTableDataStream.ToArray());
                                var symbolDescriptorTableSection = new PESection(SymbolDescriptorSection, SectionFlags.MemoryRead) {
                                    Contents = symbolDescriptorTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({SymbolDescriptorSection}) section...");
                                portableExecutable.Sections.Add(symbolDescriptorTableSection);

                                var symbolNameTableSegment = new DataSegment(symbolNameTableDataStream.ToArray());
                                var symbolNameTableSection = new PESection(SymbolNameTableSection, SectionFlags.MemoryRead) {
                                    Contents = symbolNameTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({SymbolNameTableSection}) section...");
                                portableExecutable.Sections.Add(symbolNameTableSection);

                                var symbolDemangledNameTableSegment = new DataSegment(symbolDemangledNameTableDataStream.ToArray());
                                var symbolDemangledNameTableSection = new PESection(SymbolDemangledNameTableSection, SectionFlags.MemoryRead) {
                                    Contents = symbolDemangledNameTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({SymbolDemangledNameTableSection}) section...");
                                portableExecutable.Sections.Add(symbolDemangledNameTableSection);

                                var symbolPointerSignatureTableSegment = new DataSegment(symbolPointerSignatureTableDataStream.ToArray());
                                var symbolPointerSignatureTableSection = new PESection(SymbolPointerSignatureTableSection, SectionFlags.MemoryRead) {
                                    Contents = symbolPointerSignatureTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({SymbolPointerSignatureTableSection}) section...");
                                portableExecutable.Sections.Add(symbolPointerSignatureTableSection);
                            }

                            // Create/modify the import descriptor table and the dynamic import descriptor table
                            {
                                var importDirectoryReader = portableExecutable.CreateReaderAtRva(importDirectory.VirtualAddress);
                                if (portableExecutable.Sections.Any(x => x.Name == ImportDescriptorSection)) {
                                    importDirectoryReader.ReadUInt32(); // Skip the original import directory RVA
                                    importDirectoryReader.ReadUInt32(); // Skip the original import directory size
                                }
                                List<ImportDirectoryEntry> importDirectoryEntries = [.. ImportDirectoryEntry.GetAllFrom(ref importDirectoryReader)];
                                List<ImportDirectoryEntry> dynamicImportDirectoryEntries = [];
                                if (portableExecutable.Sections.Any(x => x.Name == DynamicImportDescriptorSection)) {
                                    var dynamicImportDirectorySection = portableExecutable.Sections.First(x => x.Name == DynamicImportDescriptorSection);
                                    var dynamicImportDirectoryReader = portableExecutable.CreateReaderAtRva(dynamicImportDirectorySection.Rva);
                                    dynamicImportDirectoryEntries = [.. ImportDirectoryEntry.GetAllFrom(ref dynamicImportDirectoryReader)];
                                }

                                Log.Trace("Creating import descriptors on the binary...");
                                var dynamicImportDescriptor = importDirectoryEntries.FirstOrDefault(x => {
                                    return portableExecutable.CreateReaderAtRva(x.Name).ReadAsciiString() == model.Target;
                                });

                                if (dynamicImportDescriptor is null) {
                                    Log.Warn("Failed to find the specified target in the import directory.");
                                    AlignAndFinish();
                                    return 0;
                                }

                                if (portableExecutable.Sections.Any(x => x.Name == DynamicImportDescriptorSection))
                                    portableExecutable.Sections.Remove(portableExecutable.Sections.First(x => x.Name == DynamicImportDescriptorSection));

                                dynamicImportDirectoryEntries.Add(dynamicImportDescriptor);
                                importDirectoryEntries = importDirectoryEntries.Where(x => x.Name != dynamicImportDescriptor.Name).ToList();
                                dynamicImportDirectoryEntries = dynamicImportDirectoryEntries.Distinct().ToList();

                                importDirectoryEntries.Add(0u);
                                dynamicImportDirectoryEntries.Add(0u);

                                var importDirectoryTableDataStream = new MemoryStream();
                                var importDirectoryTableDataWriter = new BinaryStreamWriter(importDirectoryTableDataStream);
                                if (portableExecutable.Sections.Any(x => x.Name == ImportDescriptorSection)) {
                                    var existingImportDescriptorSection = portableExecutable.Sections.First(x => x.Name == ImportDescriptorSection);
                                    var existingImportDescriptorReader = portableExecutable.CreateReaderAtRva(existingImportDescriptorSection.Rva);
                                    importDirectoryTableDataWriter.WriteUInt32(existingImportDescriptorReader.ReadUInt32());
                                    importDirectoryTableDataWriter.WriteUInt32(existingImportDescriptorReader.ReadUInt32());
                                }
                                else {
                                    importDirectoryTableDataWriter.WriteUInt32(importDirectory.VirtualAddress);
                                    importDirectoryTableDataWriter.WriteUInt32(importDirectory.Size);
                                }
                                foreach (var entry in importDirectoryEntries)
                                    entry.WriteTo(importDirectoryTableDataWriter);

                                var dynamicImportDirectoryTableDataStream = new MemoryStream();
                                var dynamicImportDirectoryTableDataWriter = new BinaryStreamWriter(dynamicImportDirectoryTableDataStream);
                                foreach (var entry in dynamicImportDirectoryEntries)
                                    entry.WriteTo(dynamicImportDirectoryTableDataWriter);

                                if (portableExecutable.Sections.Any(x => x.Name == ImportDescriptorSection)) {
                                    var importDescriptorSection = portableExecutable.Sections.First(x => x.Name == ImportDescriptorSection);
                                    portableExecutable.Sections.Remove(importDescriptorSection);
                                    Log.Warn($"Removing ({ImportDescriptorSection}) section...");
                                }

                                var importDirectoryTableSegment = new DataSegment(importDirectoryTableDataStream.ToArray());
                                var importDirectoryTableSection = new PESection(ImportDescriptorSection, SectionFlags.MemoryRead) {
                                    Contents = importDirectoryTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({ImportDescriptorSection}) section...");
                                portableExecutable.Sections.Add(importDirectoryTableSection);

                                var dynamicImportDirectoryTableSegment = new DataSegment(dynamicImportDirectoryTableDataStream.ToArray());
                                var dynamicImportDirectoryTableSection = new PESection(DynamicImportDescriptorSection, SectionFlags.MemoryRead) {
                                    Contents = dynamicImportDirectoryTableSegment,
                                    Characteristics = SectionFlags.ContentInitializedData | SectionFlags.MemoryRead
                                };
                                Log.Trace($"Creating ({DynamicImportDescriptorSection}) section...");
                                portableExecutable.Sections.Add(dynamicImportDirectoryTableSection);
                                portableExecutable.AlignSections();
                                portableExecutable.OptionalHeader.SetDataDirectory(DataDirectoryIndex.ImportDirectory, new DataDirectory(importDirectoryTableSection.Rva + sizeof(uint) * 2, (uint)dynamicImportDirectoryTableSegment.Data.Length));
                            }

                            void AlignAndFinish() {
                                portableExecutable.AlignSections();
                                Directory.CreateDirectory(Path.GetDirectoryName(outputPortableExecutable)!);
                                portableExecutable.Write(outputPortableExecutable);
                            }
                            AlignAndFinish();
                        }
                    }
                    catch (BadImageFormatException) {
                        Log.Error("The specified 'Module' file was not a PE file.");
                        return -1;
                    }
                    return 0;
                }
            }
            Log.Trace("No option selected.");
            return 0;
        }
    }
}