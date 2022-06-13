﻿using System.Threading;
using NAPS2.ImportExport;
using NAPS2.Ocr;
using NAPS2.Platform.Windows;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Internal;
using NAPS2.Wia;
using NAPS2.WinForms;

namespace NAPS2.Scan;

internal class ScanPerformer : IScanPerformer
{
    private readonly ScanningContext _scanningContext;
    private readonly IFormFactory _formFactory;
    private readonly ScopedConfig _config;
    private readonly OperationProgress _operationProgress;
    private readonly AutoSaver _autoSaver;
    private readonly IProfileManager _profileManager;
    private readonly ErrorOutput _errorOutput;
    private readonly ScanOptionsValidator _scanOptionsValidator;
    private readonly IScanBridgeFactory _scanBridgeFactory;
    private readonly OcrEngineManager _ocrEngineManager;

    public ScanPerformer(IFormFactory formFactory, ScopedConfig config, OperationProgress operationProgress,
        AutoSaver autoSaver, IProfileManager profileManager, ErrorOutput errorOutput,
        ScanOptionsValidator scanOptionsValidator, IScanBridgeFactory scanBridgeFactory,
        ScanningContext scanningContext, OcrEngineManager ocrEngineManager)
    {
        _formFactory = formFactory;
        _config = config;
        _operationProgress = operationProgress;
        _autoSaver = autoSaver;
        _profileManager = profileManager;
        _errorOutput = errorOutput;
        _scanOptionsValidator = scanOptionsValidator;
        _scanBridgeFactory = scanBridgeFactory;
        _scanningContext = scanningContext;
        _ocrEngineManager = ocrEngineManager;
    }

    public async Task<ScanDevice> PromptForDevice(ScanProfile scanProfile, IntPtr dialogParent = default)
    {
        var options = BuildOptions(scanProfile, new ScanParams(), dialogParent);
        return await PromptForDevice(options);
    }

    public async Task<ScannedImageSource> PerformScan(ScanProfile scanProfile, ScanParams scanParams,
        IntPtr dialogParent = default, CancellationToken cancelToken = default)
    {
        var options = BuildOptions(scanProfile, scanParams, dialogParent);
        if (!await PopulateDevice(scanProfile, options))
        {
            // User cancelled out of a dialog
            return ScannedImageSource.Empty;
        }

        var localPostProcessor = new LocalPostProcessor(ConfigureOcrController(scanParams));
        var controller = new ScanController(localPostProcessor, _scanOptionsValidator, _scanBridgeFactory);
        // TODO: Consider how to handle operations with Twain (right now there are duplicate progress windows).
        var op = new ScanOperation(options.Device, options.PaperSource);

        controller.PageStart += (sender, args) => op.NextPage(args.PageNumber);
        controller.ScanEnd += (sender, args) => op.Completed();
        controller.ScanError += (sender, args) => HandleError(args.Exception);
        TranslateProgress(controller, op);

        ShowOperation(op, scanParams);
        cancelToken.Register(op.Cancel);

        var source = controller.Scan(options, op.CancelToken);

        if (scanProfile.EnableAutoSave && scanProfile.AutoSaveSettings != null)
        {
            source = _autoSaver.Save(scanProfile.AutoSaveSettings, source);
        }

        var sink = new ScannedImageSink();
        source.ForEach(img => sink.PutImage(img)).ContinueWith(t =>
        {
            // Errors are handled by the ScanError callback so we ignore them here
            if (sink.ImageCount > 0)
            {
                Log.Event(EventType.Scan, new EventParams
                {
                    Name = MiscResources.Scan,
                    Pages = sink.ImageCount,
                    DeviceName = scanProfile.Device?.Name,
                    ProfileName = scanProfile.DisplayName,
                    BitDepth = scanProfile.BitDepth.Description()
                });
            }

            sink.SetCompleted();
        }).AssertNoAwait();
        return sink.AsSource();
    }

    private OcrController ConfigureOcrController(ScanParams scanParams)
    {
        OcrController ocrController = new OcrController(_scanningContext);
        if (scanParams.DoOcr)
        {
            ocrController.Engine = _ocrEngineManager.ActiveEngine;
            if (ocrController.Engine == null)
            {
                Log.Error("OCR is enabled but no OCR engine is available.");
            }
            else
            {
                ocrController.EnableOcr = true;
                ocrController.LanguageCode = scanParams.OcrParams.LanguageCode;
                ocrController.Mode = scanParams.OcrParams.Mode;
                ocrController.TimeoutInSeconds = scanParams.OcrParams.TimeoutInSeconds;
                // TODO: Make DoOcr mean just foreground OCR again, and check the config here to enable background ocr 
                ocrController.Priority = OcrPriority.Foreground;
                scanParams.OcrCancelToken.Register(() => ocrController.CancelAll());
            }
        }

        return ocrController;
    }

    private void HandleError(Exception error)
    {
        if (!(error is ScanDriverException))
        {
            Log.ErrorException(error.Message, error);
            _errorOutput?.DisplayError(error.Message, error);
        }
        else if (error is ScanDriverUnknownException)
        {
            Log.ErrorException(error.Message, error.InnerException);
            _errorOutput?.DisplayError(error.Message, error);
        }
        else
        {
            _errorOutput?.DisplayError(error.Message);
        }
    }

    private void ShowOperation(ScanOperation op, ScanParams scanParams)
    {
        if (scanParams.NoUI)
        {
            return;
        }

        Task.Run(() =>
        {
            Invoker.Current.SafeInvoke(() =>
            {
                if (scanParams.Modal)
                {
                    _operationProgress.ShowModalProgress(op);
                }
                else
                {
                    _operationProgress.ShowBackgroundProgress(op);
                }
            });
        });
    }

