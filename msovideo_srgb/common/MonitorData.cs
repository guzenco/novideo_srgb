using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;

namespace msovideo_srgb
{
    public class MonitorData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool? _clamped;

        private MainViewModel _viewModel;

        public MonitorData(MainViewModel viewModel, int number, Display display)
        {
            _clamped = false;
            _viewModel = viewModel;

            Number = number;        
            Display = display;

            Edid = Display.GetEDID();

            if (Display.HaveFriendlyDeviceName)
            {
                MHCProfileName =  $"{Display.FriendlyDeviceName} {Display.DeviceID}#{Display.InstanceID}";
            }
            else
            {
                MHCProfileName = $"{Display.DeviceID}#{Display.InstanceID}";
            }

            MHCProfileName = new string(MHCProfileName.Where(c => !System.IO.Path.GetInvalidFileNameChars().Contains(c)).ToArray());

            IsSupportMHC2 = DisplayColorProfileManager.IsSupportMHC2(Display);
        }

        public int Number { get; }
        public EDID Edid { get; }
        public Display Display { get; }
        public bool? IsSupportMHC2 { get; }
        public string MHCProfileName { get; }
        public string MHCProfileNameSDR => "[SDR] " + MHCProfileName + ".icm";
        public string MHCProfileNameHDR => "[HDR] " + MHCProfileName + ".icm";

        public const string MHCProfileNameReset = "msovideo_srgb_no_transform.icm";

        public const string MHCProfileNamePattern = @"^\[(?:SDR|HDR)\]\s.+#[^\s]+(?:\.icm| default\.icm)$";
        
        public void ScheduleCreateProfile(Action createProfile)
        {
            ActionScheduler.Add(Path, createProfile, HandleClampException);
        }

        private void ScheduleApplyProfile(string profileName, bool hdr)
        {
            ActionScheduler.Add(Path, () => ApplyProfile(profileName, hdr), HandleClampException);
        }

        private bool IsManagedProfileActive(bool hdr)
        {
            string profileName = DisplayColorProfileManager.GetProfile(Display, hdr);
            if (Regex.IsMatch(profileName, MHCProfileNamePattern) && ICCProfileGenerator.IsGeneratedByThis(profileName))
            {
                return true;
            }
            return false;
        }

        private void ApplyProfile(string profileName, bool hdr)
        {
            ColorProfileFactory.CreateProfile(MHCProfileNameReset, CurveResolution);

            DisplayColorProfileManager.AddAssociation(Display, MHCProfileNameReset, hdr);
            DisplayColorProfileManager.SetProfile(Display, MHCProfileNameReset, hdr);

            DisplayColorProfileManager.AddAssociation(Display, profileName, hdr);
            DisplayColorProfileManager.SetProfile(Display, profileName, hdr);

            DisplayColorProfileManager.RemoveAssociation(Display, MHCProfileNameReset, hdr);
        }

        private void UnapplyProfile(string profileName, bool hdr, bool force = true)
        {
            if (DisplayColorProfileManager.GetProfile(Display, hdr).Equals(profileName))
            {
                if (force)
                {
                    ColorProfileFactory.CreateProfile(MHCProfileNameReset, CurveResolution);

                    DisplayColorProfileManager.AddAssociation(Display, MHCProfileNameReset, hdr);
                    DisplayColorProfileManager.SetProfile(Display, MHCProfileNameReset, hdr);

                    DisplayColorProfileManager.RemoveAssociation(Display, profileName, hdr);

                    DisplayColorProfileManager.RemoveAssociation(Display, MHCProfileNameReset, hdr);
                }
                else
                {
                    DisplayColorProfileManager.RemoveAssociation(Display, profileName, hdr);
                }
            }
        }

        private void ScheduleRemoveWrongProfileAssociations()
        {
            ActionScheduler.Add(Path, () => RemoveWrongProfileAssociations(), HandleClampException);
        }

