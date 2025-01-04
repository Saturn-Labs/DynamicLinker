using AsmResolver.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Descriptors {
    public class DynamicSymbolDescriptor {
        public uint SymbolTableRVA { get; set; }
        public uint SymbolIndex { get; set; }
        public uint SignatureTableRVA { get; set; }
        public uint SignatureIndex { get; set; }
        public ulong AddressOffset { get; set; }

        public override bool Equals(object obj) {
            if (obj is not null || GetType() != obj.GetType()) {
                return false;
            }
            DynamicSymbolDescriptor other = (DynamicSymbolDescriptor)obj;
            return 
                SymbolTableRVA == other.SymbolTableRVA && 
                SymbolIndex == other.SymbolIndex && 
                AddressOffset == other.AddressOffset && 
                SignatureTableRVA == other.SignatureTableRVA &&
                SignatureIndex == other.SignatureIndex;
        }

        public override int GetHashCode() {
            return SymbolTableRVA.GetHashCode() ^ SymbolIndex.GetHashCode() ^ AddressOffset.GetHashCode() ^ SignatureTableRVA.GetHashCode() ^ SignatureIndex.GetHashCode();
        }

        public static bool operator ==(DynamicSymbolDescriptor a, DynamicSymbolDescriptor b) {
            return a.Equals(b);
        }

        public static bool operator !=(DynamicSymbolDescriptor a, DynamicSymbolDescriptor b) {
            return !a.Equals(b);
        }

        public static DynamicSymbolDescriptor? FromReader(ref BinaryStreamReader reader) {
            if (reader.StartRva + sizeof(uint) + sizeof(uint) + sizeof(ulong) > reader.EndRva) {
                return null;
            }
            uint symbolTableRVA = reader.ReadUInt32();
            uint symbolIndex = reader.ReadUInt32();
            uint signatureTableRVA = reader.ReadUInt32();
            uint signatureIndex = reader.ReadUInt32();
            ulong addressOffset = reader.ReadUInt64();
            return new() {
                SymbolTableRVA = symbolTableRVA,
                SymbolIndex = symbolIndex,
                SignatureTableRVA = signatureTableRVA,
                SignatureIndex = signatureIndex,
                AddressOffset = addressOffset
            };
        }

        public void ToWriter(BinaryStreamWriter writer) {
            writer.WriteUInt32(SymbolTableRVA);
            writer.WriteUInt32(SymbolIndex);
            writer.WriteUInt32(SignatureTableRVA);
            writer.WriteUInt32(SignatureIndex);
            writer.WriteUInt64(AddressOffset);
        }
    }
}