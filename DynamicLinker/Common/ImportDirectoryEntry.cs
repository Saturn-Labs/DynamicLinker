using AsmResolver.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Common {
    public class ImportDirectoryEntry {
        public uint OriginalFirstThunk { get; set; }
        public uint TimeDateStamp { get; set; }
        public uint ForwarderChain { get; set; }
        public uint Name { get; set; }
        public uint FirstThunk { get; set; }

        public ImportDirectoryEntry(uint originalFirstThunk, uint timeDateStamp, uint forwarderChain, uint name, uint firstThunk) {
            OriginalFirstThunk = originalFirstThunk;
            TimeDateStamp = timeDateStamp;
            ForwarderChain = forwarderChain;
            Name = name;
            FirstThunk = firstThunk;
        }

        public static ImportDirectoryEntry? FromReader(ref BinaryStreamReader reader) {
            if (reader.Rva + Size > reader.EndRva)
                return null;

            return new ImportDirectoryEntry(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        }

        public void WriteTo(BinaryStreamWriter writer) {
            writer.WriteUInt32(OriginalFirstThunk);
            writer.WriteUInt32(TimeDateStamp);
            writer.WriteUInt32(ForwarderChain);
            writer.WriteUInt32(Name);
            writer.WriteUInt32(FirstThunk);
        }

        public override string ToString() {
            return $"OriginalFirstThunk: {OriginalFirstThunk}, TimeDateStamp: {TimeDateStamp}, ForwarderChain: {ForwarderChain}, Name: {Name}, FirstThunk: {FirstThunk}";
        }

        public override bool Equals(object? obj) {
            return obj is ImportDirectoryEntry entry &&
                   OriginalFirstThunk == entry.OriginalFirstThunk &&
                   TimeDateStamp == entry.TimeDateStamp &&
                   ForwarderChain == entry.ForwarderChain &&
                   Name == entry.Name &&
                   FirstThunk == entry.FirstThunk;
        }

        public override int GetHashCode() {
            return HashCode.Combine(OriginalFirstThunk, TimeDateStamp, ForwarderChain, Name, FirstThunk);
        }

        public static bool operator ==(ImportDirectoryEntry left, ImportDirectoryEntry right) {
            return left.Equals(right);
        }

        public static bool operator !=(ImportDirectoryEntry left, ImportDirectoryEntry right) {
            return !(left == right);
        }

        public bool IsZero() {
            return IsZero(this);
        }

        public static bool IsZero(ImportDirectoryEntry entry) {
            return entry.OriginalFirstThunk == 0 && entry.TimeDateStamp == 0 && entry.ForwarderChain == 0 && entry.Name == 0 && entry.FirstThunk == 0;
        }

        public static implicit operator uint(ImportDirectoryEntry entry) {
            return entry.IsZero() ? 0u : 1u;
        }

        public static implicit operator ImportDirectoryEntry(uint value) {
            return new ImportDirectoryEntry(value, value, value, value, value);
        }

        public static ImportDirectoryEntry[] GetAllFrom(ref BinaryStreamReader reader) {
            var entries = new List<ImportDirectoryEntry>();
            var entry = FromReader(ref reader);
            while (entry is not null && !entry.IsZero()) {
                entries.Add(entry);
                entry = FromReader(ref reader);
            }
            return entries.ToArray();
        }

        public static ImportDirectoryEntry Zero => 0u;

        public static byte[] GetBytes(ImportDirectoryEntry entry) {
            using var stream = new MemoryStream();
            var writer = new BinaryStreamWriter(stream);
            entry.WriteTo(writer);
            return stream.ToArray();
        }

        public static uint Size => sizeof(uint) * 5;
    }
}
