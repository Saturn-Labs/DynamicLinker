using AsmResolver.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Descriptors {
    public class DynamicImportDescriptor {
        public uint TargetNameRVA { get; set; }
        public uint DefaultVersionStringRVA { get; set; }
        public uint SymbolsRVA { get; set; }

        public DynamicImportDescriptor(uint targetNameRVA, uint defaultVersionStringRVA, uint symbolsRVA) {
            TargetNameRVA = targetNameRVA;
            DefaultVersionStringRVA = defaultVersionStringRVA;
            SymbolsRVA = symbolsRVA;
        }

        public override string ToString() {
            return $"TargetNameRVA: {TargetNameRVA}, DefaultVersionStringRVA: {DefaultVersionStringRVA}, SymbolsRVA: {SymbolsRVA}";
        }

        public override bool Equals(object obj) {
            if (obj == null || GetType() != obj.GetType()) {
                return false;
            }
            DynamicImportDescriptor other = (DynamicImportDescriptor)obj;
            return TargetNameRVA == other.TargetNameRVA && DefaultVersionStringRVA == other.DefaultVersionStringRVA && SymbolsRVA == other.SymbolsRVA;
        }

        public override int GetHashCode() {
            return TargetNameRVA.GetHashCode() ^ DefaultVersionStringRVA.GetHashCode() ^ SymbolsRVA.GetHashCode();
        }

        public static bool operator ==(DynamicImportDescriptor a, DynamicImportDescriptor b) {
            return a.Equals(b);
        }

        public static bool operator !=(DynamicImportDescriptor a, DynamicImportDescriptor b) {
            return !a.Equals(b);
        }

        public static DynamicImportDescriptor? FromBytes(byte[] bytes) {
            if (bytes.Length != 12) {
                return null;
            }
            return new DynamicImportDescriptor(BitConverter.ToUInt32(bytes, 0), BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8));
        }

        public byte[] ToBytes() {
            byte[] bytes = new byte[12];
            BitConverter.GetBytes(TargetNameRVA).CopyTo(bytes, 0);
            BitConverter.GetBytes(DefaultVersionStringRVA).CopyTo(bytes, 4);
            BitConverter.GetBytes(SymbolsRVA).CopyTo(bytes, 8);
            return bytes;
        }

        public static DynamicImportDescriptor? FromReader(ref BinaryStreamReader reader) {
            if (reader.StartRva + 12 > reader.EndRva) {
                return null;
            }

            uint targetNameRVA = reader.ReadUInt32();
            uint defaultVersionStringRVA = reader.ReadUInt32();
            uint symbolsRVA = reader.ReadUInt32();
            return new DynamicImportDescriptor(targetNameRVA, defaultVersionStringRVA, symbolsRVA);
        }

        public void ToWriter(BinaryStreamWriter writer) {
            writer.WriteUInt32(TargetNameRVA);
            writer.WriteUInt32(DefaultVersionStringRVA);
            writer.WriteUInt32(SymbolsRVA);
        }
    }
}