        private void RemoveWrongProfileAssociations()
        {
            var profiles = DisplayColorProfileManager.GetAllProfiles()?.ToList();
            if (profiles == null) return;

            string profileNameSDR = DisplayColorProfileManager.GetProfile(Display, false);
            if (profiles.Contains(profileNameSDR))
            {
                profiles.Remove(profileNameSDR);
            }
            else
            {
                profileNameSDR = "";
            }

            string profileNameHDR = DisplayColorProfileManager.GetProfile(Display, true);
            if (profiles.Contains(profileNameHDR))
            {
                profiles.Remove(profileNameHDR);
            }
            else
            {
                profileNameHDR = "";
            }

            foreach (string profileName in profiles)
            {
                if (!Regex.IsMatch(profileName, MHCProfileNamePattern)) continue;
                if (!ICCProfileGenerator.IsGeneratedByThis(profileName)) continue;

                DisplayColorProfileManager.RemoveAssociation(Display, profileName, false);
                DisplayColorProfileManager.RemoveAssociation(Display, profileName, true);
            }

            if (profileNameSDR != MHCProfileNameSDR && Regex.IsMatch(profileNameSDR, MHCProfileNamePattern) && ICCProfileGenerator.IsGeneratedByThis(profileNameSDR))
            {
                UnapplyProfile(profileNameSDR, false);
            }

            if (profileNameHDR != MHCProfileNameHDR && Regex.IsMatch(profileNameHDR, MHCProfileNamePattern) && ICCProfileGenerator.IsGeneratedByThis(profileNameHDR))
            {
                UnapplyProfile(profileNameHDR, true);
            }
        }

        private void ScheduleUnapplyProfile(bool doClamp)
        {
            ActionScheduler.Add(Path, () => UnapplyProfiles(doClamp), HandleClampException);
        }

        private void UnapplyProfiles(bool doClamp)
        {

            if (!doClamp || !CanClampSDR || !(UseEdid || UseIcc))
            {
                UnapplyProfile(MHCProfileNameSDR, false);
            }
            if (!doClamp || !CanClampHDR || !(UseIccHDR || OverrideMetadataHDR))
            {
                UnapplyProfile(MHCProfileNameHDR, true);
            }
        }

