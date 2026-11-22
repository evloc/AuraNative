using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Aura
{
    // Media Foundation, Windows' built-in media framework: reading songs as raw sound (Source Reader)
    // and writing MP3 files (Sink Writer + Windows' MP3 encoder).
    static class Mf
    {
        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int max, out IMFMediaBuffer buffer);
        [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);

        static bool started;
        public static void Start()
        {
            if (started) return;
            int hr = MFStartup(0x20070, 0);
            if (hr < 0) throw new COMException("Media Foundation didn't start", hr);
            started = true;
        }

        public static void Check(int hr, string what) { if (hr < 0) throw new COMException(what + " (0x" + hr.ToString("X8") + ")", hr); }

        public static Guid MajorType = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
        public static Guid SubType = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
        public static Guid Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static Guid Float = new Guid("00000003-0000-0010-8000-00AA00389B71");
        public static Guid Pcm = new Guid("00000001-0000-0010-8000-00AA00389B71");
        public static Guid Mp3 = new Guid("00000055-0000-0010-8000-00AA00389B71");
        public static Guid Channels = new Guid("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
        public static Guid Rate = new Guid("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
        public static Guid BytesPerSecond = new Guid("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
        public static Guid BlockAlign = new Guid("322DE230-9EEB-43BD-AB7A-FF412251541D");
        public static Guid Bits = new Guid("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
        public const int FirstAudio = unchecked((int)0xFFFFFFFD), AllStreams = unchecked((int)0xFFFFFFFE);

        // IMFAttributes' 30 methods come first in every attribute-based interface (order matters)
        [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaType
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare();
            [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
            void GetUINT64(); void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
            void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
            [PreserveSig] int SetUINT32([In] ref Guid key, int value);
            void SetUINT64(); void SetDouble();
            [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
            void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
            // IMFMediaType
            void GetMajorType(); void IsCompressedFormat(); void IsEqual(); void GetRepresentation(); void FreeRepresentation();
        }

        [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSample
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64(); void GetDouble(); void GetGUID();
            void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown();
            void SetItem(); void DeleteItem(); void DeleteAllItems(); void SetUINT32(); void SetUINT64(); void SetDouble(); void SetGUID(); void SetString();
            void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
            // IMFSample
            void GetSampleFlags(); void SetSampleFlags();
            void GetSampleTime();
            [PreserveSig] int SetSampleTime(long time);
            void GetSampleDuration();
            [PreserveSig] int SetSampleDuration(long duration);
            void GetBufferCount(); void GetBufferByIndex();
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
            void RemoveBufferByIndex(); void RemoveAllBuffers(); void GetTotalLength(); void CopyToBuffer();
        }

        [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr data, out int max, out int current);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int length);
            [PreserveSig] int SetCurrentLength(int length);
            [PreserveSig] int GetMaxLength(out int length);
        }

        [ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSourceReader
        {
            [PreserveSig] int GetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] out bool selected);
            [PreserveSig] int SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
            [PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
            [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
            [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
            [PreserveSig] int SetCurrentPosition([In] ref Guid format, IntPtr position);
            [PreserveSig] int ReadSample(int stream, int flags, out int actualStream, out int streamFlags, out long timestamp, out IMFSample sample);
            void Flush(); void GetServiceForStream(); void GetPresentationAttribute();
        }

        [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSinkWriter
        {
            [PreserveSig] int AddStream(IMFMediaType type, out int index);
            [PreserveSig] int SetInputMediaType(int index, IMFMediaType type, IntPtr encodingParameters);
            [PreserveSig] int BeginWriting();
            [PreserveSig] int WriteSample(int index, IMFSample sample);
            void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
            [PreserveSig] int DoFinalize();
            void GetServiceForStream(); void GetStatistics();
        }

        static void Release(object o) { if (o != null) try { Marshal.ReleaseComObject(o); } catch { } }

        // the whole song as 32-bit float samples, interleaved; calls back with each block
        public static void Read(string path, Action<float[], int, int, int> block, Func<bool> cancelled)
        {
            Start();
            Check(MFCreateSourceReaderFromURL(new Uri(path).AbsoluteUri, IntPtr.Zero, out var reader), "Windows couldn't open the song");
            IMFMediaType want = null, got = null;
            try
            {
                reader.SetStreamSelection(AllStreams, false);
                reader.SetStreamSelection(FirstAudio, true);
                Check(MFCreateMediaType(out want), "media type");
                want.SetGUID(ref MajorType, ref Audio);
                want.SetGUID(ref SubType, ref Float);
                Check(reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, want), "Windows can't decode this kind of file");
                Check(reader.GetCurrentMediaType(FirstAudio, out got), "media type");
                got.GetUINT32(ref Channels, out int ch);
                got.GetUINT32(ref Rate, out int rate);
                if (ch <= 0 || rate <= 0) throw new InvalidOperationException("no sound in this file");
                var buf = new float[0];
                while (cancelled == null || !cancelled())
                {
                    Check(reader.ReadSample(FirstAudio, 0, out _, out int flags, out _, out var sample), "reading the song");
                    if ((flags & 0x1) != 0) { if (sample != null) Release(sample); throw new System.IO.IOException("Windows hit an error reading the song"); }
                    if ((flags & 0x20) != 0 && reader.GetCurrentMediaType(FirstAudio, out var changed) >= 0)
                    {
                        changed.GetUINT32(ref Channels, out int c2); changed.GetUINT32(ref Rate, out int r2);
                        if (c2 > 0) ch = c2; if (r2 > 0) rate = r2;
                        Release(changed);
                    }
                    if (sample != null)
                    {
                        try
                        {
                            Check(sample.ConvertToContiguousBuffer(out var mb), "buffer");
                            try
                            {
                                mb.Lock(out IntPtr p, out _, out int len);
                                int n = len / 4;
                                if (buf.Length < n) buf = new float[n];
                                Marshal.Copy(p, buf, 0, n);
                                mb.Unlock();
                                block(buf, n, ch, rate);
                            }
                            finally { Release(mb); }
                        }
                        finally { Release(sample); }
                    }
                    if ((flags & 0x2) != 0) break;          // end of the song
                }
            }
            finally { Release(want); Release(got); Release(reader); }
        }

        // writes 16-bit PCM (interleaved) as an MP3 using Windows' MP3 encoder (Windows 8 and later)
        public sealed class Mp3Writer : IDisposable
        {
            IMFSinkWriter writer; IMFMediaType outT, inT;
            readonly int idx, channels, rate;
            long time;
            public Mp3Writer(string path, int channels, int rate, int kbps)
            {
                Start();
                this.channels = channels; this.rate = rate;
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out writer), "Windows couldn't create the MP3 file");
                Check(MFCreateMediaType(out outT), "media type");
                outT.SetGUID(ref MajorType, ref Audio);
                outT.SetGUID(ref SubType, ref Mp3);
                outT.SetUINT32(ref Channels, channels);
                outT.SetUINT32(ref Rate, rate);
                outT.SetUINT32(ref BytesPerSecond, kbps * 1000 / 8);
                Check(writer.AddStream(outT, out idx), "Windows' MP3 encoder isn't available");
                Check(MFCreateMediaType(out inT), "media type");
                inT.SetGUID(ref MajorType, ref Audio);
                inT.SetGUID(ref SubType, ref Pcm);
                inT.SetUINT32(ref Channels, channels);
                inT.SetUINT32(ref Rate, rate);
                inT.SetUINT32(ref Bits, 16);
                inT.SetUINT32(ref BlockAlign, channels * 2);
                inT.SetUINT32(ref BytesPerSecond, rate * channels * 2);
                Check(writer.SetInputMediaType(idx, inT, IntPtr.Zero), "the MP3 encoder didn't accept the sound");
                Check(writer.BeginWriting(), "writing");
            }

            public void Write(short[] pcm, int n)
            {
                if (n <= 0) return;
                Check(MFCreateMemoryBuffer(n * 2, out var mb), "buffer");
                Check(MFCreateSample(out var sample), "sample");
                try
                {
                    mb.Lock(out IntPtr p, out _, out _);
                    Marshal.Copy(pcm, 0, p, n);
                    mb.Unlock();
                    mb.SetCurrentLength(n * 2);
                    sample.AddBuffer(mb);
                    long dur = (long)(n / channels) * 10000000L / rate;
                    sample.SetSampleTime(time); sample.SetSampleDuration(dur);
                    time += dur;
                    Check(writer.WriteSample(idx, sample), "writing");
                }
                finally { Release(sample); Release(mb); }
            }

            public void Finish() => Check(writer.DoFinalize(), "finishing the MP3");

            public void Dispose() { Release(outT); Release(inT); Release(writer); outT = inT = null; writer = null; }
        }
    }
}
