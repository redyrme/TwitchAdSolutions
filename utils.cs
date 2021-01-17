using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;
using System.Net;
using System.IO;
using System.Diagnostics;

namespace TwitchAdUtils
{
    class Program
    {
        static string ClientID = "kimne78kx3ncx6brgo4mv6wki5h1ko";//ilfexgv3nnljz3isbm257gzwrzr7bi - Xtra for Twitch
        static string UserAgentChrome = "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.88 Safari/537.36";
        static string UserAgentFirefox = "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:84.0) Gecko/20100101 Firefox/84.0";
        static string UserAgent = UserAgentChrome;
        static bool UseOldAccessToken = false;
        static bool UseAccessTokenTemplate = false;
        static bool ShouldNotifyAdWatched = true;
        static bool ShouldNotifyAdWatchedMin = true;
        static bool ShouldDenyAd = false;
        static string PlayerTypeNormal = "site";//embed squad_secondary squad_primary
        static string PlayerTypeMiniNoAd = "picture-by-picture";//"thunderdome";
        static string Platform = "web";
        static string PlayerBackend = "mediaplayer";
        static string MainM3U8AdditionalParams = "";
        static string AdSignifier = "stitched-ad";
        static string ProxyUrl = "";
        static int TargetResolution = 480;
        static TimeSpan LoopDelay = TimeSpan.FromSeconds(1);
        
        enum RunnerMode
        {
            Normal,
            MiniNoAd,
            Proxy
        }
        
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            
            if (args.Length >= 1 && args[0] == "build_scripts")
            {
                // This takes "base.user.js" and updates all of the other scripts based on the cfg values
                BuildScripts();
                return;
            }
            if (args.Length >= 1 && args[0] == "m3u8")
            {
                // Tests modifications of m3u8 files
                Console.WriteLine("Starting local server (http://localhost)");
                TwitchTestServer testServer = new TwitchTestServer();
                testServer.Start(80);
                Console.ReadLine();
                return;
            }
            
            Console.Write("Enter channel name: ");
            string channel = Console.ReadLine().ToLower();
            Console.WriteLine("Fetching channel '" + channel + "'");
            RunImpl(RunnerMode.Normal, channel);
            //RunImpl(RunnerMode.MiniNoAd, channel);
        }
        
