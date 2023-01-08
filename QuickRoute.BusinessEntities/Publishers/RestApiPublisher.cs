using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using QuickRoute.Common;
using Encoder = System.Drawing.Imaging.Encoder;

namespace QuickRoute.BusinessEntities.Publishers;

public class RestApiPublisher : IMapPublisher
{
    private string webServiceUrl;

    public string WebServiceUrl
    {
        get => webServiceUrl + (webServiceUrl.EndsWith("/") ? string.Empty : "/");
        set => webServiceUrl = value;
    }

    public string Username { get; set; }
    public string Password { get; set; }
    
    private HttpClient Client { get; set; }
    
    private string AccessToken { get; set; }
    private DateTime TokenExpires { get; set; }

    public RestApiPublisher()
    {
        Client = new HttpClient();
    }

    private void CheckAndRefreshToken()
    {
        if (TokenExpires < DateTime.Now.AddMinutes(4))
        {
            Connect();
        }
    }
    
    public PublishResult Publish(MapInfo map)
    {
        var request = new PublishPreUploadedMapRequest
        {
            MapInfo = map,
        };

        try
        {
            // use partial upload
            // map image
            FileUploadResult mapFileUploadResult = null;
            if (request.MapInfo.MapImageData != null) mapFileUploadResult = PartialFileUpload(request.MapInfo.MapImageData, request.MapInfo.MapImageFileExtension);
            // blank map image
            FileUploadResult blankMapFileUploadResult = null;
            if (request.MapInfo.BlankMapImageData != null) blankMapFileUploadResult = PartialFileUpload(request.MapInfo.BlankMapImageData, request.MapInfo.MapImageFileExtension);
            // thumbnail
            FileUploadResult thumbnailFileUploadResult = null;
            var thumbnailImageData = CreateThumbnailImageData(map);
            if (thumbnailImageData != null) thumbnailFileUploadResult = PartialFileUpload(thumbnailImageData, "jpg");

            if (mapFileUploadResult is not { Success: false } && blankMapFileUploadResult is not { Success: false } && thumbnailFileUploadResult is not { Success: false })
            {
                if (mapFileUploadResult != null) request.PreUploadedMapImageFileName = mapFileUploadResult.FileName;
                if (blankMapFileUploadResult != null) request.PreUploadedBlankMapImageFileName = blankMapFileUploadResult.FileName;
                if (thumbnailFileUploadResult != null) request.PreUploadedThumbnailImageFileName = thumbnailFileUploadResult.FileName;
            }

            // reset image data as it already has been uploaded
            request.MapInfo.MapImageData = null;
            request.MapInfo.BlankMapImageData = null;
            var response = PublishPreUploadedMap(request);
            return new PublishResult
            {
                Success = response.Success,
                ErrorMessage = response.ErrorMessage,
                Url = response.Url
            };            
        }
        catch (Exception ex)
        {
            return new PublishResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private PublishResult PublishPreUploadedMap(PublishPreUploadedMapRequest publishPreUploadedMap)
    {
        CheckAndRefreshToken();
        
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{WebServiceUrl}publish"),
        };
        
        request.Content = new StringContent(JsonConvert.SerializeObject(publishPreUploadedMap), Encoding.UTF8, "application/json");
        
        var response = Client.SendAsync(request).Result;

        if (!response.IsSuccessStatusCode)
        {
            return new PublishResult { Success = false, ErrorMessage = "Error publishing map" };
        }

        var result = JsonConvert.DeserializeObject<PublishResult>(response.Content.ReadAsStringAsync().Result);
        
        return result ?? new PublishResult { Success = false, ErrorMessage = "Response content error" };
    }

    private FileUploadResult PartialFileUpload(byte[] mapImageData, string extension)
    {
        CheckAndRefreshToken();
        
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{WebServiceUrl}upload"),
        };

        var stream = new MemoryStream(mapImageData);
        
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(stream), "file", $"file.{extension}" },
            { new StringContent(extension), "extension" },
        };
        request.Content = content;
        var response = Client.SendAsync(request).Result;

        if (!response.IsSuccessStatusCode)
        {
            return new FileUploadResult { Success = false, ErrorMessage = "Error uploading map image" };
        }

        var result = JsonConvert.DeserializeObject<FileUploadResult>(response.Content.ReadAsStringAsync().Result);
        
        return result ?? new FileUploadResult { Success = false, ErrorMessage = "Response content error" };
    }

    public GetAllCategoriesResult GetAllCategories()
    {
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{WebServiceUrl}allcategories"),
        };
        var response = Client.SendAsync(request).Result;

        if (!response.IsSuccessStatusCode)
            return new GetAllCategoriesResult { Success = false };
        
        var result = JsonConvert.DeserializeObject<GetAllCategoriesResult>(response.Content.ReadAsStringAsync().Result);
        
        return result ?? new GetAllCategoriesResult { Success = false, ErrorMessage = "Response content error" };
    }

    public GetAllMapsResult GetAllMaps()
    {
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri($"{WebServiceUrl}allmaps"),
        };
        var response = Client.SendAsync(request).Result;

        if (!response.IsSuccessStatusCode)
            return new GetAllMapsResult { Success = false };
        
        var result = JsonConvert.DeserializeObject<GetAllMapsResult>(response.Content.ReadAsStringAsync().Result);
        
        return result ?? new GetAllMapsResult { Success = false, ErrorMessage = "Response content error" };
    }

    public ConnectResult Connect()
    {
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{WebServiceUrl}token"),
        };
        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                {"username", Username}, 
                {"password", Password}, 
            });

        var response = Client.SendAsync(request).Result;

        if (!response.IsSuccessStatusCode)
        {
            return new ConnectResult { Success = false, ErrorMessage = "Authentication error" };
        }

        var tokenResponse = JsonConvert.DeserializeObject<AuthenticationTokenResponse>(response.Content.ReadAsStringAsync().Result);
        if (tokenResponse == null)
            return new ConnectResult { Success = false, ErrorMessage = "Response content error" };
        
        TokenExpires = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn);
        AccessToken = tokenResponse.AccessToken;
        
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        return new ConnectResult
        {
            Success = true,
        };
    }
    
    private static byte[] CreateThumbnailImageData(MapInfo mapInfo) //, ThumbnailProperties tp)
    {
      if (mapInfo.MapImageData == null && mapInfo.BlankMapImageData == null) return null;
      // get original image from byte array
      var ms = new MemoryStream(mapInfo.MapImageData ?? mapInfo.BlankMapImageData);
      var image = Image.FromStream(ms);
      ms.Close();
      ms.Dispose();

      // create blank thumbnail image
      // todo: get these values from webservice
      var thumbnailSize = new Size(400, 100);
      var thumbnailScale = 0.5;
      var thumbnailBitmap = new Bitmap(thumbnailSize.Width, thumbnailSize.Height);
      // the rectangle in the original image that corresponds to the thumbnail image
      var imageRectangle = new Rectangle((image.Width - thumbnailSize.Width) / 2, (image.Height - thumbnailSize.Height) / 2,
                                         Convert.ToInt32(thumbnailSize.Width / thumbnailScale), Convert.ToInt32(thumbnailSize.Height / thumbnailScale));
      // perform some resizing if the image is not large enough
      if (imageRectangle.Width > image.Width)
      {
        imageRectangle.X = 0;
        imageRectangle.Width = image.Width;
      }
      if (imageRectangle.Height > image.Height)
      {
        imageRectangle.Y = 0;
        imageRectangle.Height = image.Height;
      }

      // calculate actual thumbnail rectangle and draw the image to the thumbnail
      var thumbnailRectangle = new Rectangle(
        Convert.ToInt32((thumbnailSize.Width - thumbnailScale * imageRectangle.Width) / 2),
        Convert.ToInt32((thumbnailSize.Height - thumbnailScale * imageRectangle.Height) / 2),
        Convert.ToInt32(thumbnailScale * imageRectangle.Width),
        Convert.ToInt32(thumbnailScale * imageRectangle.Height));
      var thumbnailGraphics = Graphics.FromImage(thumbnailBitmap);
      thumbnailGraphics.SmoothingMode = SmoothingMode.AntiAlias;
      thumbnailGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
      using (var b = new SolidBrush(Color.White))
      {
        thumbnailGraphics.FillRectangle(b, new Rectangle(new Point(0, 0), thumbnailSize));
      }
      thumbnailGraphics.DrawImage(image, thumbnailRectangle, imageRectangle, GraphicsUnit.Pixel);
      thumbnailGraphics.Dispose();
      image.Dispose();

      // create byte array from image
      var thumbnailStream = new MemoryStream();
      var info = ImageCodecInfo.GetImageEncoders();
      var encoderParams = new EncoderParameters(1);
      encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
      
      thumbnailBitmap.Save(thumbnailStream, info[1], encoderParams);
      thumbnailBitmap.Dispose();
      var data = thumbnailStream.ToArray();
      thumbnailStream.Dispose();

      return data;
    }    

    private class PublishPreUploadedMapRequest
    {
        public MapInfo MapInfo { get; set; }
        public string PreUploadedMapImageFileName { get; set; }
        public string PreUploadedBlankMapImageFileName { get; set; }
        public string PreUploadedThumbnailImageFileName { get; set; }
    }

    private class FileUploadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string FileName { get; set; }
    }
    
    private class AuthenticationTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonProperty("token_type")]
        public string TokenType { get; set; }
    }    
}