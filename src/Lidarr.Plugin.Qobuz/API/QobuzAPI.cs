using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using NLog;
using NzbDrone.Core.Indexers.Qobuz;
using QobuzApiSharp.Exceptions;
using QobuzApiSharp.Models.User;
using QobuzApiSharp.Service;

namespace NzbDrone.Plugin.Qobuz.API;

public class QobuzAPI
{
    public static QobuzAPI Instance { get; private set; }

    public static void Initialize(Logger logger, string appId, string appSecret, bool forceRecreate = false)
    {
        if (Instance != null && !forceRecreate)
            return;
        Instance = new QobuzAPI(appId, appSecret);
    }

    private QobuzAPI(string appId, string appSecret)
    {
        Instance = this;
        if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(appSecret))
            _client = new(appId, appSecret);
        else
            _client = new();
    }

    public QobuzApiService Client => _client;
    private QobuzApiService _client;

    public Login Login => _login;
    private Login _login;

    public string? LastPassword => _lastPassword;
    private string? _lastPassword;

    public void PickSignInFromSettings(QobuzIndexerSettings settings, Logger logger)
    {
        bool ep = !string.IsNullOrEmpty(settings.Email) && !string.IsNullOrEmpty(settings.MD5Password);
        bool it = !string.IsNullOrEmpty(settings.UserID) && !string.IsNullOrEmpty(settings.UserAuthToken);

        _lastPassword = settings.MD5Password;

        logger.Debug($"Qobuz signing in with App ID: {_client.AppId ?? "(auto-detected)"}");

        try
        {
            if (ep)
                LoginWithEmail(settings.Email, settings.MD5Password);
            else if (it)
                LoginWithToken(settings.UserID, settings.UserAuthToken);
        }
        catch (ApiErrorResponseException ex)
        {
            logger.Error($"Qobuz login failed — Status: {ex.ResponseStatusCode} {ex.ResponseStatus}, Reason: {ex.ResponseReason}\n{ex}");
        }
        catch (Exception ex)
        {
            logger.Error($"Qobuz login failed:\n{ex}");
        }
    }

    public void LoginWithEmail(string email, string password)
    {
        _login = _client.LoginWithEmail(email, password);
    }

    public void LoginWithToken(string userId, string userAuthToken)
    {
        _login = _client.LoginWithToken(userId, userAuthToken);
    }

    public string GetAPIUrl(string method, Dictionary<string, string> parameters = null)
    {
        parameters ??= [];

        StringBuilder stringBuilder = new("https://www.qobuz.com/api.json/0.2");
        stringBuilder.Append(method);
        for (var i = 0; i < parameters.Count; i++)
        {
            var start = i == 0 ? "?" : "&";
            var key = WebUtility.UrlEncode(parameters.ElementAt(i).Key);
            var value = WebUtility.UrlEncode(parameters.ElementAt(i).Value);
            stringBuilder.Append(start + key + "=" + value);
        }
        return stringBuilder.ToString();
    }
}

public enum AudioQuality
{
    MP3320 = 5,
    FLACLossless = 6,
    FLACHiRes24Bit96kHz = 7,
    FLACHiRes24Bit192Khz = 27,
}
