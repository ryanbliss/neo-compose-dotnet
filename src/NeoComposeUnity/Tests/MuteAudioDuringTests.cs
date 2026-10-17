// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;
using UnityEditor;

/// <summary>
/// Mutes editor audio for every test in this assembly, then restores the
/// editor's own mute setting, which persists between sessions.
/// </summary>
[SetUpFixture]
public sealed class MuteAudioDuringTests
{
    private bool wasMuted;

    [OneTimeSetUp]
    public void Mute()
    {
        wasMuted = EditorUtility.audioMasterMute;
        EditorUtility.audioMasterMute = true;
    }

    [OneTimeTearDown]
    public void Restore() => EditorUtility.audioMasterMute = wasMuted;
}
