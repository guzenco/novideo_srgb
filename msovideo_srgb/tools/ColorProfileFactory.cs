using System;
using System.IO;

namespace msovideo_srgb
{
    public class ColorProfileFactory
    {
        private static void AddDesc(ICCProfileGenerator profileGenerator, string profileName)
        {
            profileGenerator.AddTag("desc", ICCProfileGenerator.MakeAsciiTag("MHC2 for " + Path.GetFileNameWithoutExtension(profileName)));
            profileGenerator.AddTag("cprt", ICCProfileGenerator.MakeAsciiTag("No copyright. Created with msovideo_srgb v" + AboutWindow.Version));
        }

        private static void AddMatrix(ICCProfileGenerator profileGenerator, Colorimetry.ColorSpace target)
        {
            var matrixXYZ = Colorimetry.RGBToPCSXYZ(target);
            AddMatrix(profileGenerator, matrixXYZ);
        }

        private static void AddMatrix(ICCProfileGenerator profileGenerator, Matrix matrixXYZ)
        {
            profileGenerator.AddTag("rXYZ", ICCProfileGenerator.MakeXYZTag(matrixXYZ[0, 0], matrixXYZ[1, 0], matrixXYZ[2, 0]));
            profileGenerator.AddTag("gXYZ", ICCProfileGenerator.MakeXYZTag(matrixXYZ[0, 1], matrixXYZ[1, 1], matrixXYZ[2, 1]));
            profileGenerator.AddTag("bXYZ", ICCProfileGenerator.MakeXYZTag(matrixXYZ[0, 2], matrixXYZ[1, 2], matrixXYZ[2, 2]));
        }

        private static void AddCurve(ICCProfileGenerator profileGenerator, ToneCurve curve, uint resolution)
        {
            var tagData = ICCProfileGenerator.MakeCurveTag(curve, resolution);
            profileGenerator.AddTag("rTRC", tagData);
            profileGenerator.AddTag("gTRC", tagData);
            profileGenerator.AddTag("bTRC", tagData);
        }

        private static void AddCurve(ICCProfileGenerator profileGenerator, ICCMatrixProfile profile, Matrix matrixWhite, bool useVsgt, uint resolution)
        {
            byte[] tagDataR = ICCProfileGenerator.MakeCurveTag(x => profile.TrcSample(0, x, useVsgt, matrixWhite), resolution);
            byte[] tagDataG = ICCProfileGenerator.MakeCurveTag(x => profile.TrcSample(1, x, useVsgt, matrixWhite), resolution);
            byte[] tagDataB = ICCProfileGenerator.MakeCurveTag(x => profile.TrcSample(2, x, useVsgt, matrixWhite), resolution);
            profileGenerator.AddTag("rTRC", tagDataR);
            profileGenerator.AddTag("gTRC", tagDataG);
            profileGenerator.AddTag("bTRC", tagDataB);
        }

        private static Matrix OptimizeMatrix(Matrix matrixCSC, Func<int, double, double> sampleAt)
        {
            ToneCurve srgbCurve = new SrgbEOTF();

            Matrix white = Colorimetry.RGBToXYZ(Colorimetry.D65);
            Matrix white3x3 = Matrix.FromDiagonal(white);

            Matrix rgbToXYZ = Colorimetry.RGBToXYZ(Colorimetry.sRGB);

            Matrix target = rgbToXYZ.Inverse() * matrixCSC * rgbToXYZ;
            target = target.Map(x => x > 0 ? x < 1 ? x : 1 : 0);
            target = Matrix.FromDiagonal(target * white).Inverse() * white3x3 * target;

            Matrix identityMatrix = Matrix.Identity();
            Matrix finalMatrixOptimization = identityMatrix;

            for (int i = 0; i < 10000; i++)
            {
                Matrix result = rgbToXYZ.Inverse() * matrixCSC * finalMatrixOptimization * rgbToXYZ;

                result = result.Map(x => x > 0 ? x < 1 ? x : 1 : 0);

                result = result.Map((r, c, x) => sampleAt(r, srgbCurve.SampleInverseAt(x)));

                result = Matrix.FromDiagonal(result * white).Inverse() * white3x3 * result;

                Matrix matrixOptimization = result.Inverse() * target;

                if (identityMatrix.DifferenceMax(matrixOptimization) < 1E-10)
                {
                    break;
                }

                matrixOptimization = 0.9 * identityMatrix + 0.1 * matrixOptimization;
                finalMatrixOptimization = finalMatrixOptimization * matrixOptimization;

            }

            return finalMatrixOptimization;
        }

