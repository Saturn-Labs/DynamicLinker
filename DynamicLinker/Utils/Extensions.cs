using AsmResolver.IO;
using DynamicLinker.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DynamicLinker.Utils {
    public static class Extensions {
        public static byte[] GetBytesCollection(this IEnumerable<ImportDirectoryEntry> entries) {
            using var stream = new MemoryStream();
            var writer = new BinaryStreamWriter(stream);
            foreach (var entry in entries)
                entry.WriteTo(writer);
            return stream.ToArray();
        }
    }
}
