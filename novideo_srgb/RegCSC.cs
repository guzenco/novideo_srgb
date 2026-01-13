using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace novideo_srgb
{
    public static class RegCSC
    {
        private static readonly float[] noMatrix = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };

        [StructLayout(LayoutKind.Sequential)]
        public struct RegCsc
        {

            public ColorSpaceConfig Config;
            public ColorSpaceLut Lut;

            public RegCsc(uint contentColorSpace, uint monitorColorSpace, float[] matrix1, float[] matrix2, float[] degamma, float[] regamma)
            {
                var hasLut = degamma != null && regamma != null;
                this.Config = new ColorSpaceConfig(contentColorSpace, monitorColorSpace, matrix1, matrix2, hasLut);
                this.Lut = new ColorSpaceLut(degamma, regamma);
   
            }

            public bool hasLut()
            {
                return (this.Config.mask & 0b11) == 3 && this.Config.bufferSize == 0x6000;
            }
            private bool hasMatrix(float[] matrix)
            {
                return !Array.Equals(matrix,noMatrix);
            }
            public bool hasMatrix1()
            {
                return hasMatrix(this.Config.matrix1);
            }

            public bool hasMatrix2()
            {
                return hasMatrix(this.Config.matrix2);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ColorSpaceConfig
        {

            public uint unknown1; // 475
            public uint unknown2; // 132
            public uint contentColorSpace;
            public uint monitorColorSpace;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public float[] matrix1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public float[] matrix2;
            public int mask; // ? 0b11 < 0 regma/degama enable; 1 << 3 ?? control
            public uint unknown3; // 0
            public uint unknown4; // 0
            public uint bufferSize;
            public uint bytesum;

            public ColorSpaceConfig(uint contentColorSpace, uint monitorColorSpace, float[] matrix1, float[] matrix2, bool hasLut)
            {
                this.unknown1 = 475;
                this.unknown2 = 132;
                this.unknown3 = 0;
                this.unknown4 = 0;
                this.bytesum = 0;

                this.contentColorSpace = contentColorSpace;
                this.monitorColorSpace = monitorColorSpace;

                this.matrix1 = matrix1 ?? noMatrix;
                this.matrix2 = matrix2 ?? noMatrix;

                this.mask = (hasLut ? 0b11 : 0) | (hasLut || matrix1 != null || matrix2 != null ? 3 : 0);
                this.bufferSize = (uint)(hasLut ? 0x6000 : 0);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ColorSpaceLut
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024 * 3)]
            public float[] degamma;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024 * 3)]
            public float[] regamma;

            public ColorSpaceLut(float[] degamma, float[] regamma)
            {
                this.degamma = degamma ?? new float[1024 * 3];
                this.regamma = regamma ?? new float[1024 * 3];
            }
        }

        public static uint GetByteSum<T>(T obj) where T : struct
        {

            int size = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {

                Marshal.StructureToPtr(obj, ptr, false);

                Marshal.Copy(ptr, buffer, 0, size);

                uint sum = 0;
                for (int i = 0; i < size - 4; i++)
                    sum += buffer[i];

                return sum;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static bool Compare<T>(T a, T b) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] bytesA = new byte[size];
            byte[] bytesB = new byte[size];

            IntPtr ptrA = Marshal.AllocHGlobal(size);
            IntPtr ptrB = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(a, ptrA, false);
                Marshal.StructureToPtr(b, ptrB, false);

                Marshal.Copy(ptrA, bytesA, 0, size);
                Marshal.Copy(ptrB, bytesB, 0, size);

                for (int i = 0; i < size; i++)
                {
                    if (bytesA[i] != bytesB[i])
                        return false;
                }
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(ptrA);
                Marshal.FreeHGlobal(ptrB);
            }
        }

        public static T BytesToStruct<T>(byte[] rawData) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private static string[] GetPaths(string nvRegDisplayName)
        {
            const string basePath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase";
            List<string> keyNames = new List<string>();

            try
            {
                using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(basePath))
                {
                    if (baseKey != null)
                    {
                        foreach (string subKeyName in baseKey.GetSubKeyNames())
                        {
                            if (subKeyName.IndexOf(nvRegDisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                keyNames.Add($@"{basePath}\{subKeyName}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error accessing registry: {ex.Message}\nUse unlock_registry.bat to remove restrictions.");
            }

            string[] result = keyNames.ToArray();
            if (result.Length == 0)
            {
                throw new Exception($"Can't find registry for {nvRegDisplayName}");
            }

            return result;
        }

        private static string GetPath(string nvRegDisplayName)
        {
            string[] keyPaths = GetPaths(nvRegDisplayName);

            foreach (string path in keyPaths)
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ColorspaceConfig");
                        if (value != null)
                        {
                            return path;
                        }
                    }
                }
            }

            return null;
        }


        public static RegCsc GetRegCsc(string NVRegIdentifier)
        {
            RegCsc result = new RegCsc(2, 0, null, null, null, null);

            string path = GetPath(NVRegIdentifier);
            if (path != null)
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        object configValue = key.GetValue("ColorspaceConfig");
                        if (configValue is byte[] configBytes)
                        {
                            if (configBytes.Length != 33 * 4)
                                throw new Exception("Unsupported reg ColorspaceConfig content.");
                            result.Config = BytesToStruct<ColorSpaceConfig>(configBytes);
                        }

                        object lutValue = key.GetValue("ColorSpaceLutRegistryKey");
                        if (lutValue is byte[] lutBytes)
                        {
                            if (lutBytes.Length != 1024 * 4 * 3 * 2)
                                throw new Exception("Unsupported reg ColorSpaceLutRegistryKey content.");
                            result.Lut = BytesToStruct<ColorSpaceLut>(lutBytes);
                        }
                    }
                }
            }

            return result;
        }

        public static bool SetRegCsc(string NVRegIdentifier, RegCsc data)
        {
            data.Config.bytesum = GetByteSum(data.Config);

            RegCsc data_old = GetRegCsc(NVRegIdentifier);
            if (Compare(data.Config, data_old.Config) && (!data.hasLut() || Compare(data.Lut, data_old.Lut))) return false;

            string[] keyPaths = GetPaths(NVRegIdentifier);

            foreach (string path in keyPaths)
            {

                try
                {
                    int sizeConfig = Marshal.SizeOf<ColorSpaceConfig>();
                    byte[] configBytes = new byte[sizeConfig];
                    IntPtr ptrConfig = Marshal.AllocHGlobal(sizeConfig);
                    try
                    {
                        Marshal.StructureToPtr(data.Config, ptrConfig, false);
                        Marshal.Copy(ptrConfig, configBytes, 0, sizeConfig);
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, writable: true))
                        {
                            key.SetValue("ColorspaceConfig", configBytes, RegistryValueKind.Binary);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptrConfig);
                    }

                    if (!data.hasLut()) continue;
                    int sizeLut = Marshal.SizeOf<ColorSpaceLut>();
                    byte[] lutBytes = new byte[sizeLut];
                    IntPtr ptrLut = Marshal.AllocHGlobal(sizeLut);
                    try
                    {
                        Marshal.StructureToPtr(data.Lut, ptrLut, false);
                        Marshal.Copy(ptrLut, lutBytes, 0, sizeLut);
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, writable: true))
                        {
                            key.SetValue("ColorSpaceLutRegistryKey", lutBytes, RegistryValueKind.Binary);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptrLut);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error writing to {path}: {ex.Message}\nUse unlock_registry.bat to remove restrictions.");
                }

            }
            DisplayManager.RequestDisplayRefresh();
            return true;
        }

    }
}
