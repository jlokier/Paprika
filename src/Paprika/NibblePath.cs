﻿using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Paprika.Crypto;

namespace Paprika;

/// <summary>
/// Represents a nibble path in a way that makes it efficient for comparisons.
/// </summary>
/// <remarks>
/// The implementation diverges from the Ethereum encoding for extensions or leafs.
/// The divergence is to never perform bit shift of the whole path and always align to byte boundary.
/// If the path starts in on odd nibble, it will include one byte and use only its higher nibble.
/// </remarks>
public readonly ref struct NibblePath
{
    private const int NibbleBitSize = 4;
    private const int NibblePerByte = 2;
    public const int NibbleShift = 8 / NibblePerByte;
    public const int NibbleMask = 15;

    private const int LengthShift = 1;
    private const int PreambleLength = 1;
    private const int OddBit = 1;

    public readonly byte Length;
    private readonly ref byte _span;
    private readonly byte _odd;

    public static NibblePath FromKey(ReadOnlySpan<byte> key, int nibbleFrom = 0)
    {
        var count = key.Length * NibblePerByte;
        return new NibblePath(key, nibbleFrom, count - nibbleFrom);
    }

    public static NibblePath FromKey(Keccak key, int nibbleFrom = 0)
    {
        var count = Keccak.Size * NibblePerByte;
        return new NibblePath(key.BytesAsSpan, nibbleFrom, count - nibbleFrom);
    }

    private NibblePath(ReadOnlySpan<byte> key, int nibbleFrom, int length)
    {
        _span = ref Unsafe.Add(ref MemoryMarshal.GetReference(key), nibbleFrom / 2);
        _odd = (byte)(nibbleFrom & OddBit);
        Length = (byte)length;
    }

    private NibblePath(ref byte span, byte odd, byte length)
    {
        _span = ref span;
        _odd = odd;
        Length = length;
    }

    /// <summary>
    /// The estimate of the max length, used for stackalloc estimations.
    /// </summary>
    public int MaxLength => Length / 2 + 2;

    public Span<byte> WriteTo(Span<byte> destination)
    {
        var odd = _odd & OddBit;
        var lenght = GetSpanLength(Length, _odd);

        destination[0] = (byte)(odd | (Length << LengthShift));

        MemoryMarshal.CreateSpan(ref _span, lenght).CopyTo(destination.Slice(PreambleLength));

        return destination.Slice(lenght + PreambleLength);
    }

    /// <summary>
    /// Slices the beginning of the nibble path as <see cref="Span{T}.Slice(int)"/> does.
    /// </summary>
    public NibblePath SliceFrom(int start) => new(ref Unsafe.Add(ref _span, (_odd + start) / 2),
        (byte)((start & 1) ^ _odd), (byte)(Length - start));

    /// <summary>
    /// Trims the end of the nibble path so that it gets to the specified length.
    /// </summary>
    public NibblePath SliceTo(int length) => new(ref _span, _odd, (byte)length);

    public byte GetAt(int nibble)
    {
        ref var b = ref Unsafe.Add(ref _span, (nibble + _odd) / 2);
        return (byte)((b >> ((1 - ((nibble + _odd) & OddBit)) * NibbleShift)) & NibbleMask);
    }

    public byte FirstNibble => (byte)((_span >> ((1 - _odd) * NibbleShift)) & NibbleMask);

    private static int GetSpanLength(byte length, int odd) => (length + 1 + odd) / 2;

    public static ReadOnlySpan<byte> ReadFrom(ReadOnlySpan<byte> source, out NibblePath nibblePath)
    {
        var b = source[0];

        var odd = OddBit & b;
        var length = (byte)(b >> LengthShift);

        nibblePath = new NibblePath(source.Slice(PreambleLength), odd, length);

        return source.Slice(PreambleLength + GetSpanLength(length, odd));
    }

    public int RawByteLength => PreambleLength + GetSpanLength(Length, _odd & OddBit);

    public int FindFirstDifferentNibble(in NibblePath other)
    {
        var length = Math.Min(other.Length, Length);

        if (length == 0)
        {
            // special case, empty is different at zero
            return 0;
        }

        if (_odd == other._odd)
        {
            // The most common case in Trie.
            // As paths will start on the same level, the odd will be encoded same way for them.
            // This means that an urolled version can be used.

            var position = 0;

            ref var a = ref _span;
            ref var b = ref other._span;

            var isOdd = (_odd & OddBit) == OddBit;

            if (isOdd)
            {
                if ((a & NibbleMask) != (b & NibbleMask))
                {
                    return 0;
                }

                // move by 1
                a = ref Unsafe.Add(ref a, 1);
                b = ref Unsafe.Add(ref b, 1);
                position = 1;
            }

            // oddity aligned, move in large jumps
            // long
            const int longJump = 8;
            while (position + longJump * NibblePerByte < length)
            {
                var u1 = Unsafe.ReadUnaligned<ulong>(ref a);
                var u2 = Unsafe.ReadUnaligned<ulong>(ref b);

                if (u1 != u2)
                {
                    break;
                }

                a = ref Unsafe.Add(ref a, longJump);
                b = ref Unsafe.Add(ref b, longJump);

                position += longJump * NibblePerByte;
            }

            // uint as there must be less than 16 nibbles here
            const int intJump = 4;
            if (position + intJump * NibblePerByte < length)
            {
                var u1 = Unsafe.ReadUnaligned<uint>(ref a);
                var u2 = Unsafe.ReadUnaligned<uint>(ref b);

                if (u1 == u2)
                {
                    a = ref Unsafe.Add(ref a, intJump);
                    b = ref Unsafe.Add(ref b, intJump);

                    position += intJump * NibblePerByte;
                }
            }

            // length must be less than 8 nibbles (4 bytes)
            // scan through
            const int byteJump = 1;
            while (position + byteJump * NibblePerByte < length)
            {
                if (a != b)
                {
                    break;
                }

                a = ref Unsafe.Add(ref a, byteJump);
                b = ref Unsafe.Add(ref b, byteJump);

                position += byteJump * NibblePerByte;
            }

            // it might be already processed, when length of 1
            if (position == length)
            {
                return position;
            }

            // two or one nibbles left
            if (((a >> NibbleShift) & NibbleMask) != ((b >> NibbleShift) & NibbleMask))
            {
                return position;
            }

            position++;
            if (position < length)
            {
                if ((a & NibbleMask) != (b & NibbleMask))
                {
                    return position;
                }

                position++;
            }

            return position;
        }

        // fallback, the slow path version to make the method work in any case
        int i = 0;
        for (; i < length; i++)
        {
            if (GetAt(i) != other.GetAt(i))
            {
                return i;
            }
        }

        return length;
    }

    const int HexPreambleLength = 1;
    public int HexEncodedLength => Length / NibblePerByte + HexPreambleLength;

    private const byte OddFlag = 0x10;
    private const byte LeafFlag = 0x20;

    public void HexEncode(Span<byte> destination, bool isLeaf)
    {
        destination[0] = (byte)(isLeaf ? LeafFlag : 0x000);

        // This is the usual fast path for leaves, as they are aligned with oddity and length.
        // length: odd, odd: 1
        // length: even, odd: 0
        if ((Length & OddBit) == _odd)
        {
            if (_odd == OddBit)
            {
                // store odd
                destination[0] += (byte)(OddFlag + (_span & NibbleMask));
            }

            // copy the rest as is
            MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _span, _odd), Length / 2)
                .CopyTo(destination.Slice(HexPreambleLength));

            return;
        }

        // this cases should happen only on extensions, as leafs are aligned to the end of the key.
        ref var b = ref _span;

        // length: even, odd: 1
        if (_odd == OddBit)
        {
            // the length is even, no need to amend destination[0]
            for (var i = 0; i < Length / 2; i++)
            {
                destination[i + 1] = (byte)(((b & NibbleMask) << NibbleShift) |
                                            ((Unsafe.Add(ref b, 1) >> NibbleShift) & NibbleMask));
                b = ref Unsafe.Add(ref b, 1);
            }

            return;
        }

        // length: odd, odd: 0
        if ((Length & OddBit) == OddBit)
        {
            destination[0] += (byte)(OddFlag + ((b >> NibbleShift) & NibbleMask));

            // the length is even, no need to amend destination[0]
            for (var i = 0; i < Length / 2; i++)
            {
                destination[i + 1] = (byte)(((b & NibbleMask) << NibbleShift) |
                                            ((Unsafe.Add(ref b, 1) >> NibbleShift) & NibbleMask));
                b = ref Unsafe.Add(ref b, 1);
            }

            return;
        }


        throw new Exception("WRONG!");
    }

    public override string ToString()
    {
        Span<char> path = stackalloc char[Length];
        ref var ch = ref path[0];

        for (int i = _odd; i < Length + _odd; i++)
        {
            var b = Unsafe.Add(ref _span, i / 2);
            var nibble = (b >> ((1 - (i & OddBit)) * NibbleShift)) & NibbleMask;

            ch = Hex[nibble];
            ch = ref Unsafe.Add(ref ch, 1);
        }

        return new string(path);
    }

    private static readonly char[] Hex = "0123456789ABCDEF".ToArray();

    public bool Equals(in NibblePath other)
    {
        if (other.Length != Length || (other._odd & OddBit) != (_odd & OddBit))
            return false;

        return FindFirstDifferentNibble(other) == Length;
    }
}