using AsmResolver;
using AsmResolver.IO;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using DynamicLinker.Common;
using DynamicLinker.Models;
using DynamicLinker.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace DynamicLinker
{
    public class Program
    {
        #region Statics and Constants
        public const string CreatedBy = "rydev";
        public const string License = "GPLv3";
        public const string ImportDescriptorSection = ".idtnw";
        public const string DynamicLinkSection = ".dlink";
        public const string DllMainRedirectSection = ".dlmre";
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
                var arch = ArchitectureUtils.IsTechnicalName(symbol.Architecture) ? ArchitectureUtils.ConvertToFriendlyNameArch(symbol.Architecture) : symbol.Architecture;
                if (arch != targetArch || symbol.Version == "ignore" || !Semver.IsValidVersion(symbol.Version) || new Semver(symbol.Version) != targetVersion)
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
            if (VC.GetToolsPath(arch) == null || string.IsNullOrEmpty(defFilePath) || !File.Exists(defFilePath))
                return null;

            if (ArchitectureUtils.IsTechnicalName(arch)) {
                arch = ArchitectureUtils.ConvertToFriendlyNameArch(arch);
            }

            string libFilePath = Path.ChangeExtension(defFilePath, ".lib");
            string arguments = $"/def:\"{defFilePath}\" /machine:{arch} /out:\"{libFilePath}\"";
            bool success = await VC.LibAsync(arguments);
            return success ? libFilePath : null;
        }

        private async Task<int> Start(string[] _arguments) {
            var arguments = new ArgumentParser(_arguments);
            _ = VC.ToolsPath;
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
                Log.Trace("  -machine [<x64,x86_64,x86,i386,amd64>] | The specified architecture, default is this machine architecture");
                Log.Trace("  -input <file> | The input file to process");
                Log.Trace("  -output [<directory>] | The output directory to save the processed file, default is current working directory");
                Log.Trace("  -genlib | Generate libraries from the input files");
                Log.Trace("  -modlnk | Parse PE file to support dynamic/runtime linking");
                Log.Trace("  -dlmred | Redirect the Entry Point to support late DllMain call (This will essentially break the module for loaders other than dynalnk)");
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

            string machine = arguments.Get("machine", ArchitectureUtils.GetArchitecture())!;
            if (!ArchitectureUtils.IsArchitectureValid(machine)) {
                Log.Error("Invalid architecture specified. Use -help to see the valid options.");
                return -1;
            }
            if (ArchitectureUtils.IsTechnicalName(machine)) {
                machine = ArchitectureUtils.ConvertToFriendlyNameArch(machine);
            }
            Log.Trace($"Architecture: {machine}");

            if (!arguments.Has("version") || !Semver.IsValidVersion(arguments.Get("version")!)) {
                Log.Error("Invalid version specified. Use -help to see the valid options.");
                return -1;
            }
            Log.Trace("Target version: " + arguments.Get("version")!);
            Semver version = new(arguments.Get("version")!);

            if (VC.ToolsVersion is null) {
                Log.Error("Visual C++ tools not found.");
                return -1;
            }
            Log.Trace("Visual C++ tools version: " + VC.ToolsVersion);

            if (VC.ToolsPath is null) {
                Log.Error("Visual C++ tools path not found.");
                return -1;
            }
            Log.Trace("Visual C++ tools path: " + VC.ToolsPath);

            DynamicImportModel? model = DynamicImportModel.Parse(await File.ReadAllTextAsync(inputFile));
            if (model is null) {
                Log.Error("Failed to parse the input file.");
                return -1;
            }

            if (arguments.Has("genlib")) {
                string? export = await GenerateExportDefinition(outputDirectory, machine, model!, version);
                if (string.IsNullOrWhiteSpace(export)) {
                    Log.Error($"Failed to generate export definition for {model!.Target}");
                    return -1;
                }

                string? lib = await GenerateLinkLibrary(export, machine);
                if (string.IsNullOrWhiteSpace(lib)) {
                    Log.Error($"Failed to generate library for {model!.Target}");
                    return -1;
                }

                Log.Trace($"Generated library for {model!.Target} at {lib}");
                return 0;
            }
            else if (arguments.Has("modlnk")) {
                string inputPortableExecutable = arguments.Get("modlnk")!;
                if (string.IsNullOrWhiteSpace(inputPortableExecutable) || !File.Exists(inputPortableExecutable)) {
                    Log.Error("Invalid specified PE filepath.");
                    return -1;
                }
                
                try {
                    var portableExecutable = PEFile.FromFile(inputPortableExecutable);
                    if (portableExecutable is null) {
                        Log.Error("Failed to parse the specified PE file.");
                        return -1;
                    }
                    string outputPortableExecutable = Path.Combine(outputDirectory, Path.GetFileName(inputPortableExecutable));
                    if (arguments.Has("dlmred")) {
                        if (portableExecutable.Sections.Any(x => x.Name == DllMainRedirectSection)) {
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
                                DllMainRedirectSection,
                                SectionFlags.MemoryRead | SectionFlags.MemoryExecute
                            ) { Contents = data };
                            portableExecutable.Sections.Add(dllMainRedirectSection);
                            portableExecutable.AlignSections();
                            var newEntryPoint = dllMainRedirectSection.Rva + (sizeof(uint) * 2);
                            portableExecutable.OptionalHeader!.AddressOfEntryPoint = newEntryPoint;
                            Log.Trace($"  New Entry Point RVA: 0x{newEntryPoint:x}");
                        }
                    }

                    var importDirectory = portableExecutable.OptionalHeader.GetDataDirectory(DataDirectoryIndex.ImportDirectory);
                    if (!importDirectory.IsPresentInPE) {
                        Log.Error("The specified PE file does not have an import directory.");
                        return -1;
                    }

                    var dynamicLinkSection = portableExecutable.Sections.FirstOrDefault(x => x.Name == DynamicLinkSection);
                    var importDescriptorTableSection = portableExecutable.Sections.FirstOrDefault(x => x.Name == ImportDescriptorSection);
                    if (dynamicLinkSection is not null) {
                        Log.Warn("The specified PE file already has a dynamic linking section, overwriting it.");
                        var dynamicReader = portableExecutable.CreateReaderAtRva(dynamicLinkSection.Rva);
                        var importReader = portableExecutable.CreateReaderAtRva(importDirectory.VirtualAddress);
                        List<ImportDirectoryEntry> existingDynamicImportEntries = [.. ImportDirectoryEntry.GetAllFrom(ref dynamicReader)];
                        List<ImportDirectoryEntry> importDirectoryEntries = [.. ImportDirectoryEntry.GetAllFrom(ref importReader)];
                        ImportDirectoryEntry? toBeDynamic = importDirectoryEntries.FirstOrDefault(x => portableExecutable.CreateReaderAtRva(x.Name).ReadUtf8String() == model.Target);
                        if (toBeDynamic is not null) {
                            existingDynamicImportEntries.Add(toBeDynamic);
                            importDirectoryEntries = importDirectoryEntries.Where(x => x.Name != toBeDynamic?.Name).ToList();
                        }
                        existingDynamicImportEntries.Add(0u);
                        importDirectoryEntries.Add(0u);

                        var importsDataSegment = new DataSegment(importDirectoryEntries.GetBytesCollection());
                        if (importDescriptorTableSection is not null) {
                            importDescriptorTableSection.Contents = importsDataSegment;
                        }
                        else {
                            importDescriptorTableSection = new PESection(
                                ImportDescriptorSection,
                                SectionFlags.MemoryRead
                            ) { Contents = importsDataSegment };
                            portableExecutable.Sections.Add(importDescriptorTableSection);
                        }
                        portableExecutable.AlignSections();
                        portableExecutable.OptionalHeader.SetDataDirectory(DataDirectoryIndex.ImportDirectory, new DataDirectory(importDescriptorTableSection.Rva, (uint)importsDataSegment.Data.Length));

                        var dynamicImportsDataSegment = new DataSegment(existingDynamicImportEntries.GetBytesCollection());
                        dynamicLinkSection.Contents = dynamicImportsDataSegment;
                        portableExecutable.AlignSections();
                    }
                    else {
                        var importReader = portableExecutable.CreateReaderAtRva(importDirectory.VirtualAddress);
                        List<ImportDirectoryEntry> existingDynamicImportEntries = [];
                        List<ImportDirectoryEntry> importDirectoryEntries = [.. ImportDirectoryEntry.GetAllFrom(ref importReader)];
                        ImportDirectoryEntry? toBeDynamic = importDirectoryEntries.FirstOrDefault(x => portableExecutable.CreateReaderAtRva(x.Name).ReadUtf8String() == model.Target);
                        if (toBeDynamic is not null) {
                            existingDynamicImportEntries.Add(toBeDynamic);
                            importDirectoryEntries = importDirectoryEntries.Where(x => x.Name != toBeDynamic?.Name).ToList();
                        }
                        existingDynamicImportEntries.Add(0u);
                        importDirectoryEntries.Add(0u);

                        var importsDataSegment = new DataSegment(importDirectoryEntries.GetBytesCollection());
                        if (importDescriptorTableSection is not null) {
                            importDescriptorTableSection.Contents = importsDataSegment;
                        }
                        else {
                            importDescriptorTableSection = new PESection(
                                ImportDescriptorSection,
                                SectionFlags.MemoryRead
                            ) { Contents = importsDataSegment };
                            portableExecutable.Sections.Add(importDescriptorTableSection);
                        }
                        portableExecutable.AlignSections();
                        portableExecutable.OptionalHeader.SetDataDirectory(DataDirectoryIndex.ImportDirectory, new DataDirectory(importDescriptorTableSection.Rva, (uint)importsDataSegment.Data.Length));

                        var dynamicImportsDataSegment = new DataSegment(existingDynamicImportEntries.GetBytesCollection());
                        dynamicLinkSection = new PESection(
                            DynamicLinkSection,
                            SectionFlags.MemoryRead
                        ) { Contents = dynamicImportsDataSegment };
                        portableExecutable.Sections.Add(dynamicLinkSection);
                        portableExecutable.AlignSections();
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPortableExecutable)!);
                    portableExecutable.Write(outputPortableExecutable);
                    return 0;
                }
                catch (BadImageFormatException) {
                    Log.Error("The specified 'modlnk' file was not a PE file.");
                    return -1;
                }
            }

            Log.Warn("Unknown command. Use -help to see the available commands.");
            return 0;
        }
    }
}