        public static void CreateProfile(string profileName, uint resolution)
        {
            var profileGenerator = new ICCProfileGenerator();

            AddDesc(profileGenerator, profileName);

            profileGenerator.AddTag("wtpt", ICCProfileGenerator.MakeXYZTag(Colorimetry.RGBToXYZ(Colorimetry.D65)));
            AddMatrix(profileGenerator, Colorimetry.sRGB);

            ToneCurve gamaCurve = new SrgbEOTF();
            AddCurve(profileGenerator, gamaCurve, resolution);

            profileGenerator.AddTag("lumi", ICCProfileGenerator.MakeLuminanceTag(80));

            profileGenerator.AddTag("MHC2", ICCProfileGenerator.MakeMHC2(-1, -1));

            profileGenerator.SaveAs(profileName);
        }

        public static void CreateProfile(string profileName, uint resolution, EDID edid, double peakLuminance, double maxFullFrameLuminance, double minLuminance)
        {
            var profileGenerator = new ICCProfileGenerator();

            AddDesc(profileGenerator, profileName);

            Colorimetry.ColorSpace edidColorSpace;
            double edidGamma;
            if (edid != null)
            {
                edidColorSpace = edid.ColorSpace;
                edidGamma = edid.Gamma;

                edidColorSpace.White = Colorimetry.D65;

                profileGenerator.SetManufacturerID(edid.ManufacturerId);
                profileGenerator.SetDeviceModel(edid.ProductCodeId);
            }
            else
            {
                edidColorSpace = Colorimetry.sRGB;
                edidGamma = 2.2;
            }

            profileGenerator.SetManufacturerID(edid.ManufacturerId);
            profileGenerator.SetDeviceModel(edid.ProductCodeId);

            Matrix chromaticAdaptation = Colorimetry.WhiteToWhiteAdaptation(Colorimetry.RGBToXYZ(Colorimetry.D65), Colorimetry.D50);
            profileGenerator.AddTag("chad", ICCProfileGenerator.MakeMatrixTag(chromaticAdaptation));

            profileGenerator.AddTag("wtpt", ICCProfileGenerator.MakeXYZTag(Colorimetry.D50));
            AddMatrix(profileGenerator, edidColorSpace);

            ToneCurve gamaCurve = new GammaToneCurve(edidGamma);
            AddCurve(profileGenerator, gamaCurve, resolution);

            profileGenerator.AddTag("lumi", ICCProfileGenerator.MakeLuminanceTag(maxFullFrameLuminance));

            double[][] luts = new double[][] {
                    new double[] { 0, 1 },
                    new double[] { 0, 1 },
                    new double[] { 0, 1 }
                };

            Matrix matrix = Matrix.Identity();

            profileGenerator.AddTag("MHC2", ICCProfileGenerator.MakeMHC2(minLuminance, peakLuminance, matrix, luts));

            profileGenerator.SaveAs(profileName);
        }

