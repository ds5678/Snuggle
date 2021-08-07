﻿using Equilibrium.IO;
using JetBrains.Annotations;

namespace Equilibrium.Models.Objects.Settings {
    [PublicAPI]
    public record GoogleCardboardSettings(
        int DepthFormat,
        bool EnableTransitionView) {
        public static GoogleCardboardSettings Default { get; } = new(0, false);

        public static GoogleCardboardSettings FromReader(BiEndianBinaryReader reader, SerializedFile file) {
            var format = reader.ReadInt32();
            var enable = reader.ReadBoolean();
            reader.Align();
            return new GoogleCardboardSettings(format, enable);
        }
    }
}
