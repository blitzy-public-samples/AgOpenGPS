// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgOpenGPS
{
    // [XPLAT] CSound is re-platformed off the Windows-only System.Media.SoundPlayer onto a small,
    // self-contained cross-platform audio wrapper (CSoundClip, nested below). Behaviour is frozen: the
    // eleven cue fields keep their exact names and their exact wav-resource mapping, the bool state
    // flags are unchanged, and the constructor still reads the same four user sound toggles — so the
    // "which sound plays on which event" contract is byte-for-byte identical to the net48/WinForms
    // build. Only the playback mechanism changes; see CSoundClip for the per-OS strategy and the
    // graceful-degradation (silent no-op) guarantee that keeps audio failures off the real-time loop.
    public class CSound
    {
        //sound objects - wave files in resources
        // [XPLAT] Field type SoundPlayer -> CSoundClip; field names and the resource mapping are
        // preserved verbatim. Each wav resource is passed as a deferred factory (() => Properties.Resources.X)
        // so the cross-platform Resources.Wav() File.OpenRead happens inside CSoundClip's guarded ctor —
        // a missing/locked wav then degrades to a silent no-op instead of throwing during app startup.
        public readonly CSoundClip sndBoundaryAlarm = new CSoundClip(() => Properties.Resources.Alarm10);
        public readonly CSoundClip sndUTurnTooClose = new CSoundClip(() => Properties.Resources.TF012);

        public readonly CSoundClip sndAutoSteerOn = new CSoundClip(() => Properties.Resources.SteerOn);
        public readonly CSoundClip sndAutoSteerOff = new CSoundClip(() => Properties.Resources.SteerOff);
        public readonly CSoundClip sndHydLiftUp = new CSoundClip(() => Properties.Resources.HydUp);
        public readonly CSoundClip sndHydLiftDn = new CSoundClip(() => Properties.Resources.HydDown);
        public readonly CSoundClip sndRTKAlarm = new CSoundClip(() => Properties.Resources.rtk_lost);
        public readonly CSoundClip sndSectionOn = new CSoundClip(() => Properties.Resources.SectionOn);
        public readonly CSoundClip sndSectionOff = new CSoundClip(() => Properties.Resources.SectionOff);
        public readonly CSoundClip sndHeadland = new CSoundClip(() => Properties.Resources.Headland);
        public readonly CSoundClip sndRTKRecoverd = new CSoundClip(() => Properties.Resources.rtk_back);


        public bool isBoundAlarming, isRTKAlarming;
        public bool RTKWasAlarming = false;


        public bool isSteerSoundOn, isTurnSoundOn, isHydLiftSoundOn, isSectionsSoundOn;

        public bool isHydLiftChange;

        public CSound()
        {
            isSteerSoundOn = Properties.Settings.Default.setSound_isAutoSteerOn;
            isHydLiftSoundOn = Properties.Settings.Default.setSound_isHydLiftOn;
            isTurnSoundOn = Properties.Settings.Default.setSound_isUturnOn;
            isSectionsSoundOn = Properties.Settings.Default.setSound_isSectionsOn;
        }

        /// <summary>
        ///   [XPLAT] Minimal cross-platform audio cue, replacing the Windows-only
        ///   <c>System.Media.SoundPlayer</c>. It is constructed from a wav resource (passed as a
        ///   deferred <see cref="Stream"/> factory so resource access is guarded), eagerly reads and
        ///   caches the wav bytes once, then exposes a single fire-and-forget <see cref="Play"/> that
        ///   restarts playback from the beginning on every call — matching <c>SoundPlayer.Play()</c>'s
        ///   asynchronous semantics, so alarm cues re-trigger every frame while their alarm flag is set.
        ///   <para>
        ///   Playback is platform-gated with graceful degradation (AAP §0.7.2): Windows plays the cached
        ///   bytes in-memory through the always-present <c>winmm</c> <c>PlaySound</c> API (no extra
        ///   package, so it compiles for both the net8.0 and net8.0-windows targets); Linux and macOS
        ///   write the bytes to a temp file once and spawn an external player (paplay/aplay/afplay). Every
        ///   path is wrapped so a missing wav, an absent player, or any audio error becomes a silent
        ///   no-op: audio never throws and never blocks the receive-&gt;fuse-&gt;steer-&gt;section loop.
        ///   </para>
        ///   <para>
        ///   Type is <c>public</c> because the eleven <c>CSound</c> cue fields are public; only
        ///   <see cref="Play"/> is consumed by the rest of the application (verified by repo-wide scan).
        ///   </para>
        /// </summary>
        public sealed class CSoundClip
        {
            // [XPLAT] winmm PlaySound flags (mmsystem.h); referenced only on the Windows playback path.
            private const uint SND_ASYNC = 0x0001;      // play asynchronously (PlaySound returns at once)
            private const uint SND_NODEFAULT = 0x0002;  // do not fall back to the default system beep
            private const uint SND_MEMORY = 0x0004;     // pszSound points to an in-memory .wav image

            // Cached wav bytes. Null => the resource was unreadable => Play() is a silent no-op.
            private readonly byte[] _wav;

            // Windows in-memory playback: the cached buffer is pinned for the clip's lifetime because
            // SND_ASYNC keeps reading the buffer after PlaySound returns, so it must not move or be
            // collected. Non-Windows never touches winmm, so the buffer is not pinned there.
            private GCHandle _pin;
            private readonly IntPtr _wavPtr;

            // Unix playback: the cached bytes are materialised to a temp .wav once, then reused across
            // plays so the audio path never performs per-frame disk writes.
            private string _tempPath;
            private readonly object _tempLock = new object();

            /// <summary>
            ///   Reads and caches the wav bytes produced by <paramref name="streamFactory"/>. The factory
            ///   is invoked inside a guard so the cross-platform <c>Resources.Wav()</c> <c>File.OpenRead</c>
            ///   (which can throw if the packaged wav is missing) degrades to a silent no-op rather than
            ///   failing <see cref="CSound"/> construction during application startup.
            /// </summary>
            /// <param name="streamFactory">
            ///   Deferred accessor for the wav resource stream (e.g. <c>() =&gt; Properties.Resources.Alarm10</c>).
            ///   The returned stream is fully read into the byte cache and then disposed (this clip owns it).
            /// </param>
            public CSoundClip(Func<Stream> streamFactory)
            {
                try
                {
                    if (streamFactory != null)
                    {
                        // The factory owns the stream's creation; we own its disposal once cached.
                        using (Stream source = streamFactory())
                        {
                            if (source != null)
                            {
                                using (var buffer = new MemoryStream())
                                {
                                    source.CopyTo(buffer);
                                    if (buffer.Length > 0)
                                    {
                                        _wav = buffer.ToArray();
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Unreadable/missing resource => no cached bytes => Play() becomes a silent no-op.
                    _wav = null;
                }

                // Pin the cached buffer once for Windows SND_MEMORY|SND_ASYNC playback (the OS reads it
                // asynchronously after the call returns). Skipped on non-Windows, which never uses winmm.
                if (_wav != null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        _pin = GCHandle.Alloc(_wav, GCHandleType.Pinned);
                        _wavPtr = _pin.AddrOfPinnedObject();
                    }
                    catch
                    {
                        // Pinning failure => fall through with a null pointer => Windows playback no-ops.
                        _wavPtr = IntPtr.Zero;
                    }
                }
            }

            /// <summary>
            ///   Fire-and-forget playback. Restarts the cue from the beginning on each call (parity with
            ///   <c>SoundPlayer.Play()</c>). Never throws and never blocks the calling (scan-loop/UI) thread.
            /// </summary>
            public void Play()
            {
                if (_wav == null)
                {
                    return; // no audio data => graceful no-op
                }

                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        PlayWindows();
                    }
                    else
                    {
                        // Spawning an external player can cost a few milliseconds, so run it off the
                        // calling thread; no latency is added to the real-time guidance path.
                        ThreadPool.QueueUserWorkItem(static clip => clip.PlayUnix(), this, preferLocal: false);
                    }
                }
                catch
                {
                    // Audio is best-effort; never propagate a failure into the guidance loop.
                }
            }

            // ----- Windows: in-memory asynchronous playback via winmm (no extra package required) -----

            private void PlayWindows()
            {
                IntPtr ptr = _wavPtr;
                if (ptr == IntPtr.Zero)
                {
                    return;
                }

                // SND_ASYNC returns immediately; issuing PlaySound again restarts the cue from the start,
                // exactly matching the former SoundPlayer.Play() behaviour.
                PlaySound(ptr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            }

            // P/Invoke into the Windows multimedia library. The declaration compiles on every target
            // framework/RID; it is only ever invoked behind the RuntimeInformation.IsOSPlatform(Windows)
            // guard above, so it is never resolved on Linux/macOS.
            [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = false)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

            // ----- Linux/macOS: materialise the wav once, then spawn a system player fire-and-forget ---

            private void PlayUnix()
            {
                try
                {
                    string file = EnsureTempFile();
                    if (file == null)
                    {
                        return;
                    }

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        TrySpawn("afplay", file);
                    }
                    else
                    {
                        // Linux (and other Unix): prefer PulseAudio/PipeWire's paplay, fall back to ALSA's aplay.
                        if (!TrySpawn("paplay", file))
                        {
                            TrySpawn("aplay", file);
                        }
                    }
                }
                catch
                {
                    // best-effort: any failure on the external-player path is a silent no-op
                }
            }

            /// <summary>
            ///   Writes the cached wav to a temp file exactly once (thread-safe, double-checked) and
            ///   returns its path, or <c>null</c> when no bytes are cached or the write fails.
            /// </summary>
            private string EnsureTempFile()
            {
                string path = _tempPath;
                if (path != null)
                {
                    return path;
                }

                lock (_tempLock)
                {
                    if (_tempPath != null)
                    {
                        return _tempPath;
                    }

                    try
                    {
                        byte[] bytes = _wav;
                        if (bytes == null)
                        {
                            return null;
                        }

                        string candidate = Path.Combine(
                            Path.GetTempPath(),
                            "aog_snd_" + Guid.NewGuid().ToString("N") + ".wav");
                        File.WriteAllBytes(candidate, bytes);
                        _tempPath = candidate;
                        return _tempPath;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            /// <summary>
            ///   Launches <paramref name="player"/> on <paramref name="file"/> fire-and-forget, returning
            ///   <c>true</c> if the process started. Any failure (e.g. the player is not installed) is
            ///   swallowed and reported as <c>false</c> so the caller can try the next candidate player.
            /// </summary>
            private static bool TrySpawn(string player, string file)
            {
                try
                {
                    var psi = new ProcessStartInfo(player)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(file);

                    // Disposing the wrapper does not terminate the OS process; the cue plays to completion.
                    using (Process process = Process.Start(psi))
                    {
                        return process != null;
                    }
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            ///   Releases the pinned playback buffer and best-effort removes the temp file. These clips
            ///   live for the application lifetime and are never explicitly disposed, so this cleanup runs
            ///   at finalization / process teardown.
            /// </summary>
            ~CSoundClip()
            {
                try
                {
                    if (_pin.IsAllocated)
                    {
                        _pin.Free();
                    }
                }
                catch
                {
                    // ignore — process is tearing down
                }

                try
                {
                    string path = _tempPath;
                    if (path != null && File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // ignore — best-effort cleanup only
                }
            }
        }
    }
}
