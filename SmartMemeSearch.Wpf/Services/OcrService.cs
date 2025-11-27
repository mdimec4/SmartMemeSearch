using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using System;
using System.IO;
using OpenCvSharp;

namespace SmartMemeSearch.Wpf.Services
{
    public class OcrService
    {
        private readonly PaddleOcrAll _ocr;

        public OcrService()
        {
            // loads built-in English OCR model
            _ocr = new PaddleOcrAll(LocalFullModels.EnglishV4)
            {
                AllowRotateDetection = true,
                Enable180Classification = true,
            };
        }

        public async Task<string> ExtractTextAsync(string filePath)
        {
            return await Task.Run(() => ExtractText(filePath));
        }

        private string ExtractText(string filePath)
        {
            if (!File.Exists(filePath))
                return "";

            try
            {
 
                Mat image = Cv2.ImRead(filePath, ImreadModes.Color);

                // run OCR
                PaddleOcrResult result = _ocr.Run(image);

                // return all recognized text in a single line
                return result.Text;
            }
            catch
            {
                return "";
            }
        }
    }
}
