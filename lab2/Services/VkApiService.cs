using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using lab2.Entities;
using lab2.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lab2.Services
{
    class VkApiService : IVkApiService
    {
        private readonly ILogger<VkApiService> _logger;
        private readonly string _appId;
        private readonly string _secret;
        private AccessTokenModel? _accessTokenModel;
        private readonly string _redirectUri;
        private const string ApiVersion = "5.126";

        public VkApiService(ILogger<VkApiService> logger, IConfiguration configuration)
        {
            _logger = logger;

            _appId = configuration["ApplicationId"];
            _secret = configuration["SecretKey"];

            string protocol = configuration["AppProtocol"];
            string domain = configuration["AppDomain"];
            int port = int.Parse(configuration["AppPort"]);
            var uriBuilder = new UriBuilder { Scheme = protocol, Host = domain, Path = "Auth", Port = port };
            _redirectUri = uriBuilder.ToString();
        }

        public async Task<User?> Auth(string code)
        {
            UriBuilder builder = new()
            {
                Scheme = "https",
                Host = "oauth.vk.com",
                Path = "access_token",
                Query = $"client_id={_appId}&client_secret={_secret}&redirect_uri={_redirectUri}&code={code}"
            };
            
            WebRequest request = WebRequest.CreateHttp(builder.ToString());
            using WebResponse response = await request.GetResponseAsync();
            await using Stream stream = response.GetResponseStream();
            var accessTokenModel = await JsonSerializer.DeserializeAsync<AccessTokenModel>(stream);
            _accessTokenModel = accessTokenModel;
            _logger.LogInformation(_accessTokenModel?.ToString());

            return await GetUser(accessTokenModel!.UserId!.Value);
        }

        private async Task<User?> GetUser(int id)
        {
            if (_accessTokenModel is null) return null;
            
            const string methodName = "users.get";
            UriBuilder builder = new()
            {
                Scheme = "https",
                Host = "api.vk.com",
                Path = $"method/{methodName}",
                Query = $"user_ids={id}&fields=photo_100&access_token={_accessTokenModel.AccessToken}&v={ApiVersion}"
            };
            
            WebRequest request = WebRequest.CreateHttp(builder.ToString());
            using WebResponse response = await request.GetResponseAsync();
            await using Stream stream = response.GetResponseStream();
            var profileResponseInfoModel = await JsonSerializer.DeserializeAsync<ApiResponseModel<UserInfoModel>>(stream);
            
            if (profileResponseInfoModel is null)
            {
                _logger.LogError("Cannot get profile info");
                return null;
            }

            if (profileResponseInfoModel.Response is null)
            {
                _logger.LogWarning($"Got error {profileResponseInfoModel.Error}");
                return null;
            }

            UserInfoModel? userInfoModel = profileResponseInfoModel.Response.FirstOrDefault();
            if (userInfoModel is null)
            {
                _logger.LogError("Couldn't get user from result set");
                return null;
            }

            _logger.LogInformation(userInfoModel.ToString());
            return new User(userInfoModel.FirstName, userInfoModel.LastName) { ProfilePic = userInfoModel.ProfilePic100 };
        }

        public string GetAuthorizeUrl() =>
            new UriBuilder
            {
                Scheme = "https",
                Host = "oauth.vk.com",
                Path = "authorize",
                Query = $"client_id={_appId}&display=page&redirect_uri={_redirectUri}&response_type=code&v={ApiVersion}"
            }.ToString();
    }
}
