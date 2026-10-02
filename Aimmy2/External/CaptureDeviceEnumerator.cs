using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Other;
using static Other.LogManager;

namespace Aimmy2.External
{
    /// <summary>
    /// Énumère les périphériques de capture vidéo USB (cartes de capture)
    /// Utilise DirectShow COM pour détecter les dispositifs
    /// </summary>
    internal class CaptureDeviceEnumerator
    {
        // DirectShow COM interfaces
        [ComImport, Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F")]
        private interface ICreateDevEnum { }

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
        private interface IEnumMoniker { }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
        private interface IPropertyBag { }

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumMonikerInterface
        {
            [PreserveSig]
            int Next(uint celt, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IMoniker[] rgelt, IntPtr pceltFetched);

            [PreserveSig]
            int Skip(uint celt);

            void Reset();

            void Clone(out IEnumMonikerInterface ppEnum);
        }

        [ComImport, Guid("0000010c-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersist
        {
            void GetClassID(out Guid pClassID);
        }

        [ComImport, Guid("7C0FFAB0-CD84-11D0-949F-00A0C932E65B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMoniker : IPersist
        {
            void BindToObject(IBindCtx? pbc, IMoniker? pmkToLeft, [In] ref Guid riidResult, [MarshalAs(UnmanagedType.Interface)] out object ppvResult);

            void BindToStorage(IBindCtx? pbc, IMoniker? pmkToLeft, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvStorage);
        }

        [ComImport, Guid("0000000e-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IBindCtx { }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBagInterface
        {
            [PreserveSig]
            int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, out object pVar, IErrorLog? pErrorLog);
        }

        [ComImport, Guid("3127CA40-446D-11CE-8135-00AA004BB851")]
        private interface IErrorLog { }

        private static readonly Guid CLSID_SystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D91-11d0-BD4B-00A0C911CE86");

        /// <summary>
        /// Énumère tous les périphériques de capture vidéo disponibles
        /// </summary>
        public static List<(int index, string name, string devicePath)> EnumerateCaptureDevices()
        {
            var devices = new List<(int, string, string)>();
            IEnumMonikerInterface? enumMoniker = null;

            try
            {
                // Crée le DirectShow device enumerator
                var sysDevEnum = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum))!;

                // Obtient l'énumérateur pour les périphériques d'entrée vidéo
                sysDevEnum.GetType().InvokeMember(
                    "CreateClassEnumerator",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    sysDevEnum,
                    new object[] { CLSID_VideoInputDeviceCategory, null });

                var createEnumMethod = sysDevEnum.GetType().GetMethod("CreateClassEnumerator");
                enumMoniker = (IEnumMonikerInterface?)createEnumMethod?.Invoke(sysDevEnum, new object[] { CLSID_VideoInputDeviceCategory, null });

                if (enumMoniker == null)
                {
                    Log(LogLevel.Warning, "Aucun énumérateur vidéo DirectShow trouvé", true);
                    return devices;
                }

                // Énumère tous les monikers (dispositifs)
                IMoniker[] monikers = new IMoniker[1];
                int index = 0;

                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    IMoniker moniker = monikers[0];

                    try
                    {
                        // Obtient les propriétés du périphérique
                        Guid iid = typeof(IPropertyBagInterface).GUID;
                        moniker.BindToStorage(null, null, ref iid, out object bagObj);
                        IPropertyBagInterface bag = (IPropertyBagInterface)bagObj;

                        // Récupère le nom du périphérique
                        bag.Read("FriendlyName", out object nameObj, null);
                        string friendlyName = (string?)nameObj ?? $"Capture Device {index}";

                        // Récupère le chemin d'accès du périphérique (VID/PID pour USB)
                        bag.Read("DevicePath", out object pathObj, null);
                        string devicePath = (string?)pathObj ?? $"Unknown-{index}";

                        devices.Add((index, friendlyName, devicePath));
                        Log(LogLevel.Info, $"Trouvé: {friendlyName} ({devicePath})", false);

                        index++;

                        // Libère les ressources COM
                        Marshal.ReleaseComObject(bag);
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Warning, $"Erreur lecture propriétés périphérique: {ex.Message}", false);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(moniker);
                    }
                }

                Log(LogLevel.Info, $"Énumération complète: {devices.Count} périphérique(s) trouvé(s)", true, 1500);
                return devices;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erreur énumération DirectShow: {ex.Message}", true);
                return devices;
            }
            finally
            {
                if (enumMoniker != null)
                {
                    Marshal.ReleaseComObject(enumMoniker);
                }
            }
        }

        /// <summary>
        /// Filtre pour obtenir uniquement les cartes de capture USB (exclut webcams intégrées)
        /// </summary>
        public static List<(int index, string name, string devicePath)> EnumerateUSBCaptureCards()
        {
            var allDevices = EnumerateCaptureDevices();
            var usbDevices = new List<(int, string, string)>();

            foreach (var (index, name, path) in allDevices)
            {
                // Détecte USB par présence de VID/PID dans le chemin
                if (path.Contains("VID") && path.Contains("PID"))
                {
                    usbDevices.Add((index, name, path));
                    Log(LogLevel.Info, $"USB Capture Card détecté: {name}", false);
                }
            }

            return usbDevices;
        }

        /// <summary>
        /// Filtre pour obtenir uniquement les webcams/caméras
        /// </summary>
        public static List<(int index, string name, string devicePath)> EnumerateWebcams()
        {
            var allDevices = EnumerateCaptureDevices();
            var webcams = new List<(int, string, string)>();

            foreach (var (index, name, path) in allDevices)
            {
                // Les webcams n'ont généralement pas VID/PID, ou ont des noms spécifiques
                if (!path.Contains("VID") || name.Contains("Webcam") || name.Contains("Camera"))
                {
                    webcams.Add((index, name, path));
                    Log(LogLevel.Info, $"Webcam détecté: {name}", false);
                }
            }

            return webcams;
        }

        /// <summary>
        /// Extrait les info VID/PID d'un chemin de périphérique
        /// </summary>
        public static bool TryExtractVIDPID(string devicePath, out ushort vid, out ushort pid)
        {
            vid = 0;
            pid = 0;

            try
            {
                var vidMatch = System.Text.RegularExpressions.Regex.Match(devicePath, @"VID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var pidMatch = System.Text.RegularExpressions.Regex.Match(devicePath, @"PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (vidMatch.Success && pidMatch.Success)
                {
                    vid = ushort.Parse(vidMatch.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                    pid = ushort.Parse(pidMatch.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
