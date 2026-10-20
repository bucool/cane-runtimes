using System;
using System.IO;
using System.Text;

namespace Cane.Format
{
    internal readonly struct NumericBuffer
    {
        internal readonly TypeRecord Type;
        internal readonly Array Values;
        internal NumericBuffer(TypeRecord type, Array values) { Type = type; Values = values; }
    }

    internal sealed class RuntimeDocument
    {
        internal readonly ModelRecord Model;
        internal readonly NumericBuffer[] Buffers;
        internal RuntimeDocument(ModelRecord model, NumericBuffer[] buffers) { Model = model; Buffers = buffers; }
    }

    internal static class DocumentReader
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static RuntimeDocument Decode(byte[] data)
        {
            if (data.Length >= 8 && data[0] == 'C') return Binary(data);
            return Json(Utf8.GetString(data));
        }

        internal static RuntimeDocument Json(string text)
        {
            var r = new JsonInput(text);
            ModelRecord? model = null;
            NumericBuffer[]? buffers = null;
            uint version = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String(); r.Expect(':');
                switch (field)
                {
                    case "version": version = r.UInt(); break;
                    case "model": model = Records.ReadModel(r); break;
                    case "buffers": buffers = r.List(ReadJsonBuffer); break;
                    default: throw r.Error("Unknown document field: " + field);
                }
            } while (r.More('}'));
            r.End();
            if (version != 2) throw r.Error("Unsupported runtime version");
            if (model == null || buffers == null) throw r.Error("Missing runtime data");
            return new RuntimeDocument(model, buffers);
        }

        private static NumericBuffer ReadJsonBuffer(JsonInput r)
        {
            TypeRecord? type = null;
            EncodingRecord? encoding = null;
            uint? length = null;
            string? packed = null;
            double[]? numbers = null;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String(); r.Expect(':');
                switch (field)
                {
                    case "type": type = Records.ReadType(r); break;
                    case "encoding": encoding = Records.ReadEncoding(r); break;
                    case "length": length = r.UInt(); break;
                    case "data":
                        if (r.Peek() == '"') packed = r.String();
                        else numbers = r.List(x => x.Number());
                        break;
                    default: throw r.Error("Unknown buffer field: " + field);
                }
            } while (r.More('}'));
            if (type == null) throw r.Error("Missing buffer type");
            if (encoding != null)
            {
                if (packed == null || length == null || numbers != null || packed.Length % 4 != 0)
                    throw r.Error("Invalid encoded buffer");
                foreach (char c in packed) if (char.IsWhiteSpace(c)) throw r.Error("Invalid Base64 buffer");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(packed); }
                catch (FormatException) { throw r.Error("Invalid Base64 buffer"); }
                return NumericBlocks.Read(bytes, 0, new ViewRecord { Type = type.Value, Encoding = encoding.Value,
                    Length = length.Value, Bytes = (uint)bytes.Length });
            }
            if (numbers == null || packed != null || length != null) throw r.Error("Invalid numeric buffer");
            if (type == TypeRecord.F64) return new NumericBuffer(type.Value, numbers);
            if (type == TypeRecord.F32)
            {
                var values = new float[numbers.Length];
                for (int i = 0; i < values.Length; i++) values[i] = (float)numbers[i];
                return new NumericBuffer(type.Value, values);
            }
            var indices = new uint[numbers.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                double value = numbers[i];
                if (value < 0 || value > uint.MaxValue || value != Math.Truncate(value)) throw r.Error("Invalid index buffer");
                indices[i] = (uint)value;
            }
            return new NumericBuffer(type.Value, indices);
        }

        private static RuntimeDocument Binary(byte[] data)
        {
            const string magic = "CANE2IDX";
            if (data.Length < 20) throw new InvalidDataException("Truncated Cane header");
            for (int i = 0; i < magic.Length; i++) if (data[i] != magic[i]) throw new InvalidDataException("Unsupported Cane binary");
            var header = new BinaryInput(data, 8, 20);
            long dictionaryEnd = 20L + header.U32(), metadataEnd = dictionaryEnd + header.U32();
            long body = (metadataEnd + 7) & ~7L;
            if (body + header.U32() != data.Length) throw header.Error("Invalid binary envelope");
            var stringsReader = new JsonInput(Utf8.GetString(data, 20, checked((int)dictionaryEnd - 20)));
            string[] strings = stringsReader.List(x => x.String()); stringsReader.End();
            var metadata = new BinaryInput(data, (int)dictionaryEnd, (int)metadataEnd, strings);
            ModelRecord model = Records.ReadModel(metadata);
            ViewRecord[] views = metadata.List(Records.ReadView); metadata.End();
            var buffers = new NumericBuffer[views.Length];
            for (int i = 0; i < buffers.Length; i++) buffers[i] = NumericBlocks.Read(data, (int)body, views[i]);
            return new RuntimeDocument(model, buffers);
        }
    }
}
