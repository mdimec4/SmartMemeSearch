using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SmartMemeSearch.Wpf.Services
{
    public class OcrService
    {
        // Adjust "OcrNative.dll" if you rename the DLL
        private const string NativeDllName = "OcrNative.dll";

        // P/Invoke signature matching the C++ function
        [DllImport(NativeDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern int OcrExtractText(
            byte[] data,
            int length,
            out IntPtr textPtr
        );

        public async Task<string> ExtractTextAsync(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return string.Empty;

            return await Task.Run(() =>
            {
                IntPtr textPtr = IntPtr.Zero;

                try
                {
                    int hr = OcrExtractText(imageBytes, imageBytes.Length, out textPtr);

                    if (hr != 0 || textPtr == IntPtr.Zero)
                    {
                        System.Diagnostics.Debug.WriteLine($"OCR native error: 0x{hr:X8}");
                        return string.Empty;
                    }

                    // Marshal CoTaskMemAlloc'ed UTF-16 string to managed
                    string result = Marshal.PtrToStringUni(textPtr) ?? string.Empty;

                    return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("OCR ERROR: " + ex);
                    return string.Empty;
                }
                finally
                {
                    if (textPtr != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(textPtr);
                        textPtr = IntPtr.Zero;
                    }
                }
            });
        }
    }
}