        private void UpdateClamp(bool doClamp)
        {
            ActionScheduler.Clear(Path);
            ActionScheduler.SetPriority(Path, -Number);

            var scope = DisplayColorProfileManager.GetDisplayUserScope(Display);

            if (scope == DisplayColorProfileManager.WcsProfileManagementScope.SystemWide)
            {
                DisplayColorProfileManager.SetDisplayUserScope(Display, DisplayColorProfileManager.WcsProfileManagementScope.CurrentUser);
            }

            ScheduleRemoveWrongProfileAssociations();
            ScheduleUnapplyProfile(doClamp);

            if (!doClamp || !CanClamp) return;

            if (CanClampSDR)
            {
                Action createProfile = null;

                double? PeakLuminance = null;
                double? MaxFullFrameLuminance = null;
                double? MinLuminance = null;

                if (ExcludeHdrMetadata)
                {
                    if (AcmActive)
                    {
                        var colorCapabilities = DisplayColorCapabilities.GetColorCapabilities(Display);
                        if (colorCapabilities != null)
                        {
                            PeakLuminance = colorCapabilities?.PeakLuminance;
                            MaxFullFrameLuminance = colorCapabilities?.MaxFullFrameLuminance;
                            MinLuminance = colorCapabilities?.MinLuminance;
                        }
                    }
                    else
                    {
                        PeakLuminance = -1;
                        MinLuminance = -1;
                    }
                }

                if (UseEdid)
                {
                    createProfile = () =>
                    {
                        ColorProfileFactory.CreateProfile(MHCProfileNameSDR, CurveResolution, Edid, TargetColorSpace, TargetWhitePoint,
                                reportWhiteD65: ReportWhiteD65 || AcmActive,
                                reportColorSpaceSRGB: ReportColorSpaceSRGB && !AcmActive,
                                reportGammaSRGB: ReportGammaSRGB && !AcmActive,
                                peakLuminanceOverride: PeakLuminance,
                                maxFullFrameLuminanceOverride: MaxFullFrameLuminance,
                                minLuminanceOverride: MinLuminance);
                    };
                }
                else if (UseIcc)
                {
                    var profile = ICCMatrixProfile.FromFile(ProfilePath);

                    Matrix matrixWhite = Matrix.Identity();
                    if (!TargetWhitePoint.Equals(Colorimetry.NativeWhite))
                    {
                        matrixWhite = Colorimetry.CreateWhiteMatrix(profile.matrix, profile.whitePoint, TargetWhitePoint);
                    }

                    double luminance = profile.Luminance(matrixWhite);
                    if (LimitLuminance)
                    {
                        luminance = Math.Min(luminance, MaxLuminance);
                    }

                    ToneCurve gamma = null;
                    if (CalibrateGamma)
                    {
                        var tagBlack = profile.tagBlack;

                        tagBlack *= profile.luminance / luminance;

                        switch (SelectedGamma)
                        {
                            case 0:
                                gamma = new SrgbEOTF();
                                break;
                            case 1:
                                gamma = new GammaToneCurve(2.4, tagBlack, 0);
                                break;
                            case 2:
                                gamma = new GammaToneCurve(CustomGamma, tagBlack, CustomPercentage / 100);
                                break;
                            case 3:
                                gamma = new GammaToneCurve(CustomGamma, tagBlack, CustomPercentage / 100, true);
                                break;
                            case 4:
                                gamma = new LstarEOTF();
                                break;
                            default:
                                throw new NotSupportedException("Unsupported gamma type " + SelectedGamma);
                        }
                    }

                    createProfile =() =>
                    {
                        ColorProfileFactory.CreateProfile(MHCProfileNameSDR, CurveResolution, Edid, profile, TargetColorSpace, TargetWhitePoint, luminance,
                                reportWhiteD65: ReportWhiteD65 || AcmActive,
                                reportColorSpaceSRGB: ReportColorSpaceSRGB && !AcmActive,
                                reportGammaSRGB: ReportGammaSRGB && !AcmActive,
                                useVcgt: UseVcgt,
                                optimizeMatrix: OptimizeMatrix,
                                acmMode: AcmActive,
                                gamma: gamma,
                                peakLuminanceOverride: PeakLuminance,
                                maxFullFrameLuminanceOverride: MaxFullFrameLuminance,
                                minLuminanceOverride: MinLuminance);
                    };
                }

                if (createProfile != null)
                {
                    ScheduleCreateProfile(createProfile);
                    ScheduleApplyProfile(MHCProfileNameSDR, false);
                }
            }

            if (CanClampHDR)
            {
                Action createProfile = null;

                if (UseIccHDR)
                {
                    var profile = ICCMatrixProfile.FromFile(ProfilePathHDR);


                    Matrix matrixWhite = Matrix.Identity();
                    if (!TargetWhitePointHDR.Equals(Colorimetry.NativeWhite))
                    {
                        matrixWhite = Colorimetry.CreateWhiteMatrix(profile.matrix, profile.whitePoint, TargetWhitePointHDR);
                    }

                    double luminance = profile.Luminance(matrixWhite);

                    ToneCurve gamma = null;
                    if (CalibrateGammaHDR)
                    {
                        gamma = new ST2084(TargetPeak, profile.trcBlack * profile.luminance, luminance, BPCThreshold);
                        luminance = profile.Luminance(matrixWhite, gamma);
                    }
                    createProfile = () =>
                    {
                        ColorProfileFactory.CreateProfile(MHCProfileNameHDR, CurveResolution, Edid, profile, Colorimetry.Native, TargetWhitePointHDR, luminance,
                                gamma: gamma,
                                curve: new SrgbEOTF(),
                                peakLuminanceOverride: OverrideMetadataHDR ? (double?)PeakLuminanceHDR : null,
                                maxFullFrameLuminanceOverride: OverrideMetadataHDR ? (double?)MaxFullFrameLuminanceHDR : null,
                                minLuminanceOverride: OverrideMetadataHDR ? (double?)MinLuminanceHDR : null);
                    };

                }
                else if (OverrideMetadataHDR)
                {
                    createProfile = () =>
                    {
                        ColorProfileFactory.CreateProfile(MHCProfileNameHDR, CurveResolution, Edid, PeakLuminanceHDR, MaxFullFrameLuminanceHDR, MinLuminanceHDR);
                    };
                }

                if(createProfile != null)
                {
                    ScheduleCreateProfile(createProfile);
                    ScheduleApplyProfile(MHCProfileNameHDR, true);
                }
            }   
        }

