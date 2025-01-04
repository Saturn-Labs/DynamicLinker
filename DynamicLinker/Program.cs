using AsmResolver;
using AsmResolver.IO;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using DynamicLinker.Common;
using DynamicLinker.Descriptors;
using DynamicLinker.Models;
using DynamicLinker.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace DynamicLinker {
    public class Program {
        #region Statics and Constants
        public const string CreatedBy = "rydev";
        public const string License = "GPLv3";

        public const string ImportDescriptorSection = ".idnew";
        public const string DynamicImportDescriptorSection = ".didata";
        public const string EntryRedirectSection = ".entnew";
        public const string DynamicLinkDescriptorSection = ".dlnkdt";
        public const string SymbolDescriptorSection = ".symsdt";
        public const string SymbolNameTableSection = ".symsnt";
        public const string SymbolPointerSignatureTableSection = ".sympst";

        public readonly static byte[] RedirectionBytes64 = [
            0x48, 0xC7, 0xC0, 0x01, 0x00, 0x00, 0x00,   // mov rax, 1
            0xC3                                        // ret
        ];
        public readonly static byte[] RedirectionBytes32 = [
            0xB8, 0x01, 0x00, 0x00, 0x00,               // mov eax, 1
            0xC3                                        // ret
        ];
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
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
            if (portableExecutable.Sections.Any(x => x.Name == EntryRedirectSection)) {
                Log.Trace("Removing the Entry Point redirection...");
                var entryRedirectSection = portableExecutable.Sections.First(x => x.Name == EntryRedirectSection);
                var reader = portableExecutable.CreateReaderAtRva(entryRedirectSection.Rva);
                if (reader.ReadUInt32() == 1) {
                    portableExecutable.OptionalHeader.AddressOfEntryPoint = reader.ReadUInt32();
                    portableExecutable.Sections.Remove(entryRedirectSection);
                }
            }
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

        private async Task<int> Start(string[] _arguments) {
            var arguments = new ArgumentParser(_arguments);
            if (arguments.Has("help") || arguments.Empty()) {
                Log.Trace($"Usage: {FilenameWithoutExtension} [options]");
                Log.Trace("Argument Syntax:");
                Log.Trace("  <arg> | Required argument");
                Log.Trace("  [<arg>] | Optional argument");
                Log.Trace("  <arg...> | List of arguments enclosed in '[]' and separated by ','");
                Log.Trace("Options:");
                Log.Trace("  -help | Show this help message");
                Log.Trace("  -info | Show the informations about DynamicLinker");
                Log.Trace("  -version [<x[.x.x.x]>] | Target version to search for symbols");
                Log.Trace("  -input <file> | The input file to process");
                Log.Trace("  -output [<directory>] | The output directory to save the processed file, default is current working directory");
                Log.Trace("  -generatelib [<architecture (x64 or x86)>] | Generate library from the input file");
                Log.Trace("  -modulepatch <module> [-entryredirect] [-eraseold] | Parse PE file to support dynamic/runtime linking, and optionally redirect the Entry Point to support late DllMain call (This will essentially break the module for loaders other than dynalnk)");
                Log.Trace("  -moduleunpatch <module> | Removes all the patches did to a PE module");
                return 0;
            }

            if (arguments.Has("info")) {
                Log.Trace($"DynamicLinker v{Version}");
                Log.Trace($"DynamicLinker was created by {CreatedBy}");
                Log.Trace($"DynamicLinker is licensed under {License}");
            }

            string inputFile;
            if (!arguments.Has("input")) {
                Log.Error("No input file specified. Use -help to see the valid options.");
                return -1;
            }
            inputFile = arguments.Get("input")!;
            if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile)) {
                Log.Error("Invalid input file specified.");
                return -1;
            }
            Log.Trace($"Input file: {inputFile}");

            string outputDirectory = arguments.Get("output", Directory.GetCurrentDirectory())!;
            if (string.IsNullOrWhiteSpace(outputDirectory) || Path.GetInvalidPathChars().Any(outputDirectory.Contains)) {
                Log.Error("Invalid output directory.");
                return -1;
            }
            Log.Trace($"Output directory: {outputDirectory}");

            if (!arguments.Has("version") || !Semver.IsValidVersion(arguments.Get("version")!)) {
                Log.Error("Invalid version specified. Use -help to see the valid options.");
                return -1;
            }
            Log.Trace("Target version: " + arguments.Get("version")!);
            Semver version = new(arguments.Get("version")!);

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

            bool didWork = false;
            if (arguments.Has("generatelib")) {
                string arch = arguments.Get("generatelib") is "true" ? ArchitectureUtils.GetArchitecture() : arguments.Get("generatelib")!;
                Log.Trace($"Architecture: {arch}");

                string? export = await GenerateExportDefinition(outputDirectory, arch, model!, version);
                if (string.IsNullOrWhiteSpace(export)) {
                    Log.Error($"Failed to generate export definition for {model!.Target}");
                    return -1;
                }

                string? lib = await GenerateLinkLibrary(export, arch);
                if (string.IsNullOrWhiteSpace(lib)) {
                    Log.Error($"Failed to generate library for {model!.Target}");
                    return -1;
                }

                Log.Trace($"Generated library for {model!.Target} at {lib}");
                didWork = true;
            }

            if (arguments.Has("moduleunpatch")) {
                string inputPortableExecutable = arguments.Get("moduleunpatch")!;
                if (string.IsNullOrWhiteSpace(inputPortableExecutable) || !File.Exists(inputPortableExecutable)) {
                    Log.Error("Invalid specified PE filepath.");
                    return -1;
                }
                Log.Trace($"Target PE: {inputPortableExecutable}");
                try {
                    var portableExecutable = PEFile.FromFile(inputPortableExecutable);
                    if (portableExecutable is null) {
                        Log.Error("Failed to parse the specified PE file.");
                        return -1;
                    }
                    
                }
                catch (BadImageFormatException) {
                    Log.Error($"The specified 'moduleunpatch' file was not a PE file.");
                    return -1;
                }
            }

            if (arguments.Has("modulepatch")) {
                string inputPortableExecutable = arguments.Get("modulepatch")!;
                if (string.IsNullOrWhiteSpace(inputPortableExecutable) || !File.Exists(inputPortableExecutable)) {
                    Log.Error("Invalid specified PE filepath.");
                    return -1;
                }
                Log.Trace($"Target PE: {inputPortableExecutable}");

                try {
                    var portableExecutable = PEFile.FromFile(inputPortableExecutable);
                    if (portableExecutable is null) {
                        Log.Error("Failed to parse the specified PE file.");
                        return -1;
                    }
                    string outputPortableExecutable = Path.Combine(outputDirectory, Path.GetFileName(inputPortableExecutable));
                    Log.Trace("Starting mod process for dynamic linking...");
                    if (arguments.Has("entryredirect")) {
                        if (portableExecutable.Sections.Any(x => x.Name == EntryRedirectSection)) {
                            Log.Warn("The specified PE file already has a DllMain redirect section, no need to redirect it again.");
                        }
                        else {
                            Log.Trace("Redirecting the Entry Point to support late DllMain call...");
                            var originalEntryPoint = portableExecutable.OptionalHeader!.AddressOfEntryPoint;
                            Log.Trace($"  Original Entry Point RVA: 0x{originalEntryPoint:x}");
                            var data = new DataSegment([
                                ..BitConverter.GetBytes(0x1u),                                                                                          // 4 bytes - off(0x0)
                                ..BitConverter.GetBytes(originalEntryPoint),                                                                            // 4 bytes - off(0x4)
                                ..(portableExecutable.OptionalHeader.Magic == OptionalHeaderMagic.PE32Plus ? RedirectionBytes64 : RedirectionBytes32)   // PE32+ ? (8 bytes - off(0x8)) : (6 bytes - off(0x8))
                            ]);
                            var dllMainRedirectSection = new PESection(
                                EntryRedirectSection,
                                SectionFlags.MemoryRead | SectionFlags.MemoryExecute
                            ) { Contents = data };
                            portableExecutable.Sections.Add(dllMainRedirectSection);
                            portableExecutable.AlignSections();
                            var newEntryPoint = dllMainRedirectSection.Rva + (sizeof(uint) * 2);
                            portableExecutable.OptionalHeader!.AddressOfEntryPoint = newEntryPoint;
                            Log.Trace($"  New Entry Point RVA: 0x{newEntryPoint:x}");
                            didWork = true;
                        }
                    }

                    var importDirectory = portableExecutable.OptionalHeader.GetDataDirectory(DataDirectoryIndex.ImportDirectory);
                    if (!importDirectory.IsPresentInPE) {
                        Log.Error("The specified PE file does not have an import directory.");
                        return -1;
                    }

                     //|| portableExecutable.Sections.Any(x => {
                     //    return x.Name is
                     //        EntryRedirectSection or
                     //        ImportDescriptorSection or
                     //        DynamicImportDescriptorSection or
                     //        DynamicLinkDescriptorSection or
                     //        SymbolDescriptorSection or
                     //        SymbolNameTableSection or
                     //        SymbolPointerSignatureTableSection;
                     //})

                    if (arguments.Has("eraseold")) {
                        RemoveAllSections(portableExecutable);
                        didWork = true;
                    }

                    // Parse the symbols and create .symsnt and .sympst sections
                    List<string> symbolNames = [];
                    List<DynamicSymbolDescriptor> symbolDescriptors = [];
                    if (portableExecutable.Sections.Any(x => x.Name == SymbolDescriptorSection)) {
                        var symbolDescriptorSection = portableExecutable.Sections.First(x => x.Name == SymbolDescriptorSection);
                        var reader = portableExecutable.CreateReaderAtRva(symbolDescriptorSection.Rva);
                        uint count = reader.ReadUInt32();
                        for (uint i = 0; i < count; i++) {
                            symbolDescriptors.Add(DynamicSymbolDescriptor.FromReader(ref reader)!);
                        }
                    }

                    Log.Trace($"Creating Symbol Descriptor Table ({SymbolNameTableSection})...");

                    var symbolDescriptorTableDataStream = new MemoryStream();
                    var symbolDescriptorTableDataWriter = new BinaryStreamWriter(symbolDescriptorTableDataStream);
                    symbolDescriptorTableDataWriter.WriteUInt32((uint)symbolDescriptors.Count);
                    foreach (var symbolName in symbolDescriptors) {
                        symbolDescriptorTableDataWriter.WriteAsciiString(symbolName + '\0');
                    }
                    var symbolNameTableSegment = new DataSegment(symbolNameTableDataStream.ToArray());
                    var symbolNameTableSection = new PESection(SymbolNameTableSection, SectionFlags.MemoryRead | SectionFlags.MemoryWrite) { Contents = symbolNameTableSegment };
                    portableExecutable.Sections.Add(symbolNameTableSection);
                    portableExecutable.AlignSections();

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPortableExecutable)!);
                    portableExecutable.Write(outputPortableExecutable);
                    didWork = true;
                }
                catch (BadImageFormatException) {
                    Log.Error($"The specified 'modulepatch' file was not a PE file.");
                    return -1;
                }
            }

            if (!didWork)
                Log.Warn("Unknown command. Use -help to see the available commands.");
            return 0;
        }
    }
}