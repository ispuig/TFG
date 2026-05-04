using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using Recorder = UnityEngine.Recorder;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class PassthroughStereoImage : MonoBehaviour
{
    public Meta.XR.PassthroughCameraAccess LeftCamera;
    public Meta.XR.PassthroughCameraAccess RightCamera;
    public string AlbumName = "PassthroughStereo";
    public string FilePrefix = "stereo_";
    [SerializeField] private int PixelOffset = 96; //64 ON QUEST 3S
    
    /// <summary>
    /// Starts capture and save operation.
    /// </summary>
    public void SaveStereoNow()
    {
        StartCoroutine(CaptureAndSaveCoroutine());
    }

    private IEnumerator CaptureAndSaveCoroutine()
    {
        if (LeftCamera == null || RightCamera == null)
        {
            Debug.LogError("Assign LeftCamera and RightCamera before calling SaveStereoNow().");
            yield break;
        }
        if (!LeftCamera.IsPlaying || !RightCamera.IsPlaying)
        {
            Debug.LogError("Both PassthroughCameraAccess instances must be playing.");
            yield break;
        }

        yield return new WaitForEndOfFrame();

        var leftRT = LeftCamera.GetTexture() as RenderTexture;
        var rightRT = RightCamera.GetTexture() as RenderTexture;
        if (leftRT == null || rightRT == null)
        {
            Debug.LogError("Could not get RenderTexture from one of the PassthroughCameraAccess instances.");
            yield break;
        }

        int w = Math.Min(leftRT.width, rightRT.width);
        int h = Math.Min(leftRT.height, rightRT.height);
        if (w <= 0 || h <= 0)
        {
            Debug.LogError("Invalid camera resolution.");
            yield break;
        }

        var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;

        // Read left image
        Graphics.Blit(leftRT, tmp);
        var leftTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        RenderTexture.active = tmp;
        leftTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        leftTex.Apply();

        // Read right image
        Graphics.Blit(rightRT, tmp);
        var rightTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        RenderTexture.active = tmp;
        rightTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        rightTex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);

        // Combine side-by-side (left | right)
        var combined = new Texture2D(w * 2, h, TextureFormat.RGBA32, false);
        var leftPixels = leftTex.GetPixels32();
        var rightPixels = rightTex.GetPixels32();
        combined.SetPixels32(0, 0, w, h, leftPixels);
        combined.SetPixels32(w, 0, w, h, rightPixels);
        combined.Apply();

        byte[] png = combined.EncodeToPNG();
        
        // Inject XMP metadata for stereo
        byte[] stereoImage = InjectStereoMetadata(png, w * 2, h);
        
        string filename = FilePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission("android.permission.WRITE_EXTERNAL_STORAGE"))
        {
            Permission.RequestUserPermission("android.permission.WRITE_EXTERNAL_STORAGE");
            yield return new WaitForSeconds(0.5f);
        }
        SaveImageToGallery_Android(stereoImage, filename, AlbumName);
#else
        string path = Path.Combine(Application.persistentDataPath, filename);
        try
        {
            File.WriteAllBytes(path, stereoImage);
            Debug.Log($"Saved stereo image to: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save file: {e}");
        }