        private void HandleClampException(Exception e)
        {
            ActionScheduler.Clear(Path);
            MessageBox.Show(e.Message);

            try
            {
                _clamped = false;
                if (Clamp || IsManagedProfileActive(false) || IsManagedProfileActive(true))
                {
                    _clamped = null;
                }
            }
            catch
            {
                _clamped = null;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(Clamped));
            });
        }
        
        public bool? Clamped
        {
            set
            {
                try
                {
                    Clamp = value == true;
                    UpdateClamp(value == true);
                    _clamped = Clamp;
                    OnPropertyChanged(nameof(Clamped));
                }
                catch (Exception e)
                {
                    HandleClampException(e);
                    return;
                }
                finally
                {
                    _viewModel.OnClampChanged(this);
                }
            }
            get => _clamped;
        }

        public void ReapplyClamp()
        {
            try
            {
                var clamped = CanClamp && Clamp;
                UpdateClamp(clamped);
                _clamped = clamped;
                OnPropertyChanged(nameof(CanClamp));
                OnPropertyChanged(nameof(Clamped));
            }
            catch (Exception e)
            {
                HandleClampException(e);
            }
        }

        public string Name => Display.HaveFriendlyDeviceName ? Display.FriendlyDeviceName : Display.DeviceID;
        public string Path => Display.DevicePath;

        public bool IsUnique => Display.IsSourceUnique;

        public bool HdrActive => Display.HdrActive;
        public bool AcmActive => Display.AcmActive;

        public string Mode => HdrActive && AcmActive ? "HDR/ACM" : HdrActive ? "HDR" : AcmActive ? "ACM" : "SDR";

        public bool CanClamp => IsSupportMHC2 != false && IsUnique && (CanClampSDR || CanClampHDR);

        public bool CanClampSDR => UseEdid || (UseIcc && ProfilePath != "");

        public bool CanClampHDR => (UseIccHDR && ProfilePathHDR != "") || (OverrideMetadataHDR && !UseIccHDR);

        public bool UseEdid
        {
            set => UseIcc = !value;
            get => !UseIcc;
        }

        [Persistent("clamp", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Clamp))]
        public bool Clamp { get; set; }

        [Persistent("target", 0)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Target))]
        public int Target { set; get; }

        [Persistent("resolution", 2)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Resolution))]
        public int Resolution { set; get; }

        [Persistent("use_icc", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.UseIcc))]
        public bool UseIcc { set; get; }

        [Persistent("icc_path", "")]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.ProfilePath))]
        public string ProfilePath { set; get; }

        [Persistent("limit_luminance", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.LimitLuminance))]
        public bool LimitLuminance { set; get; }

        [Persistent("max_luminance", 80)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.LimitLuminance))]
        public int MaxLuminance { set; get; }

        [Persistent("calibrate_gamma", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Gamma))]
        public bool CalibrateGamma { set; get; }

        [Persistent("selected_gamma", 0)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Gamma))]
        public int SelectedGamma { set; get; }

        [Persistent("custom_gamma", 2.2)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Gamma))]
        public double CustomGamma { set; get; }

        [Persistent("custom_percentage", 100)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Gamma))]
        public double CustomPercentage { set; get; }

        [Persistent("use_vcgt", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Gamma))]
        public bool UseVcgt { set; get; }

        [Persistent("optimize_matrix", true)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.OptimizeMatrix))]
        public bool OptimizeMatrix { set; get; }

        [Persistent("target_white", 0)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.TargetWhite))]
        public int TargetWhite { set; get; }

        [Persistent("custom_white_x", 0.3127)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.TargetWhite))]
        public double CustomWhiteX { set; get; }

        [Persistent("custom_white_y", 0.3290)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.TargetWhite))]
        public double CustomWhiteY { set; get; }

        [Persistent("report_white_d65", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Report))]
        public bool ReportWhiteD65 { set; get; }

        [Persistent("report_color_space_srgb", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Report))]
        public bool ReportColorSpaceSRGB { set; get; }

        [Persistent("report_gamma_srgb", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Report))]
        public bool ReportGammaSRGB { set; get; }

        [Persistent("exclude_hdr_metadata", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.Report))]
        public bool ExcludeHdrMetadata { set; get; }

        [Persistent("use_icc_hdr", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.UseIccHDR))]
        public bool UseIccHDR { set; get; }

        [Persistent("icc_path_hdr", "")]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.ProfilePathHDR))]
        public string ProfilePathHDR { set; get; }

        [Persistent("calibrate_gamma_hdr", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.GammaHDR))]
        public bool CalibrateGammaHDR { set; get; }

        [Persistent("target_peak", 10000)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.GammaHDR))]
        public int TargetPeak { set; get; }

        [Persistent("bpc_threshold", 80)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.GammaHDR))]
        public double BPCThreshold { set; get; }

        [Persistent("target_white_hdr", 0)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.TargetWhiteHDR))]
        public int TargetWhiteHDR { set; get; }

        [Persistent("custom_white_hdr_x", 0.3127)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.TargetWhiteHDR))]
        public double CustomWhiteHdrX { set; get; }

        [Persistent("custom_white_hdr_y", 0.3290)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.TargetWhiteHDR))]
        public double CustomWhiteHdrY { set; get; }

        [Persistent("override_metadata_hdr", false)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.OverrideMetadataHDR))]
        public bool OverrideMetadataHDR { set; get; }

        [Persistent("peak_luminance_hdr", 10000)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.OverrideMetadataHDR))]
        public int PeakLuminanceHDR { set; get; }

        [Persistent("max_full_frame_luminance_hdr", 10000)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.OverrideMetadataHDR))]
        public int MaxFullFrameLuminanceHDR { set; get; }

        [Persistent("min_luminance_hdr", 0)]
        [BindToProperty(typeof(SettingsSourceMap), nameof(SettingsSourceMap.OverrideMetadataHDR))]
        public double MinLuminanceHDR { set; get; }

        private Colorimetry.ColorSpace TargetColorSpace => !AcmActive ? Colorimetry.ColorSpaces[Target]: Colorimetry.Native;

        private uint[] Resolutions = new uint[] { 256, 1024, 4096 };
        private uint CurveResolution => Resolutions[Resolution];

        private Colorimetry.Point[] TargerWhites = new Colorimetry.Point[] { Colorimetry.NativeWhite, Colorimetry.D50_xy, Colorimetry.D65, Colorimetry.D93 };
        private Colorimetry.Point TargetWhitePoint => TargetWhite < TargerWhites.Length ? TargerWhites[TargetWhite] : new Colorimetry.Point { X = CustomWhiteX, Y = CustomWhiteY };
        private Colorimetry.Point TargetWhitePointHDR => TargetWhiteHDR < TargerWhites.Length ? TargerWhites[TargetWhiteHDR] : new Colorimetry.Point { X = CustomWhiteHdrX, Y = CustomWhiteHdrY };

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}