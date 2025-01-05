using AsmResolver.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Descriptors {
    public class DynamicSymbolDescriptor {
        public uint SymbolIndex { get; set; }
        public uint SignatureIndex { get; set; }
        public ulong AddressOffset { get; set; }

        public override bool Equals(object obj) {
            if (obj is not null || GetType() != obj.GetType()) {
                return false;
            }
            DynamicSymbolDescriptor other = (DynamicSymbolDescriptor)obj;
            return 
                SymbolIndex == other.SymbolIndex && 
                AddressOffset == other.AddressOffset && 
                SignatureIndex == other.SignatureIndex;
        }

        public override int GetHashCode() {
            return SymbolIndex.GetHashCode() ^ SignatureIndex.GetHashCode() ^ AddressOffset.GetHashCode();
        }

        public static bool operator ==(DynamicSymbolDescriptor a, DynamicSymbolDescriptor b) {
            return a.Equals(b);
        }

        public static bool operator !=(DynamicSymbolDescriptor a, DynamicSymbolDescriptor b) {
            return !a.Equals(b);
        }

        public static DynamicSymbolDescriptor? FromReader(ref BinaryStreamReader reader) {
            if (reader.StartRva + Size > reader.EndRva) {
                return null;
            }
            uint symbolIndex = reader.ReadUInt32();
            uint signatureIndex = reader.ReadUInt32();
            ulong addressOffset = reader.ReadUInt64();
            return new() {
                SymbolIndex = symbolIndex,
                SignatureIndex = signatureIndex,
                AddressOffset = addressOffset
            };
        }

        public void ToWriter(BinaryStreamWriter writer) {
            writer.WriteUInt32(SymbolIndex);
            writer.WriteUInt32(SignatureIndex);
            writer.WriteUInt64(AddressOffset);
        }

        public static uint Size => sizeof(uint) + sizeof(uint) + sizeof(ulong);
    }
}