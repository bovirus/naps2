﻿using System.Runtime.InteropServices;
using NAPS2.Platform.Windows;

namespace NAPS2.Platform;

internal abstract class WindowsSystemCompat : ISystemCompat
{
    public bool IsWiaDriverSupported => true;

    public bool IsTwainDriverSupported => true;

    public bool IsAppleDriverSupported => false;

    public bool IsSaneDriverSupported => false;

    public bool IsEsclDriverSupported => true;

    public bool SupportsShowPageNumbers => true;

    public bool SupportsProfilesToolbar => true;

    public bool SupportsButtonActions => true;

    public bool SupportsSingleInstance => true;

    public bool CanUseWin32 => true;

    public bool CanEmail => true;

    public bool CanPrint => true;

    public bool CombinedPdfAndImageSaving => false;

    public bool ShouldRememberBackgroundOperations => true;

    public bool RenderInWorker => true;

    public bool SupportsWinX86Worker => true;

    public string WorkerCrashMessage => SdkResources.WorkerCrashWindows;

    public abstract string[] ExeSearchPaths { get;  }

    public abstract string[] LibrarySearchPaths { get;  }

    public string TesseractExecutableName => "tesseract.exe";

    public string PdfiumLibraryName => "pdfium.dll";

    public string[]? SaneLibraryDeps => null;

    public string SaneLibraryName => "sane.dll";

    public bool IsLibUsbReliable => true;

    public IntPtr LoadLibrary(string path) => Win32.LoadLibrary(path);

    public string GetLoadError() => Marshal.GetLastWin32Error().ToString();

    public abstract IntPtr LoadSymbol(IntPtr libraryHandle, string symbol);

    public void SetEnv(string name, string value) => throw new NotSupportedException();

    public IDisposable? FileReadLock(string path) => null;

    public IDisposable? FileWriteLock(string path) => null;
}