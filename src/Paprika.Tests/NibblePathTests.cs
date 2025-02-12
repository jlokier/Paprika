﻿using System.Buffers.Binary;
using NUnit.Framework;

// ReSharper disable HeapView.BoxingAllocation

namespace Paprika.Tests;

public class NibblePathTests
{
    [Test]
    public void Equal_From([Range(0, 15)] int from)
    {
        const ulong value = 0xFE_DC_BA_98_76_54_32_10;
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);

        var path = NibblePath.FromKey(span, from);

        Span<byte> destination = stackalloc byte[path.MaxLength];
        var leftover = path.WriteTo(destination);

        NibblePath.ReadFrom(destination, out var parsed);

        Assert.IsTrue(parsed.Equals(path));
    }

    [Test]
    public void Get_at_even()
    {
        const byte first = 0xDC;
        const byte second = 0xBA;
        Span<byte> span = stackalloc byte[2] { first, second };

        var path = NibblePath.FromKey(span);

        Assert.AreEqual(0xD, path.GetAt(0));
        Assert.AreEqual(0xC, path.GetAt(1));
        Assert.AreEqual(0xB, path.GetAt(2));
        Assert.AreEqual(0xA, path.GetAt(3));
    }

    [Test]
    public void Get_at_odd()
    {
        const byte first = 0xDC;
        const byte second = 0xBA;
        const byte third = 0x90;
        Span<byte> span = stackalloc byte[3] { first, second, third };

        var path = NibblePath.FromKey(span, 1);

        Assert.AreEqual(0xC, path.GetAt(0));
        Assert.AreEqual(0xB, path.GetAt(1));
        Assert.AreEqual(0xA, path.GetAt(2));
        Assert.AreEqual(0x9, path.GetAt(3));
    }

    private void AssertDiffAt(in NibblePath path1, in NibblePath path2, int diffAt)
    {
        Assert.AreEqual(diffAt, path1.FindFirstDifferentNibble(path2));
        Assert.AreEqual(diffAt, path2.FindFirstDifferentNibble(path1));
    }

    [Test]
    public void FindFirstDifferent_equal_even_even()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[] { 0xDC, 0xBA });
        var path2 = NibblePath.FromKey(stackalloc byte[] { 0xDC, 0xBA });

        AssertDiffAt(path1, path2, 4);
    }

    [Test]
    public void FindFirstDifferent_diff_even_even()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[] { 0xD1, 0xBA });
        var path2 = NibblePath.FromKey(stackalloc byte[] { 0xDC, 0xBA });

        AssertDiffAt(path1, path2, 1);
    }

    [Test]
    public void FindFirstDifferent_equal_even_odd()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[] { 0xDC, 0xBA });
        var path2 = NibblePath.FromKey(stackalloc byte[] { 0x0D, 0xCB, 0xA0 }, 1).SliceTo(path1.Length);

        AssertDiffAt(path1, path2, 4);
    }

    [Test]
    public void FindFirstDifferent_diff_even_odd()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[] { 0xDC, 0x1A });
        var path2 = NibblePath.FromKey(stackalloc byte[] { 0x0D, 0xCB, 0xA0 }, 1).SliceTo(path1.Length);

        AssertDiffAt(path1, path2, 2);
    }

    [Test]
    public void FindFirstDifferent_diff_odd_odd()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[] { 0x0D, 0xCB, 0xA0 }, 1).SliceTo(4);
        var path2 = NibblePath.FromKey(stackalloc byte[] { 0x0D, 0x1B, 0xA0 }, 1).SliceTo(4);

        AssertDiffAt(path1, path2, 1);
    }

    [Test]
    public void FindFirstDifferent_diff_at_0__odd_odd()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[] { 0x0D, 0xCB, 0xA0 }, 1).SliceTo(4);
        var path2 = NibblePath.FromKey(stackalloc byte[] { 0x01, 0xCB, 0xA0 }, 1).SliceTo(4);

        AssertDiffAt(path1, path2, 0);
    }

    [Test]
    public void FindFirstDifferent_diff_at_last_long_even_even()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[]
            { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0x24, 0x56, 0xAC });
        var path2 = NibblePath.FromKey(stackalloc byte[]
            { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0x24, 0x56, 0xA1 });

        AssertDiffAt(path1, path2, 19);
    }

    [Test]
    public void FindFirstDifferent_diff_at_1_long_even_even()
    {
        var path1 = NibblePath.FromKey(stackalloc byte[]
            { 0xA2, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0x24, 0x56, 0xAC });
        var path2 = NibblePath.FromKey(stackalloc byte[]
            { 0xAD, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0x24, 0x56, 0xAC });

        AssertDiffAt(path1, path2, 1);
    }

    [Test]
    public void SliceFrom([Values(0, 2, 4, 6, 8, 10, 12, 14)] int sliceFrom)
    {
        const int length = 8;
        const ulong value = 0xFE_DC_BA_98_76_54_32_10;
        Span<byte> span = stackalloc byte[length];
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);

        var original = NibblePath.FromKey(span, 0);
        var sliced = original.SliceFrom(sliceFrom);
        var expected = NibblePath.FromKey(span.Slice(sliceFrom / 2), 0);

        Assert.IsTrue(expected.Equals(sliced));
    }

    [Test]
    public void ToString_even_even()
    {
        var path = NibblePath.FromKey(stackalloc byte[] { 0x12, 0x34, 0x56, 0x78 });
        Assert.AreEqual("12345678", path.ToString());
    }

    [Test]
    public void ToString_odd_odd()
    {
        var path = NibblePath.FromKey(stackalloc byte[] { 0x12, 0x34, 0x56, 0x78 }, 1);
        Assert.AreEqual("2345678", path.ToString());
    }

    [Test]
    public void ToString_odd_even()
    {
        var path = NibblePath.FromKey(stackalloc byte[] { 0x12, 0x34, 0x56, 0x78 }, 1).SliceTo(6);
        Assert.AreEqual("234567", path.ToString());
    }

    [Test]
    public void ToString_even_odd()
    {
        var path = NibblePath.FromKey(stackalloc byte[] { 0x12, 0x34, 0x56, 0x78 }).SliceTo(7);
        Assert.AreEqual("1234567", path.ToString());
    }

    [TestCase(0, false, 1)]
    [TestCase(3, false, 4)]
    [TestCase(0, true, 2)]
    [TestCase(3, true, 5)]
    public void NibbleAt(int at, bool odd, byte result)
    {
        var path = NibblePath.FromKey(stackalloc byte[] { 0x12, 0x34, 0x56 }, odd ? 1 : 0);
        Assert.AreEqual(result, path.GetAt(at));
    }
}