    private void TranslateProgress(ScanController controller, ScanOperation op)
    {
        var smoothProgress = new SmoothProgress();
        controller.PageStart += (sender, args) => smoothProgress.Reset();
        controller.PageProgress += (sender, args) => smoothProgress.InputProgressChanged(args.Progress);
        controller.ScanEnd += (senders, args) => smoothProgress.Reset();
        smoothProgress.OutputProgressChanged +=
            (sender, args) => op.Progress((int) Math.Round(args.Value * 1000), 1000);
    }

    private ScanOptions BuildOptions(ScanProfile scanProfile, ScanParams scanParams, IntPtr dialogParent)
    {
        var options = new ScanOptions
        {
            Driver = scanProfile.DriverName == DriverNames.WIA ? Driver.Wia
                : scanProfile.DriverName == DriverNames.SANE ? Driver.Sane
                : scanProfile.DriverName == DriverNames.TWAIN ? Driver.Twain
                : Driver.Default,
            WiaOptions =
            {
                WiaVersion = scanProfile.WiaVersion,
                OffsetWidth = scanProfile.WiaOffsetWidth
            },
            TwainOptions =
            {
                Adapter = scanProfile.TwainImpl == TwainImpl.Legacy ? TwainAdapter.Legacy : TwainAdapter.NTwain,
                Dsm = scanProfile.TwainImpl == TwainImpl.X64
                    ? TwainDsm.NewX64
                    : scanProfile.TwainImpl == TwainImpl.OldDsm || scanProfile.TwainImpl == TwainImpl.Legacy
                        ? TwainDsm.Old
                        : TwainDsm.New,
                TransferMode = scanProfile.TwainImpl == TwainImpl.MemXfer
                    ? TwainTransferMode.Memory
                    : TwainTransferMode.Native,
                IncludeWiaDevices = false
            },
            SaneOptions =
            {
                KeyValueOptions = scanProfile.KeyValueOptions != null
                    ? new KeyValueScanOptions(scanProfile.KeyValueOptions)
                    : new KeyValueScanOptions()
            },
            NetworkOptions =
            {
                Ip = scanProfile.ProxyConfig?.Ip,
                Port = scanProfile.ProxyConfig?.Port
            },
            BarcodeDetectionOptions =
            {
                DetectBarcodes = scanParams.DetectPatchT ||
                                 scanProfile.AutoSaveSettings?.Separator == SaveSeparator.PatchT,
                PatchTOnly = true
            },
            Brightness = scanProfile.Brightness,
            Contrast = scanProfile.Contrast,
            Dpi = scanProfile.Resolution.ToIntDpi(),
            Modal = scanParams.Modal,
            Quality = scanProfile.Quality,
            AutoDeskew = scanProfile.AutoDeskew,
            BitDepth = scanProfile.BitDepth.ToBitDepth(),
            DialogParent = dialogParent,
            MaxQuality = scanProfile.MaxQuality,
            PageAlign = scanProfile.PageAlign.ToHorizontalAlign(),
            PaperSource = scanProfile.PaperSource.ToPaperSource(),
            ScaleRatio = scanProfile.AfterScanScale.ToIntScaleFactor(),
            ThumbnailSize = scanParams.ThumbnailSize,
            ExcludeBlankPages = scanProfile.ExcludeBlankPages,
            FlipDuplexedPages = scanProfile.FlipDuplexedPages,
            NoUI = scanParams.NoUI,
            BlankPageCoverageThreshold = scanProfile.BlankPageCoverageThreshold,
            BlankPageWhiteThreshold = scanProfile.BlankPageWhiteThreshold,
            BrightnessContrastAfterScan = scanProfile.BrightnessContrastAfterScan,
            CropToPageSize = scanProfile.ForcePageSizeCrop,
            StretchToPageSize = scanProfile.ForcePageSize,
            UseNativeUI = scanProfile.UseNativeUI,
            Device = null, // Set after
            PageSize = null, // Set after
        };

        PageDimensions pageDimensions = scanProfile.PageSize.PageDimensions() ?? scanProfile.CustomPageSize;
        if (pageDimensions == null)
        {
            throw new ArgumentException("No page size specified");
        }

        options.PageSize = new PageSize(pageDimensions.Width, pageDimensions.Height, pageDimensions.Unit);

        return options;
    }

    private async Task<bool> PopulateDevice(ScanProfile scanProfile, ScanOptions options)
    {
        // If a device wasn't specified, prompt the user to pick one
        if (string.IsNullOrEmpty(scanProfile.Device?.ID))
        {
            options.Device = await PromptForDevice(options);
            if (options.Device == null)
            {
                return false;
            }

            // Persist the device in the profile if configured to do so
            if (_config.Get(c => c.AlwaysRememberDevice))
            {
                scanProfile.Device = options.Device;
                _profileManager.Save();
            }
        }
        else
        {
            options.Device = scanProfile.Device;
        }

        return true;
    }

    private async Task<ScanDevice> PromptForDevice(ScanOptions options)
    {
        if (options.Driver == Driver.Wia)
        {
            // WIA has a nice built-in device selection dialog, so use it
            using var deviceManager = new WiaDeviceManager(options.WiaOptions.WiaVersion);
            var wiaDevice = deviceManager.PromptForDevice(options.DialogParent);
            if (wiaDevice == null)
            {
                return null;
            }

            return new ScanDevice(wiaDevice.Id(), wiaDevice.Name());
        }

        // Other drivers do not, so use a generic dialog
        var deviceForm = _formFactory.Create<FSelectDevice>();
        deviceForm.DeviceList = await new ScanController(_scanningContext).GetDeviceList(options);
        deviceForm.ShowDialog(new Win32Window(options.DialogParent));
        return deviceForm.SelectedDevice;
    }
}