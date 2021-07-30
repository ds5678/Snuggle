﻿using System;
using JetBrains.Annotations;

namespace Equilibrium.Meta {
    [PublicAPI, AttributeUsage(AttributeTargets.Enum)]
    public class ClassIdExtensionAttribute : Attribute {
        public ClassIdExtensionAttribute(UnityGame game) => Game = game;

        public UnityGame Game { get; }

        public override object TypeId => Game;
        public override bool IsDefaultAttribute() => Game == UnityGame.Default;
        public override int GetHashCode() => Game.GetHashCode();
        public override string ToString() => $"ClassIdExtension {{ Game = {Game:G} }}";
    }
}