        public static void CreateProfile(string profileName, uint resolution, EDID edid, Colorimetry.ColorSpace targetColorSpace, Colorimetry.Point targetWhitePoint, 
            bool reportWhiteD65 = false, 
            bool reportColorSpaceSRGB = false, 
            bool reportGammaSRGB = false,
            double? peakLuminanceOverride = null,
            double? maxFullFrameLuminanceOverride = null,
            double? minLuminanceOverride = null)
        {
            var profileGenerator = new ICCProfileGenerator();

            AddDesc(profileGenerator, profileName);

            Colorimetry.ColorSpace edidColorSpace;
            Colorimetry.Point edidWhite;
            double edidGamma;
            if (edid != null)
            {
                edidColorSpace = edid.ColorSpace;
                edidWhite = edidColorSpace.White;
                edidGamma = edid.Gamma;

                edidColorSpace.White = Colorimetry.D65;

                profileGenerator.SetManufacturerID(edid.ManufacturerId);
                profileGenerator.SetDeviceModel(edid.ProductCodeId);
            }
            else
            {
                edidColorSpace = Colorimetry.sRGB;
                edidWhite = Colorimetry.D65;
                edidGamma = 2.2;
            }

            Matrix targetWhite;
            Matrix matrixWhite = Matrix.Identity();
            if (targetWhitePoint.Equals(Colorimetry.NativeWhite))
            {
                targetWhite = Colorimetry.RGBToXYZ(edidWhite);
            }
            else
            {
                targetWhite = Colorimetry.RGBToXYZ(targetWhitePoint);
                matrixWhite = Colorimetry.CreateWhiteMatrix(Colorimetry.RGBToXYZ(edidColorSpace), Colorimetry.RGBToXYZ(edidWhite), targetWhite);
            }

            Matrix reportedWhite = reportWhiteD65 ? Colorimetry.RGBToXYZ(Colorimetry.D65) : targetWhite;

            Matrix chromaticAdaptation = Colorimetry.WhiteToWhiteAdaptation(reportedWhite, Colorimetry.D50);
            profileGenerator.AddTag("chad", ICCProfileGenerator.MakeMatrixTag(chromaticAdaptation));
            reportedWhite = Colorimetry.D50;

            profileGenerator.AddTag("wtpt", ICCProfileGenerator.MakeXYZTag(reportedWhite));

            Colorimetry.ColorSpace finalColorSpace; 
            Matrix matrixCsc = Matrix.Identity();
            if (targetColorSpace.Equals(Colorimetry.Native))
            {
                finalColorSpace = edidColorSpace;
            }
            else
            {
                finalColorSpace = targetColorSpace;  
                matrixCsc = Colorimetry.CreateMatrix(edidColorSpace, targetColorSpace);
            }

            Colorimetry.ColorSpace reportedColorSpace = reportColorSpaceSRGB ? Colorimetry.sRGB : finalColorSpace;
            AddMatrix(profileGenerator, reportedColorSpace);

            ToneCurve gamaCurve = new GammaToneCurve(edidGamma);
            ToneCurve reportedCurve = reportGammaSRGB ? new SrgbEOTF() : gamaCurve;
            AddCurve(profileGenerator, reportedCurve, resolution);

            double[][] luts = new double[][] {
                    new double[] { 0, gamaCurve.SampleInverseAt(matrixWhite[0, 0]) },
                    new double[] { 0, gamaCurve.SampleInverseAt(matrixWhite[1, 1]) },
                    new double[] { 0, gamaCurve.SampleInverseAt(matrixWhite[2, 2]) }
                };

            Matrix matrix = matrixCsc;

            double peakLuminance = peakLuminanceOverride ?? 80;
            double maxFullFrameLuminance = maxFullFrameLuminanceOverride ?? 80;
            double minLuminance = minLuminanceOverride ?? 0;

            profileGenerator.AddTag("lumi", ICCProfileGenerator.MakeLuminanceTag(maxFullFrameLuminance));

            profileGenerator.AddTag("MHC2", ICCProfileGenerator.MakeMHC2(minLuminance, peakLuminance, matrix, luts));

            profileGenerator.SaveAs(profileName);
        }

