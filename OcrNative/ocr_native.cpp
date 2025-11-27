#include <windows.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Media.Ocr.h>

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Storage::Streams;
using namespace Windows::Graphics::Imaging;
using namespace Windows::Media::Ocr;

// Exported function signature:
//  return: 0 on success, non-zero on failure
//  data: pointer to image bytes (e.g. PNG/JPEG)
//  length: size in bytes
//  outText: receives CoTaskMemAlloc'ed wide string (UTF-16, null-terminated)
extern "C" __declspec(dllexport)
int __stdcall OcrExtractText(const std::uint8_t* data, int length, wchar_t** outText)
{
    if (!data || length <= 0 || !outText)
        return E_INVALIDARG;

    try
    {
        // Ensure WinRT apartment is initialized for this thread
        init_apartment();

        // Wrap raw bytes into an in-memory stream
        InMemoryRandomAccessStream stream;

        DataWriter writer(stream);
        writer.WriteBytes(array_view<const std::uint8_t>(data, data + length));
        writer.StoreAsync().get();
        stream.Seek(0);

        // Decode the image
        auto decoder = BitmapDecoder::CreateAsync(stream).get();
        auto bitmap = decoder.GetSoftwareBitmapAsync().get();

        // Create OCR engine using user profile languages
        auto engine = OcrEngine::TryCreateFromUserProfileLanguages();
        if (!engine)
            return E_FAIL;

        auto result = engine.RecognizeAsync(bitmap).get();
        auto text = result.Text(); // hstring

        // Allocate UTF-16 buffer for managed side (CoTaskMemAlloc so C# can free with Marshal.FreeCoTaskMem)
        size_t len = text.size();
        size_t bytes = (len + 1) * sizeof(wchar_t);

        wchar_t* buffer = static_cast<wchar_t*>(CoTaskMemAlloc(bytes));
        if (!buffer)
            return E_OUTOFMEMORY;

        memcpy(buffer, text.c_str(), len * sizeof(wchar_t));
        buffer[len] = L'\0';

        *outText = buffer;
        return S_OK;
    }
    catch (const hresult_error&)
    {
        return E_FAIL;
    }
    catch (...)
    {
        return E_FAIL;
    }
}