#endif

        Destroy(leftTex);
        Destroy(rightTex);
        Destroy(combined);
    }

    private byte[] InjectStereoMetadata(byte[] pngBytes, int width, int height)
    {
        // Create XMP metadata for left-right stereo
        string xmpMetadata = CreateStereoXMP(width, height);
        byte[] xmpBytes = Encoding.UTF8.GetBytes(xmpMetadata);
        
        // PNG chunk structure: Length (4 bytes) + Type (4 bytes) + Data + CRC (4 bytes)
        byte[] chunkType = Encoding.ASCII.GetBytes("iTXt");
        byte[] keyword = Encoding.UTF8.GetBytes("XML:com.adobe.xmp\0");
        
        // Prepare chunk data
        byte[] chunkData = new byte[keyword.Length + xmpBytes.Length];
        Array.Copy(keyword, 0, chunkData, 0, keyword.Length);
        Array.Copy(xmpBytes, 0, chunkData, keyword.Length, xmpBytes.Length);
        
        // Calculate CRC
        uint crc = CalculateCRC(chunkType, chunkData);
        
        // Build the complete chunk
        using (MemoryStream ms = new MemoryStream())
        {
            // Write PNG signature and IHDR (first 33 bytes typically)
            // Find IEND chunk position (last 12 bytes)
            int iendPos = pngBytes.Length - 12;
            
            // Write everything up to IEND
            ms.Write(pngBytes, 0, iendPos);
            
            // Write XMP chunk
            ms.Write(BitConverter.GetBytes(ReverseBytes((uint)chunkData.Length)), 0, 4);
            ms.Write(chunkType, 0, 4);
            ms.Write(chunkData, 0, chunkData.Length);
            ms.Write(BitConverter.GetBytes(ReverseBytes(crc)), 0, 4);
            
            // Write IEND chunk
            ms.Write(pngBytes, iendPos, 12);
            
            return ms.ToArray();
        }
    }

    private string CreateStereoXMP(int width, int height)
    {
        int halfWidth = width / 2;
        
        return $@"<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?>
<x:xmpmeta xmlns:x='adobe:ns:meta/'>
  <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>
    <rdf:Description rdf:about='' xmlns:GImage='http://ns.google.com/photos/1.0/image/'>
      <GImage:Mime>image/png</GImage:Mime>
      <GImage:Data>
        <rdf:Seq>
          <rdf:li rdf:parseType='Resource'>
            <GImage:Mime>image/png</GImage:Mime>
            <GImage:Data>{Convert.ToBase64String(new byte[0])}</GImage:Data>
          </rdf:li>
        </rdf:Seq>
      </GImage:Data>
    </rdf:Description>
    <rdf:Description rdf:about='' xmlns:GDepth='http://ns.google.com/photos/1.0/depthmap/'>
      <GDepth:Format>RangeInverse</GDepth:Format>
      <GDepth:Near>0</GDepth:Near>
      <GDepth:Far>1</GDepth:Far>
      <GDepth:Mime>image/png</GDepth:Mime>
    </rdf:Description>
    <rdf:Description rdf:about='' xmlns:GFocus='http://ns.google.com/photos/1.0/focus/'>
      <GFocus:BlurAtInfinity>0</GFocus:BlurAtInfinity>
      <GFocus:FocalDistance>0</GFocus:FocalDistance>
      <GFocus:FocalPointX>0.5</GFocus:FocalPointX>
      <GFocus:FocalPointY>0.5</GFocus:FocalPointY>
    </rdf:Description>
  </rdf:RDF>
</x:xmpmeta>
<?xpacket end='w'?>";
    }

    private uint CalculateCRC(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        
        // CRC for type
        for (int i = 0; i < type.Length; i++)
        {
            crc = UpdateCRC(crc, type[i]);
        }
        
        // CRC for data
        for (int i = 0; i < data.Length; i++)
        {
            crc = UpdateCRC(crc, data[i]);
        }
        
        return crc ^ 0xFFFFFFFF;
    }

    private uint UpdateCRC(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++)
        {
            if ((crc & 1) != 0)
                crc = 0xEDB88320 ^ (crc >> 1);
            else
                crc = crc >> 1;
        }
        return crc;
    }

    private uint ReverseBytes(uint value)
    {
        return (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
               (value & 0x00FF0000U) >> 8 | (value & 0xFF000000U) >> 24;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void SaveImageToGallery_Android(byte[] imageBytes, string filename, string album)
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var contentResolver = activity.Call<AndroidJavaObject>("getContentResolver");

                var mediaClass = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                var externalUri = mediaClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

                var contentValues = new AndroidJavaObject("android.content.ContentValues");
                contentValues.Call("put", "display_name", filename);
                contentValues.Call("put", "mime_type", "image/png");
                contentValues.Call("put", "relative_path", "Pictures/" + album);

                var uri = contentResolver.Call<AndroidJavaObject>("insert", externalUri, contentValues);
                if (uri == null)
                {
                    Debug.LogError("Failed to create MediaStore entry.");
                    return;
                }

                var outputStream = contentResolver.Call<AndroidJavaObject>("openOutputStream", uri);
                if (outputStream == null)
                {
                    Debug.LogError("Failed to open output stream for MediaStore URI.");
                    return;
                }

                outputStream.Call("write", imageBytes);
                outputStream.Call("close");

                Debug.Log($"Saved stereo image to gallery: {filename}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveImageToGallery_Android failed: {e}");
        }
    }
#endif
}