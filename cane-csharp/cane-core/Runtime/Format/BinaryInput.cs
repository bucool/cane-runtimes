using System;
using System.Buffers.Binary;
using System.IO;

namespace Cane.Format
{
    internal sealed class BinaryInput
    {
        internal readonly byte[] Data;
        internal readonly int Limit;
        internal int At;
        private readonly string[] dictionary;
        internal int Remaining => Limit - At;
        internal BinaryInput(byte[] data, int start, int limit, string[]? dictionary = null)
        {
            if (start < 0 || limit < start || limit > data.Length) throw new InvalidDataException("Invalid Cane binary range");
            Data = data; At = start; Limit = limit; this.dictionary = dictionary ?? Array.Empty<string>();
        }
        internal InvalidDataException Error(string message) => new InvalidDataException(message + " at binary byte " + At);
        internal byte Byte() { if (At == Limit) throw Error("Truncated Cane binary"); return Data[At++]; }
        internal ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > Remaining) throw Error("Truncated Cane binary");
            var result = Data.AsSpan(At, count); At += count; return result;
        }
        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        internal ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        internal float F32() => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Bytes(4)));
        internal double F64() => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)));
        internal uint UInt()
        {
            uint value = Byte();
            if (value < 128) return value;
            uint result = value & 127;
            for (int shift = 7; shift <= 28; shift += 7)
            {
                value = Byte();
                if (shift == 28 && value >= 16) throw Error("LEB128 overflow");
                result |= (value & 127) << shift;
                if (value < 128) return result;
            }
            throw Error("LEB128 overflow");
        }
        internal bool Boolean()
        {
            byte value = Byte(); if (value > 1) throw Error("Invalid boolean"); return value == 1;
        }
        internal string String()
        {
            uint index = UInt(); if (index >= dictionary.Length) throw Error("Invalid string reference"); return dictionary[index];
        }
        internal double Number()
        {
            byte tag = Byte();
            if (tag >= 16) return tag - 16;
            if (tag == 8) return UInt();
            if (tag == 9) return -(double)UInt();
            double value = tag == 3 ? F32() : tag == 4 ? F64() : throw Error("Invalid numeric token");
            if (double.IsNaN(value) || double.IsInfinity(value)) throw Error("Non-finite metadata number");
            return value;
        }
        internal long Integer()
        {
            double value = Number();
            if (Math.Abs(value) > 9007199254740991 || value != Math.Truncate(value)) throw Error("Invalid exact integer");
            return (long)value;
        }
        internal T[] List<T>(Func<BinaryInput, T> read)
        {
            uint length = UInt();
            if (length > Remaining) throw Error("Truncated list");
            if (length == 0) return Array.Empty<T>();
            var result = new T[(int)length];
            for (int i = 0; i < result.Length; i++) result[i] = read(this);
            return result;
        }
        internal void End() { if (At != Limit) throw Error("Trailing binary content"); }
    }
}
