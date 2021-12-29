﻿using System;
using JetBrains.Annotations;

namespace Snuggle.Core.Exceptions;

[PublicAPI]
public class PPtrNullReference : Exception {
    public PPtrNullReference(object classId) : base($"PPtr<{classId:G}> failed to resolve properly") { }
    public PPtrNullReference(object classId, Exception e) : base($"PPtr<{classId:G}> failed to resolve properly", e) { }
}
