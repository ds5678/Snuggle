﻿using System;
using System.IO;
using System.Linq;
using K4os.Compression.LZ4;
using Snuggle.Core.Interfaces;
using Snuggle.Core.IO;
using Snuggle.Core.Meta;
using Snuggle.Core.Options;

namespace Snuggle.Core.Models.Bundle;

public record UnityFS(long Size, int CompressedBlockInfoSize, int BlockInfoSize, UnityFSFlags Flags) : UnityContainer {
    public byte[] Hash { get; set; } = new byte[16];
    public override long Length => Size;
    protected override long DataStart => -1;

    public override void ToWriter(BiEndianBinaryWriter writer, UnityBundle header, SnuggleCoreOptions options, UnityBundleBlock[] blocks, Stream blockStream, BundleSerializationOptions serializationOptions, long start) {
        var headerStart = writer.BaseStream.Position;
        writer.Write(0L);
        writer.Write(0);
        writer.Write(0);
        var flags = UnityFSFlags.CombinedData | (UnityFSFlags) serializationOptions.CompressionType | (Flags & (UnityFSFlags) 0b11111111111111111111111100000000);
        writer.Write((int) (UnityFSFlags.CombinedData | (UnityFSFlags) serializationOptions.CompressionType));
        if (serializationOptions.TargetFormatVersion >= 7) {
            writer.Align(16);
        }

        using var blockInfoStream = new MemoryStream();
        using var blockDataStream = new MemoryStream();
        using var blockInfoWriter = new BiEndianBinaryWriter(blockInfoStream, true);
        blockInfoWriter.Write(Hash);
        var (blockSize, unityCompressionType, blockCompressionType) = serializationOptions;
        var blockLength = blocks.Sum(x => x.Size);
        var blockInfoCount = blockCompressionType == UnityCompressionType.None ? 1 : (int) Math.Ceiling((double) blockLength / blockSize);

        var blockInfoStart = blockInfoStream.Position;
        var blockStart = blockInfoStart + 4 + blockInfoCount * 10;
        blockInfoStream.Seek(blockStart, SeekOrigin.Begin);
        UnityBundleBlock.ArrayToWriter(blockInfoWriter, blocks, header, options, serializationOptions);
        blockInfoStream.Seek(blockInfoStart, SeekOrigin.Begin);
        blockInfoWriter.Write(blockInfoCount);

        if (blockCompressionType is not (UnityCompressionType.None or UnityCompressionType.LZMA)) {
            var chunk = new byte[blockSize].AsSpan();
            for (var i = 0; i < blockInfoCount; ++i) {
                var size = (int) Math.Min(blockSize, blockStream.Length - blockStream.Position);
                blockStream.ReadExactly(chunk[..size]);
                UnityBundleBlockInfo.ToWriterChunked(blockInfoWriter, header, options, serializationOptions, blockDataStream, chunk[..size]);
            }
        } else {
            UnityBundleBlockInfo.ToWriter(blockInfoWriter, header, serializationOptions, blockDataStream, blockStream);
        }

        var blockInfoSize = (int) blockInfoStream.Length;
        blockInfoStream.Seek(0, SeekOrigin.Begin);
        using var compressedStream = new MemoryStream();
        switch (unityCompressionType) {
            case UnityCompressionType.None: {
                blockInfoStream.CopyTo(compressedStream);
                break;
            }
            case UnityCompressionType.LZMA: {
                Utils.EncodeLZMA(compressedStream, blockInfoStream, (int) blockInfoStream.Length);
                break;
            }
            case UnityCompressionType.LZ4:
            case UnityCompressionType.LZ4HC: {
                Utils.CompressLZ4(blockInfoStream, compressedStream, unityCompressionType == UnityCompressionType.LZ4HC ? LZ4Level.L12_MAX : LZ4Level.L00_FAST, (int) blockInfoStream.Length);
                break;
            }
            default:
                throw new NotSupportedException($"Unity Compression format {unityCompressionType:G} is not supported");
        }

        compressedStream.Seek(0, SeekOrigin.Begin);
        compressedStream.CopyTo(writer.BaseStream);
        blockDataStream.Seek(0, SeekOrigin.Begin);
        blockDataStream.CopyTo(writer.BaseStream);

        if (flags.HasFlag(UnityFSFlags.BlockInfoNeedPaddingAtStart)) {
            writer.Align(16);
        }

        writer.BaseStream.Seek(headerStart, SeekOrigin.Begin);
        writer.Write(writer.BaseStream.Length - start);
        writer.Write((int) compressedStream.Length);
        writer.Write(blockInfoSize);
    }

    public static UnityFS FromReader(BiEndianBinaryReader reader, UnityBundle header, SnuggleCoreOptions options) {
        var size = reader.ReadInt64();
        var compressedBlockSize = reader.ReadInt32();
        var blockSize = reader.ReadInt32();
        var flags = (UnityFSFlags) reader.ReadUInt32();

        if (options.Game is UnityGame.PokemonUnite && flags.HasFlag(UnityFSFlags.Encrypted)) {
            throw new NotSupportedException("Pokemon Unite bundle is encrypted, use UntieUnite or another decryption tool");
        }

        var fs = new UnityFS(size, compressedBlockSize, blockSize, flags);
        var pos = reader.BaseStream.Position;
        if (fs.Flags.HasFlag(UnityFSFlags.BlocksInfoAtEnd)) {
            reader.BaseStream.Seek(fs.CompressedBlockInfoSize, SeekOrigin.End);
        } else if (header.FormatVersion >= 7) {
            reader.Align(16);
        }

        var compressionType = (UnityCompressionType) (fs.Flags & UnityFSFlags.CompressionRange);
        using var blocksReader = new BiEndianBinaryReader(
            compressionType switch {
                UnityCompressionType.None => new OffsetStream(reader.BaseStream, length: fs.BlockInfoSize),
                UnityCompressionType.LZMA => Utils.DecodeLZMA(reader.BaseStream, fs.CompressedBlockInfoSize, fs.BlockInfoSize),
                UnityCompressionType.LZ4 => Utils.DecompressLZ4(reader.BaseStream, fs.CompressedBlockInfoSize, fs.BlockInfoSize),
                UnityCompressionType.LZ4HC => Utils.DecompressLZ4(reader.BaseStream, fs.CompressedBlockInfoSize, fs.BlockInfoSize),
                _ => throw new NotSupportedException($"Unity Compression format {compressionType} is not supported"),
            },
            true,
            compressionType == UnityCompressionType.None);
        blocksReader.BaseStream.Seek(0, SeekOrigin.Begin);
        fs.Hash = blocksReader.ReadBytes(16);
        var infoCount = blocksReader.ReadInt32();
        fs.BlockInfos = UnityBundleBlockInfo.ArrayFromReader(blocksReader, header, (int) flags, infoCount, options);
        var blockCount = blocksReader.ReadInt32();
        fs.Blocks = UnityBundleBlock.ArrayFromReader(blocksReader, header, (int) flags, blockCount, options);

        if (fs.Flags.HasFlag(UnityFSFlags.BlocksInfoAtEnd)) {
            reader.BaseStream.Seek(pos, SeekOrigin.Begin);
        }

        if (flags.HasFlag(UnityFSFlags.BlockInfoNeedPaddingAtStart)) {
            reader.Align(16);
        }

        return fs;
    }
}
