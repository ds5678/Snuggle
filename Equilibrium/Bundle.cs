﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Equilibrium.IO;
using Equilibrium.Models.Bundle;
using Equilibrium.Models.IO;
using JetBrains.Annotations;

namespace Equilibrium {
    [PublicAPI]
    public class Bundle : IDisposable, IRenewable {
        public Bundle(string path, bool cacheData = false) :
            this(File.OpenRead(path), path, FileStreamHandler.Instance.Value, false, cacheData) { }

        public Bundle(Stream dataStream, object tag, IFileHandler fileHandler, bool leaveOpen = false, bool cacheData = false) {
            using var reader = new BiEndianBinaryReader(dataStream, true, leaveOpen);

            Header = UnityBundle.FromReader(reader);
            Container = Header.Format switch {
                UnityFormat.FS => UnityFS.FromReader(reader, Header),
                UnityFormat.Archive => throw new NotImplementedException(),
                UnityFormat.Web => throw new NotImplementedException(),
                UnityFormat.Raw => throw new NotImplementedException(),
                _ => throw new NotImplementedException(),
            };

            DataStart = dataStream.Position;
            Handler = fileHandler;
            Tag = fileHandler.GetTag(tag, this);

            if (ShouldCacheData) {
                CacheData(reader);
            }
        }

        public static ImmutableArray<Bundle> OpenBundleSequence(Stream dataStream, string path, int align = 1, bool leaveOpen = false, bool cacheData = false) {
            var bundles = new List<Bundle>();
            while (dataStream.Position < dataStream.Length) {
                var start = dataStream.Position;
                var bundle = new Bundle(dataStream, new MultiMetaInfo(path, start, 0), MultiStreamHandler.Instance.Value, true, cacheData);
                bundles.Add(bundle);
                dataStream.Seek(start + bundle.Container.Length, SeekOrigin.Begin);

                if (align > 1) {
                    if (dataStream.Position % align == 0) {
                        continue;
                    }

                    var delta = (int) (align - dataStream.Position % align);
                    dataStream.Seek(delta, SeekOrigin.Current);
                }
            }

            if (!leaveOpen) {
                dataStream.Close();
            }

            return bundles.ToImmutableArray();
        }

        public void CacheData(BiEndianBinaryReader? reader = null) {
            if (DataStream != null) {
                return;
            }

            var shouldDispose = false;
            if (reader == null) {
                reader = new BiEndianBinaryReader(Handler.OpenFile(Tag));
                shouldDispose = true;
            }

            DataStream = new MemoryStream(Container.OpenFile(new UnityBundleBlock(0, Container.BlockInfos.Select(x => x.Size).Sum(), 0, ""), reader).ToArray());

            if (shouldDispose) {
                reader.Dispose();
            }
        }

        public void ClearCache() {
            DataStream?.Dispose();
            DataStream = null;
        }

        public UnityBundle Header { get; init; }
        public IUnityContainer Container { get; init; }
        public long DataStart { get; set; }
        public bool ShouldCacheData { get; private set; }
        private Stream? DataStream { get; set; }

        public void Dispose() {
            DataStream?.Dispose();
            Handler.Dispose();
            GC.SuppressFinalize(this);
        }

        public object Tag { get; set; }
        public IFileHandler Handler { get; set; }

        public Span<byte> OpenFile(string path) {
            var block = Container.Blocks.FirstOrDefault(x => x.Path.Equals(path, StringComparison.InvariantCultureIgnoreCase));
            if (block == null) {
                return Span<byte>.Empty;
            }

            BiEndianBinaryReader? reader = null;
            if (!ShouldCacheData ||
                DataStream == null) {
                reader = new BiEndianBinaryReader(Handler.OpenFile(Tag), true);
                reader.BaseStream.Seek(DataStart, SeekOrigin.Begin);
            }

            var data = Container.OpenFile(block, reader, DataStream);
            reader?.Dispose();
            return data;
        }
    }
}