        static void BuildScripts()
        {
            string baseScriptName = "base";
            string suffixConfg = ".cfg";
            string suffixUserscript = ".user.js";
            string suffixUblock = "-ublock-origin.js";
            string baseFile = Path.Combine(baseScriptName, baseScriptName + ".user.js");
            if (File.Exists(baseFile))
            {
                foreach (string dir in Directory.GetDirectories(Environment.CurrentDirectory))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.Name != baseScriptName)
                    {
                        string cfgFile = Path.Combine(dir, dirInfo.Name + suffixConfg);
                        string userscriptFile = Path.Combine(dir, dirInfo.Name + suffixUserscript);
                        string ublockFile = Path.Combine(dir, dirInfo.Name + suffixUblock);
                        if (File.Exists(userscriptFile) && File.Exists(ublockFile) && File.Exists(cfgFile))
                        {
                            Dictionary<string, string> cfgValues = new Dictionary<string, string>();
                            string[] cfgLines = File.ReadAllLines(cfgFile);
                            for (int i = 0; i < cfgLines.Length; i++)
                            {
                                string line = cfgLines[i];
                                if (!string.IsNullOrEmpty(line))
                                {
                                    int spaceIndex = line.IndexOf(' ');
                                    if (spaceIndex > 0)
                                    {
                                        cfgValues["scope." + line.Substring(0, spaceIndex).Trim() + " "] = line.Substring(spaceIndex + 1).Trim();
                                    }
                                }
                            }
                            Console.WriteLine(dir);
                            foreach (KeyValuePair<string, string> val in cfgValues)
                            {
                                Console.WriteLine(val.Key + "= " + val.Value);
                            }
                            Console.WriteLine("=============================");
                            
                            StringBuilder sbUserscript = new StringBuilder();
                            StringBuilder sbUblock = new StringBuilder();
                            string[] lines = File.ReadAllLines(baseFile);
                            bool modifiedOptions = false;
                            bool foundUserScriptEnd = false;
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i];
                                string lineTrimmed = line.Trim();
                                if (lineTrimmed.StartsWith("// Modify options based on mode"))
                                {
                                    modifiedOptions = true;
                                }
                                if (lineTrimmed.StartsWith("// @description"))
                                {
                                    string url = "https://github.com/pixeltris/TwitchAdSolutions/raw/master/" + dirInfo.Name + "/" + dirInfo.Name + suffixUserscript;
                                    sbUserscript.AppendLine("// @updateURL    " + url);
                                    sbUserscript.AppendLine("// @downloadURL  " + url);
                                    line = line += " (" + dirInfo.Name + ")";
                                }
                                if (!modifiedOptions)
                                {
                                    if (!foundUserScriptEnd)
                                    {
                                        sbUserscript.AppendLine(line);
                                        if (line.Contains("/UserScript"))
                                        {
                                            sbUblock.AppendLine("twitch-videoad.js application/javascript");
                                            foundUserScriptEnd = true;
                                        }
                                    }
                                    else if (lineTrimmed.StartsWith("'use strict'"))
                                    {
                                        sbUserscript.AppendLine(line);
                                        sbUblock.AppendLine("    if ( /(^|\\.)twitch\\.tv$/.test(document.location.hostname) === false ) { return; }");
                                    }
                                    else
                                    {
                                        foreach (KeyValuePair<string, string> val in cfgValues)
                                        {
                                            if (line.Contains(val.Key))
                                            {
                                                line = line.Substring(0, line.IndexOf(val.Key) + val.Key.Length) + "= " + val.Value + ";";
                                                break;
                                            }
                                        }
                                        sbUserscript.AppendLine(line);
                                        sbUblock.AppendLine(line);
                                    }
                                }
                                else
                                {
                                    sbUserscript.AppendLine(line);
                                    sbUblock.AppendLine(line);
                                }
                            }
                            File.WriteAllText(userscriptFile, sbUserscript.ToString());
                            File.WriteAllText(ublockFile, sbUblock.ToString());
                        }
                    }
                }
            }
        }
        
        static void Run(RunnerMode mode, string channel)
        {
            Thread thread = new Thread(delegate()
            {
                RunImpl(mode, channel);
            });
            thread.IsBackground = true;
            thread.Start();
        }
        
        static string RunImpl(RunnerMode mode, string channel, bool isFetchingM3U8 = false, bool forceSkipAd = false)
        {
            string playerType = mode == RunnerMode.MiniNoAd ? PlayerTypeMiniNoAd : PlayerTypeNormal;
            string cookies = null;
            string uniqueId = null;
            int cycle = 0;
            while (true)
            {
                if (string.IsNullOrEmpty(cookies))
                {
                    using (CookieAwareWebClient wc = new CookieAwareWebClient())
                    {
                        wc.Proxy = null;
                        wc.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";
                        wc.DownloadString("https://www.twitch.tv/" + channel);
                        cookies = ProcessCookies(wc.Cookies, out uniqueId);
                        //Console.WriteLine("unique_id: " + uniqueId);
                    }
                }
                if (string.IsNullOrEmpty(uniqueId))
                {
                    Console.WriteLine("unique_id is null");
                    return null;
                }
                using (WebClient wc = new WebClient())
                {
                    string response = null, token = null, sig = null;
                    wc.Proxy = null;
                    if (mode != RunnerMode.Proxy)
                    {
                        if (UseOldAccessToken)
                        {
                            wc.Headers.Clear();
                            wc.Headers["client-id"] = ClientID;
                            wc.Headers["accept"] = "application/vnd.twitchtv.v5+json; charset=UTF-8";
                            wc.Headers["accept-encoding"] = "gzip, deflate, br";
                            wc.Headers["accept-language"] = "en-us";
                            wc.Headers["content-type"] = "application/json; charset=UTF-8";
                            wc.Headers["origin"] = "https://www.twitch.tv";
                            wc.Headers["referer"] = "https://www.twitch.tv/";
                            wc.Headers["user-agent"] = UserAgent;
                            wc.Headers["x-requested-with"] = "XMLHttpRequest";
                            wc.Headers["cookie"] = cookies;
                            response = wc.DownloadString("https://api.twitch.tv/api/channels/" + channel + "/access_token?oauth_token=undefined&need_https=true&platform=" + Platform + "&player_type=" + playerType + "&player_backend=" + PlayerBackend);
                            if (!string.IsNullOrEmpty(response))
                            {
                                TwitchAccessTokenOld tokenInfo = JSONSerializer<TwitchAccessTokenOld>.DeSerialize(response);
                                if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.token) && !string.IsNullOrEmpty(tokenInfo.sig))
                                {
                                    token = tokenInfo.token;
                                    sig = tokenInfo.sig;
                                }
                            }
                        }
                        else
                        {
                            wc.Headers.Clear();
                            wc.Headers["client-id"] = ClientID;
                            wc.Headers["Device-ID"] = uniqueId;
                            wc.Headers["accept"] = "*/*";
                            wc.Headers["accept-encoding"] = "gzip, deflate, br";
                            wc.Headers["accept-language"] = "en-us";
                            wc.Headers["content-type"] = "text/plain; charset=UTF-8";
                            wc.Headers["origin"] = "https://www.twitch.tv";
                            wc.Headers["referer"] = "https://www.twitch.tv/";
                            wc.Headers["user-agent"] = UserAgent;
                            if (UseAccessTokenTemplate)
                            {
                                response = wc.UploadString("https://gql.twitch.tv/gql", @"{""operationName"":""PlaybackAccessToken_Template"",""query"":""query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!) {  streamPlaybackAccessToken(channelName: $login, params: {platform: \""" + Platform + @"\"", playerBackend: \""" + PlayerBackend + @"\"", playerType: $playerType}) @include(if: $isLive) {    value    signature    __typename  }  videoPlaybackAccessToken(id: $vodID, params: {platform: \""" + Platform + @"\"", playerBackend: \""" + PlayerBackend + @"\"", playerType: $playerType}) @include(if: $isVod) {    value    signature    __typename  }}"",""variables"":{""isLive"":true,""login"":""" + channel + @""",""isVod"":false,""vodID"":"""",""playerType"":""" + playerType + @"""}}");
                            }
                            else
                            {
                                response = wc.UploadString("https://gql.twitch.tv/gql", @"{""operationName"":""PlaybackAccessToken"",""variables"":{""isLive"":true,""login"":""" + channel + @""",""isVod"":false,""vodID"":"""",""playerType"":""" + playerType + @"""},""extensions"":{""persistedQuery"":{""version"":1,""sha256Hash"":""0828119ded1c13477966434e15800ff57ddacf13ba1911c129dc2200705b0712""}}}");
                            }
                            if (!string.IsNullOrEmpty(response))
                            {
                                TwitchAccessToken tokenInfo = JSONSerializer<TwitchAccessToken>.DeSerialize(response);
                                if (tokenInfo != null && tokenInfo.data != null && tokenInfo.data.streamPlaybackAccessToken != null &&
                                    !string.IsNullOrEmpty(tokenInfo.data.streamPlaybackAccessToken.value) && !string.IsNullOrEmpty(tokenInfo.data.streamPlaybackAccessToken.signature))
                                {
                                    token = tokenInfo.data.streamPlaybackAccessToken.value;
                                    sig = tokenInfo.data.streamPlaybackAccessToken.signature;
                                }
                            }
                        }
                    }
                    if (mode == RunnerMode.Proxy || !string.IsNullOrEmpty(token))
                    {
                        string url = null;
                        if (mode == RunnerMode.Proxy)
                        {
                            url = ProxyUrl + channel;
                        }
                        else
                        {
                            url = "https://usher.ttvnw.net/api/channel/hls/" + channel + ".m3u8?allow_source=true&sig=" + sig + "&token=" + System.Web.HttpUtility.UrlEncode(token) + MainM3U8AdditionalParams;
                        }
                        if (isFetchingM3U8)
                        {
                            if (!forceSkipAd || cycle > 0)
                            {
                                return url;
                            }
                        }
                        wc.Headers.Clear();
                        wc.Headers["accept"] = "application/x-mpegURL, application/vnd.apple.mpegurl, application/json, text/plain";
                        wc.Headers["host"] = "usher.ttvnw.net";
                        wc.Headers["cookie"] = "DNT=1;";
                        wc.Headers["DNT"] = "1";
                        wc.Headers["user-agent"] = UserAgent;
                        string encodingsM3u8 = wc.DownloadString(url);
                        if (!string.IsNullOrEmpty(encodingsM3u8))
                        {
                            string[] lines = encodingsM3u8.Split('\n');
                            string streamM3u8Url = lines.FirstOrDefault(x => x.EndsWith(".m3u8"));
                            if (!string.IsNullOrEmpty(streamM3u8Url))
                            {
                                bool foundAd = true;
                                while (foundAd)
                                {
                                    string streamM3u8 = wc.DownloadString(streamM3u8Url);
                                    if (!string.IsNullOrEmpty(streamM3u8Url))
                                    {
                                        if (streamM3u8.Contains(AdSignifier))
                                        {
                                            Console.WriteLine("has ad " + DateTime.Now.TimeOfDay);
                                            if (ShouldDenyAd)
                                            {
                                                DeclineAd(uniqueId, streamM3u8, sig, token, true);
                                                DeclineAd(uniqueId, streamM3u8, sig, token, false);
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("no ad " + DateTime.Now.TimeOfDay);
                                        }
                                        if ((streamM3u8.Contains(AdSignifier) || forceSkipAd) &&
                                            (!UseOldAccessToken && (ShouldNotifyAdWatched || forceSkipAd)))
                                        {
                                            NotifyWatchedAd(uniqueId, streamM3u8);
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("Failed to fetch streamM3u8Url");
                                    }
                                    if (!ShouldDenyAd)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        Thread.Sleep(LoopDelay);
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("Failed to find streamM3u8Url");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Failed to fetch encodingsM3u8");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Failed to get stream token");
                    }
                }
                Thread.Sleep(LoopDelay);
                cycle++;
            }
        }
        
        static Dictionary<string, string> ParseAttributes(string tag)
        {
            string tagName;
            return ParseAttributes(tag, out tagName);
        }
        
        static Dictionary<string, string> ParseAttributes(string tag, out string tagName)
        {
            // TODO: Improve this
            Dictionary<string, string> result = new Dictionary<string, string>();
            tagName = null;
            int tagDataSplitIndex = tag.IndexOf(':');
            if (tagDataSplitIndex > 0)
            {
                tagName = tag.Substring(0, tagDataSplitIndex);
                tag = tag.Substring(tagDataSplitIndex + 1);
                string[] splitted = tag.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string str in splitted)
                {
                    int index = str.IndexOf('=');
                    if (index > 0)
                    {
                        result[str.Substring(0, index)] = str.Substring(index + 1).Trim('\"');
                    }
                }
            }
            return result;
        }
        
        static TValue GetOrDefault<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default(TValue))
        {
            TValue result;
            if (dict.TryGetValue(key, out result))
            {
                return result;
            }
            return defaultValue;
        }
        
        static void DeclineAd(string uniqueId, string streamM3u8, string sig, string token, bool first)
        {
            string[] lines = streamM3u8.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(AdSignifier))
                {
                    Dictionary<string, string> attr = ParseAttributes(lines[i]);
                    Dictionary<string, string> vals = new Dictionary<string, string>();
                    vals["TARG_adSessionID"] = GetOrDefault(attr, "X-TV-TWITCH-AD-AD-SESSION-ID");
                    vals["TARG_sig"] = sig;
                    vals["TARG_token"] = token.Replace("\"", "\\\"");
                    string str = null;
                    //string str = @"[{""operationName"":""VideoAdRequestDecline"",""variables"":{""context"":{""adSessionID"":""TARG_adSessionID"",""clientContext"":""{\""isAudioOnly\"":false,\""isMiniTheater\"":false,\""isPIP\"":true,\""isUsingExternalPlayback\"":false}"",""isAudioOnly"":false,""isMiniTheater"":false,""isPIP"":false,""isUsingExternalPlayback"":false,""duration"":30,""isVLM"":false,""rollType"":""PREROLL""}},""extensions"":{""persistedQuery"":{""version"":1,""sha256Hash"":""6f5d9fdc36a3c879cca7debdbe21c62d5cac4ad5b30b635263eff68335b96a71""}}}]";
                    if (first)
                        str = @"[{""operationName"":""VideoAdRequestDecline"",""variables"":{""context"":{""adSessionID"":""TARG_adSessionID"",""clientContext"":{""isAudioOnly"":false,""isMiniTheater"":false,""isPIP"":false,""isUsingExternalPlayback"":false},""duration"":30,""playerContext"":{""contentType"":""LIVE"",""isAutoPlay"":true,""nauthSig"":""TARG_sig"",""nauthToken"":""TARG_token""},""rollType"":""PREROLL"",""isVLM"":false,""commercialID"":""""}},""extensions"":{""persistedQuery"":{""version"":1,""sha256Hash"":""6f5d9fdc36a3c879cca7debdbe21c62d5cac4ad5b30b635263eff68335b96a71""}}}]";
                    else
                    {
                        vals["TARG_ad_session_id"] = GetOrDefault(attr, "X-TV-TWITCH-AD-AD-SESSION-ID");
                        vals["TARG_radToken"] = GetOrDefault(attr, "X-TV-TWITCH-AD-RADS-TOKEN");
                        str = @"[{""operationName"":""ClientSideAdEventHandling_RecordAdEvent"",""variables"":{""input"":{""eventName"":""video_ad_request_declined"",""eventPayload"":""{\""reason_channeladfree\"":false,\""reason_channelsub\"":false,\""reason_vod_ads_disabled\"":false,\""reason_bounty\"":false,\""reason_vod_midroll\"":false,\""reason_stream_broadcaster\"":false,\""reason_embed_promo\"":false,\""reason_p4m\"":false,\""reason_lt\"":false,\""reason_raid\"":false,\""reason_midroll_during_preroll\"":false,\""reason_ratelimit\"":false,\""reason_short_vod\"":false,\""reason_turbo\"":false,\""reason_vod_creator\"":false,\""reason_wp\"":false,\""reason_zagd\"":false,\""reason_zagu\"":false,\""reason_midlimit\"":false,\""reason_amazon_product_page\"":false,\""reason_animated_thumbnails\"":false,\""reason_creative_player\"":false,\""reason_dashboard\"":false,\""reason_facebook\"":false,\""reason_frontpage\"":false,\""reason_highlighter\"":false,\""reason_onboarding\"":false,\""reason_pbyp\"":false,\""reason_squad_stream_secondary_player\"":false,\""reason_thunderdome\"":true,\""reason_embed\"":false,\""twitch_correlator\"":\""\"",\""ad_session_id\"":\""TARG_ad_session_id\"",\""roll_type\"":\""preroll\"",\""time_break\"":30}"",""radToken"":""TARG_radToken""}},""extensions"":{""persistedQuery"":{""version"":1,""sha256Hash"":""7e6c69e6eb59f8ccb97ab73686f3d8b7d85a72a0298745ccd8bfc68e4054ca5b""}}}]";
                    }
                    foreach (KeyValuePair<string, string> val in vals)
                    {
                        str = str.Replace(val.Key, val.Value);
                    }
                    //Console.WriteLine(str);
                    using (WebClient wc = new WebClient())
                    {
                        wc.Proxy = null;
                        wc.Headers["Client-Id"] = ClientID;
                        wc.Headers["X-Device-Id"] = uniqueId;
                        wc.Headers["accept"] = "*/*";
                        wc.Headers["accept-encoding"] = "gzip, deflate, br";
                        wc.Headers["accept-language"] = "en-us";
                        wc.Headers["content-type"] = "text/plain; charset=UTF-8";
                        wc.Headers["origin"] = "https://www.twitch.tv";
                        wc.Headers["referer"] = "https://www.twitch.tv/";
                        wc.Headers["user-agent"] = UserAgent;
                        string st2 = wc.UploadString("https://gql.twitch.tv/gql", str);
                        Console.WriteLine(st2);
                    }
                    return;
                }
            }
        }
        
        static void SendGqlAdEvent(WebClient wc, string eventName, bool includeAdInfo, int adQuartile, int adPos, Dictionary<string, string> vals)
        {
            // TARG_eventName TARG_roll_type TARG_radToken TARG_adInfo
            // TARG_ad_id TARG_ad_position TARG_duration TARG_creative_id TARG_total_ads TARG_order_id TARG_line_item_id TARG_quartile
            string str = @"[{""operationName"":""ClientSideAdEventHandling_RecordAdEvent"",""variables"":{""input"":{""eventName"":""TARG_eventName"",""eventPayload"":""{\""player_mute\"":false,\""player_volume\"":0.5,\""visible\"":true,\""roll_type\"":\""TARG_roll_type\"",\""stitched\"":trueTARG_adInfo}"",""radToken"":""TARG_radToken""}},""extensions"":{""persistedQuery"":{""version"":1,""sha256Hash"":""7e6c69e6eb59f8ccb97ab73686f3d8b7d85a72a0298745ccd8bfc68e4054ca5b""}}}]";
            string strAdInfo = @",\""ad_id\"":\""TARG_ad_id\"",\""ad_position\"":TARG_ad_position,\""duration\"":TARG_duration,\""creative_id\"":\""TARG_creative_id\"",\""total_ads\"":TARG_total_ads,\""order_id\"":\""TARG_order_id\"",\""line_item_id\"":\""TARG_line_item_id\""TARG_quartile";
            vals["TARG_eventName"] = eventName;
            vals["TARG_quartile"] = adQuartile > 0 ? (@",\""quartile\"":" + adQuartile) : string.Empty;
            if (includeAdInfo)
            {
                foreach (KeyValuePair<string, string> val in vals)
                {
                    strAdInfo = strAdInfo.Replace(val.Key, val.Value);
                }
                vals["TARG_adInfo"] = strAdInfo;
            }
            else
            {
                vals["TARG_adInfo"] = "";
            }
            foreach (KeyValuePair<string, string> val in vals)
            {
                str = str.Replace(val.Key, val.Value);
            }
            //Console.WriteLine(str);
            Console.WriteLine("SendGqlAdEvent " + eventName + " adinfo: " + includeAdInfo + " quartile: " + adQuartile + " adPos: " + adPos);
            wc.UploadString("https://gql.twitch.tv/gql", str);
        }
        
        static void NotifyWatchedAd(string uniqueId, string streamM3u8)
        {
            string[] lines = streamM3u8.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(AdSignifier))
                {
                    Dictionary<string, string> attr = ParseAttributes(lines[i]);
                    Dictionary<string, string> vals = new Dictionary<string, string>();
                    vals["TARG_roll_type"] = GetOrDefault(attr, "X-TV-TWITCH-AD-ROLL-TYPE", "preroll").ToLower();
                    vals["TARG_radToken"] = GetOrDefault(attr, "X-TV-TWITCH-AD-RADS-TOKEN");
                    vals["TARG_ad_id"] = GetOrDefault(attr, "X-TV-TWITCH-AD-ADVERTISER-ID");
                    vals["TARG_duration"] = "30";
                    vals["TARG_creative_id"] = GetOrDefault(attr, "X-TV-TWITCH-AD-CREATIVE-ID");
                    vals["TARG_total_ads"] = GetOrDefault(attr, "X-TV-TWITCH-AD-POD-LENGTH", "1");
                    vals["TARG_order_id"] = GetOrDefault(attr, "X-TV-TWITCH-AD-ORDER-ID");
                    vals["TARG_line_item_id"] = GetOrDefault(attr, "X-TV-TWITCH-AD-LINE-ITEM-ID");
                    using (WebClient wc = new WebClient())
                    {
                        wc.Proxy = null;
                        wc.Headers["Client-Id"] = ClientID;
                        wc.Headers["X-Device-Id"] = uniqueId;
                        wc.Headers["accept"] = "*/*";
                        wc.Headers["accept-encoding"] = "gzip, deflate, br";
                        wc.Headers["accept-language"] = "en-us";
                        wc.Headers["content-type"] = "text/plain; charset=UTF-8";
                        wc.Headers["origin"] = "https://www.twitch.tv";
                        wc.Headers["referer"] = "https://www.twitch.tv/";
                        wc.Headers["user-agent"] = UserAgent;
                        if (ShouldNotifyAdWatchedMin)
                        {
                            SendGqlAdEvent(wc, "video_ad_pod_complete", false, 0, 0, vals);
                        }
                        else
                        {
                            int totalAds = int.Parse(vals["TARG_total_ads"]);
                            for (int adPos = 0; adPos < totalAds; adPos++)
                            {
                                vals["TARG_ad_position"] = adPos.ToString();
                                SendGqlAdEvent(wc, "video_ad_impression", true, 0, adPos, vals);
                                for (int quartile = 1; quartile <= 4; quartile++)
                                {
                                    SendGqlAdEvent(wc, "video_ad_quartile_complete", true, quartile, adPos, vals);
                                }
                                SendGqlAdEvent(wc, "video_ad_pod_complete", false, 0, adPos, vals);
                            }
                        }
                    }
                    break;
                }
            }
            //Console.WriteLine(streamM3u8);
        }
        
        static string ProcessCookies(string str)
        {
            string uniqueId;
            return ProcessCookies(str, out uniqueId);
        }
        
        static string ProcessCookies(string str, out string uniqueId)
        {
            uniqueId = null;
            string result = string.Empty;
            string[] cookies = str.Split(',');
            foreach (string cookie in cookies)
            {
                if (cookie.Split(';')[0].Contains('='))
                {
                    string[] splitted = cookie.Split(';')[0].Split('=');
                    if (splitted.Length >= 2 && splitted[0] == "unique_id")
                    {
                        uniqueId = splitted[1];
                    }
                    result += cookie.Split(';')[0] + ";";
                }
            }
            return result;
        }
        
        [DataContract]
        public class TwitchAccessTokenOld
        {
            [DataMember]
            public string token { get; set; }
            [DataMember]
            public string sig { get; set; }        
        }

        [DataContract]
        public class TwitchAccessToken
        {
            [DataMember]
            public TwitchAccessToken_data data { get; set; }
        }

        [DataContract]
        public class TwitchAccessToken_data
        {
            [DataMember]
            public TwitchAccessToken_streamPlaybackAccessToken streamPlaybackAccessToken { get; set; }
        }

        [DataContract]
        public class TwitchAccessToken_streamPlaybackAccessToken
        {
            [DataMember]
            public string value { get; set; }
            [DataMember]
            public string signature { get; set; }
        }
        
        class CookieAwareWebClient : WebClient
        {
            public CookieContainer CookieContainer { get; set; }
            public Uri Uri { get; set; }

            public string Cookies { get; private set; }

            public CookieAwareWebClient()
                : this(new CookieContainer())
            {
            }

            public CookieAwareWebClient(CookieContainer cookies)
            {
                this.CookieContainer = new CookieContainer();
            }

            protected override WebResponse GetWebResponse(WebRequest request)
            {
                WebResponse response = base.GetWebResponse(request);
                string setCookieHeader = response.Headers.Get("Set-Cookie");
                Cookies = setCookieHeader;
                return response;
            }
        }
        
        static class JSONSerializer<TType> where TType : class
        {
            public static TType DeSerialize(string json)
            {
                return TinyJson.JSONParser.FromJson<TType>(json);
            }
        }
        
        class TwitchTestServer
        {
            const string RecordDir = "recordings";
            Dictionary<string, State> states = new Dictionary<string, State>();
            class State
            {
                public bool IsReplay = false;
                public string RecordingName = null;
                public string RecordingPath = null;
                public string ChannelName = null;
                public string UrlChRecName { get { return ChannelName + "|" + RecordingName; } }
                public string M3U8Normal = null;
                public string M3U8Mini = null;
                public string M3U8Alt = null;
                public Dictionary<string, Dictionary<string, string>> M3U8Map = new Dictionary<string, Dictionary<string, string>>();
                public Stopwatch Stopwatch = new Stopwatch();
                
                public State(string channelName, string name)
                {
                    ChannelName = channelName;
                    RecordingName = name;
                    RecordingPath = Path.GetFullPath(Path.Combine(RecordDir, name));
                    try
                    {
                        if (!Directory.Exists(RecordingPath))
                        {
                            Directory.CreateDirectory(RecordingPath);
                        }
                    }
                    catch
                    {
                    }
                }
                
                public void Clear()
                {
                    M3U8Map.Clear();
                    Stopwatch.Restart();
                    try
                    {
                        while (Directory.Exists(RecordingPath))
                        {
                            Directory.Delete(RecordingPath, true);
                        }
                    }
                    catch
                    {
                    }
                    try
                    {
                        if (!Directory.Exists(RecordingPath))
                        {
                            Directory.CreateDirectory(RecordingPath);
                        }
                    }
                    catch
                    {
                    }
                }
                
                public void Load()
                {
                    M3U8Map.Clear();
                    Stopwatch.Restart();
                    IsReplay = true;
                }
            }
            
            private Thread thread;
            private HttpListener listener;
            
            public void Start(int port)
            {
                Stop();
                
                thread = new Thread(delegate()
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add("http://*:" + port + "/");
                    listener.Start();
                    while (listener != null)
                    {
                        try
                        {
                            HttpListenerContext context = listener.GetContext();
                            Process(context);
                        }
                        catch
                        {
                        }
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
            
            public void Stop()
            {
                if (listener != null)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }
                    listener = null;
                }
                if (thread != null)
                {
                    try
                    {
                        thread.Abort();
                    }
                    catch
                    {
                    }
                    thread = null;
                }
            }
            
            private void Process(HttpListenerContext context)
            {
                try
                {
                    string url = context.Request.Url.OriginalString;
                    //Console.WriteLine("req " + DateTime.Now.TimeOfDay + " - " + url);

                    string response = string.Empty;
                    string contentType = "text/html";

                    if (url.Contains("favicon.ico"))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        context.Response.OutputStream.Close();
                        return;
                    }
                    
                    byte[] responseBuffer = null;
                    if (context.Request.Url.Segments.Length == 1 && context.Request.Url.Segments[0] == "/")
                    {
                        response = "<html><script src='utils.js'></script></html>";
                    }
                    else if (context.Request.Url.Segments.Length == 2 && context.Request.Url.Segments[1] == "utils.js")
                    {
                        response = File.ReadAllText("utils.js");
                    }
                    else if (context.Request.Url.Segments.Length >= 3)
                    {
                        string[] reqTypeSplitted = context.Request.Url.Segments[1].Trim('/').Split('_');
                        string reqType = reqTypeSplitted[0].ToLower();
                        string reqStreamType = reqTypeSplitted.Length > 1 ? reqTypeSplitted[1] : null;
                        string[] splitted = context.Request.Url.Segments[2].Trim('/').ToLower().Replace("%7c", "|").Split('|');
                        string channelName = splitted[0];
                        string recordingName = splitted[1];
                        if (!string.IsNullOrEmpty(channelName) && !string.IsNullOrEmpty(recordingName))
                        {
                            State state;
                            if (!states.TryGetValue(recordingName, out state))
                            {
                                states[recordingName] = state = new State(channelName, recordingName);
                            }
                            switch (reqType)
                            {
                                case "record-begin":
                                    {
                                        state.Clear();
                                        string normal = RunImpl(RunnerMode.Normal, channelName, true);
                                        if (!string.IsNullOrEmpty(normal))
                                        {
                                            string mini = RunImpl(RunnerMode.MiniNoAd, channelName, true);
                                            if (!string.IsNullOrEmpty(mini))
                                            {
                                                //string alt = RunImpl(RunnerMode.Proxy, channelName, true);
                                                string alt = RunImpl(RunnerMode.Normal, channelName, true, true);
                                                state.M3U8Normal = normal;
                                                state.M3U8Mini = mini;
                                                state.M3U8Alt = alt;
                                                response = "ok";
                                            }
                                        }                                        
                                    }
                                    break;
                                case "replay-begin":
                                    {
                                        DirectoryInfo dir = new DirectoryInfo(Path.Combine(RecordDir, recordingName));
                                        if (dir.Exists && dir.GetFiles().Length > 0)
                                        {
                                            state.Load();
                                            response = "ok";
                                        }
                                    }
                                    break;
                                case "m3u8":
                                    {
                                        string type = reqTypeSplitted[1].ToLower();
                                        string m3u8Url = null;
                                        switch (type)
                                        {
                                            case "normal":
                                            case "output":
                                                m3u8Url = state.M3U8Normal;
                                                break;
                                            case "mini":
                                                m3u8Url = state.M3U8Mini;
                                                break;
                                            case "alt":
                                                m3u8Url = state.M3U8Alt;
                                                break;
                                        }
                                        if (!string.IsNullOrEmpty(m3u8Url))
                                        {
                                            response = GetM3U8(state, m3u8Url, reqStreamType, true);
                                        }
                                    }
                                    break;
                                case "m3u8-sub":
                                    {
                                        string type = reqTypeSplitted[1].ToLower();
                                        string m3u8Url = null;
                                        if (!state.IsReplay)
                                        {
                                            m3u8Url = GetM3U8Url(state, reqStreamType);
                                        }
                                        if (!string.IsNullOrEmpty(m3u8Url) || state.IsReplay)
                                        {
                                            response = GetM3U8(state, m3u8Url, reqStreamType, false);
                                        }
                                    }
                                    break;
                                case "m3u8-seg":
                                    {
                                        // TODO: Load segment, return as binary file
                                    }
                                    break;
                                default:
                                    Console.WriteLine("Unhandled request '" + reqType + "'");
                                    break;
                            }
                        }
                    }
                    
                    if (responseBuffer == null)
                    {
                        responseBuffer = Encoding.UTF8.GetBytes(response == null ? string.Empty : response.ToString());
                    }
                    context.Response.ContentType = contentType;
                    context.Response.ContentEncoding = Encoding.UTF8;
                    context.Response.ContentLength64 = responseBuffer.Length;
                    context.Response.OutputStream.Write(responseBuffer, 0, responseBuffer.Length);
                    context.Response.OutputStream.Flush();
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
                context.Response.OutputStream.Close();
            }
            
            private string DownloadM3U8(string url)
            {
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Proxy = null;
                        wc.Headers["accept"] = "application/x-mpegURL, application/vnd.apple.mpegurl, application/json, text/plain";
                        wc.Headers["host"] = "usher.ttvnw.net";
                        wc.Headers["cookie"] = "DNT=1;";
                        wc.Headers["DNT"] = "1";
                        wc.Headers["user-agent"] = UserAgent;
                        return wc.DownloadString(url);
                    }
                }
                catch (Exception e)
                {
                    //Console.WriteLine(url);
                    Console.WriteLine(e);
                    return null;
                }
            }
            
            private string GetM3U8Url(State state, string reqStreamType)
            {
                Dictionary<string, string> m3u8Map;
                if (state.M3U8Map.TryGetValue(reqStreamType, out m3u8Map) && m3u8Map.Count > 0)
                {
                    string resUrl = null;
                    int res = int.MaxValue;
                    string backupUrl = null;
                    foreach (KeyValuePair<string, string> mappedUrl in m3u8Map)
                    {
                        if (mappedUrl.Key.Contains("x"))
                        {
                            int val;
                            if (int.TryParse(mappedUrl.Key.Split('x')[1], out val))
                            {
                                if (backupUrl == null)
                                {
                                    backupUrl = mappedUrl.Value;
                                }
                                if (val < res && val >= TargetResolution)
                                {
                                    res = val;
                                    resUrl = mappedUrl.Value;
                                }
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(resUrl))
                    {
                        resUrl = backupUrl;
                    }
                    return resUrl;
                }
                return null;
            }
            
            private string GetM3U8(State state, string url, string reqStreamType, bool isMain)
            {
                string m3u8 = null;
                string backupUrl = null;
                if (state.IsReplay)
                {
                    // TODO: Load replay m3u8
                }
                else
                {
                    m3u8 = DownloadM3U8(url);
                    if (reqStreamType == "output")
                    {
                        backupUrl = GetM3U8Url(state, "mini");
                    }
                }
                if (string.IsNullOrEmpty(m3u8))
                {
                    return null;
                }
                if (!state.M3U8Map.ContainsKey(reqStreamType))
                {
                    state.M3U8Map[reqStreamType] = new Dictionary<string, string>();
                }
                m3u8 = m3u8.Replace("\r", string.Empty);
                string prevRes = null;
                string[] lines = m3u8.Split('\n');
                string mainM3U8Name = "m3u8-sub_" + reqStreamType;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#"))
                    {
                        string tagName;
                        Dictionary<string, string> attr = ParseAttributes(line, out tagName);
                        if (tagName == "#EXT-X-STREAM-INF")
                        {
                            attr.TryGetValue("RESOLUTION", out prevRes);
                        }
                    }
                    else if (line.EndsWith(".m3u8"))
                    {
                        if (!string.IsNullOrEmpty(prevRes) && !state.M3U8Map[reqStreamType].ContainsKey(prevRes))
                        {
                            state.M3U8Map[reqStreamType][prevRes] = line;
                        }
                        lines[i] = "/" + mainM3U8Name + "/" + state.UrlChRecName;
                    }
                    else if (line.EndsWith(".ts"))
                    {
                        // TODO: Save seg
                    }
                }
                if (!isMain && m3u8.Contains("stitched-ad") && !string.IsNullOrEmpty(backupUrl))
                {
                    string m3u8Backup = DownloadM3U8(backupUrl);
                    if (!string.IsNullOrEmpty(m3u8Backup))
                    {
                        m3u8Backup = m3u8Backup.Replace("\r", string.Empty);
                        string[] backupLines = m3u8Backup.Split('\n');
                        Dictionary<string, string> segmentMap = new Dictionary<string, string>();
                        Dictionary<long, string> segTimes = GetSegmentTimes(lines);
                        Dictionary<long, string> backupSegTimes = GetSegmentTimes(backupLines);
                        foreach (KeyValuePair<long, string> seg in segTimes)
                        {
                            //segmentMap[seg.Value] = backupSegTimes.Last().Value;
                            long closestTime = long.MaxValue;
                            long matchingBackupTime = long.MaxValue;
                            foreach (KeyValuePair<long, string> backupSeg in backupSegTimes)
                            {
                                long timeDiff = Math.Abs(seg.Key - backupSeg.Key);
                                if (timeDiff < closestTime)
                                {
                                    closestTime = timeDiff;
                                    matchingBackupTime = backupSeg.Key;
                                    segmentMap[seg.Value] = backupSeg.Value;
                                }
                            }
                            if (closestTime != long.MaxValue)
                            {
                                backupSegTimes.Remove(matchingBackupTime);
                            }
                        }
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i];
                            if (line.Contains("stitched-ad"))
                            {
                                line = "";
                            }
                            if (line.StartsWith("#EXTINF:") && !line.Contains(",live"))
                            {
                                lines[i] = line.Substring(0, line.IndexOf(',')) + ",live";
                                string backupSegment;
                                segmentMap.TryGetValue(lines[i + 1], out backupSegment);
                                lines[i + 1] = backupSegment != null ? backupSegment : "";
                            }
                        }
                    }
                }
                if (isMain)
                {
                    File.WriteAllText(Path.Combine(state.RecordingPath, mainM3U8Name + "-original"), m3u8);
                    File.WriteAllLines(Path.Combine(state.RecordingPath, mainM3U8Name), lines);
                }
                // TODO: Save m3u8
                return string.Join(Environment.NewLine, lines);
            }
            
            private Dictionary<long, string> GetSegmentTimes(string[] lines)
            {
                Dictionary<long, string> result = new Dictionary<long, string>();
                long lastDate = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:"))
                    {
                        lastDate = DateTime.Parse(line.Substring(line.IndexOf(":") + 1)).Ticks;
                    }
                    else if (line.StartsWith("http"))
                    {
                        result[lastDate] = line;
                    }
                }
                return result;
            }
        }
    }
}

