using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace msovideo_srgb
{
    public class ICCProfileGenerator
    {
        private class TagRecord
        {
            public string Signature;
            public byte[] Data;
        }

        private List<TagRecord> tags = new List<TagRecord>();
        private ushort version = 0x0220;
        private uint manufacturerID = 0;
        private uint deviceModel = 0;
        private const uint creator = 'm' << 24 | 's' << 16 | 'o' << 8 | 'v';

        public void AddTag(string signature, byte[] data)
        {
            if (signature == null || signature.Length != 4) throw new ArgumentException("Tag signature must be 4 chars.");
            tags.Add(new TagRecord { Signature = signature, Data = Align4(data) });

            if (signature.Equals("chad"))
            {
                version = 0x0400;
            }
        }

        public void SetManufacturerID(uint id)
        {
            manufacturerID = id;
        }

        public void SetDeviceModel(uint model)
        {
            deviceModel = model;
        }

        public byte[] Generate()
        {
            byte[] header = new byte[128];
            header[8] = (byte) (version >> 8); header[9] = (byte)(version & 0x00FF);
            WriteAscii(header, 12, "mntr");
            WriteAscii(header, 16, "RGB ");
            WriteAscii(header, 20, "XYZ ");

            WriteUInt16BE(header, 24, 2026);    // year
            WriteUInt16BE(header, 26, 3);       // month
            WriteUInt16BE(header, 28, 1);       // day
            WriteUInt16BE(header, 30, 0);       // hour
            WriteUInt16BE(header, 32, 0);       // minute
            WriteUInt16BE(header, 34, 0);       // second

            WriteAscii(header, 36, "acsp");
            WriteAscii(header, 40, "MSFT");

            WriteUInt32BE(header, 48, manufacturerID);
            WriteUInt32BE(header, 52, deviceModel);

            WriteUInt32BE(header, 64, 1);

            WriteS15Fixed16BE(header, 68, 0.9642);
            WriteS15Fixed16BE(header, 72, 1.0);
            WriteS15Fixed16BE(header, 76, 0.8249);

            WriteUInt32BE(header, 80, creator);

            int tagCount = tags.Count;
            int tagTableSize = 4 + tagCount * 12;
            byte[] tagTable = new byte[tagTableSize];
            WriteUInt32BE(tagTable, 0, (uint)tagCount);

            int offset = 128 + tagTableSize;
            List<byte[]> tagDataBlocks = new List<byte[]>();

            for (int i = 0; i < tagCount; i++)
            {
                var tag = tags[i];
                WriteAscii(tagTable, 4 + i * 12, tag.Signature);
                WriteUInt32BE(tagTable, 8 + i * 12, (uint)offset);
                WriteUInt32BE(tagTable, 12 + i * 12, (uint)tag.Data.Length);

                tagDataBlocks.Add(tag.Data);
                offset += tag.Data.Length;
            }

            int totalSize = offset;
            WriteUInt32BE(header, 0, (uint)totalSize);

            byte[] profile = new byte[totalSize];
            Buffer.BlockCopy(header, 0, profile, 0, header.Length);
            Buffer.BlockCopy(tagTable, 0, profile, header.Length, tagTable.Length);

            int pos = header.Length + tagTable.Length;
            foreach (var block in tagDataBlocks)
            {
                Buffer.BlockCopy(block, 0, profile, pos, block.Length);
                pos += block.Length;
            }

            SetProfileID(profile);

            return profile;
        }

        private static void SetProfileID(byte[] profile)
        {
            byte[] temp = new byte[profile.Length];
            Buffer.BlockCopy(profile, 0, temp, 0, profile.Length);

            for (int i = 44; i < 48; i++) temp[i] = 0;
            for (int i = 64; i < 68; i++) temp[i] = 0;
            for (int i = 84; i < 100; i++) temp[i] = 0;

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(temp);

                Buffer.BlockCopy(hash, 0, profile, 84, 16);
            }
        }

        private static void WriteAscii(byte[] buf, int offset, string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            Buffer.BlockCopy(b, 0, buf, offset, b.Length);
        }

        private static void WriteUInt32BE(byte[] buf, int offset, uint value)
        {
            buf[offset + 0] = (byte)((value >> 24) & 0xFF);
            buf[offset + 1] = (byte)((value >> 16) & 0xFF);
            buf[offset + 2] = (byte)((value >> 8) & 0xFF);
            buf[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteUInt16BE(byte[] buf, int offset, ushort value)
        {
            buf[offset + 0] = (byte)((value >> 8) & 0xFF);
            buf[offset + 1] = (byte)(value & 0xFF);
        }

        private static void WriteS15Fixed16BE(byte[] buf, int offset, double value)
        {
            int fixedVal = (int)Math.Round(value * 65536.0);
            buf[offset + 0] = (byte)((fixedVal >> 24) & 0xFF);
            buf[offset + 1] = (byte)((fixedVal >> 16) & 0xFF);
            buf[offset + 2] = (byte)((fixedVal >> 8) & 0xFF);
            buf[offset + 3] = (byte)(fixedVal & 0xFF);
        }

        private static byte[] Align4(byte[] data)
        {
            int pad = (4 - (data.Length % 4)) % 4;
            if (pad == 0) return data;
            byte[] aligned = new byte[data.Length + pad];
            Buffer.BlockCopy(data, 0, aligned, 0, data.Length);
            return aligned;
        }

        public static byte[] MakeAsciiTag(string description)
        {
            byte[] ascii = Encoding.ASCII.GetBytes(description);
            int asciiLen = ascii.Length + 1;
            int total = 12 + asciiLen + 4 + 0 + 4;
            byte[] data = new byte[12 + asciiLen / 4 * 4 + 8 + 8];
            WriteAscii(data, 0, "desc");

            WriteUInt32BE(data, 8, (uint)asciiLen);
            Buffer.BlockCopy(ascii, 0, data, 12, ascii.Length);
            data[12 + ascii.Length] = 0;

            WriteUInt32BE(data, 12 + asciiLen, 0);
            return data;
        }

        public static byte[] MakeXYZTag(Matrix valueXYZ)
        {
            return MakeXYZTag(valueXYZ[0], valueXYZ[1], valueXYZ[2]);
        }

        public static byte[] MakeXYZTag(double x, double y, double z)
        {
            byte[] data = new byte[20];
            WriteAscii(data, 0, "XYZ ");

            WriteS15Fixed16BE(data, 8, x);
            WriteS15Fixed16BE(data, 12, y);
            WriteS15Fixed16BE(data, 16, z);
            return data;
        }

        public static byte[] MakeCurveTag(Func<double, double> sampleAt, uint resolution)
        {
            double[] curve = new double[resolution];

            for (int i = 0; i < resolution; i++)
            {
                curve[i] = sampleAt(i / (resolution - 1.0));
            }

            return MakeCurveTag(curve);
        }

        public static byte[] MakeCurveTag(ToneCurve toneCurve, uint resolution)
        {
            double[] curve = new double[resolution];

            for (int i = 0; i < resolution; i++)
            {
                curve[i] = toneCurve.SampleAt(i / (resolution - 1.0));
            }

            return MakeCurveTag(curve);
        }

        public static byte[] MakeCurveTag(double[] curve)
        {
            int count = curve.Length;
            int header = 12;
            int dataLen = count * 2;
            byte[] data = new byte[header + dataLen];

            WriteAscii(data, 0, "curv");
            WriteUInt32BE(data, 8, (uint)count);

            for (int i = 0; i < count; i++)
            {
                ushort entry = (ushort)Math.Round(curve[i] * 65535.0);
                WriteUInt16BE(data, header + i * 2, entry);
            }

            return data;
        }

        public static byte[] MakeLuminanceTag(double luminanceCdM2)
        {
            byte[] data = new byte[20];
            WriteAscii(data, 0, "XYZ ");

            WriteS15Fixed16BE(data, 8, 0.0);
            WriteS15Fixed16BE(data, 12, luminanceCdM2);
            WriteS15Fixed16BE(data, 16, 0.0);
            return data;
        }

        public static byte[] MakeMatrixTag(Matrix matrix)
        {
            int headerSize = 8;
            byte[] buf = new byte[headerSize + 36];

            WriteAscii(buf, 0, "sf32");
            WriteUInt32BE(buf, 4, 0);

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    WriteS15Fixed16BE(buf, headerSize + (row * 3 + col) * 4, matrix[row, col]);
                }
            }

            return buf;
        }

        public static byte[] MakeMHC2(double minLuminance, double peakLuminance, Matrix matrix, double[][] curves = null)
        {
            double[,] dMatrix = null;
            if (matrix != null)
            {
                dMatrix = new double[3, 4];
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 3; j++)
                    {
                        dMatrix[i, j] = matrix[i, j];
                    }
                }
            }
            return MakeMHC2(minLuminance, peakLuminance, dMatrix, curves);
        }

        private static byte[] Make1DLUT(double[] values)
        {
            int headerSize = 8;
            int lutSize = values.Length * 4;
            byte[] buf = new byte[headerSize + lutSize];

            WriteAscii(buf, 0, "sf32");
            WriteUInt32BE(buf, 4, 0);

            for (int i = 0; i < values.Length; i++)
                WriteS15Fixed16BE(buf, headerSize + i * 4, values[i]);

            return buf;
        }

        public static byte[] MakeMHC2(double minLuminance, double peakLuminance, double[,] matrix = null, double[][] curves = null)
        {
            int curveResolution = (curves != null ? curves[0].Length : 0);

            int headerSize = 36;
            int matrixSize = (matrix != null ? 48 : 0);

            byte[] redLut = (curves != null ? Make1DLUT(curves[0]) : Array.Empty<byte>());
            byte[] greenLut = (curves != null ? Make1DLUT(curves[1]) : Array.Empty<byte>());
            byte[] blueLut = (curves != null ? Make1DLUT(curves[2]) : Array.Empty<byte>());

            int totalSize = headerSize + matrixSize + redLut.Length + greenLut.Length + blueLut.Length;
            byte[] buf = new byte[totalSize];

            WriteAscii(buf, 0, "MHC2");
            WriteUInt32BE(buf, 8, (uint)curveResolution);
            WriteS15Fixed16BE(buf, 12, minLuminance);
            WriteS15Fixed16BE(buf, 16, peakLuminance);

            WriteUInt32BE(buf, 20, (uint)(matrix != null ? headerSize : 0));
            WriteUInt32BE(buf, 24, (uint)(headerSize + matrixSize));
            WriteUInt32BE(buf, 28, (uint)(headerSize + matrixSize + redLut.Length));
            WriteUInt32BE(buf, 32, (uint)(headerSize + matrixSize + redLut.Length + greenLut.Length));


            if (matrix != null)
            {
                int offset = headerSize;
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        WriteS15Fixed16BE(buf, offset, matrix[row, col]);
                        offset += 4;
                    }
                    offset += 4;
                }
            }

            int lutOffset = headerSize + matrixSize;
            Buffer.BlockCopy(redLut, 0, buf, lutOffset, redLut.Length);
            Buffer.BlockCopy(greenLut, 0, buf, lutOffset + redLut.Length, greenLut.Length);
            Buffer.BlockCopy(blueLut, 0, buf, lutOffset + redLut.Length + greenLut.Length, blueLut.Length);

            return buf;
        }

        private static readonly string _profilesPath = Path.Combine(Path.GetTempPath(), Process.GetCurrentProcess().ProcessName);
        public static string ProfilesPath {
            get
            {
                Directory.CreateDirectory(_profilesPath);
                return _profilesPath;
            }      
        }

        public void SaveAs(string profileName)
        {
            byte[] profileData = Generate();

            var header = DisplayColorProfileManager.GetProfileHeader(profileName);

            if (header?.dwProfileID is byte[] profileId)
            {
                for (int i = 0; i < 4; i++)
                {
                    Array.Reverse(profileId, i * 4, 4);
                }

                if (profileId.SequenceEqual(profileData.Skip(84).Take(16))) return;
            }

            string profilePath = Path.Combine(ProfilesPath, profileName);

            using (FileStream fs = new FileStream(profilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.Write(profileData, 0, profileData.Length);
            }
            
            DisplayColorProfileManager.UninstallProfile(profileName, true);
            DisplayColorProfileManager.InstallProfile(profilePath);
        }

        public static bool IsGeneratedByThis(string profileName)
        {
            var header = DisplayColorProfileManager.GetProfileHeader(profileName);

            return header?.phCreator == creator;
        }
    }
}
