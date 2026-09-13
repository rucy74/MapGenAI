using System;

namespace MapGenAI.LLM
{
    public static class VisionPayload
    {
        public static void Validate(byte[] image,string mimeType)
        {
            if (image == null || image.Length < 8 || image.Length > 1024 * 1024)
                throw new ArgumentException("Vision image must be 8 bytes to 1 MiB after resizing");
            if (mimeType != "image/png" && mimeType != "image/jpeg") throw new ArgumentException("PNG/JPEG vision input required");
        }
    }
}
