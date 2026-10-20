using System;
using System.IO;

namespace Cane.Format
{
    internal static class NumericBlocks
    {
        internal static NumericBuffer Read(byte[] data, int body, ViewRecord view)
        {
            long start = (long)body + view.Offset;
            if (start > data.Length || view.Length > int.MaxValue) throw new InvalidDataException("Invalid numeric buffer range");
            int length = (int)view.Length;
            if (view.Encoding == EncodingRecord.Packed || view.Encoding == EncodingRecord.Curves)
            {
                if (view.Type != TypeRecord.F64 || start + view.Bytes > data.Length ||
                    view.Encoding == EncodingRecord.Curves && view.Offset % 8 != 0)
                    throw new InvalidDataException("Invalid packed numeric buffer");
                var r = new BinaryInput(data, (int)start, (int)(start + view.Bytes));
                double[] values = view.Encoding == EncodingRecord.Packed ? Packed(r, length) : Curves(r, length);
                r.End();
                return new NumericBuffer(view.Type, values);
            }
            int width = view.Encoding switch { EncodingRecord.F64 => 8, EncodingRecord.F32 => 4,
                EncodingRecord.U32 => 4, EncodingRecord.U16 => 2, EncodingRecord.U8 => 1, _ => 0 };
            bool matching = (int)view.Type == (int)view.Encoding || view.Type == TypeRecord.F64 && view.Encoding == EncodingRecord.F32
                || view.Type == TypeRecord.U32 && (view.Encoding == EncodingRecord.U8 || view.Encoding == EncodingRecord.U16);
            long bytes = (long)length * width;
            if (width == 0 || !matching || view.Offset % width != 0 || start + bytes > data.Length || view.Bytes != 0 && view.Bytes != bytes)
                throw new InvalidDataException("Invalid dense numeric buffer");
            Array result = view.Type switch { TypeRecord.F64 => new double[length], TypeRecord.F32 => new float[length],
                TypeRecord.U32 => new uint[length], _ => throw new InvalidDataException("Unknown numeric type") };
            if (BitConverter.IsLittleEndian && (int)view.Type == (int)view.Encoding)
                Buffer.BlockCopy(data, (int)start, result, 0, (int)bytes);
            else
            {
                var r = new BinaryInput(data, (int)start, (int)(start + bytes));
                if (result is double[] doubles) for (int i = 0; i < length; i++) doubles[i] = width == 8 ? r.F64() : r.F32();
                else if (result is float[] floats) for (int i = 0; i < length; i++) floats[i] = r.F32();
                else { var indices = (uint[])result; for (int i = 0; i < length; i++) indices[i] = width == 1 ? r.Byte() : width == 2 ? r.U16() : r.U32(); }
            }
            return new NumericBuffer(view.Type, result);
        }

        private static double[] Packed(BinaryInput r, int length)
        {
            int flags = r.At;
            r.Bytes((int)(((long)length + 3) / 4));
            var result = new double[length];
            for (int i = 0; i < length; i++)
                result[i] = ((r.Data[flags + (i >> 2)] >> ((i & 3) * 2)) & 3) switch
                { 1 => 1, 2 => r.F32(), 3 => r.F64(), _ => 0 };
            return result;
        }