namespace TinyJson
{
    // Really simple JSON parser in ~300 lines
    // - Attempts to parse JSON files with minimal GC allocation
    // - Nice and simple "[1,2,3]".FromJson<List<int>>() API
    // - Classes and structs can be parsed too!
    //      class Foo { public int Value; }
    //      "{\"Value\":10}".FromJson<Foo>()
    // - Can parse JSON without type information into Dictionary<string,object> and List<object> e.g.
    //      "[1,2,3]".FromJson<object>().GetType() == typeof(List<object>)
    //      "{\"Value\":10}".FromJson<object>().GetType() == typeof(Dictionary<string,object>)
    // - No JIT Emit support to support AOT compilation on iOS
    // - Attempts are made to NOT throw an exception if the JSON is corrupted or invalid: returns null instead.
    // - Only public fields and property setters on classes/structs will be written to
    //
    // Limitations:
    // - No JIT Emit support to parse structures quickly
    // - Limited to parsing <2GB JSON files (due to int.MaxValue)
    // - Parsing of abstract classes or interfaces is NOT supported and will throw an exception.
    public static class JSONParser
    {
        [ThreadStatic] static Stack<List<string>> splitArrayPool;
        [ThreadStatic] static StringBuilder stringBuilder;
        [ThreadStatic] static Dictionary<Type, Dictionary<string, FieldInfo>> fieldInfoCache;
        [ThreadStatic] static Dictionary<Type, Dictionary<string, PropertyInfo>> propertyInfoCache;

