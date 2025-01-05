using AsmResolver.IO;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Descriptors {
    public class DynamicImportDescriptor {
        public uint TargetNameRVA { get; set; }
        public uint SymbolIndexerOffset { get; set; }

        public override string ToString() {
            return $"TargetNameRVA: {TargetNameRVA}, SymbolIndexerRVA: {SymbolIndexerOffset}";
        }

        public override bool Equals(object? obj) {
            if (obj is not DynamicImportDescriptor other) {
                return false;
            }
            return TargetNameRVA == other.TargetNameRVA && SymbolIndexerOffset == other.SymbolIndexerOffset;
        }

        public override int GetHashCode() {
            return TargetNameRVA.GetHashCode() ^ SymbolIndexerOffset.GetHashCode();
        }

        public static bool operator ==(DynamicImportDescriptor a, DynamicImportDescriptor b) {
            return a.Equals(b);
        }

        public static bool operator !=(DynamicImportDescriptor a, DynamicImportDescriptor b) {
            return !a.Equals(b);
        }

        public static DynamicImportDescriptor? FromReader(ref BinaryStreamReader reader) {
            if (reader.StartRva + Size > reader.EndRva) {
                return null;
            }

            uint targetNameRVA = reader.ReadUInt32();
            uint symbolIndexerOffset = reader.ReadUInt32();
            return new() {
                TargetNameRVA = targetNameRVA,
                SymbolIndexerOffset = symbolIndexerOffset
            };
        }

        public void ToWriter(BinaryStreamWriter writer) {
            writer.WriteUInt32(TargetNameRVA);
            writer.WriteUInt32(SymbolIndexerOffset);
        }

        public static uint Size => sizeof(uint) * 2;
    }
}