        private static double[] Curves(BinaryInput r, int length)
        {
            uint segmentCount = r.U32(), timeInfo = r.U32();
            int timeWidth = (timeInfo & 1) == 0 ? 4 : 8;
            uint timeCount = timeInfo >> 1;
            if (timeCount > r.Remaining / timeWidth) throw r.Error("Truncated curve times");
            var times = new double[(int)timeCount];
            for (int i = 0; i < times.Length; i++) times[i] = timeWidth == 4 ? r.F32() : r.F64();
            if (segmentCount > r.Remaining) throw r.Error("Truncated curve plan");
            var plan = new uint[(int)segmentCount];
            var offsets = new int[(int)segmentCount + 1];
            long total = 0, flagsCount = 0;
            for (int i = 0; i < plan.Length; i++)
            {
                uint code = plan[i] = r.UInt(), count = code >> 2;
                if ((code & 3) == 3) throw r.Error("Unknown curve plan");
                if ((code & 3) == 2)
                {
                    if (count >= i) throw r.Error("Invalid curve reference");
                    total += offsets[count + 1] - offsets[count];
                }
                else { flagsCount += count; total += (long)count * ((code & 1) == 0 ? 3 : 9); }
                if (total > length) throw r.Error("Curve length overflow");
                offsets[i + 1] = (int)total;
            }
            if (total != length || flagsCount > r.Remaining) throw r.Error("Invalid curve length");
            int flags = r.At;
            r.Bytes((int)flagsCount);
            int timeIndexWidth = timeCount <= 256 ? 1 : timeCount <= 65536 ? 2 : 4;
            var result = new double[length];
            for (int s = 0; s < plan.Length; s++)
            {
                int count = (int)(plan[s] >> 2), offset = offsets[s];
                if ((plan[s] & 3) == 2)
                {
                    Array.Copy(result, offsets[count], result, offset, offsets[count + 1] - offsets[count]);
                    continue;
                }
                int stride = (plan[s] & 1) == 0 ? 2 : 8, coefficients = offset + count;
                for (int i = 0; i < count; i++)
                {
                    uint time = timeIndexWidth == 1 ? r.Byte() : timeIndexWidth == 2 ? r.U16() : r.U32();
                    if (time >= timeCount) throw r.Error("Invalid curve time reference");
                    result[offset + i] = times[time];
                    byte flag = r.Data[flags + i];
                    if ((flag & 128) != 0) throw r.Error("Reserved curve flag");
                    result[coefficients + i * stride] = ((flag >> 3) & 3) switch { 0 => r.F32(), 2 => 1, 3 => r.F64(), _ => 0 };
                }
                for (int i = 0; i < count; i++)
                {
                    byte flag = r.Data[flags++];
                    int mode = flag & 7, at = coefficients + i * stride, slope = at + (stride == 2 ? 1 : 5);
                    if (mode == 0) continue;
                    if (mode == 2) { result[slope] = r.F64(); continue; }
                    if (mode == 5)
                    {
                        if (stride != 8) throw r.Error("Invalid literal curve stride");
                        for (int j = 1; j < 8; j++) result[at + j] = r.F64();
                        continue;
                    }
                    if (i + 1 == count) throw r.Error("Missing curve endpoint");
                    double dt = result[offset + i + 1] - result[offset + i], dv = result[at + stride] - result[at];
                    if (mode == 1) { result[slope] = dv / dt; continue; }
                    if (stride != 8 || mode != 3 && mode != 4) throw r.Error("Unknown curve coefficient mode");
                    uint n = r.UInt();
                    double x1 = BitConverter.Int32BitsToSingle(unchecked(0x3e800000 + (int)((n >> 1) ^ (uint)-(int)(n & 1))));
                    n = r.UInt();
                    double x2 = BitConverter.Int32BitsToSingle(unchecked(0x3f400000 + (int)((n >> 1) ^ (uint)-(int)(n & 1))));
                    double u = (flag & 32) == 0 ? r.F32() : 0, v = (flag & 64) == 0 ? r.F32() : 0;
                    double y1 = mode == 3 ? u * dv : u, y2 = mode == 3 ? v * dv : dv + v;
                    result[at + 1] = (x1 >= 0 && x1 <= 1 && x2 >= 0 && x2 <= 1 ? 1 : -1) / dt;
                    result[at + 2] = 3 * x1;
                    result[at + 3] = 3 * x2 - 6 * x1;
                    result[at + 4] = 1 + 3 * x1 - 3 * x2;
                    result[at + 5] = 3 * y1;
                    result[at + 6] = 3 * y2 - 6 * y1;
                    result[at + 7] = dv + 3 * y1 - 3 * y2;
                }
            }
            return result;
        }
    }
}