        public static void CreateProfile(string profileName, uint resolution, EDID edid, ICCMatrixProfile profile, Colorimetry.ColorSpace targetColorSpace, Colorimetry.Point targetWhitePoint, double luminance, 
            bool reportWhiteD65 = false,
            bool reportColorSpaceSRGB = false,
            bool reportGammaSRGB = false, 
            bool useVcgt = false, 
            bool optimizeMatrix = false, 
            bool acmMode = false, 
            ToneCurve gamma = null, 
            ToneCurve curve = null,
            double? peakLuminanceOverride = null,
            double? maxFullFrameLuminanceOverride = null,
            double? minLuminanceOverride = null)
        {
            var profileGenerator = new ICCProfileGenerator();

            AddDesc(profileGenerator, profileName);

            if (edid != null)
            {
                profileGenerator.SetManufacturerID(edid.ManufacturerId);
                profileGenerator.SetDeviceModel(edid.ProductCodeId);
            }

            Matrix targetWhite;
            Matrix matrixWhite = Matrix.Identity();
            if (targetWhitePoint.Equals(Colorimetry.NativeWhite))
            {
                if (gamma == null && !useVcgt && profile.vcgt != null)
                {
                    Matrix profileMatrixWhite = Matrix.FromDiagonal(new double[] {
                        profile.TrcSample(0, profile.vcgt[0].SampleAt(1), true, Matrix.Identity()),
                        profile.TrcSample(1, profile.vcgt[1].SampleAt(1), true, Matrix.Identity()),
                        profile.TrcSample(2, profile.vcgt[2].SampleAt(1), true, Matrix.Identity())
                    });
                    targetWhite = Colorimetry.XYZScale(profile.matrix * Colorimetry.WhiteToWhiteAdaptation(Colorimetry.D50, profile.whitePoint), profile.whitePoint) * profileMatrixWhite.Inverse() * Matrix.One3x1();
                }
                else
                {
                    targetWhite = profile.whitePoint;
                }
            }
            else
            {
                targetWhite = Colorimetry.RGBToXYZ(targetWhitePoint);
                matrixWhite = Colorimetry.CreateWhiteMatrix(profile.matrix, profile.whitePoint, targetWhite);
            }

            double currentLuminance = profile.Luminance(matrixWhite, gamma);
            matrixWhite *= Math.Min(luminance, currentLuminance) / currentLuminance;

            Matrix reportWhite = reportWhiteD65 ? Colorimetry.RGBToXYZ(Colorimetry.D65) : targetWhite;

            Matrix chromaticAdaptation = Colorimetry.WhiteToWhiteAdaptation(reportWhite, Colorimetry.D50);
            profileGenerator.AddTag("chad", ICCProfileGenerator.MakeMatrixTag(chromaticAdaptation));
            reportWhite = Colorimetry.D50;

            profileGenerator.AddTag("wtpt", ICCProfileGenerator.MakeXYZTag(reportWhite));

            Matrix finalColorSpace;
            Matrix matrixCSC = Matrix.Identity();
            if (targetColorSpace.Equals(Colorimetry.Native))
            {
                finalColorSpace = profile.matrix;
            }
            else
            {
                finalColorSpace = Colorimetry.RGBToPCSXYZ(targetColorSpace);
                matrixCSC = Colorimetry.CreateMatrix(profile.matrix, targetColorSpace);
            }

            if (optimizeMatrix)
            {
                Matrix matrixOptimization;
                Matrix matrixToOpt = acmMode ? Colorimetry.CreateMatrix(profile.matrix, Colorimetry.sRGB) : matrixCSC;

                if (gamma != null)
                {
                    ToneCurve scaledGamma = new ScaledToneCurve(gamma);
                    matrixOptimization = OptimizeMatrix(matrixToOpt, (i, x) => scaledGamma.SampleAt(x));
                }
                else
                {
                    matrixOptimization = OptimizeMatrix(matrixToOpt, (i, x) => (new ScaledToneCurve(sampleAt: (v) => profile.TrcSample(i, v, !useVcgt, matrixWhite)).SampleAt(x)));
                }

                matrixCSC = matrixCSC * matrixOptimization;
            }

            Matrix reportedColorSpace = reportColorSpaceSRGB ? Colorimetry.RGBToPCSXYZ(Colorimetry.sRGB) : finalColorSpace;
            AddMatrix(profileGenerator, reportedColorSpace);

            ToneCurve reportedCurve = curve != null ? curve : gamma;
            reportedCurve = reportGammaSRGB ? new SrgbEOTF() : reportedCurve;
            if (reportedCurve != null)
            {
                AddCurve(profileGenerator, reportedCurve, resolution);
            }
            else
            {
                AddCurve(profileGenerator, profile, matrixWhite, !useVcgt, resolution);
            }

            double[][] luts;

            if (gamma != null || (useVcgt && profile.vcgt != null))
            {
                luts = new double[3][];


                for (int i = 0; i < 3; i++)
                {
                    luts[i] = new double[resolution];

                    ScaledToneCurve scaledGamma;
                    if (gamma != null)
                    {
                        scaledGamma = new ScaledToneCurve(gamma, profile.trcBlack, matrixWhite[i, i]);
                    }
                    else
                    {
                        scaledGamma = new ScaledToneCurve(isAbsolute: true, sampleAt: (x) => profile.TrcSample(i, x, !useVcgt, matrixWhite), white: matrixWhite[i, i]);
                    }

                    for (int j = 1; j < resolution; j++)
                    {
                        double value = j / (resolution - 1.0);

                        value = scaledGamma.SampleAt(value);

                        value = profile.TrcSampleInverse(i, value);

                        luts[i][j] = value;
                    }
                }
            }
            else
            {
                luts = new double[][] {
                    new double[] { 0, profile.TrcSampleInverse(0, matrixWhite[0, 0]) },
                    new double[] { 0, profile.TrcSampleInverse(1, matrixWhite[1, 1]) },
                    new double[] { 0, profile.TrcSampleInverse(2, matrixWhite[2, 2]) }
                };
            }

            double peakLuminance = peakLuminanceOverride ?? luminance;
            double maxFullFrameLuminance = maxFullFrameLuminanceOverride ?? luminance;
            double minLuminance = minLuminanceOverride ?? profile.tagBlack * profile.luminance;

            Matrix matrix = matrixCSC;

            profileGenerator.AddTag("lumi", ICCProfileGenerator.MakeLuminanceTag(maxFullFrameLuminance));

            profileGenerator.AddTag("MHC2", ICCProfileGenerator.MakeMHC2(minLuminance, peakLuminance, matrix, luts));

            profileGenerator.SaveAs(profileName);
        }
    }
}