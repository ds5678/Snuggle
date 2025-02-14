﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace Snuggle.Core;

public static class Utils {
    private static readonly CoderPropID[] PropIDs = { CoderPropID.DictionarySize, CoderPropID.PosStateBits, CoderPropID.LitContextBits, CoderPropID.LitPosBits, CoderPropID.Algorithm, CoderPropID.NumFastBytes, CoderPropID.MatchFinder, CoderPropID.EndMarker };

    private static readonly object[] Properties = { 1 << 23, 2, 3, 0, 2, 128, "bt4", false };

    internal static Stream DecodeLZMA(Stream inStream, int compressedSize, int size, Stream? outStream = null) {
        outStream ??= new MemoryStream(size) { Position = 0 };
        var coder = new Decoder();
        Span<byte> properties = stackalloc byte[5];
        inStream.ReadExactly(properties);
        coder.SetDecoderProperties(properties.ToArray());
        coder.Code(inStream, outStream, compressedSize - 5, size, null);
        return outStream;
    }

    public static void EncodeLZMA(Stream outStream, Stream inStream, long size, CoderPropID[]? propIds = null, object[]? properties = null) {
        var coder = new Encoder();
        coder.SetCoderProperties(propIds ?? PropIDs, properties ?? Properties);
        coder.WriteCoderProperties(outStream);
        coder.Code(inStream, outStream, size, -1, null);
    }

    public static void EncodeLZMA(Stream outStream, Span<byte> inStream, long size, CoderPropID[]? propIds = null, object[]? properties = null) {
        var coder = new Encoder();
        using var ms = new MemoryStream(inStream.ToArray()) { Position = 0 };
        coder.SetCoderProperties(propIds ?? PropIDs, properties ?? Properties);
        coder.WriteCoderProperties(outStream);
        coder.Code(ms, outStream, size, -1, null);
    }

    public static Stream DecompressLZ4(Stream inStream, int compressedSize, int size, Stream? outStream = null) {
        outStream ??= new MemoryStream(size) { Position = 0 };
        var inPool = new byte[compressedSize].AsSpan();
        var outPool = new byte[size].AsSpan();
        inStream.ReadExactly(inPool);
        var amount = LZ4Codec.Decode(inPool, outPool);
        outStream.Write(outPool[..amount]);
        return outStream;
    }

    public static void CompressLZ4(Stream inStream, Stream outStream, LZ4Level level, int size) {
        var inPool = new byte[size].AsSpan();
        var outPool = new byte[LZ4Codec.MaximumOutputSize(size)].AsSpan();
        inStream.ReadExactly(inPool);
        var amount = LZ4Codec.Encode(inPool, outPool, level);
        outStream.Write(outPool[..amount]);
    }

    public static void CompressLZ4(Span<byte> inPool, Stream outStream, LZ4Level level) {
        var outPool = new byte[LZ4Codec.MaximumOutputSize(inPool.Length)].AsSpan();
        var amount = LZ4Codec.Encode(inPool, outPool, level);
        outStream.Write(outPool[..amount]);
    }

    public static float[] UnwrapRGBA(uint rgba) {
        return new[] { (rgba & 0xFF) / (float) 0xFF, ((rgba >> 8) & 0xFF) / (float) 0xFF, ((rgba >> 16) & 0xFF) / (float) 0xFF, ((rgba >> 24) & 0xFF) / (float) 0xFF };
    }

    public static Memory<byte> AsBytes<T>(this Memory<T> memory) where T : struct => new(MemoryMarshal.AsBytes(memory.Span).ToArray());
}