        public static T FromJson<T>(this string json)
        {
            // Initialize, if needed, the ThreadStatic variables
            if (propertyInfoCache == null) propertyInfoCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
            if (fieldInfoCache == null) fieldInfoCache = new Dictionary<Type, Dictionary<string, FieldInfo>>();
            if (stringBuilder == null) stringBuilder = new StringBuilder();
            if (splitArrayPool == null) splitArrayPool = new Stack<List<string>>();

            //Remove all whitespace not within strings to make parsing simpler
            stringBuilder.Length = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    i = AppendUntilStringEnd(true, i, json);
                    continue;
                }
                if (char.IsWhiteSpace(c))
                    continue;

                stringBuilder.Append(c);
            }

            //Parse the thing!
            return (T)ParseValue(typeof(T), stringBuilder.ToString());
        }

        static int AppendUntilStringEnd(bool appendEscapeCharacter, int startIdx, string json)
        {
            stringBuilder.Append(json[startIdx]);
            for (int i = startIdx + 1; i < json.Length; i++)
            {
                if (json[i] == '\\')
                {
                    if (appendEscapeCharacter)
                        stringBuilder.Append(json[i]);
                    stringBuilder.Append(json[i + 1]);
                    i++;//Skip next character as it is escaped
                }
                else if (json[i] == '"')
                {
                    stringBuilder.Append(json[i]);
                    return i;
                }
                else
                    stringBuilder.Append(json[i]);
            }
            return json.Length - 1;
        }

        //Splits { <value>:<value>, <value>:<value> } and [ <value>, <value> ] into a list of <value> strings
        static List<string> Split(string json)
        {
            List<string> splitArray = splitArrayPool.Count > 0 ? splitArrayPool.Pop() : new List<string>();
            splitArray.Clear();
            if (json.Length == 2)
                return splitArray;
            int parseDepth = 0;
            stringBuilder.Length = 0;
            for (int i = 1; i < json.Length - 1; i++)
            {
                switch (json[i])
                {
                    case '[':
                    case '{':
                        parseDepth++;
                        break;
                    case ']':
                    case '}':
                        parseDepth--;
                        break;
                    case '"':
                        i = AppendUntilStringEnd(true, i, json);
                        continue;
                    case ',':
                    case ':':
                        if (parseDepth == 0)
                        {
                            splitArray.Add(stringBuilder.ToString());
                            stringBuilder.Length = 0;
                            continue;
                        }
                        break;
                }

                stringBuilder.Append(json[i]);
            }

            splitArray.Add(stringBuilder.ToString());

            return splitArray;
        }

        internal static object ParseValue(Type type, string json)
        {
            if (type == typeof(string))
            {
                if (json.Length <= 2)
                    return string.Empty;
                StringBuilder parseStringBuilder = new StringBuilder(json.Length);
                for (int i = 1; i < json.Length - 1; ++i)
                {
                    if (json[i] == '\\' && i + 1 < json.Length - 1)
                    {
                        int j = "\"\\nrtbf/".IndexOf(json[i + 1]);
                        if (j >= 0)
                        {
                            parseStringBuilder.Append("\"\\\n\r\t\b\f/"[j]);
                            ++i;
                            continue;
                        }
                        if (json[i + 1] == 'u' && i + 5 < json.Length - 1)
                        {
                            UInt32 c = 0;
                            if (UInt32.TryParse(json.Substring(i + 2, 4), System.Globalization.NumberStyles.AllowHexSpecifier, null, out c))
                            {
                                parseStringBuilder.Append((char)c);
                                i += 5;
                                continue;
                            }
                        }
                    }
                    parseStringBuilder.Append(json[i]);
                }
                return parseStringBuilder.ToString();
            }
            if (type.IsPrimitive)
            {
                var result = Convert.ChangeType(json, type, System.Globalization.CultureInfo.InvariantCulture);
                return result;
            }
            if (type == typeof(decimal))
            {
                decimal result;
                decimal.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
                return result;
            }
            if (json == "null")
            {
                return null;
            }
            if (type.IsEnum)
            {
                if (json[0] == '"')
                    json = json.Substring(1, json.Length - 2);
                try
                {
                    return Enum.Parse(type, json, false);
                }
                catch
                {
                    return 0;
                }
            }
            if (type.IsArray)
            {
                Type arrayType = type.GetElementType();
                if (json[0] != '[' || json[json.Length - 1] != ']')
                    return null;

                List<string> elems = Split(json);
                Array newArray = Array.CreateInstance(arrayType, elems.Count);
                for (int i = 0; i < elems.Count; i++)
                    newArray.SetValue(ParseValue(arrayType, elems[i]), i);
                splitArrayPool.Push(elems);
                return newArray;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type listType = type.GetGenericArguments()[0];
                if (json[0] != '[' || json[json.Length - 1] != ']')
                    return null;

                List<string> elems = Split(json);
                var list = (IList)type.GetConstructor(new Type[] { typeof(int) }).Invoke(new object[] { elems.Count });
                for (int i = 0; i < elems.Count; i++)
                    list.Add(ParseValue(listType, elems[i]));
                splitArrayPool.Push(elems);
                return list;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type keyType, valueType;
                {
                    Type[] args = type.GetGenericArguments();
                    keyType = args[0];
                    valueType = args[1];
                }

                //Refuse to parse dictionary keys that aren't of type string
                if (keyType != typeof(string))
                    return null;
                //Must be a valid dictionary element
                if (json[0] != '{' || json[json.Length - 1] != '}')
                    return null;
                //The list is split into key/value pairs only, this means the split must be divisible by 2 to be valid JSON
                List<string> elems = Split(json);
                if (elems.Count % 2 != 0)
                    return null;

                var dictionary = (IDictionary)type.GetConstructor(new Type[] { typeof(int) }).Invoke(new object[] { elems.Count / 2 });
                for (int i = 0; i < elems.Count; i += 2)
                {
                    if (elems[i].Length <= 2)
                        continue;
                    string keyValue = elems[i].Substring(1, elems[i].Length - 2);
                    object val = ParseValue(valueType, elems[i + 1]);
                    dictionary[keyValue] = val;
                }
                return dictionary;
            }
            if (type == typeof(object))
            {
                return ParseAnonymousValue(json);
            }
            if (json[0] == '{' && json[json.Length - 1] == '}')
            {
                return ParseObject(type, json);
            }

            return null;
        }

        static object ParseAnonymousValue(string json)
        {
            if (json.Length == 0)
                return null;
            if (json[0] == '{' && json[json.Length - 1] == '}')
            {
                List<string> elems = Split(json);
                if (elems.Count % 2 != 0)
                    return null;
                var dict = new Dictionary<string, object>(elems.Count / 2);
                for (int i = 0; i < elems.Count; i += 2)
                    dict[elems[i].Substring(1, elems[i].Length - 2)] = ParseAnonymousValue(elems[i + 1]);
                return dict;
            }
            if (json[0] == '[' && json[json.Length - 1] == ']')
            {
                List<string> items = Split(json);
                var finalList = new List<object>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    finalList.Add(ParseAnonymousValue(items[i]));
                return finalList;
            }
            if (json[0] == '"' && json[json.Length - 1] == '"')
            {
                string str = json.Substring(1, json.Length - 2);
                return str.Replace("\\", string.Empty);
            }
            if (char.IsDigit(json[0]) || json[0] == '-')
            {
                if (json.Contains("."))
                {
                    double result;
                    double.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
                    return result;
                }
                else
                {
                    int result;
                    int.TryParse(json, out result);
                    return result;
                }
            }
            if (json == "true")
                return true;
            if (json == "false")
                return false;
            // handles json == "null" as well as invalid JSON
            return null;
        }

        static Dictionary<string, T> CreateMemberNameDictionary<T>(T[] members) where T : MemberInfo
        {
            Dictionary<string, T> nameToMember = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < members.Length; i++)
            {
                T member = members[i];
                if (member.IsDefined(typeof(IgnoreDataMemberAttribute), true))
                    continue;

                string name = member.Name;
                if (member.IsDefined(typeof(DataMemberAttribute), true))
                {
                    DataMemberAttribute dataMemberAttribute = (DataMemberAttribute)Attribute.GetCustomAttribute(member, typeof(DataMemberAttribute), true);
                    if (!string.IsNullOrEmpty(dataMemberAttribute.Name))
                        name = dataMemberAttribute.Name;
                }

                nameToMember.Add(name, member);
            }

            return nameToMember;
        }

        static object ParseObject(Type type, string json)
        {
            object instance = FormatterServices.GetUninitializedObject(type);

            //The list is split into key/value pairs only, this means the split must be divisible by 2 to be valid JSON
            List<string> elems = Split(json);
            if (elems.Count % 2 != 0)
                return instance;

            Dictionary<string, FieldInfo> nameToField;
            Dictionary<string, PropertyInfo> nameToProperty;
            if (!fieldInfoCache.TryGetValue(type, out nameToField))
            {
                nameToField = CreateMemberNameDictionary(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy));
                fieldInfoCache.Add(type, nameToField);
            }
            if (!propertyInfoCache.TryGetValue(type, out nameToProperty))
            {
                nameToProperty = CreateMemberNameDictionary(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy));
                propertyInfoCache.Add(type, nameToProperty);
            }

            for (int i = 0; i < elems.Count; i += 2)
            {
                if (elems[i].Length <= 2)
                    continue;
                string key = elems[i].Substring(1, elems[i].Length - 2);
                string value = elems[i + 1];

                FieldInfo fieldInfo;
                PropertyInfo propertyInfo;
                if (nameToField.TryGetValue(key, out fieldInfo))
                    fieldInfo.SetValue(instance, ParseValue(fieldInfo.FieldType, value));
                else if (nameToProperty.TryGetValue(key, out propertyInfo))
                    propertyInfo.SetValue(instance, ParseValue(propertyInfo.PropertyType, value), null);
            }

            return instance;
        }
    }
}