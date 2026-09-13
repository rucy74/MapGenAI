using System;

namespace MapGenAI.ImageInput
{
    // Keep codecs and saved data while image generation is paused by product policy.
    public static class ImageFeatureGate
    {
        public static bool Enabled => false;
        public static string Message => "이미지 기반 생성은 일시 중단되었습니다. 텍스트 요청을 사용해 주세요. / Image-based generation is paused. Use text requests.";
        public static void RequireEnabled()
        {
            if (!Enabled) throw new InvalidOperationException(Message);
        }
    }
}
