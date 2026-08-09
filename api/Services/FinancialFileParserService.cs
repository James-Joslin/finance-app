// Services/FinancialFileParserService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using financesApi.models;
using financesApi.utilities;

namespace financesApi.services
{
    public static class FinancialFileParserService
    {
        public static List<TransactionDto> Parse(Stream fileStream, string fileName)
        {
            // Buffer the stream so we can read it multiple times if needed
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            
            // Detect file type by extension first
            var extension = Path.GetExtension(fileName)?.ToLower();
            
            // If no extension, try to detect by content
            if (string.IsNullOrEmpty(extension) || extension == ".txt")
            {
                extension = DetectFileTypeByContent(memoryStream);
                memoryStream.Position = 0; // Reset after detection
            }
            
            Console.WriteLine($"Processing file: {fileName} as type: {extension}");
            
            List<TransactionDto> results;
            switch (extension)
            {
                case ".ofx":
                    var ofxResults = OfxParser.Parse(memoryStream);
                    results = ofxResults.Cast<TransactionDto>().ToList();
                    break;
                    
                case ".qif":
                    results = QifParser.Parse(memoryStream);
                    break;

                case ".pdf":
                    if (!HasPdfSignature(memoryStream))
                        throw new InvalidDataException("The uploaded file has a .pdf name but is not a valid PDF file.");
                    memoryStream.Position = 0;
                    results = HalifaxPdfParser.Parse(memoryStream);
                    break;
                    
                default:
                    throw new NotSupportedException($"File type {extension} is not supported. Please upload an OFX, QIF, or Halifax PDF file.");
            }
            return results;
        }

        private static bool HasPdfSignature(Stream stream)
        {
            var signature = new byte[5];
            var bytesRead = stream.Read(signature, 0, signature.Length);
            stream.Position = 0;
            return bytesRead == signature.Length && Encoding.ASCII.GetString(signature) == "%PDF-";
        }
        
        private static string DetectFileTypeByContent(Stream stream)
        {
            // Read first 500 bytes to detect file type
            var buffer = new byte[500];
            int bytesRead = stream.Read(buffer, 0, 500);
            stream.Position = 0; // Reset stream position
            
            if (bytesRead == 0)
                throw new InvalidOperationException("File is empty");

            if (bytesRead >= 5 && Encoding.ASCII.GetString(buffer, 0, 5) == "%PDF-") return ".pdf";
            
            var header = Encoding.UTF8.GetString(buffer, 0, bytesRead).ToUpper();
            
            // Check for OFX markers
            if (header.Contains("OFXHEADER") || header.Contains("<OFX>") || header.Contains("</OFX>"))
            {
                return ".ofx";
            }
            
            // Check for QIF markers
            if (header.Contains("!TYPE:") || header.Contains("!ACCOUNT") || 
                (header.Contains("^") && (header.Contains("D") || header.Contains("T") || header.Contains("P"))))
            {
                return ".qif";
            }
            
            throw new NotSupportedException("Could not determine file type from content");
        }
    }
}
