using System;
using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class PassthroughStereoSaver : MonoBehaviour
{
    public Meta.XR.PassthroughCameraAccess LeftCamera;
    public Meta.XR.PassthroughCameraAccess RightCamera;
    public string AlbumName = "PassthroughStereo";
    public string FilePrefix = "stereo_";

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

        // Wait end of frame so render textures are up-to-date
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

        // Temporary RT to read pixels (use ARGB32 to be compatible)
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

        // Restore active RT and release temp
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
        string filename = FilePrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

#if UNITY_ANDROID && !UNITY_EDITOR
        // Try to save to MediaStore (Scoped Storage). If permission required, request it first.
        if (!Permission.HasUserAuthorizedPermission("android.permission.WRITE_EXTERNAL_STORAGE"))
        {
            Permission.RequestUserPermission("android.permission.WRITE_EXTERNAL_STORAGE");
            // give user a frame to respond; in production you'd want a robust flow
            yield return new WaitForSeconds(0.5f);
        }
        SaveImageToGallery_Android(png, filename, AlbumName);
#else
        // Editor / other platforms: save to persistentDataPath
        string path = Path.Combine(Application.persistentDataPath, filename);
        try
        {
            File.WriteAllBytes(path, png);
            Debug.Log($"Saved stereo image to: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save file: {e}");
        }
#endif

        // cleanup
        Destroy(leftTex);
        Destroy(rightTex);
        Destroy(combined);
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
